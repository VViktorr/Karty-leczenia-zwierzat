#!/usr/bin/env python3
"""Batch 51b: restore exact archived Velactis and Kriptazen variants with verified
Polish ChPLs. Do not alter dosing, indications, withdrawal, or other clinical data.
Halofusol leaflet remains unassigned.
"""
from pathlib import Path
import fitz,hashlib,json,sqlite3,re,unicodedata
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf/partia51b_velactis_kriptazen_powiazania.json"
ASSIGN=[
 {"id":2314,"brand":"velactis","upd":"700000141165","subiekt":"VELACTIS1,12MG/ML 5 ML 105A4","strength":"1,12 mg/ml","file":"data/ChPL/UPD/anx_133354_pl.pdf"},
 {"id":2305,"brand":"kriptazen","upd":"600000004142","subiekt":"KRIPTAZEN 980 ML roztwór","strength":"0,5 mg/ml","file":"data/ChPL/UPD/anx_168157_pl.pdf"}
]
def norm(s):
 t=unicodedata.normalize("NFKD",str(s or "").casefold().replace("ł","l").replace(",","."))
 return " ".join("".join(c for c in t if not unicodedata.combining(c)).split())
docs=[]
for a in ASSIGN:
 p=ROOT/a["file"]
 assert p.is_file() and p.stat().st_size>10000 and p.open("rb").read(5)==b"%PDF-"
 with fitz.open(p) as d:
  assert not d.is_encrypted and len(d)>=2
  txt=norm(" ".join(pg.get_text(sort=True) for pg in d))
  pages=len(d)
 assert a["brand"] in txt
 assert norm(a["strength"]) in txt, (a["brand"],"strength absent")
 assert "charakterystyka" in txt and "przeciwwskazania" in txt, (a["brand"],"ChPL sections absent")
 assert ("bydla" in txt or "cielat" in txt),a["brand"]
 docs.append({"id":a["id"],"product":a["brand"],"pdf":a["file"],"pages":pages,"sha256":hashlib.sha256(p.read_bytes()).hexdigest()})
# Validate other uploaded leaflet was not mistakenly used as Kriptazen.
HALO="data/ChPL/UPD/Ulotka-44168-2020-10-23-2702_W-2021-04-14.pdf"
assert (ROOT/HALO).is_file()
with fitz.open(ROOT/HALO) as d:
 t=norm(" ".join(pg.get_text(sort=True) for pg in d))
assert "halofusol" in t and "kriptazen" not in t
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
cols=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"] not in ("scope","pdf_path")]
def protected_hash():
 h=hashlib.sha256()
 q="SELECT "+",".join('"'+c+'"' for c in cols)+" FROM variants ORDER BY id"
 for r in db.execute(q):h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=protected_hash()
try:
 db.execute("BEGIN IMMEDIATE")
 for a in ASSIGN:
  r=db.execute("""SELECT v.id,v.name,v.upd_id,v.scope,v.pdf_path,p.name AS subiekt FROM variants v JOIN products p ON v.symbol=p.symbol WHERE v.id=?""",(a["id"],)).fetchone()
  assert r and a["brand"] in norm(r["name"]),(a["id"],"wrong variant")
  assert str(r["upd_id"])==a["upd"] and r["subiekt"]==a["subiekt"],(a["id"],"identity mismatch")
  assert r["scope"]=="archiwalny" and r["pdf_path"] is None,(a["id"],"already changed")
  db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(a["file"],))
  assert db.execute("UPDATE variants SET scope=NULL,pdf_path=? WHERE id=?",(a["file"],a["id"])).rowcount==1
 assert before==protected_hash(), "Unrelated/clinical columns changed"
 assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not db.execute("PRAGMA foreign_key_check").fetchall()
 db.commit()
except:
 db.rollback()
 raise
report={"partia":"51b","updated_variants":2,"changes":docs,"skipped":{"path":HALO,"reason":"Halofusol leaflet, not a Kriptazen ChPL"},"clinical_data_unchanged":True,"sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()}
OUT.write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf-8")
print("PARTIA51B",json.dumps(report,ensure_ascii=False))
db.close()
