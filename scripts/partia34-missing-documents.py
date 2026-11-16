#!/usr/bin/env python3
"""Partia 34: brakujace dokumenty, alternatywne sciezki i wczesniej potwierdzone powiazania."""
import hashlib, json, re, sqlite3, unicodedata
from collections import defaultdict, Counter
from pathlib import Path
import pymupdf

ROOT=Path.cwd()
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"partia34_raport"
OUT.mkdir(exist_ok=True)
BASE="3948226ef30b27d4c70658106de786770494d2c65447a4142403f5aff4b6ac09"
sha=hashlib.sha256(DB.read_bytes()).hexdigest()
assert sha==BASE, f"SQLite SHA mismatch: {sha}"
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
assert db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]==2944
def norm(s):
    s=unicodedata.normalize("NFKD",str(s or "").casefold())
    return re.sub(r"[^a-z0-9]+"," ","".join(c for c in s if not unicodedata.combining(c))).strip()
def validpdf(p):
    return p.is_file() and p.open("rb").read(5)==b"%PDF-"
def ident(v):
    s=str(v or "").strip()
    return s if re.fullmatch(r"\d{4,15}",s) and int(s)>0 else ""
def product_root(name):
    return next((x for x in norm(name).split() if len(x)>=4 and not x.isdigit()),"")
def strengths(name):
    s=unicodedata.normalize("NFKD",str(name or "").casefold())
    s="".join(c for c in s if not unicodedata.combining(c))
    return [(a.replace(",","."),u) for a,u in re.findall(r"(?<!\w)(\d+(?:[,.]\d+)?)\s*(mg|mcg|ug|g|iu|%)\b",s)]
def equivalent_name(name,text):
    words=[w for w in norm(name).split() if len(w)>=4 and not w.isdigit()]
    ignore={"roztwor","zawiesina","tabletki","tabletka","proszek","doustny","doustna","wstrzykiwan","szczepionka","opakowanie","iniekcja","fiolka","ampulka","pasta","masc"}
    words=[w for w in words if w not in ignore]
    if not words or not re.search(r"\b"+re.escape(words[0])+r"\b",text):return False,"name_mismatch"
    if len(words)>1 and len(words[1])>=5 and words[1] not in {"flavour","flavor","forte"}:
        if not re.search(r"\b"+re.escape(words[1])+r"\b",text):return False,"second_name_token"
    for amount,unit in strengths(name):
        pat=r"\b"+re.escape(amount).replace(r"\.",r"[., ]")+r"\s*"+re.escape(unit)+(r"\b" if unit!="%" else "")
        if not re.search(pat,text):return False,"strength_mismatch"
    return True,"matched"

rows=[dict(r) for r in db.execute("SELECT id,name,rpl_id,upd_id,pdf_path,scope FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'")]
assert len(rows)==2902
missing=[r for r in rows if not str(r["pdf_path"] or "").strip() or not validpdf(ROOT/str(r["pdf_path"]))]
assert len(missing)==209,len(missing)
cols=[r[1] for r in db.execute("PRAGMA table_info(variants)") if r[1]!="pdf_path"]
def clinical_sha():
    sql="SELECT "+",".join('"'+c+'"' for c in cols)+" FROM variants ORDER BY id"
    return hashlib.sha256(json.dumps([list(x) for x in db.execute(sql)],ensure_ascii=False,default=str).encode()).hexdigest()
before_clinical=clinical_sha()

# Catalogue direct PDF indexes plus validated existing links from the same medicine ID.
indexed={"RPL":defaultdict(list),"UPD":defaultdict(list)}
for source in indexed:
    for p in (ROOT/"data/ChPL"/source).glob(source+"_*.pdf"):
        m=re.match(r"^"+source+r"_(\d+)_",p.name)
        if m:indexed[source][m[1]].append(p)
peer=defaultdict(set)
for r in rows:
    p=ROOT/str(r["pdf_path"] or "")
    if not validpdf(p):continue
    rid,uid=ident(r["rpl_id"]),ident(r["upd_id"])
    if rid:peer[("RPL",rid,product_root(r["name"]))].add(p)
    if uid:peer[("UPD",uid,product_root(r["name"]))].add(p)

groups=defaultdict(list)
for r in missing:
    key=(ident(r["rpl_id"]),ident(r["upd_id"]),product_root(r["name"]))
    groups[key].append(r)
cache={}
def doc_info(p):
    if p in cache:return cache[p]
    data={"path":p.relative_to(ROOT).as_posix(),"quality":"unreadable","pages":0,"normalized_text":""}
    try:
        if not validpdf(p):raise ValueError("bad PDF")
        with pymupdf.open(p) as pdf:
            if not len(pdf) or pdf.is_encrypted:raise ValueError("empty or encrypted")
            data["pages"]=len(pdf)
            body=" ".join(pg.get_text(sort=True) for pg in pdf[:min(8,len(pdf))])
        n=norm(body)
        data["normalized_text"]=n
        data["quality"]=("pl_chpl" if len(n)>=300 and
            re.search(r"charakterystyk.{0,120}(produktu|leczniczego)",n) and
            sum(x in n for x in ("wskazania","karencji","dawkowanie","przeciwwskazania","podania","weterynaryjnego"))>=2
            else "needs_manual_review")
    except Exception as e:data["reason"]=str(e)[:150]
    cache[p]=data
    return data

def candidates(key):
    rid,uid,root=key
    paths=defaultdict(set)
    if rid:
        for p in indexed["RPL"].get(rid,[]):paths[p].add("exact_rpl_filename")
        for p in peer[("RPL",rid,root)]:paths[p].add("same_rpl_peer_link")
    if uid:
        for p in indexed["UPD"].get(uid,[]):paths[p].add("exact_upd_filename")
        for p in peer[("UPD",uid,root)]:paths[p].add("same_upd_peer_link")
    return [(p,sorted(tags)) for p,tags in sorted(paths.items(),key=lambda x:str(x[0]))]

tasks=[]
for key,items in groups.items():
    opts=candidates(key)
    kind=("missing_identifiers" if not key[0] and not key[1] else
          "no_repository_match" if not opts else
          "peer_document" if any("peer" in tag for _,tags in opts for tag in tags) else "existing_document")
    tasks.append((key,items,opts,kind))
# New tasks first, without rerunning all 50 old OCR cases.
priority={"peer_document":0,"no_repository_match":1,"missing_identifiers":2,"existing_document":3}
tasks.sort(key=lambda x:(priority[x[3]],-len(x[1]),x[0]))
selected=tasks[:50]
fixes=[]
details=[]
for key,items,opts,kind in selected:
    item={"ids":[r["id"] for r in items],"names":sorted({r["name"] for r in items}),"rpl_id":key[0],"upd_id":key[1],"category":kind,"candidates":[]}
    approved=[]
    for p,tags in opts:
        info=doc_info(p)
        results=[equivalent_name(r["name"],info["normalized_text"]) for r in items]
        check=(info["quality"]=="pl_chpl" and all(ok for ok,_ in results))
        item["candidates"].append({"path":info["path"],"sources":tags,"pages":info["pages"],"quality":info["quality"],"check":check,"per_variant_reason":[reason for _,reason in results]})
        if check:approved.append(p)
    # An unverified ID, no document, or multiple different matches cannot be auto-linked.
    if len(approved)==1 and (key[0] or key[1]):
        rel=approved[0].relative_to(ROOT).as_posix()
        item["status"]="approved"
        item["pdf"]=rel
        fixes.append((items,rel))
    else:
        item["status"]="review_needed"
        if not opts:item["reason"]="search_official_RPL_UPD_community_register"
        elif len(approved)>1:item["reason"]="ambiguous_multiple_documents"
        else:item["reason"]="no_confirmed_name_strength_Polish_ChPL"
    details.append(item)

if fixes:
    try:
        db.execute("BEGIN IMMEDIATE")
        for items,rel in fixes:
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(rel,))
            for r in items:
                old=str(r["pdf_path"] or "").strip()
                assert not old or not validpdf(ROOT/old)
                c=db.execute("UPDATE variants SET pdf_path=? WHERE id=? AND COALESCE(rpl_id,'')=COALESCE(?, '') AND COALESCE(upd_id,'')=COALESCE(?, '')",(rel,r["id"],r["rpl_id"],r["upd_id"]))
                assert c.rowcount==1,r["id"]
        assert clinical_sha()==before_clinical
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
    except:
        db.rollback()
        raise
assert clinical_sha()==before_clinical
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
remaining=sum(not str(x[0] or "").strip() or not validpdf(ROOT/str(x[0])) for x in db.execute("SELECT pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'"))
added=sum(len(items) for items,_ in fixes)
assert remaining==209-added
summary={"phase":34,"before_missing":209,"after_missing":remaining,"total_groups":len(tasks),"all_categories":dict(Counter(x[3] for x in tasks)),"selected_groups":len(selected),"checked_variants":sum(len(x[1]) for x in selected),"selected_categories":dict(Counter(x[3] for x in selected)),"approved_groups":len(fixes),"new_pdf_links":added,"clinical_unchanged":True,"before_sha":sha,"after_sha":hashlib.sha256(DB.read_bytes()).hexdigest(),"details":details}
(OUT/"result.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf8")
(OUT/"needs_official_search.json").write_text(json.dumps([x for x in details if x["status"]!="approved"],ensure_ascii=False,indent=2),encoding="utf8")
print("PARTIA34_RESULT",json.dumps({k:v for k,v in summary.items() if k!="details"},ensure_ascii=False))
db.close()
