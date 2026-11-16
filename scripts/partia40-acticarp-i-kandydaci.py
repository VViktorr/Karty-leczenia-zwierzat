#!/usr/bin/env python3
"""Partia 40: inspect existing ChPL files, repair Acticarp only when verified,
and inventory possible multi-variant PDFs for all still-unlinked medicines.

Never modify anything other than variants.pdf_path and write audit files.
"""
from pathlib import Path
from collections import defaultdict,Counter
import csv, hashlib, json, re, sqlite3, unicodedata
import fitz

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(exist_ok=True)
ACT="data/ChPL/UPD/UPD_600000036571_ACTICARP_50_ML_P12155_UPD_ChPL.pdf"
ACT_IDS=(453,454,455,470,471,472)
def n(s):
    v=unicodedata.normalize("NFKD",str(s or "").casefold())
    return re.sub(r"\s+"," ","".join(c for c in v if not unicodedata.combining(c)).replace("–","-").replace(",",".")).strip()
def pdfpath(rel):
    p=ROOT/str(rel or "")
    if not str(rel or "").strip() or not p.is_file() or p.stat().st_size<1000:return False
    with p.open("rb") as f:return f.read(5)==b"%PDF-"
def pdftext(path):
    try:
        with fitz.open(path) as d:
            if d.is_encrypted or not d.page_count:return "",0
            return n("\n".join(p.get_text(sort=True) for p in d)),d.page_count
    except Exception:return "",0
def is_chpl(s):
    head=s[:15000]
    return bool(re.search(r"charakterystyka (?:weterynaryjnego )?produktu leczniczego",head)
     or re.search(r"charakterystyka produktu leczniczego weterynaryjnego",head)) and (
        "przeciwwskazania" in s and ("karencji" in s or "wskazania" in s))
def nearest_brand(s,b,limit=800):
    rex=re.compile(r"(?<![a-z0-9])"+re.escape(b)+r"(?![a-z0-9])")
    return [s[max(0,m.start()-70):m.end()+limit] for m in list(rex.finditer(s))[:100]]
def selected_brand(v):
    name=n(v)
    special=["simparica trio","simparica","bravecto","stronghold","acticarp","zuprevo","metacam","melovem","loxicom","naxcel","previcox","draxxin","pirsue","sileo","coff einum bioveta","coffeinum bioveta","tulaven","zactran","exzolt","bovela","startvac","coxevac","effydral","sputop","porcilis coli","porcilis pcv","ingelvac circoflex"]
    for brand in special:
        if name==brand or name.startswith(brand+" "):return brand
    return name.split(" ")[0]
def name_hit(s,brand):
    positions=nearest_brand(s,brand)
    if brand=="simparica":
        positions=[p for p in positions if not re.search(r"simparica\s+trio",p[:120])]
    if brand=="draxxin":
        positions=[p for p in positions if not re.search(r"draxxin\s+plus",p[:120])]
    return positions
def strength(v):
    return [(a,b) for a,b in re.findall(r"(?<!\w)(\d+(?:[.,]\d+)?)\s*(mg/ml|mcg/ml|mg|mcg)\b",n(v))]
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
act=[dict(x) for x in db.execute("SELECT id,name,symbol,upd_id,rpl_id,pdf_path FROM variants WHERE id IN ("+",".join("?"*len(ACT_IDS))+")",ACT_IDS)]
assert {x["id"] for x in act}==set(ACT_IDS)
assert all("acticarp" in n(x["name"]) and n(x["upd_id"])=="600000036571" for x in act),act
preserve_cols=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def clinicalhash():
    h=hashlib.sha256()
    for row in db.execute("SELECT "+",".join('"'+c+'"' for c in preserve_cols)+" FROM variants ORDER BY id"):
        h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
    return h.hexdigest()
before=clinicalhash()
text,pages=pdftext(ROOT/ACT)
# The title 'UPD_ChPL' is not evidence. The PDF must actually contain a full SPC
# and "Acticarp", concentration 50mg/ml, injectable form, relevant cattle indication.
windows=name_hit(text,"acticarp")
valid=bool(is_chpl(text) and windows
           and any(re.search(r"50\s*mg\s*/\s*ml",w) for w in windows)
           and any(("wstrzykiwan" in w or "injection" in w) for w in windows)
           and any(("bydl" in w or "cattle" in w) for w in windows)
           and not re.search(r"vetcarp",text[:1200]))
reason="verified_act icarp_50mg_injection_cattle".replace(" ","") if valid else (
    "scan_needs_visual_review" if len(text)<300 else
    "not_verified_as_matching_official_chpl")
links=[]
if valid:
    try:
        db.execute("BEGIN IMMEDIATE")
        for variant in act:
            old=str(variant["pdf_path"] or "").strip()
            if old and pdfpath(old):
                links.append({"id":variant["id"],"action":"existing_kept","path":old})
                continue
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(ACT,))
            db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(ACT,variant["id"]))
            links.append({"id":variant["id"],"action":"linked","path":ACT})
        assert clinicalhash()==before
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
    except:
        db.rollback()
        raise
assert clinicalhash()==before
# Read-only inventory of still missing PDF variants. Build name-first candidate index:
# no auto-assignment beyond the six explicitly validated Acticarp variants.
rows=[dict(r) for r in db.execute("""SELECT v.id,v.symbol,v.name,v.rpl_id,v.upd_id,v.pdf_path,p.name AS subiekt
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%' ORDER BY v.name,v.id""")]
missing=[r for r in rows if not pdfpath(r["pdf_path"])]
brands={selected_brand(r["name"]) for r in missing}
index=defaultdict(list)
pdfcount=0
for p in sorted((ROOT/"data/ChPL").rglob("*.pdf")):
    if not pdfpath(p.relative_to(ROOT)):continue
    pdfcount+=1
    s,npages=pdftext(p)
    if len(s)<300:continue
    full=is_chpl(s)
    for brand in brands:
        w=name_hit(s,brand)
        if not w:continue
        index[brand].append({"path":p.relative_to(ROOT).as_posix(),"is_chpl":full,"pages":npages,
                              "near_name":w})
data=[]
for r in missing:
    b=selected_brand(r["name"])
    strengths=strength(r["name"])
    candidate=[]
    for doc in index.get(b,[]):
        found=[]
        for a,u in strengths:
            pattern=re.compile(r"(?<!\d)"+re.escape(a.replace(",","." )).replace(r"\.",r"[.,]")+r"\s*"+re.escape(u)+r"\b")
            found.append(any(pattern.search(t) for t in doc["near_name"]))
        candidate.append({"path":doc["path"],"full_chpl":doc["is_chpl"],
                        "strength_near_name": bool(strengths) and all(found),
                        "name_match_only":not strengths})
    data.append({"variant_id":r["id"],"medicine":r["name"],"symbol":r["symbol"],"subiekt":r["subiekt"],
                "rpl":r["rpl_id"],"upd":r["upd_id"],"candidates":candidate})
csvpath=OUT/"partia40_kandydaci_pdf_nazwa_moc_subiekt.csv"
with csvpath.open("w",encoding="utf-8-sig",newline="") as f:
    w=csv.writer(f,delimiter=";")
    w.writerow(["ID","Lek","Symbol Subiekta","Nazwa w Subiekcie","RPL","UPD","PDF z nazwa","PDF z moca i ChPL"])
    for r in data:
        candidates=r["candidates"]
        w.writerow([r["variant_id"],r["medicine"],r["symbol"],r["subiekt"],r["rpl"],r["upd"],
                    " | ".join(d["path"] for d in candidates[:8]),
                    " | ".join(d["path"] for d in candidates if d["full_chpl"] and d["strength_near_name"])])
result={"partia":40,"database_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
        "clinical_unchanged":True,"active_variants":len(rows),"pdf_files":pdfcount,
        "missing_or_invalid_pdf_after":len(missing),
        "acticarp":{"present_pdf":pdfpath(ACT),"pages":pages,"extracted_chars":len(text),
                    "validated":valid,"validation_reason":reason,"actions":links,
                    "subiekt_names":[{"symbol":r["symbol"],"name":r["name"]} for r in act]},
        "candidate_pdf_by_name_count":sum(bool(r["candidates"]) for r in data),
        "candidate_complete_chpl_and_strength_count":sum(any(d["full_chpl"] and d["strength_near_name"] for d in r["candidates"]) for r in data),
        "note":"Only Acticarp was eligible for automatic attachment. All other PDF matches require further verification.",
        "rows":data}
(OUT/"partia40_wynik.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
print("PARTIA40",json.dumps({k:v for k,v in result.items() if k!="rows"},ensure_ascii=False))
db.close()
