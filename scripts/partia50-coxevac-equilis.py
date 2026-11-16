#!/usr/bin/env python3
"""Batch 50. Verify newly uploaded PDF contents before restoring exact Equilis and Coxevac rows.
Clinical fields and all unrelated variants are immutable.
"""
import sqlite3,hashlib,json,re,unicodedata
from pathlib import Path
import fitz

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf/partia50_equilis_coxevac.json"
FILES=[
 "data/ChPL/UPD/anx_131897_pl.pdf",
 "data/ChPL/UPD/anx_12972_pl.pdf",
 "data/ChPL/UPD/OI-Equilis-Prequenza-Te-ema-combined-v95_equilisprequenzate-vra0019g-pl.94e71c7511cd927eb6f9e2df36154bba.pdf"
]
def norm(value):
 t=unicodedata.normalize("NFKD",str(value or "").casefold().replace("ł","l"))
 return " ".join("".join(c for c in t if not unicodedata.combining(c)).split())
docs=[]
for rel in FILES:
 p=ROOT/rel
 item={"path":rel}
 try:
  assert p.is_file() and p.stat().st_size>5000 and p.open("rb").read(5)==b"%PDF-", "No valid PDF header or size"
  with fitz.open(p) as pdf:
   assert not pdf.is_encrypted and len(pdf)>=2
   content=norm(" ".join(page.get_text(sort=True) for page in pdf))
   item["pages"]=len(pdf)
  item["sha256"]=hashlib.sha256(p.read_bytes()).hexdigest()
  item["title_extract"]=content[:900]
  item["has_spc"]=("charakterystyka" in content and "przeciwwskazania" in content)
  # Match explicit product name, not just one component (Equilis Prequenza without -Te is different).
  equilis=bool(re.search(r"\bequilis\s+prequenza\s*[-–]\s*te\b",content))
  coxevac=bool(re.search(r"\bcoxevac\b",content))
  item["contains_equi_prequenza_te"]=equilis
  item["contains_coxevac"]=coxevac
  item["accepted_product"]=("Equilis Prequenza-Te" if equilis and not coxevac else "Coxevac" if coxevac and not equilis else None) if item["has_spc"] else None
  if not item["accepted_product"]:item["reason"]="Not an unambiguous Polish SPC for requested product"
 except Exception as e:
  item["accepted_product"]=None
  item["reason"]=str(e)
 docs.append(item)
accepted={name:[x for x in docs if x["accepted_product"]==name] for name in ("Equilis Prequenza-Te","Coxevac")}
# Prefer product-labelled combined ChPL for Equilis, exact newest upload for Coxevac if good.
def select(name):
 opts=accepted[name]
 if not opts:return None
 if name=="Equilis Prequenza-Te":
  opts.sort(key=lambda x:(0 if "OI-Equilis-Prequenza-Te" in x["path"] else 1,x["path"]))
 if name=="Coxevac":
  opts.sort(key=lambda x:(0 if "anx_131897" in x["path"] else 1,x["path"]))
 return opts[0]
selected={name:select(name) for name in accepted}

db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
colnames=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"] not in ("scope","pdf_path")]
def protected_hash():
 h=hashlib.sha256()
 q="SELECT "+",".join('"'+c+'"' for c in colnames)+" FROM variants ORDER BY id"
 for row in db.execute(q):h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=protected_hash()
target={
 "Equilis Prequenza-Te":{73:("equilis prequenza-te","600000000720")},
 "Coxevac":{917:("coxevac","600000003433"),918:("coxevac","600000003433")}
}
changes=[]
try:
 db.execute("BEGIN IMMEDIATE")
 for product,mapping in target.items():
  doc=selected[product]
  if not doc:continue
  path=doc["path"]
  for id,(name,upd) in mapping.items():
   r=db.execute("SELECT id,name,upd_id,scope,pdf_path FROM variants WHERE id=?",(id,)).fetchone()
   assert r and norm(name) in norm(r["name"]) and str(r["upd_id"])==upd,(product,id,"identity mismatch")
   assert r["pdf_path"] is None and (r["scope"] is None or "archiwal" in norm(r["scope"])), (id,"unexpected existing status/path")
  db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(path,))
  for id in mapping:
   assert db.execute("UPDATE variants SET scope=NULL,pdf_path=? WHERE id=?",(path,id)).rowcount==1
   changes.append({"id":id,"product":product,"pdf":path,"status":"active"})
 assert before==protected_hash(),"Protected clinical fields changed"
 assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not db.execute("PRAGMA foreign_key_check").fetchall()
 db.commit()
except:
 db.rollback()
 raise
report={"partia":50,"new_files_examined":len(docs),"documents":docs,
 "selected_documents":{k:v["path"] if v else None for k,v in selected.items()},
 "changes":changes,"updated_variants":len(changes),"protected_clinical_fields_unchanged":True,
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()}
OUT.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA50",json.dumps(report,ensure_ascii=False))
db.close()
