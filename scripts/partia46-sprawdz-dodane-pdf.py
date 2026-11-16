#!/usr/bin/env python3
"""Partia 46 — read-only audit of newly uploaded Sputop PDF and current links."""
import hashlib,json,sqlite3,re,unicodedata
from pathlib import Path
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
PDF="data/ChPL/RPL/RPL_20339_Sputop_Spot_On_RPL_ChPL.pdf"
p=ROOT/PDF
assert p.is_file(), f"Missing PDF: {p}"
print("SPUTOP_FILE",json.dumps({"size":p.stat().st_size,"first_bytes_hex":p.open("rb").read(64).hex()},ensure_ascii=False),flush=True)
assert p.stat().st_size>1000 and p.open("rb").read(5)==b"%PDF-", "File is not recognized as a PDF"
def norm(s):
 s=unicodedata.normalize("NFKD",str(s or "").casefold().replace("ł","l").replace(",","."))
 return " ".join("".join(c for c in s if not unicodedata.combining(c)).split())
with fitz.open(p) as f:
 assert not f.is_encrypted
 pages=len(f)
 txt=norm(" ".join(pg.get_text(sort=True) for pg in f))
assert "sputop" in txt, "Product name not found in PDF"
assert re.search(r"10\s*mg\s*/\s*ml",txt), "Strength 10 mg/ml not confirmed"
assert "charakterystyka" in txt and "przeciwwskazania" in txt, "Polish ChPL sections missing"
con=sqlite3.connect(f"file:{DB}?mode=ro",uri=True)
con.row_factory=sqlite3.Row
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
rows=[dict(r) for r in con.execute("""
 SELECT v.id,v.name AS variant_name,v.symbol,v.rpl_id,v.upd_id,v.pdf_path,p.name AS subiekt_name
 FROM variants v JOIN products p ON v.symbol=p.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%'""")]
sputop=[r for r in rows if r["pdf_path"]==PDF]
assert len(sputop)==12, f"Expected 12 exact-linked Sputop variants; got {len(sputop)}"
assert all(r["rpl_id"]=="100203390" and r["upd_id"]=="600000084331" for r in sputop)
assert all("sputop" in norm(r["variant_name"]) and "10 mg/ml" in norm(r["variant_name"]) for r in sputop)
assert all("spot on 250 ml" in norm(r["subiekt_name"]) for r in sputop)
def good(rel):
 if not str(rel or "").strip():return False
 q=ROOT/str(rel)
 try:return q.is_file() and q.stat().st_size>=1000 and q.open("rb").read(5)==b"%PDF-"
 except (OSError,ValueError):return False
missing=[r for r in rows if not good(r["pdf_path"])]
report={
 "partia":46,"read_only":True,"active_variants":len(rows),
 "pdf":PDF,"pdf_size_bytes":p.stat().st_size,"pdf_sha256":hashlib.sha256(p.read_bytes()).hexdigest(),"pdf_pages":pages,
 "pdf_verified_name_strength_polish_sections":True,"sputop_valid_links":len(sputop),
 "remaining_missing_or_invalid_paths":len(missing),
 "remaining_missing_ids":[r["id"] for r in missing],
 "current_sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "subiekt_symbols":sorted({r["symbol"] for r in sputop}),
 "subiekt_names":sorted({r["subiekt_name"] for r in sputop}),
 "clinical_data_unchanged":True,
 "note":"PDF header/size/existence checks on all links; textual content verification limited to newly uploaded Sputop."
}
(OUT/"partia46_nowy_sputop_audyt.json").write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf-8")
print("PARTIA46",json.dumps({k:v for k,v in report.items() if k!="remaining_missing_ids"},ensure_ascii=False))
con.close()
