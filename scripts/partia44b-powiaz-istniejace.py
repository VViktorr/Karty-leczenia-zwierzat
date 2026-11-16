#!/usr/bin/env python3
"""Partia 44b: only already-reviewed exact-ID local PDFs. No clinical edits."""
from pathlib import Path
import sqlite3,hashlib,json,re,unicodedata
from collections import Counter
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
AUDIT=json.loads((OUT/"partia44_pelny_audyt.json").read_text(encoding="utf-8"))
assert hashlib.sha256(DB.read_bytes()).hexdigest()==AUDIT["db_sha256"],"SQLite changed: rerun audit"
# Exact UPD identifiers and product families; never mix combination vaccines.
GROUPS=[
 {"ids":[227,228,318,319,388,389,2688,2689],"upd":"600000004229","brand":"melovem","strength":"5 mg/ml","pdf":"data/ChPL/UPD/UPD_600000004229_Melovem_UPD_ChPL.pdf"},
 {"ids":[883,1042],"upd":"600000003470","brand":"porcilis pcv","pdf":"data/ChPL/UPD/UPD_600000003470_Porcilis_PCV_UPD_ChPL.pdf"},
 {"ids":[1372],"upd":"600000000779","brand":"porcilis porcoli","pdf":"data/ChPL/UPD/UPD_600000000779_Porcilis_Porcoli_Diluvac_Forte_UPD_ChPL.pdf"},
 {"ids":[1629],"upd":"600000004089","brand":"porcilis ar-t","pdf":"data/ChPL/UPD/UPD_600000004089_Porcilis_AR-T_DF_UPD_ChPL.pdf"},
]
def norm(s):
 s=unicodedata.normalize("NFKD",str(s or "").lower().replace("ł","l").replace(",","."));return " ".join("".join(c for c in s if not unicodedata.combining(c)).split())
def good(rel):
 p=ROOT/rel
 return p.is_file() and p.stat().st_size>1000 and p.open("rb").read(5)==b"%PDF-"
for g in GROUPS:
 assert good(g["pdf"]),g["pdf"]
 assert g["upd"] in g["pdf"]
 with fitz.open(ROOT/g["pdf"]) as d:
  assert not d.is_encrypted
  txt=norm(" ".join(page.get_text(sort=True) for page in d))
 assert g["brand"] in txt and len(txt)>1000,(g["brand"],"product name not found")
 if g.get("strength"):assert re.search(r"(?<!\d)5\s*mg\s*/\s*ml",txt)
 # Clinical validity of combined SPC need not be inferred merely from filename.
 if "melovem" in g["brand"]:
  assert (("charakterystyka" in txt and "przeciwwskazania" in txt) or ("summary of product characteristics" in txt and "contraindications" in txt)), "SPC title/sections missing"
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
columns=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def clinical_hash():
 h=hashlib.sha256()
 for row in db.execute("SELECT "+",".join('"'+c+'"' for c in columns)+" FROM variants ORDER BY id"):
  h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=clinical_hash()
report=[]
try:
 db.execute("BEGIN IMMEDIATE")
 for g in GROUPS:
  for id in g["ids"]:
   r=db.execute("""SELECT v.id,v.name,v.symbol,v.upd_id,v.pdf_path,p.name AS subiekt
    FROM variants v JOIN products p ON p.symbol=v.symbol WHERE v.id=?""",(id,)).fetchone()
   assert r,id
   n=norm(r["name"]+" "+r["subiekt"])
   assert g["brand"].split()[0] in n,(id,n)
   assert str(r["upd_id"])==g["upd"],(id,"UPD mismatch",r["upd_id"])
   if g.get("strength"):
    assert re.search(r"(?<!\d)5\s*mg\s*/\s*ml",n),(id,"strength mismatch",n)
    assert not re.search(r"(?<!\d)30\s*mg\s*/\s*ml",n)
   if g["brand"]=="porcilis pcv":assert "pcv" in n and "coliclos" not in n
   elif g["brand"]=="porcilis porcoli":assert "porcoli" in n
   elif g["brand"]=="porcilis ar-t":assert "ar-t" in n
   old=str(r["pdf_path"] or "")
   assert not good(old) or old==g["pdf"],(id,"existing valid PDF",old)
   if old==g["pdf"]:state="already_linked"
   else:
    db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(g["pdf"],))
    assert db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(g["pdf"],id)).rowcount==1
    state="linked"
   report.append({"id":id,"medicine":r["name"],"subiekt_name":r["subiekt"],"symbol":r["symbol"],"upd_id":r["upd_id"],"pdf":g["pdf"],"status":state})
 assert before==clinical_hash(),"clinical data changed"
 assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not db.execute("PRAGMA foreign_key_check").fetchall()
 db.commit()
except:db.rollback();raise
summary={"batch":"44b","counts":dict(Counter(r["status"] for r in report)),"linked":report,"clinical_unchanged":True,"sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()}
(OUT/"partia44b_powiazania.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf8")
print("PARTIA44B",json.dumps({k:v for k,v in summary.items() if k!="linked"},ensure_ascii=False))
db.close()
