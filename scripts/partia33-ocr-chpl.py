#!/usr/bin/env python3
"""Partia 33: OCR skanow i bezpieczne rozstrzyganie istniejacych ChPL."""
from pathlib import Path
from collections import defaultdict, Counter
import hashlib, json, re, sqlite3, subprocess, tempfile, unicodedata
import fitz

ROOT=Path.cwd()
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"partia33_raport"
OUT.mkdir(exist_ok=True)
EXPECTED="5f9a17a9d22b0fe41e9f1e2166e5d1b77e77e96ab0c8ea1f642acc6ecc55c957"
BEFORE=hashlib.sha256(DB.read_bytes()).hexdigest()
assert BEFORE==EXPECTED, f"SQLite zmieniona: {BEFORE}"
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
assert db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]==2944

def norm(s):
    s=unicodedata.normalize("NFKD",str(s or "").casefold())
    return re.sub(r"[^a-z0-9]+"," ","".join(c for c in s if not unicodedata.combining(c))).strip()

def pdf_ok(p):
    return p.is_file() and p.open("rb").read(5)==b"%PDF-"

def id_ok(x):
    s=str(x or "").strip()
    return s if re.fullmatch(r"\d{4,15}",s) and int(s)>0 else ""

rows=[dict(r) for r in db.execute("SELECT id,name,rpl_id,upd_id,pdf_path,scope FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'")]
assert len(rows)==2902
missing=[r for r in rows if not str(r["pdf_path"] or "").strip() or not pdf_ok(ROOT/str(r["pdf_path"]))]
assert len(missing)==221, len(missing)
clinical_cols=[r[1] for r in db.execute("PRAGMA table_info(variants)") if r[1]!="pdf_path"]
def clinical_digest():
    sql="SELECT "+",".join('"'+c+'"' for c in clinical_cols)+" FROM variants ORDER BY id"
    return hashlib.sha256(json.dumps([list(x) for x in db.execute(sql)],ensure_ascii=False,default=str).encode()).hexdigest()
clinical_before=clinical_digest()

index={"RPL":defaultdict(list),"UPD":defaultdict(list)}
for src in index:
    for p in sorted((ROOT/"data/ChPL"/src).glob(src+"_*.pdf")):
        m=re.match(r"^"+src+r"_(\d+)_",p.name)
        if m:index[src][m[1]].append(p)

def root_name(s):
    return next((w for w in norm(s).split() if len(w)>=4 and not w.isdigit()), "")

groups=defaultdict(list)
for r in missing:
    groups[(id_ok(r["rpl_id"]),id_ok(r["upd_id"]),root_name(r["name"]))].append(r)

def options(key):
    rid,uid,_=key
    a=[]
    if rid:a.extend(("RPL",p) for p in index["RPL"].get(rid,[]))
    if uid:a.extend(("UPD",p) for p in index["UPD"].get(uid,[]))
    return a

cache={}
def inspect(p):
    if p in cache:return cache[p]
    info={"path":p.relative_to(ROOT).as_posix(),"status":"unreadable","pages":0,"text":""}
    try:
        if not pdf_ok(p):raise ValueError("invalid PDF file")
        with fitz.open(p) as doc:
            if doc.is_encrypted or not len(doc):raise ValueError("empty/encrypted")
            info["pages"]=len(doc)
            body=" ".join(page.get_text(sort=True) for page in doc)
            info["raw_extract_length"]=len(body)
            if len(body.strip())<300:
                info["ocr_used"]=True
                snippets=[]
                # OCR first four pages, where product name, strength, and ChPL headings are expected.
                with tempfile.TemporaryDirectory() as temp:
                    for i in range(min(4,len(doc))):
                        pic=Path(temp)/f"page{i}.png"
                        doc[i].get_pixmap(matrix=fitz.Matrix(2,2),alpha=False).save(pic)
                        proc=subprocess.run(["tesseract",str(pic),"stdout","-l","pol+eng","--psm","3"],capture_output=True,text=True,timeout=90)
                        if proc.returncode:raise ValueError("OCR failed: "+proc.stderr[-100:])
                        snippets.append(proc.stdout)
                body=" ".join(snippets)
            else:info["ocr_used"]=False
            info["text"]=norm(body)
            info["raw_excerpt"]=body[:1000]
            n=info["text"]
            pol_markers=sum(bool(re.search(r"\b"+re.escape(w)+r"\b",n)) for w in ("wskazania","karencji","dawkowanie","przeciwwskazania","podania","weterynaryjnego","leczniczego"))
            info["polish_marker_count"]=pol_markers
            info["chpl_heading"]=bool(re.search(r"charakterystyk.{0,100}(produktu|leczniczego)",n))
            # No text-only declaration that an official ChPL exists unless verified.
            info["status"]="pl_chpl_evidence" if info["chpl_heading"] and pol_markers>=2 else "unverified_language_or_document"
    except Exception as exc:
        info["status"]="unreadable"
        info["reason"]=str(exc)[:200]
    cache[p]=info
    return info

def strengths(s):
    # Numbers associated with drug potency, not packaging count / volumes.
    raw=unicodedata.normalize("NFKD",str(s or "").casefold())
    raw="".join(c for c in raw if not unicodedata.combining(c))
    return [(a.replace(",","."),u.lower()) for a,u in re.findall(r"(?<!\w)(\d+(?:[,.]\d+)?)\s*(mg|mcg|ug|µg|g|iu|j\.?m\.?|%)\b",raw,re.I)]

def match_name(name,body):
    words=[w for w in norm(name).split() if len(w)>=4 and not w.isdigit()]
    stop={"roztwor","zawiesina","proszek","tabletki","tabletka","doustny","doustna","wstrzykiwan","szczepionka","ampulka","fiolka","pasta","masc","opakowanie"}
    words=[w for w in words if w not in stop]
    if not words:return False,"name_not_identifiable"
    if not re.search(r"\b"+re.escape(words[0])+r"\b",body):return False,"first_product_token_absent"
    # Ignore optional non-core words, but require one more distinctive word if one exists.
    if len(words)>1 and len(words[1])>=5 and words[1] not in ("forte","flavour","flavor"):
        if not re.search(r"\b"+re.escape(words[1])+r"\b",body):return False,"second_product_token_absent"
    for amount,unit in strengths(name):
        unit=unit.replace("µg","ug")
        # OCR replaces comma and period; also allow natural whitespace.
        pat=r"\b"+re.escape(amount).replace(r"\.",r"[.,]")+r"\s*"+re.escape(unit)+r"\b"
        if unit=="%":pat=r"\b"+re.escape(amount).replace(r"\.",r"[.,]")+r"\s*%"
        if not re.search(pat,body):
            return False,"strength_not_confirmed:"+amount+unit
    return True,"name_strength_confirmed"

# Determine all groups and limit execution to 50 with files already present.
queue=[]
for key,items in groups.items():
    opts=options(key)
    if opts:
        scanned=any(inspect(p)["raw_extract_length"]<300 for _,p in opts if inspect(p)["status"]!="unreadable")
        cat="scan_ocr" if scanned else "existing_pdf_review"
    else:
        cat="missing_identifiers" if not key[0] and not key[1] else "missing_official_pdf"
    queue.append((key,items,opts,cat))
queue.sort(key=lambda x:({"scan_ocr":0,"existing_pdf_review":1,"missing_official_pdf":2,"missing_identifiers":3}[x[3]],-len(x[1]),x[0]))
selected=[x for x in queue if x[2]][:50]
report_items=[]
fixes=[]
for key,items,opts,cat in selected:
    record={"rpl_id":key[0],"upd_id":key[1],"ids":[r["id"] for r in items],"names":sorted({r["name"] for r in items}),"category":cat,"candidates":[]}
    approved=[]
    for src,p in opts:
        d=inspect(p)
        matched=[match_name(r["name"],d["text"]) for r in items]
        valid=d["status"]=="pl_chpl_evidence" and all(t[0] for t in matched)
        record["candidates"].append({"source":src,"path":d["path"],"pages":d["pages"],"status":d["status"],"ocr_used":d.get("ocr_used"),"chpl_heading":d.get("chpl_heading",False),"polish_markers":d.get("polish_marker_count",0),"name_strength_results":[{"variant":r["id"],"result":ok,"reason":why} for r,(ok,why) in zip(items,matched)]})
        if valid:approved.append((src,p))
    # If multiple matching files, choose only one if the other is demonstrably absent.
    if len(approved)==1:
        src,p=approved[0]
        rel=p.relative_to(ROOT).as_posix()
        record["decision"]="approved"
        record["selected_pdf"]=rel
        fixes.append((items,rel))
    else:
        record["decision"]="manual_review"
        record["reason"]="multiple_candidates" if approved else "no_fully_verified_Polish_ChPL"
    report_items.append(record)

if fixes:
    try:
        db.execute("BEGIN IMMEDIATE")
        for items,rel in fixes:
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(rel,))
            for r in items:
                old=str(r["pdf_path"] or "").strip()
                assert not old or not pdf_ok(ROOT/old)
                c=db.execute("UPDATE variants SET pdf_path=? WHERE id=? AND COALESCE(rpl_id,'')=COALESCE(?, '') AND COALESCE(upd_id,'')=COALESCE(?, '')",(rel,r["id"],r["rpl_id"],r["upd_id"]))
                assert c.rowcount==1,r["id"]
        assert clinical_digest()==clinical_before,"clinical fields changed!"
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
    except:
        db.rollback()
        raise
assert clinical_digest()==clinical_before
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
after_missing=sum(not str(r[0] or "").strip() or not pdf_ok(ROOT/str(r[0])) for r in db.execute("SELECT pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'"))
linked=sum(len(items) for items,_ in fixes)
assert after_missing==221-linked
report={"phase":33,"baseline_sha":BEFORE,"after_sha":hashlib.sha256(DB.read_bytes()).hexdigest(),"before_missing":221,"after_missing":after_missing,"backlog_groups":len(queue),"all_categories":dict(Counter(x[3] for x in queue)),"selected_groups":len(selected),"checked_variants":sum(len(x[1]) for x in selected),"approved_groups":len(fixes),"new_pdf_links":linked,"clinical_unchanged":True,"details":report_items}
(OUT/"result.json").write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf8")
(OUT/"review.md").write_text("# Partia 33 — raport\n\n"+ "\n".join(f"- {r['decision']}: {', '.join(r['names'])} — {r.get('selected_pdf',r.get('reason'))}" for r in report_items),encoding="utf8")
print("PARTIA33_RESULT",json.dumps({k:v for k,v in report.items() if k!="details"},ensure_ascii=False))
db.close()
