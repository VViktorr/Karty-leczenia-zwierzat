#!/usr/bin/env python3
"""Partia 48: verify ten uploaded PDFs, link only confirmed matches, archive ALL remaining
active missing/broken PDF variants without link. Preserve every clinical field.
"""
from pathlib import Path
from collections import Counter
import hashlib,json,sqlite3,re,unicodedata
import fitz

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(exist_ok=True)
PFX="data/ChPL/UPD/"
DOCS=[
 # document, expected product in actual text, the exact catalog names and UPD/RPL references
 ("charakterystyka-18793-20200131000000-2169_w-20200217000339.pdf","L-Spec-VMD",["l-spec-vmd"],None,{"18793"},[]),
 ("ema-combined-v115-meloxidyl-vnra27546-2026-07-30-pl.pdf","Meloxidyl",["meloxidyl"],"600000000034",set(),["20 mg/ml"]),
 ("ema-combined-v130-startvac-vra0000288186-2026-06-05-pl.pdf","Startvac",["startvac"],"600000004174",set(),[]),
 ("ema-combined-v2555-vectra3d-vra0000334831-2026-10-23-pl (1).pdf","Vectra 3D",["vectra 3d"],None,set(),[]),
 ("ema-combined-v3703-bovela-vra-0023-g-pl.pdf","Bovela",["bovela"],"600000000329",set(),[]),
 ("ema-combined-v41-locatim-vra0000322515-2026-07-02-pl.pdf","Locatim",["locatim"],"600000003386",set(),[]),
 ("ema-combined-v4344-exzolt-vra0017-2024-08-20-pl.pdf","Exzolt",["exzolt"],"600000000062",set(),["10 mg/ml"]),
 ("ema-combined-v54-pirsue-wepar-2025-04-10-pl.pdf","Pirsue",["pirsue"],"600000000514",set(),[]),
 ("ema-combined-v74-proteqflute-vra0033-2022-12-20-pl.pdf","ProteqFlu-Te",["proteqflu-te"],"600000992466",set(),[]),
 ("ulotka-19365-20190522000000-1794_w-20190606000623.pdf",None,[],None,set(),[]),
]
def norm(s):
 s=unicodedata.normalize("NFKD",str(s or "").lower().replace("ł","l").replace(",","."))
 return " ".join("".join(c for c in s if not unicodedata.combining(c)).split())
def good_pdf(path):
 try:
  p=ROOT/str(path) if path else None
  return bool(p and p.is_file() and p.stat().st_size>=1000 and p.open("rb").read(5)==b"%PDF-")
 except (OSError,ValueError):return False
def fulltext(path):
 with fitz.open(ROOT/path) as f:
  assert not f.is_encrypted
  return norm(" ".join(p.get_text(sort=True) for p in f)),len(f)
con=sqlite3.connect(DB)
con.row_factory=sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
cols=[r["name"] for r in con.execute("PRAGMA table_info(variants)")]
protected=[c for c in cols if c not in ("scope","pdf_path")]
def protected_hash():
 h=hashlib.sha256()
 q="SELECT "+",".join('"'+c+'"' for c in protected)+" FROM variants ORDER BY id"
 for r in con.execute(q):
  h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=protected_hash()
rows=[dict(r) for r in con.execute("""SELECT v.id,v.name,v.symbol,v.upd_id,v.rpl_id,v.pdf_path,v.scope,p.name AS subiekt
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%'
 ORDER BY v.id""")]
missing=[r for r in rows if not good_pdf(r["pdf_path"])]
assert len(missing)==45,("Missing count changed; abort rather than archive unexpectedly",len(missing))
unmatched={r["id"]:r for r in missing}
report={"partia":48,"initial_missing_active":len(missing),"documents":[],"linked":[],"archived":[],"skipped_documents":[]}
# Review every user-uploaded file before any database modifications.
approved=[]
for filename,brand,expected_names,upd,rpl,strengths in DOCS:
 path=PFX+filename
 item={"file":path,"brand":brand,"sha256":None,"pages":0,"status":"unreviewed"}
 try:
  assert good_pdf(path),"invalid pdf magic/size or missing"
  text,pages=fulltext(path)
  item.update({"sha256":hashlib.sha256((ROOT/path).read_bytes()).hexdigest(),"pages":pages,"text_opening":text[:260]})
  assert pages>=1 and len(text)>100, "PDF has insufficient readable text"
  if not brand:
   item["status"]="unassigned_paper_without_verified_catalog_product"
   report["skipped_documents"].append(item)
   report["documents"].append(item)
   continue
  assert all(norm(n) in text for n in expected_names),("Brand not found in document",brand)
  assert all(norm(n) in text for n in strengths),("Product strength not present",brand,strengths)
  assert ("charakterystyka" in text or "summary of product characteristics" in text or "ulotka" in text), "No clinical information heading"
  item["status"]="verified_brand_in_document"
  approved.append((path,brand,expected_names,upd,rpl,item,text))
 except Exception as exc:
  item.update({"status":"verification_failed","error":str(exc)})
  report["skipped_documents"].append(item)
 report["documents"].append(item)

try:
 con.execute("BEGIN IMMEDIATE")
 for path,brand,names,upd,rpl,item,text in approved:
  matches=[]
  for r in list(unmatched.values()):
   if not any(norm(n) in norm(r["name"]) for n in names):continue
   if upd is not None and str(r["upd_id"] or "")!=upd:continue
   if rpl and str(r["rpl_id"] or "") not in rpl:continue
   if brand=="Vectra 3D":
    # All sizes in the same brand's combined authorization. Ensure size class is present.
    label=norm(r["subiekt"])
    m=re.search(r"(\d+)\s*-\s*(\d+)\s*kg",label)
    if m:
     a,b=m.groups()
     if not re.search(r"(?<!\d)"+a+r"\s*[-–]\s*"+b+r"(?!\d)",text):
      item.setdefault("size_checks_unresolved",[]).append({"id":r["id"],"class":a+"-"+b})
      continue
    elif ">40" in r["subiekt"] or "1-4" in label:
     pass # Product is in verified unified SPC; check brand and complete formulation in report
    else:continue
   matches.append(r)
  for r in matches:
   assert con.execute("UPDATE variants SET pdf_path=? WHERE id=? AND (pdf_path IS NULL OR pdf_path='')",(path,r["id"])).rowcount==1,("unexpected existing broken pointer",r["id"])
   # Do not insert new documents tables; this catalog uses direct pdf_path mapping.
   unmatched.pop(r["id"])
   report["linked"].append({"id":r["id"],"name":r["name"],"symbol":r["symbol"],"subiekt":r["subiekt"],"pdf":path,"rpl_id":r["rpl_id"],"upd_id":r["upd_id"]})
  item["linked_count"]=len(matches)
 # For ALL remaining unlinked rows which lack proper PDF: archive without attachment.
 for r in unmatched.values():
  assert not good_pdf(r["pdf_path"]),("Should not archive a working PDF",r["id"])
  assert con.execute("UPDATE variants SET scope='archiwalny',pdf_path=NULL WHERE id=?",(r["id"],)).rowcount==1
  report["archived"].append({"id":r["id"],"name":r["name"],"symbol":r["symbol"],"subiekt":r["subiekt"],"old_pdf":r["pdf_path"],"upd_id":r["upd_id"],"rpl_id":r["rpl_id"]})
 assert before==protected_hash(),"Protected clinical fields modified"
 assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not con.execute("PRAGMA foreign_key_check").fetchall()
 con.commit()
except:
 con.rollback();raise
active=con.execute("SELECT id,pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'").fetchall()
assert not [r["id"] for r in active if not good_pdf(r["pdf_path"])],"There are active variants with broken PDF after archival"
report.update({"linked_count":len(report["linked"]),"archived_count":len(report["archived"]),
 "active_variants":len(active),"remaining_missing_active":0,"protected_clinical_fields_unchanged":True,
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()})
(OUT/"partia48_10pdf_archiwizacja_wynik.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA48",json.dumps({k:v for k,v in report.items() if k not in ("linked","archived","documents")},ensure_ascii=False),flush=True)
print("DOCS",json.dumps([{"file":d["file"],"status":d["status"],"linked_count":d.get("linked_count",0),"pages":d["pages"],"error":d.get("error")} for d in report["documents"]],ensure_ascii=False),flush=True)
print("LINKED_NAMES",json.dumps(dict(Counter(r["name"] for r in report["linked"])),ensure_ascii=False),flush=True)
print("ARCHIVED_NAMES",json.dumps(dict(Counter(r["name"] for r in report["archived"])),ensure_ascii=False),flush=True)
con.close()
