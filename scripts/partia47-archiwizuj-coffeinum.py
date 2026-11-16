#!/usr/bin/env python3
"""Partia 47: archive precisely 28 Coffeinum Bioveta variants without PDF attachment.
No deletes, no clinical changes. Also verify Sputop linked PDF / uploaded duplicate.
"""
from pathlib import Path
from collections import Counter
import hashlib, json, sqlite3
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
ID_SET=set(range(2455,2483))
UPD="600000060227"
SPUTOP_LINK="data/ChPL/RPL/RPL_20339_Sputop_Spot_On_Zoetis_ChPL_2024-03.pdf"
SPUTOP_UPLOAD="data/ChPL/RPL/sputop-spot-on_qrd_2024.03_dec.pdf"
def good_pdf(rel):
 if not rel:return False
 p=ROOT/str(rel)
 try:return p.is_file() and p.stat().st_size>=1000 and p.open("rb").read(5)==b"%PDF-"
 except (OSError,ValueError):return False
assert good_pdf(SPUTOP_LINK) and good_pdf(SPUTOP_UPLOAD), "Sputop PDFs not valid"
assert hashlib.sha256((ROOT/SPUTOP_LINK).read_bytes()).digest()==hashlib.sha256((ROOT/SPUTOP_UPLOAD).read_bytes()).digest(), "Uploaded Sputop is not identical to linked verified PDF"
con=sqlite3.connect(DB)
con.row_factory=sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
variant_cols=[r["name"] for r in con.execute("PRAGMA table_info(variants)")]
assert "scope" in variant_cols and "pdf_path" in variant_cols
protected=[c for c in variant_cols if c not in ("scope","pdf_path")]
def protected_hash():
 h=hashlib.sha256()
 query="SELECT "+",".join('"'+c+'"' for c in protected)+" FROM variants ORDER BY id"
 for row in con.execute(query):
  h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=protected_hash()
coffeinum=[dict(r) for r in con.execute("""
 SELECT v.id,v.name,v.symbol,v.rpl_id,v.upd_id,v.pdf_path,v.scope,p.name AS subiekt
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.upd_id=? OR lower(v.name) LIKE '%coffeinum bioveta%'
 ORDER BY v.id
""",(UPD,))]
assert len(coffeinum)==28,("unexpected Coffeinum scope",len(coffeinum))
assert {r["id"] for r in coffeinum}==ID_SET, "Unexpected variant IDs"
assert {r["symbol"] for r in coffeinum}=={"258","05094"}
assert all("coffeinum bioveta" in r["name"].lower() and str(r["upd_id"])==UPD for r in coffeinum)
assert all(r["scope"] is None or "archiwal" in str(r["scope"]).lower() for r in coffeinum),"Existing special scope: stop"
sputop=[dict(r) for r in con.execute("""SELECT v.id,v.name,v.pdf_path,v.rpl_id,v.upd_id
 FROM variants v WHERE v.pdf_path=? ORDER BY v.id""",(SPUTOP_LINK,))]
assert len(sputop)==12,("Sputop missing links",len(sputop))
assert all(r["rpl_id"]=="100203390" and r["upd_id"]=="600000084331" for r in sputop)
try:
 con.execute("BEGIN IMMEDIATE")
 for r in coffeinum:
  assert con.execute("UPDATE variants SET scope='archiwalny',pdf_path=NULL WHERE id=?",(r["id"],)).rowcount==1
 assert before==protected_hash(),"Protected variant fields changed"
 assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not con.execute("PRAGMA foreign_key_check").fetchall()
 con.commit()
except:
 con.rollback()
 raise
after=[dict(r) for r in con.execute("SELECT id,scope,pdf_path FROM variants WHERE id BETWEEN 2455 AND 2482 ORDER BY id")]
assert len(after)==28 and all(r["scope"]=="archiwalny" and r["pdf_path"] is None for r in after)
active=[dict(r) for r in con.execute("""SELECT id,pdf_path FROM variants
 WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'""")]
missing=sum(not good_pdf(r["pdf_path"]) for r in active)
report={
 "partia":47,"operation":"archive_Coffeinum_Bioveta_without_PDF",
 "archived_variants":28,"archived_variant_ids":sorted(ID_SET),
 "subiekt_cards":[{"symbol":symbol,"name":sorted({r["subiekt"] for r in coffeinum if r["symbol"]==symbol})[0]} for symbol in sorted({r["symbol"] for r in coffeinum})],
 "coffeinum_upd_id":UPD,"scope":"archiwalny","pdf_path":None,
 "sputop_valid_pdf_link":SPUTOP_LINK,"sputop_uploaded_original":SPUTOP_UPLOAD,
 "sputop_identical":True,"sputop_linked_variants":len(sputop),
 "protected_variant_fields_unchanged":True,
 "active_variants":len(active),"remaining_missing_active_pdf":missing,
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "note":"Only variants.scope and variants.pdf_path touched for 28 Coffeinum rows. No PDF files deleted; no data on treatment, doses, withdrawals or registration changed."
}
OUT.mkdir(exist_ok=True)
(OUT/"partia47_archiwizacja_coffeinum_sputop.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA47",json.dumps({k:v for k,v in report.items() if k!="archived_variant_ids"},ensure_ascii=False),flush=True)
con.close()
