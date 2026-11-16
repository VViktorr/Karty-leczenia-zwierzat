#!/usr/bin/env python3
"""Manual batch 37: attach inspected ChPL PDFs, never change pharmacological data."""
from pathlib import Path
from collections import Counter
import hashlib, json, re, sqlite3, unicodedata
import pymupdf

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"partia37_raport"
OUT.mkdir(exist_ok=True)
PDFS={
 "bravecto":"data/ChPL/UPD/Bravecto all_134628_pl.pdf",
 "bioestrovet":"data/ChPL/UPD/Charakterystyka-18295-20180730000000-1397_W-20181016000243.pdf",
 "circovac":"data/ChPL/UPD/circovac_121949_pl.pdf",
 "advocate":"data/ChPL/UPD/ema-combined-v76-advocate-vnra19252-2025-04-03-pl.pdf",
 "contacera":"data/ChPL/UPD/UPD_600000000220_Contacera_20_mg_ml_roztwór_do_wstrzykiwań_dla_UPD_ChPL.pdf"
}
GROUPS={
 "bravecto":[2111,2112,2113,2114,2115,2487,2453],
 "bioestrovet":[1535,1536,1537,1748,1749,1750],
 "circovac":[921,922],
 "advocate":[2109],
 "contacera":[834,835,836,2116]
}
def norm(s):
    s=unicodedata.normalize("NFKD",s.casefold())
    return re.sub(r"\s+"," ","".join(ch for ch in s if not unicodedata.combining(ch))).strip()
def match(s,pattern):
    return bool(re.search(pattern,norm(s)))
def read_pdf(rel):
    p=ROOT/rel
    assert p.is_file(),f"Not uploaded: {rel}"
    raw=p.read_bytes()
    assert raw[:5]==b"%PDF-" and len(raw)>15000 and b"%%EOF" in raw[-4096:],rel
    with pymupdf.open(p) as d:
        assert not d.is_encrypted and len(d)>=2,rel
        t="\n".join(page.get_text() for page in d)
        return norm(t),len(d),hashlib.sha256(raw).hexdigest(),len(t)
verified={}
for key,rel in PDFS.items():
    t,n,sha,chars=read_pdf(rel)
    if key=="bravecto":
        ok=all(match(t,r"bravecto.{0,160}"+x) for x in ["112,5","250","500","1000","1400"]) and ("nakrapian" in t or "spot-on" in t) and ("tablet" in t)
    elif key=="bioestrovet":
        # 6 page scan: user-supplied official RPL 18295; first page visually inspected.
        ok=n==6 and chars<400 and "18295" in rel
    elif key=="circovac":
        ok="circovac" in t and ("charakterystyka" in t)
    elif key=="advocate":
        ok="advocate" in t and ("400 mg" in t or "400mg" in t) and ("100 mg" in t or "100mg" in t) and "4 ml" in t
    else:
        ok="contacera" in t and "20 mg/ml" in t
    verified[key]={"file":rel,"pages":n,"sha256":sha,"text_chars":chars,"accepted":bool(ok)}
    if key=="contacera":
        verified[key]["also_15mg"]=bool("15 mg/ml" in t and "contacera" in t)
assert all(x["accepted"] for k,x in verified.items() if k!="contacera"),verified
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
clinical=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def snapshot():
    query="SELECT "+",".join('"'+c+'"' for c in clinical)+" FROM variants ORDER BY id"
    return hashlib.sha256(json.dumps([list(row) for row in db.execute(query)],ensure_ascii=False,default=str).encode()).hexdigest()
clinical_before=snapshot()
before=hashlib.sha256(DB.read_bytes()).hexdigest()
allbefore={r["id"]:dict(r) for r in db.execute("SELECT id,name,rpl_id,upd_id,pdf_path,scope FROM variants WHERE id IN ("+",".join("?" for _ in sum(GROUPS.values(),[]))+")",sum(GROUPS.values(),[]))}
assert len(allbefore)==len(set(sum(GROUPS.values(),[]))),len(allbefore)
actions=[]
def appropriate(key,r):
    name=norm(r["name"] or "")
    if key=="bravecto":
        if "bravecto" not in name:return False
        if r["id"] in {2111,2112,2113,2114,2115}:return "tab" in name and any(x in name for x in ["112,5","250","500","1000","1400"])
        return ("spot" in name or "nakrapian" in name)
    if key=="bioestrovet":return "bioestrovet" in name and str(r["rpl_id"])=="18295"
    if key=="circovac":return "circovac" in name
    if key=="advocate":return "advocate" in name and "400" in name
    if key=="contacera":
        if "contacera" not in name:return False
        if r["id"]==2116:return verified[key].get("also_15mg",False) and "15" in name
        return verified[key]["accepted"] and "20" in name
    return False
try:
    db.execute("BEGIN IMMEDIATE")
    for key,ids in GROUPS.items():
        for vid in ids:
            r=allbefore[vid]
            current=str(r["pdf_path"] or "").strip()
            exists=current and (ROOT/current).is_file() and (ROOT/current).open("rb").read(5)==b"%PDF-"
            if exists:
                actions.append({"id":vid,"name":r["name"],"action":"existing_valid_pdf_kept","path":current})
                continue
            if not verified[key]["accepted"] or not appropriate(key,r):
                actions.append({"id":vid,"name":r["name"],"action":"not_attached_validation","document":PDFS[key]})
                continue
            rel=PDFS[key]
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)",(rel,))
            changed=db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(rel,vid)).rowcount
            assert changed==1,vid
            actions.append({"id":vid,"name":r["name"],"action":"linked","path":rel})
    assert snapshot()==clinical_before, "Clinical/identity data unexpectedly changed"
    assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
    assert not db.execute("PRAGMA foreign_key_check").fetchall()
    db.commit()
except:
    db.rollback()
    raise
after=hashlib.sha256(DB.read_bytes()).hexdigest()
linked=sum(a["action"]=="linked" for a in actions)
total_missing=db.execute("SELECT COUNT(*) FROM variants WHERE (scope IS NULL OR lower(scope) NOT LIKE '%archiwal%') AND (pdf_path IS NULL OR trim(pdf_path)='')").fetchone()[0]
report={"phase":37,"approved_pdf":verified,"actions":actions,"new_links":linked,"existing_kept":sum(a["action"]=="existing_valid_pdf_kept" for a in actions),"unlinked_validation":sum(a["action"]=="not_attached_validation" for a in actions),"clinical_unchanged":True,"db_sha_before":before,"db_sha_after":after,"remaining_null_pdf_links":total_missing}
(OUT/"result.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA37_RESULT",json.dumps({k:v for k,v in report.items() if k not in ("actions","approved_pdf")},ensure_ascii=False))
for k,v in verified.items():print("DOCUMENT",k,json.dumps(v,ensure_ascii=False))
for a in actions:print("VARIANT",json.dumps(a,ensure_ascii=False))
db.close()
