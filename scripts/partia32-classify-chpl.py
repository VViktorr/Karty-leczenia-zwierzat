#!/usr/bin/env python3
"""Partia 32: audyt zrodel PDF i bezpieczne powiazania wariantow."""
import hashlib, json, re, sqlite3, unicodedata
from collections import defaultdict, Counter
from pathlib import Path
import fitz

ROOT=Path.cwd()
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"partia32_raport"
OUT.mkdir(exist_ok=True)
EXPECTED="5f9a17a9d22b0fe41e9f1e2166e5d1b77e77e96ab0c8ea1f642acc6ecc55c957"
before=hashlib.sha256(DB.read_bytes()).hexdigest()
assert before==EXPECTED, f"SQLite changed: {before}"
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()

def norm(s):
    s=unicodedata.normalize("NFKD",str(s or "").casefold())
    return re.sub(r"[^a-z0-9]+"," ","".join(x for x in s if not unicodedata.combining(x))).strip()
def validpdf(p):
    return p.is_file() and p.open("rb").read(5)==b"%PDF-"
def exact_id(s):
    t=str(s or "").strip()
    return t if re.fullmatch(r"\d{4,15}",t) and int(t)!=0 else ""
def rootname(name):
    w=norm(name).split()
    return next((x for x in w if len(x)>=4 and not x.isdigit()), "")
def namecheck(name,txt):
    tokens=[x for x in norm(name).split() if len(x)>=4 and not x.isdigit()]
    generic={"tabletki","roztwor","zawiesina","proszek","doustny","doustna","wstrzykiwan","iniekcja","kapsulki","wielkosci","opakowanie"}
    tokens=[x for x in tokens if x not in generic]
    if not tokens: return False
    if not re.search(r"\b"+re.escape(tokens[0])+r"\b",txt):return False
    # Explicit numeric strength check is reported separately; never rely only on a shared RPL/UPD ID.
    return True

rows=[dict(x) for x in db.execute("SELECT id,name,rpl_id,upd_id,pdf_path,scope FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'")]
assert db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]==2944
assert len(rows)==2902
missing=[r for r in rows if not str(r["pdf_path"] or "").strip() or not validpdf(ROOT/str(r["pdf_path"]))]
assert len(missing)==221, f"Expected 221 missing, found {len(missing)}"
columns=[r[1] for r in db.execute("PRAGMA table_info(variants)") if r[1]!="pdf_path"]
def clinical_hash():
    select="SELECT "+",".join('"'+c+'"' for c in columns)+" FROM variants ORDER BY id"
    return hashlib.sha256(json.dumps([list(x) for x in db.execute(select)],ensure_ascii=False,default=str).encode()).hexdigest()
clinical_before=clinical_hash()
index={"RPL":defaultdict(list),"UPD":defaultdict(list)}
for source in index:
    for p in sorted((ROOT/"data/ChPL"/source).glob(source+"_*.pdf")):
        m=re.match(r"^"+source+r"_(\d+)_",p.name)
        if m:index[source][m.group(1)].append(p)
groups=defaultdict(list)
for r in missing:
    rid=exact_id(r["rpl_id"])
    uid=exact_id(r["upd_id"])
    # Never collapse different products solely because both identifiers are empty.
    groups[(rid,uid,rootname(r["name"]))].append(r)
cache={}
def inspect(p):
    if p in cache:return cache[p]
    d={"path":p.relative_to(ROOT).as_posix(),"class":"unreadable","pages":0}
    try:
        if not validpdf(p): raise ValueError("invalid PDF header")
        with fitz.open(p) as pdf:
            if pdf.is_encrypted or len(pdf)<1:raise ValueError("encrypted or empty")
            d["pages"]=len(pdf)
            texts=[page.get_text(sort=True) for page in pdf]
            text=norm(" ".join(texts))
            d["text"]=text
            if len(text)<300:
                d["class"]="scan_or_no_text_layer"
                # A preview makes it possible to inspect the actual pages manually.
                preview=OUT/"scan_previews"
                preview.mkdir(exist_ok=True)
                for n in range(min(2,len(pdf))):
                    pix=pdf[n].get_pixmap(matrix=fitz.Matrix(1.3,1.3),alpha=False)
                    pix.save(preview/(hashlib.sha256(str(p).encode()).hexdigest()[:12]+f"_p{n+1}.png"))
            elif all(x in text for x in ("charakterystyk","weterynaryjn","produkt")) and sum(x in text for x in ("wskazania","karencji","dawkowanie","przeciwwskazania","podania"))>=2:
                d["class"]="polish_chpl_text"
            else:
                d["class"]="language_or_header_unverified"
    except Exception as e:
        d["reason"]=str(e)[:160]
    cache[p]=d
    return d

def candidates(key):
    rid,uid,_=key
    options=[]
    if rid:options += [("RPL",p) for p in index["RPL"].get(rid,[])]
    if uid:options += [("UPD",p) for p in index["UPD"].get(uid,[])]
    return options

# Evaluate the whole backlog once; then prioritize genuinely new approaches.
queue=[]
for key,items in groups.items():
    opts=candidates(key)
    # Scanned RPL, then textual RPL, then UPD, then absent/ambiguous identifiers.
    classes=[inspect(p)["class"] for _,p in opts]
    if not key[0] and not key[1]:kind="missing_identifiers"
    elif "scan_or_no_text_layer" in classes:kind="rpl_or_upd_scanned"
    elif "polish_chpl_text" in classes:kind="text_candidate"
    elif opts:kind="existing_pdf_needs_verification"
    else:kind="missing_repository_document"
    queue.append((key,items,opts,kind))
priority={"rpl_or_upd_scanned":0,"text_candidate":1,"existing_pdf_needs_verification":2,"missing_repository_document":3,"missing_identifiers":4}
queue.sort(key=lambda x:(priority[x[3]],0 if x[0][0] else 1,-len(x[1]),x[0]))
selected=queue[:50]
outcomes=[]
fixes=[]
for key,items,opts,kind in selected:
    rid,uid,root=key
    item={"rpl_id":rid,"upd_id":uid,"product_root":root,"ids":[r["id"] for r in items],"names":sorted({r["name"] for r in items}),"category":kind,"candidates":[]}
    verified=[]
    for source,p in opts:
        detail=inspect(p)
        # Filename-bound ID, verified Polish ChPL text, product name verified in PDF.
        matches=all(namecheck(r["name"],detail.get("text","")) for r in items)
        item["candidates"].append({"source":source,"path":detail["path"],"class":detail["class"],"pages":detail["pages"],"name_match":matches})
        if detail["class"]=="polish_chpl_text" and matches:
            verified.append((source,p))
    # Ambiguous 2+ valid candidates must be resolved separately, not guessed.
    if len(verified)==1:
        src,p=verified[0]
        item["status"]="approved"
        item["selected_pdf"]=p.relative_to(ROOT).as_posix()
        fixes.append((items,item["selected_pdf"]))
    else:
        item["status"]="manual_review" if opts else "official_pdf_search_required"
        if len(verified)>1:item["reason"]="multiple_matching_candidates"
        elif any(x["class"]=="scan_or_no_text_layer" for x in item["candidates"]):item["reason"]="scan_requires_page_and_identifier_review"
        elif kind=="missing_identifiers":item["reason"]="product_identifier_missing"
    outcomes.append(item)
if fixes:
    try:
        db.execute("BEGIN IMMEDIATE")
        for items,path in fixes:
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(path,))
            for r in items:
                old=str(r["pdf_path"] or "").strip()
                assert not old or not validpdf(ROOT/old)
                changed=db.execute("UPDATE variants SET pdf_path=? WHERE id=? AND COALESCE(rpl_id,'')=COALESCE(?, '') AND COALESCE(upd_id,'')=COALESCE(?, '')",(path,r["id"],r["rpl_id"],r["upd_id"]))
                assert changed.rowcount==1,r["id"]
        assert clinical_hash()==clinical_before
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
    except:
        db.rollback()
        raise
assert clinical_hash()==clinical_before
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
remaining=sum(not str(r[0] or "").strip() or not validpdf(ROOT/str(r[0])) for r in db.execute("SELECT pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'"))
linked=sum(len(items) for items,_ in fixes)
assert remaining==221-linked,(remaining,linked)
report={
 "phase":"32","before_missing":221,"after_missing":remaining,
 "backlog_groups":len(groups),"selected_groups":len(selected),
 "checked_variants":sum(len(items) for _,items,_,_ in selected),
 "selected_categories":dict(Counter(kind for _,_,_,kind in selected)),
 "all_backlog_categories":dict(Counter(kind for _,_,_,kind in queue)),
 "approved_groups":len(fixes),"new_pdf_links":linked,
 "sqlite_before_sha256":before,"sqlite_after_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "clinical_fields_unchanged":True,"outcomes":outcomes}
(OUT/"result.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf8")
print("PARTIA32_RESULT",json.dumps({k:v for k,v in report.items() if k!="outcomes"},ensure_ascii=False))
db.close()
