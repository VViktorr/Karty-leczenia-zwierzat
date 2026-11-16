#!/usr/bin/env python3
"""Partia 35: official RPL ChPL downloads (manual workflow only)."""
import hashlib, json, re, sqlite3, unicodedata
from collections import Counter,defaultdict
from io import BytesIO
from pathlib import Path
from urllib.request import Request,urlopen
import pymupdf

ROOT=Path.cwd()
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"partia35_raport"
OUT.mkdir(exist_ok=True)
EXPECTED="53cafa67825b5c5a4db5d8b2381977a403587579f814c68d5f27e9df140f7b3e"
before=hashlib.sha256(DB.read_bytes()).hexdigest()
assert before==EXPECTED, f"SQLite unexpectedly changed: {before}"
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
assert db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]==2944

def norm(s):
    u=unicodedata.normalize("NFKD",str(s or "").lower())
    return re.sub(r"[^a-z0-9]+"," ","".join(c for c in u if not unicodedata.combining(c))).strip()
def validpdf(p):
    return p.is_file() and p.open("rb").read(5)==b"%PDF-"
def good_id(s):
    s=str(s or "").strip()
    return s if re.fullmatch(r"\d{4,15}",s) and int(s)>0 else ""
def root(s):
    return next((x for x in norm(s).split() if len(x)>=4 and not x.isdigit()),"")
def potency(name):
    u=unicodedata.normalize("NFKD",str(name).lower())
    u="".join(c for c in u if not unicodedata.combining(c))
    return [(n.replace(",","."),unit) for n,unit in re.findall(r"(?<!\w)(\d+(?:[,.]\d+)?)\s*(mg|mcg|ug|g|iu|%)\b",u)]

def match_product(name,text):
    words=[w for w in norm(name).split() if len(w)>=4 and not w.isdigit()]
    excluded={"roztwor","zawiesina","wstrzykiwan","tabletki","proszek","tabletka","doustna","doustny","opakowanie","flakon","butelka","ampulka","fiolka","szczepionka","pasta","masc"}
    words=[w for w in words if w not in excluded]
    if not words or not re.search(r"\b"+re.escape(words[0])+r"\b",text):
        return False,"name_not_confirmed"
    if len(words)>1 and len(words[1])>=5 and words[1] not in {"flavour","flavor"}:
        if not re.search(r"\b"+re.escape(words[1])+r"\b",text):
            return False,"secondary_name_token_not_confirmed"
    for n,unit in potency(name):
        pattern=r"\b"+re.escape(n).replace(r"\.",r"[., ]")+r"\s*"+re.escape(unit)+(r"\b" if unit!="%" else "")
        if not re.search(pattern,text):
            return False,"potency_not_confirmed:"+n+unit
    return True,"name_and_potency_match"

active=[dict(r) for r in db.execute("SELECT id,name,rpl_id,upd_id,pdf_path,scope FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'")]
assert len(active)==2902
missing=[r for r in active if not str(r["pdf_path"] or "").strip() or not validpdf(ROOT/str(r["pdf_path"]))]
assert len(missing)==193,len(missing)
clinical_columns=[r[1] for r in db.execute("PRAGMA table_info(variants)") if r[1]!="pdf_path"]
def digest_clinical():
    q="SELECT "+",".join('"'+c+'"' for c in clinical_columns)+" FROM variants ORDER BY id"
    return hashlib.sha256(json.dumps([list(row) for row in db.execute(q)],ensure_ascii=False,default=str).encode()).hexdigest()
clinical_before=digest_clinical()

indexed=defaultdict(list)
for p in (ROOT/"data/ChPL/RPL").glob("RPL_*.pdf"):
    m=re.match(r"^RPL_(\d+)_",p.name)
    if m and validpdf(p):indexed[m.group(1)].append(p)
groups=defaultdict(list)
for r in missing:
    groups[(good_id(r["rpl_id"]),good_id(r["upd_id"]),root(r["name"]))].append(r)

# Only new official RPL downloads in this stage; previously scanned existing PDFs are a separate backlog.
queue=[]
for key,items in groups.items():
    rid,uid,name=key
    if rid and not indexed[rid]:
        kind="official_rpl_download"
    elif not rid and uid:
        kind="upd_portal_document_required"
    elif not rid and not uid:
        kind="missing_identifiers"
    else:
        kind="existing_rpl_document_review"
    queue.append((key,items,kind))
queue.sort(key=lambda x:({"official_rpl_download":0,"upd_portal_document_required":1,"missing_identifiers":2,"existing_rpl_document_review":3}[x[2]],-len(x[1]),x[0]))
selected=[x for x in queue if x[2]=="official_rpl_download"][:50]
outcomes=[]
pending=[]
new_paths=[]
for (rid,uid,name),items,kind in selected:
    record={"rpl_id":rid,"upd_id":uid,"ids":[r["id"] for r in items],"names":sorted({r["name"] for r in items}),"status":"pending","sources_attempted":[]}
    uri=f"https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/{rid}/characteristic"
    approved=None
    for domain in ("rejestrymedyczne.ezdrowie.gov.pl","rejestry.ezdrowie.gov.pl"):
        url=f"https://{domain}/api/rpl/medicinal-products/{rid}/characteristic"
        attempt={"url":url}
        try:
            request=Request(url,headers={"User-Agent":"Mozilla/5.0 (official ChPL document verification)","Accept":"application/pdf"})
            with urlopen(request,timeout=35) as response:
                ctype=response.headers.get("Content-Type","")
                data=response.read(15*1024*1024+1)
            if len(data)>15*1024*1024:raise ValueError("PDF exceeds 15 MiB")
            if len(data)<6000 or not data.startswith(b"%PDF-") or b"%%EOF" not in data[-4096:]:
                raise ValueError("response not a complete PDF")
            with pymupdf.open(stream=BytesIO(data),filetype="pdf") as doc:
                if doc.is_encrypted or len(doc)<1:raise ValueError("empty/encrypted PDF")
                body=" ".join(p.get_text(sort=True) for p in doc[:min(8,len(doc))])
                pages=len(doc)
            text=norm(body)
            if len(text)<400:
                raise ValueError("scan: manual page validation required; no automatic attachment")
            if not re.search(r"charakterystyk.{0,110}(produktu|leczniczego)",text):
                raise ValueError("Polish ChPL heading not established")
            if sum(w in text for w in ("wskazania","karencji","dawkowanie","przeciwwskazania","podania","weterynaryjnego"))<2:
                raise ValueError("Polish ChPL sections not established")
            checks=[match_product(r["name"],text) for r in items]
            if not all(ok for ok,_ in checks):
                raise ValueError("variant name/strength mismatch: "+",".join(why for ok,why in checks if not ok))
            # Keep a stable official source-name, never overwrite a valid document.
            path=ROOT/f"data/ChPL/RPL/RPL_{rid}_Partia35_Oficjalna_ChPL.pdf"
            if path.exists():
                raise ValueError("target exists; do not overwrite")
            attempt.update({"status":"verified","pages":pages,"bytes":len(data),"content_type":ctype})
            approved=(path,data,url)
            record["status"]="approved"
            record["pages"]=pages
            break
        except Exception as exc:
            attempt.update({"status":"rejected","reason":str(exc)[:250]})
        finally:
            record["sources_attempted"].append(attempt)
    if approved:
        path,data,url=approved
        record["pdf_path"]=path.relative_to(ROOT).as_posix()
        record["source_url"]=url
        pending.append((items,path,data))
    else:
        record["status"]="manual_review"
    outcomes.append(record)

# Save PDF files before the SQLite transaction, then remove any newly created files on failure.
try:
    for items,path,data in pending:
        path.write_bytes(data)
        new_paths.append(path)
    if pending:
        db.execute("BEGIN IMMEDIATE")
        for items,path,data in pending:
            rel=path.relative_to(ROOT).as_posix()
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(rel,))
            for r in items:
                old=str(r["pdf_path"] or "").strip()
                assert not old or not validpdf(ROOT/old)
                c=db.execute("UPDATE variants SET pdf_path=? WHERE id=? AND COALESCE(rpl_id,'')=COALESCE(?, '') AND COALESCE(upd_id,'')=COALESCE(?, '')",(rel,r["id"],r["rpl_id"],r["upd_id"]))
                assert c.rowcount==1,r["id"]
        assert digest_clinical()==clinical_before
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
except:
    db.rollback()
    for p in new_paths:p.unlink(missing_ok=True)
    raise
assert digest_clinical()==clinical_before
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
remaining=sum(not str(r[0] or "").strip() or not validpdf(ROOT/str(r[0])) for r in db.execute("SELECT pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'"))
linked=sum(len(items) for items,_,_ in pending)
assert remaining==193-linked
report={"phase":35,"before_missing":193,"after_missing":remaining,"all_backlog_groups":len(queue),"all_categories":dict(Counter(x[2] for x in queue)),"selected_groups":len(selected),"checked_variants":sum(len(x[1]) for x in selected),"approved_groups":len(pending),"new_pdf_links":linked,"clinical_unchanged":True,"sqlite_before_sha":before,"sqlite_after_sha":hashlib.sha256(DB.read_bytes()).hexdigest(),"outcomes":outcomes,
 "not_attempted":[{"rpl_id":key[0],"upd_id":key[1],"product_name":key[2],"variants":[r["id"] for r in items],"reason":kind} for key,items,kind in queue if kind!="official_rpl_download"]}
(OUT/"result.json").write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf8")
print("PARTIA35_RESULT",json.dumps({k:v for k,v in report.items() if k not in ("outcomes","not_attempted")},ensure_ascii=False))
db.close()
