#!/usr/bin/env python3
"""Partia 49: restore Equilis Prequenza-Te ONLY after verifying the uploaded Polish ChPL.
Never edit clinical data.
"""
from pathlib import Path
import hashlib,json,re,sqlite3,unicodedata
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
PATH="data/ChPL/UPD/anx_159755_pl.pdf"
P=ROOT/PATH
REPORT=ROOT/"data/audyt_brakujacych_pdf/partia49_equilis_prequenza_te.json"
assert P.is_file() and P.stat().st_size>5000 and P.open("rb").read(5)==b"%PDF-"
def norm(s):
 t=unicodedata.normalize("NFKD",str(s or "").casefold().replace("ł","l"))
 return " ".join("".join(ch for ch in t if not unicodedata.combining(ch)).split())
with fitz.open(P) as pdf:
 assert not pdf.is_encrypted and len(pdf)>=2
 pages=len(pdf)
 txt=norm(" ".join(page.get_text(sort=True) for page in pdf))
 assert len(txt)>1000
 assert "equilis prequenza-te" in txt or "equilis prequenza te" in txt, "Wrong product: Equilis Prequenza-Te not in PDF"
 assert "charakterystyka" in txt and "przeciwwskazania" in txt, "Not a Polish ChPL"
con=sqlite3.connect(DB)
con.row_factory=sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
cols=[r["name"] for r in con.execute("PRAGMA table_info(variants)") if r["name"] not in ("scope","pdf_path")]
def hash_rest():
 h=hashlib.sha256()
 q="SELECT "+",".join('"'+c+'"' for c in cols)+" FROM variants ORDER BY id"
 for r in con.execute(q):h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=hash_rest()
r=con.execute("""SELECT v.id,v.name,v.scope,v.pdf_path,v.symbol,v.upd_id,v.rpl_id,p.name AS subiekt
FROM variants v JOIN products p ON p.symbol=v.symbol WHERE v.id=73""").fetchone()
assert r is not None and norm(r["name"])=="equilis prequenza-te"
assert r["upd_id"]=="600000000720" and r["rpl_id"]=="100253057"
assert norm(r["subiekt"]).startswith("equilis prequenza-te")
assert r["scope"]=="archiwalny" and r["pdf_path"] is None, (r["scope"],r["pdf_path"])
try:
 con.execute("BEGIN IMMEDIATE")
 con.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(PATH,))
 assert con.execute("UPDATE variants SET scope=NULL,pdf_path=? WHERE id=73",(PATH,)).rowcount==1
 assert before==hash_rest(),"Clinical data unexpectedly changed"
 assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not con.execute("PRAGMA foreign_key_check").fetchall()
 con.commit()
except:
 con.rollback();raise
report={"partia":49,"product":"Equilis Prequenza-Te","id":73,"symbol":r["symbol"],
"subiekt":r["subiekt"],"upd_id":r["upd_id"],"rpl_id":r["rpl_id"],
"status":"active","pdf":PATH,"pdf_pages":pages,"pdf_sha256":hashlib.sha256(P.read_bytes()).hexdigest(),
"other_variant_fields_unchanged":True,"sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()}
REPORT.write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf-8")
print("PARTIA49",json.dumps(report,ensure_ascii=False))
con.close()
