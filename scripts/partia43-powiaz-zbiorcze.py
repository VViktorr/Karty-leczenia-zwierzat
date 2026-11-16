#!/usr/bin/env python3
"""Partia 43: safely link the 16 reviewed variants to 3 official Polish combined SPCs."""
from pathlib import Path
from collections import Counter
import hashlib,json,re,sqlite3,unicodedata
import fitz

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
audit=json.loads((OUT/"partia43_analiza_trzech_zbiorczych_pdf.json").read_text(encoding="utf8"))
PDFS={k:v["pdf"] for k,v in audit["documents"].items()}
IDS={"librela":(2120,2121),
     "loxicom":(971,972,973,2122,2123),
     "draxxin":(1493,1494,1495,1496,1497,1498,2288,2289,2290)}
def norm(s):
 s=unicodedata.normalize("NFKD",str(s or "").lower().replace("ł","l"))
 return " ".join("".join(c for c in s if not unicodedata.combining(c)).replace(",",".").split())
def has(s,phrase):return phrase in s
checks={
 "librela":["librela 20 mg roztwor do wstrzykiwan dla psow",
            "librela 30 mg roztwor do wstrzykiwan dla psow","bedinwetmab"],
 "loxicom":["loxicom 20 mg/ml roztwor do wstrzykiwan dla bydla",
            "loxicom 0.5 mg/ml zawiesina doustna dla kotow","meloksykam"],
 "draxxin":["draxxin 100 mg/ml roztwor do wstrzykiwan dla bydla",
            "draxxin 25 mg/ml roztwor do wstrzykiwan dla swin","tulatromycyna"]}
for brand,path in PDFS.items():
 p=ROOT/path
 assert p.is_file() and p.open("rb").read(5)==b"%PDF-"
 assert hashlib.sha256(p.read_bytes()).hexdigest()==audit["documents"][brand]["pdf_sha256"],"Changed PDF; rerun audit"
 with fitz.open(p) as d:
  assert not d.is_encrypted
  content=norm(" ".join(page.get_text(sort=True) for page in d))
 assert all(has(content,q) for q in checks[brand]),(brand,"SPC product, strength, form or species mismatch")
 assert "charakterystyka" in content[:20000] and "przeciwwskazania" in content and "karencji" in content
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
clinical_columns=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def clinical_hash():
 h=hashlib.sha256()
 query="SELECT "+",".join('"'+col+'"' for col in clinical_columns)+" FROM variants ORDER BY id"
 for r in db.execute(query):
  h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=clinical_hash()
report=[]
try:
 db.execute("BEGIN IMMEDIATE")
 for brand,ids in IDS.items():
  for id in ids:
   r=db.execute("""SELECT v.id,v.name,v.symbol,v.upd_id,v.rpl_id,v.pdf_path,p.name AS subiekt
       FROM variants v JOIN products p ON p.symbol=v.symbol WHERE v.id=?""",(id,)).fetchone()
   assert r,id
   record=dict(r)
   assert brand in norm(r["name"]+" "+r["subiekt"]),record
   name=norm(r["name"]+" "+r["subiekt"])
   if brand=="draxxin":
    assert "draxxin plus" not in name and str(r["upd_id"])=="600000004232",record
    # Subiekt 50/100 mL are packaging volumes, not drug concentrations.
    # The official combined SPC contains both 100 and 25 mg/ml;
    # the pre-existing UPD identifier represents the same brand, never Draxxin Plus.
   elif brand=="librela":
    assert str(r["upd_id"]) in ("600000000910","600000000383"),record
    assert ("20mg/ml" if id==2120 else "30mg/ml") in name,record
   elif brand=="loxicom":
    assert str(r["upd_id"]) in ("600000004200","600000003472"),record
    if id in (971,972,973):
     assert "20 mg/ml" in name and ("100ml" in name or "100 ml" in name),record
    else:
     assert "0.5 mg/ml" in name and "kot" in name and "oral" in name,record
   old=str(r["pdf_path"] or "").strip()
   expected=PDFS[brand]
   if old and (ROOT/old).is_file():
    assert old==expected,(id,"Existing valid PDF must never be overwritten",old)
    status="already_correct"
   else:
    db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(expected,))
    assert db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(expected,id)).rowcount==1
    status="linked"
   report.append({"id":id,"medicine":r["name"],"subiekt":r["subiekt"],"symbol":r["symbol"],"brand":brand,"status":status,"pdf":expected})
 assert clinical_hash()==before,"Clinical fields unexpectedly changed"
 assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not db.execute("PRAGMA foreign_key_check").fetchall()
 db.commit()
except:
 db.rollback()
 raise
result={"partia":43,"statuses":dict(Counter(r["status"] for r in report)),
        "pdf_paths":PDFS,"clinical_data_unchanged":True,
        "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
        "linked_variants":report}
(OUT/"partia43_powiazania_wynik.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
print("PARTIA43_ATTACH",json.dumps({"statuses":result["statuses"],"clinical_unchanged":True,"sqlite_sha256":result["sqlite_sha256"]},ensure_ascii=False))
db.close()
