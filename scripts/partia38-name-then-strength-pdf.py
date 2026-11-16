#!/usr/bin/env python3
"""Partia 38: name first, then strength and formulation, across existing Polish ChPL.
Read all PDF content rather than assuming one UPD ID = one medicine strength.
Only automatically link uniquely supported candidates; never modify clinical fields.
"""
import csv, hashlib, json, re, sqlite3, unicodedata
from collections import Counter, defaultdict
from pathlib import Path
import pymupdf

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"partia38_raport"
OUT.mkdir(exist_ok=True)
def norm(s):
    s=unicodedata.normalize("NFKD",str(s or "").casefold())
    s="".join(c for c in s if not unicodedata.combining(c))
    return re.sub(r"\s+"," ",s).replace("–","-").replace("—","-").strip()
def is_pdf(p):
    return p.is_file() and p.stat().st_size>=6000 and p.open("rb").read(5)==b"%PDF-"
def brand(name):
    s=norm(name)
    replacements=[
        (r"^(bravecto|simparica trio|simparica|contacera|zuprevo|metacam|stronghold|melovem|draxxin|loxicom|naxcel|zactran|tulaven|librela|sputop|bioestrovet|bovela|circovac|coxevac|act icarp|acticarp|exzolt|startvac|ingelvac circoflex|porcilis ar-t df|porcilis porcoli df|porcilis coliclos|porcilis pcv|proteqflu-te|equilis prequenza-te|meloxidyl|previcox|advocate|kr iptazen|kriptazen|locatim|vectra 3d|sileo|l-spec-vmd|effydral|pirsue|ciper-pulvizoo|coffeinum bioveta|coff einum bioveta|tetra-delta|velactis|pregsure bvd)\b",None),
    ]
    m=re.match(replacements[0][0],s)
    if m:return m.group(1)
    return " ".join(s.split()[:2])
def strength(name):
    s=norm(name).replace(",",".")
    return [(v,u) for v,u in re.findall(r"(?<![a-z0-9])(\d+(?:\.\d+)?)\s*(mg/ml|mcg/ml|mg|mcg)\b",s)]
def as_regex_num(v,u):
    return re.compile(r"(?<!\d)"+re.escape(v).replace(r"\.",r"[.,]")+r"\s*"+re.escape(u)+r"\b")
def formulation(name):
    s=norm(name)
    if any(w in s for w in ("spot-on","nakrapian","pipeta","pipetka")):return "spot-on"
    if any(w in s for w in ("tab.","tablet","do rozgryzania")):return "tabletki"
    if "roztwor do wstrzykiwan" in s or "inj" in s:return "wstrzykiwania"
    return ""
def matching_brand(text,b):
    # Never use partial tokens; 'Simparica' must not match 'Simparica Trio' alone.
    p=r"(?<!\w)"+r"\s+".join(re.escape(x) for x in b.split())+r"(?!\w)"
    hits=[m.start() for m in re.finditer(p,text)]
    if b=="simparica":
        hits=[i for i in hits if not re.match(r"\s+trio\b",text[i+len(b):i+len(b)+12])]
    return hits
def pdf_text(path):
    with pymupdf.open(path) as d:
        if d.is_encrypted or not len(d):return None
        return norm("\n".join(p.get_text(sort=True) for p in d)),len(d)

db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
clinical=[r["name"] for r in db.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def clinical_hash():
    query="SELECT "+",".join('"'+c+'"' for c in clinical)+" FROM variants ORDER BY id"
    h=hashlib.sha256()
    for row in db.execute(query):
        h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
    return h.hexdigest()
clinical_before=clinical_hash()
sha_before=hashlib.sha256(DB.read_bytes()).hexdigest()
rows=[dict(r) for r in db.execute("SELECT id,name,scope,rpl_id,upd_id,pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'")]
missing=[r for r in rows if not str(r["pdf_path"] or "").strip() or not is_pdf(ROOT/str(r["pdf_path"]))]
allfiles=sorted(p for p in (ROOT/"data/ChPL").rglob("*.pdf") if is_pdf(p))
print("INITIAL",json.dumps({"active_variants":len(rows),"missing_valid_pdfs":len(missing),"files_indexed":len(allfiles)}))
# Read PDFs once, build inverted index on NAME, not ID/filename. Require authentic Polish ChPL heading;
# a UPD summary printout or patient leaflet must not masquerade as a ChPL.
indexed=defaultdict(list)
issues=[]
names=sorted({brand(r["name"]) for r in missing})
for p in allfiles:
    try:
        scanned=pdf_text(p)
        if not scanned:continue
        text,pages=scanned
        if len(text)<300:continue # scanned PDFs need separate manual visual verification
        beginning=text[:15000]
        if not any(k in beginning for k in ("charakterystyka produktu leczniczego","charakterystyka weterynaryjnego produktu leczniczego","charakterystyka produktu","charakterystyka weterynaryjnego","charakterystyka leczniczego")):
            continue
        if not any(w in text for w in ("przeciwwskazania","okres karencji","dane kliniczne","wskazania lecznicze")):continue
        for name in names:
            positions=matching_brand(text,name)
            if positions:indexed[name].append({"path":p.relative_to(ROOT).as_posix(),"text":text,"positions":positions,"pages":pages,"sha":hashlib.sha256(p.read_bytes()).hexdigest()})
    except Exception as e:
        issues.append({"pdf":p.relative_to(ROOT).as_posix(),"error":str(e)[:170]})
print("NAME_INDEX",json.dumps({k:len(v) for k,v in indexed.items() if v},ensure_ascii=False))
def evaluate(r,doc):
    text=doc["text"]
    pos=doc["positions"]
    # Do not confuse a base brand with a different combination product.
    if brand(r["name"])=="draxxin" and any(re.search(r"(?<!\w)draxxin\s+plus(?!\w)",text[max(0,i-30):i+90]) for i in pos):
        return False,"different_product_draxxin_plus"
    # Reject a document for a different RPL ID when the file includes an explicit national RPL number.
    m=re.search(r"/RPL_(\d+)_",doc["path"])
    if m and str(r["rpl_id"] or "").strip().lstrip("0")!=m.group(1).lstrip("0"):
        return False,"different_rpl_id"
    strengths=strength(r["name"])
    f=formulation(r["name"])
    # A strength must be near the medicine's actual name; not elsewhere in dosage warnings.
    # This also handles multi-strength ChPL documents containing all names at the start.
    windows=[text[max(0,i-40):min(len(text),i+1100)] for i in pos[:70]]
    if strengths:
        # all stated concentrations/strengths must appear near the name
        for v,u in strengths:
            pattern=as_regex_num(v,u)
            if not any(pattern.search(w) for w in windows):
                return False,"no_matching_strength_near_name"
    elif len(pos)<2:
        return False,"no_strength_and_only_one_name_occurrence"
    if f=="spot-on" and not any(("nakrapian" in w or "spot-on" in w) for w in windows):
        return False,"spot_on_formulation_not_confirmed"
    if f=="tabletki" and not any("tablet" in w for w in windows):
        return False,"tablet_formulation_not_confirmed"
    if f=="wstrzykiwania" and not any(("wstrzykiwan" in w or "injection" in w) for w in windows):
        return False,"injection_formulation_not_confirmed"
    return True,"name_strength_formulation_verified"
decisions=[]
for r in missing:
    b=brand(r["name"])
    docs=indexed.get(b,[])
    results=[]
    for doc in docs:
        ok,reason=evaluate(r,doc)
        if ok:results.append(doc)
    unique={}
    for doc in results:unique.setdefault(doc["sha"],[]).append(doc)
    if len(unique)==1:
        copies=next(iter(unique.values()))
        # Choose the same actual document already in repo, even if multiple identical copies.
        # Do not infer that differing PDFs are equivalent without human inspection.
        chosen=sorted(copies,key=lambda d:(0 if "_pl_" in d["path"].lower() else 1,len(d["path"]),d["path"]))[0]
        decision={"id":r["id"],"name":r["name"],"rpl":r["rpl_id"],"upd":r["upd_id"],"status":"verified_unique","pdf":chosen["path"],"strengths":strength(r["name"]),"brand":b}
    elif not unique:
        decision={"id":r["id"],"name":r["name"],"rpl":r["rpl_id"],"upd":r["upd_id"],"status":"manual_no_verified_candidate","candidates":[d["path"] for d in docs[:10]],"brand":b}
    else:
        decision={"id":r["id"],"name":r["name"],"rpl":r["rpl_id"],"upd":r["upd_id"],"status":"manual_multiple_different_pdfs","candidates":[copy[0]["path"] for copy in unique.values()],"brand":b}
    decisions.append(decision)
# Extra explicit reporting to verify Zuprevo; four variants can legitimately share one ChPL.
zuprevo=[d for d in decisions if d["brand"]=="zuprevo"]
print("ZUPREVO",json.dumps(zuprevo,ensure_ascii=False))
try:
    db.execute("BEGIN IMMEDIATE")
    for decision in decisions:
        if decision["status"]!="verified_unique":continue
        path=decision["pdf"]
        db.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)",(path,))
        old=db.execute("SELECT pdf_path FROM variants WHERE id=?",(decision["id"],)).fetchone()[0]
        assert not old or not is_pdf(ROOT/old),decision["id"]
        db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(path,decision["id"]))
        decision["status"]="linked"
    assert clinical_hash()==clinical_before
    assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
    assert not db.execute("PRAGMA foreign_key_check").fetchall()
    db.commit()
except:
    db.rollback()
    raise
remaining=sum(not str(r[0] or "").strip() or not is_pdf(ROOT/str(r[0])) for r in db.execute("SELECT pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'"))
nlinked=sum(d["status"]=="linked" for d in decisions)
assert remaining==len(missing)-nlinked,(remaining,len(missing),nlinked)
result={"phase":38,"active_variants":len(rows),"indexed_pdf_count":len(allfiles),"missing_before":len(missing),"missing_after":remaining,"new_links":nlinked,"statuses":dict(Counter(d["status"] for d in decisions)),"zuprevo":zuprevo,"clinical_data_unchanged":True,"sqlite_before":sha_before,"sqlite_after":hashlib.sha256(DB.read_bytes()).hexdigest(),"decisions":decisions,"index_errors":issues}
(OUT/"result.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
with (OUT/"braki_po_partii38.csv").open("w",encoding="utf-8-sig",newline="") as f:
    writer=csv.writer(f,delimiter=";")
    writer.writerow(["variant_id","medicine","status","candidate_pdfs"])
    for d in decisions:
        if d["status"]!="linked":writer.writerow([d["id"],d["name"],d["status"]," | ".join(d.get("candidates",[]))])
print("PARTIA38_RESULT",json.dumps({k:v for k,v in result.items() if k not in ("decisions","index_errors")},ensure_ascii=False))
db.close()
