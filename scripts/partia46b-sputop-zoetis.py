#!/usr/bin/env python3
"""Partia 46b: fetch publisher-hosted Polish ChPL replacing invalid XMP file.
For Sputop RPL 20339 only; leave all clinical fields untouched.
"""
import hashlib,json,sqlite3,re,urllib.request
from pathlib import Path
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
SOURCE="https://www.zoetis.com.pl/_locale-assets/charakterystyki-produktow/sputop-spot-on_qrd_2024.03_dec.pdf"
TARGET="data/ChPL/RPL/RPL_20339_Sputop_Spot_On_Zoetis_ChPL_2024-03.pdf"
OLD="data/ChPL/RPL/RPL_20339_Sputop_Spot_On_RPL_ChPL.pdf"
DEST=ROOT/TARGET
assert (ROOT/OLD).is_file()
assert (ROOT/OLD).read_bytes()[:5]!=b"%PDF-","Old link already repaired; re-evaluate"
if not DEST.exists():
 req=urllib.request.Request(SOURCE,headers={"User-Agent":"Mozilla/5.0","Accept":"application/pdf"})
 with urllib.request.urlopen(req,timeout=45) as response:
  assert response.geturl().startswith("https://www.zoetis.com.pl/")
  contents=response.read(10_000_001)
 assert contents[:5]==b"%PDF-" and 5000<len(contents)<10_000_000, "Publisher response not a PDF"
 DEST.write_bytes(contents)
assert DEST.read_bytes()[:5]==b"%PDF-"
with fitz.open(DEST) as doc:
 assert not doc.is_encrypted
 pages=len(doc)
 text=" ".join(p.get_text(sort=True) for p in doc).lower().replace(","," ").replace("\n"," ")
 assert pages>=2
 assert "sputop spot on" in text and re.search(r"10\s*mg\s*/\s*ml",text), "Product or strength mismatch"
 assert "bydła i owiec" in text and "deltametryna" in text
 assert "charakterystyka" in text and "przeciwwskazania" in text
con=sqlite3.connect(DB)
con.row_factory=sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
cols=[r["name"] for r in con.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def protected_hash():
 h=hashlib.sha256()
 for r in con.execute("SELECT "+",".join('"'+c+'"' for c in cols)+" FROM variants ORDER BY id"):
  h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=protected_hash()
rows=[dict(r) for r in con.execute("""SELECT v.id,v.name,v.symbol,v.rpl_id,v.upd_id,v.pdf_path,p.name as subiekt
 FROM variants v JOIN products p ON v.symbol=p.symbol WHERE v.pdf_path=? ORDER BY v.id""",(OLD,))]
assert len(rows)==12,("Unexpected matching variant count",len(rows))
for r in rows:
 assert r["rpl_id"]=="100203390" and r["upd_id"]=="600000084331"
 assert "sputop" in r["name"].lower() and "10 mg/ml" in r["name"].lower()
 assert "spot on 250 ml" in r["subiekt"].lower()
try:
 con.execute("BEGIN IMMEDIATE")
 con.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(TARGET,))
 for r in rows:
  assert con.execute("UPDATE variants SET pdf_path=? WHERE id=? AND pdf_path=?",(TARGET,r["id"],OLD)).rowcount==1
 assert before==protected_hash(),"Clinical data unexpectedly changed"
 assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not con.execute("PRAGMA foreign_key_check").fetchall()
 con.commit()
except:
 con.rollback();raise
report={"partia":"46b","brand":"Sputop Spot On","publisher":"Zoetis Polska",
 "source_url":SOURCE,"pdf_path":TARGET,"pdf_sha256":hashlib.sha256(DEST.read_bytes()).hexdigest(),
 "pages":pages,"linked":len(rows),"clinical_unchanged":True,
 "source_note":"Polish ChPL hosted by responsible marketing authorization holder, not downloaded from government RPL API",
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "variants":[{"id":r["id"],"medicine":r["name"],"subiekt":r["subiekt"],"symbol":r["symbol"],"rpl_id":r["rpl_id"],"upd_id":r["upd_id"]} for r in rows]}
(OUT/"partia46b_sputop_poprawny_pdf.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA46B",json.dumps({k:v for k,v in report.items() if k!="variants"},ensure_ascii=False))
con.close()
