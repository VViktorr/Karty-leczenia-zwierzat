#!/usr/bin/env python3
"""Repair 9 incorrect Draxxin -> Draxxin Plus PDF links from batch38 only."""
from pathlib import Path
import hashlib,json,sqlite3
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
WRONG="data/ChPL/RPL/RPL_44053_Draxxin_Plus_RPL_ChPL.pdf"
IDS=(1493,1494,1495,1496,1497,1498,2288,2289,2290)
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
columns=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def snapshot():
    h=hashlib.sha256()
    q="SELECT "+",".join('"'+col+'"' for col in columns)+" FROM variants ORDER BY id"
    for row in db.execute(q):
        h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
    return h.hexdigest()
old=snapshot()
found=[dict(r) for r in db.execute("SELECT id,name,rpl_id,upd_id,pdf_path FROM variants WHERE id IN ("+",".join("?"*len(IDS))+")",IDS)]
assert len(found)==len(IDS),(len(found),len(IDS))
assert {r["id"] for r in found}==set(IDS)
assert all(r["name"].strip().casefold()=="draxxin" and r["pdf_path"]==WRONG for r in found),found
try:
    db.execute("BEGIN IMMEDIATE")
    updated=db.execute("UPDATE variants SET pdf_path=NULL WHERE id IN ("+",".join("?"*len(IDS))+") AND pdf_path=? ",(*IDS,WRONG)).rowcount
    assert updated==9,updated
    assert snapshot()==old,"Unexpected changes to clinical data"
    assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
    assert not db.execute("PRAGMA foreign_key_check").fetchall()
    db.commit()
except:
    db.rollback()
    raise
out=ROOT/"data/audyt_brakujacych_pdf/partia38_korekta_draxxin.json"
out.write_text(json.dumps({"repaired_variants":list(IDS),"wrong_pdf_removed":WRONG,"reason":"Draxxin and Draxxin Plus are different products; name-only matching was unsafe","clinical_data_unchanged":True,"sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest()},ensure_ascii=False,indent=2),encoding="utf8")
print("REPAIR_OK 9 erroneous Draxxin Plus links removed; data and SQLite integrity preserved")
