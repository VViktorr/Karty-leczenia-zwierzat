#!/usr/bin/env python3
"""Batch 48b: correct Vectra 3D lower weight group, validating combined Polish ChPL.
Only reactivates variant 2727 (catalog old description 1-4 kg; approved SPC 1.5-4 kg).
"""
import hashlib,json,re,sqlite3
from pathlib import Path
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
PDF="data/ChPL/UPD/ema-combined-v2555-vectra3d-vra0000334831-2026-10-23-pl (1).pdf"
REPORT=ROOT/"data/audyt_brakujacych_pdf/partia48b_vectra_kategoria_wagowa.json"
with fitz.open(ROOT/PDF) as doc:
 assert len(doc)>=2 and not doc.is_encrypted
 txt=" ".join(p.get_text(sort=True) for p in doc).lower().replace("\xa0"," ")
 assert "vectra 3d" in txt and "charakterystyka" in txt
 assert re.search(r"1[.,]5\s*[–\-]\s*4\s*kg",txt), "SPC does not cover 1.5-4 kg category"
con=sqlite3.connect(DB)
con.row_factory=sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
cols=[r["name"] for r in con.execute("PRAGMA table_info(variants)") if r["name"] not in ("scope","pdf_path")]
def protected():
 h=hashlib.sha256()
 for r in con.execute("SELECT "+",".join('"'+c+'"' for c in cols)+" FROM variants ORDER BY id"):
  h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=protected()
r=con.execute("""SELECT v.id,v.name,v.symbol,v.upd_id,v.rpl_id,v.scope,v.pdf_path,p.name AS subiekt
FROM variants v JOIN products p ON p.symbol=v.symbol WHERE v.id=2727""").fetchone()
assert r and "vectra 3d" in r["name"].lower()
assert r["subiekt"]=="VECTRA 3D DLA PSÓW 1-4 kg 1 pipeta"
assert r["scope"]=="archiwalny" and r["pdf_path"] is None
# The old catalog rounded lower boundary from 1.5 kg down to 1 kg. Do not silently
# rewrite the product label/clinical attributes. Flag this for later review.
try:
 con.execute("BEGIN IMMEDIATE")
 con.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(PDF,))
 assert con.execute("UPDATE variants SET scope=NULL,pdf_path=? WHERE id=?",(PDF,2727)).rowcount==1
 assert before==protected()
 assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not con.execute("PRAGMA foreign_key_check").fetchall()
 con.commit()
except:
 con.rollback()
 raise
report={
 "partia":"48b","variant_id":2727,"medicine":"Vectra 3D",
 "catalog_subiekt_name":r["subiekt"],"catalog_weight_class":"1-4 kg",
 "document_authorized_weight_class":"1.5-4 kg",
 "warning":"Catalog label 1-4 kg may include 1-1.5 kg outside the authorized SPC; do not change dosing or label without reviewing primary Subiekt data.",
 "pdf":PDF,"pdf_sha256":hashlib.sha256((ROOT/PDF).read_bytes()).hexdigest(),
 "reactivated":True,"protected_clinical_fields_unchanged":True,
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()
}
REPORT.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA48B",json.dumps(report,ensure_ascii=False))
con.close()
