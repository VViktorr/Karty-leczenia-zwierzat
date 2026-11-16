#!/usr/bin/env python3
"""Partia 41: rescan existing ChPL with foreign trade names and multi-strength products.

Search name first, then potency, dosage form, product identity and available
registration IDs. An explicit equivalent name listed in the SPC product-name
section is valid, but a random mention elsewhere is not. Auto-link only where
the same UPD ID is shown by the PDF filename, the document clearly supports
the name and strength, and no competing identity appears. Others: audit only.
Never touch clinical fields.
"""
import csv,hashlib,json,re,sqlite3,unicodedata
from pathlib import Path
from collections import Counter,defaultdict
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(exist_ok=True)
def norm(s):
    s=unicodedata.normalize("NFKD",str(s or "").casefold())
    return re.sub(r"\s+"," ","".join(c for c in s if not unicodedata.combining(c)).replace("–","-").replace(",",".")).strip()
def good(rel):
    if not str(rel or "").strip():return False
    p=ROOT/rel
    try:return p.is_file() and p.stat().st_size>5000 and p.open("rb").read(5)==b"%PDF-"
    except (OSError,ValueError):return False
def strength(s):
    return [(a,u) for a,u in re.findall(r"(?<![a-z0-9])(\d+(?:\.\d+)?)\s*(mg/ml|mcg/ml|mg|mcg|g/l)\b",norm(s))]
def brand(name):
    s=norm(name)
    phrases=("simparica trio","simparica","porcilis coli","porcilis pcv","porcilis porcoli","ingelvac circoflex","coffeinum bioveta","l-spec-vmd")
    for b in phrases:
        if s==b or s.startswith(b+" "):return b
    return s.split()[0] if s.split() else ""
def hit(s,b):
    pat=r"(?<![a-z0-9])"+r"\s+".join(re.escape(z) for z in b.split())+r"(?![a-z0-9])"
    out=[m for m in re.finditer(pat,s)]
    if b=="draxxin":out=[m for m in out if not re.match(r"\s+plus\b",s[m.end():m.end()+12])]
    if b=="simparica":out=[m for m in out if not re.match(r"\s+trio\b",s[m.end():m.end()+12])]
    return out
def enough(s):
    head=s[:25000]
    return bool(("summary of product characteristics" in head and "contraindications" in s)
       or ("charakterystyka" in head and "przeciwwskazania" in s))
def form(name):
    v=norm(name)
    if "spot-on" in v or "nakrapian" in v or "pipet" in v:return "spot"
    if "tablet" in v or "tab." in v:return "tablet"
    if "wstrzykiwan" in v or " inj" in v:return "injection"
    if "roztwor doustny" in v or "oral" in v:return "oral"
    return ""
def form_ok(f,window):
    if not f:return True
    return {"spot":("spot-on","nakrapian","pipette"),
      "tablet":("tablet","chewable"),
      "injection":("injection","wstrzykiwan","injectie","iniettabile","inyectable"),
      "oral":("oral","doustny")}[f] and any(t in window for t in {
      "spot":("spot-on","nakrapian","pipette"),
      "tablet":("tablet","chewable"),
      "injection":("injection","wstrzykiwan","injectie","iniettabile","inyectable"),
      "oral":("oral","doustny")}[f])
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
preserve=[x["name"] for x in db.execute("PRAGMA table_info(variants)") if x["name"]!="pdf_path"]
def stable_hash():
    h=hashlib.sha256()
    for r in db.execute("SELECT "+",".join('"'+x+'"' for x in preserve)+" FROM variants ORDER BY id"):
        h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
    return h.hexdigest()
before=stable_hash()
rows=[dict(x) for x in db.execute("""SELECT v.id,v.name,v.symbol,v.upd_id,v.rpl_id,v.pdf_path,p.name AS subiekt
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%'
 ORDER BY v.id""")]
missing=[r for r in rows if not good(r["pdf_path"])]
brands={brand(r["name"]) for r in missing}
docs=defaultdict(list)
num_pdfs=0
for p in sorted((ROOT/"data/ChPL").rglob("*.pdf")):
    rel=p.relative_to(ROOT).as_posix()
    if not good(rel):continue
    num_pdfs+=1
    try:
        with fitz.open(p) as d:
            if d.is_encrypted:continue
            s=norm("\n".join(page.get_text(sort=True) for page in d))
    except Exception:continue
    if len(s)<350:continue
    official=enough(s)
    # Product names from section 1 and 2 of the SPC. This may contain aliases
    # for several countries, and multiple concentrations of a single product.
    head=s[:6500]
    in_heading=bool(re.search(r"(?:1\.\s*name of the veterinary medicinal product|1\.\s*nazwa weterynaryjnego produktu leczniczego|2\.\s*nazwa produktu leczniczego weterynaryjnego)",head))
    upd=re.search(r"/UPD_(\d+)_",rel,re.I)
    rpl=re.search(r"/RPL_(\d+)_",rel,re.I)
    for b in brands:
        hits=hit(head,b)
        if not hits:continue
        snippets=[head[max(0,m.start()-65):min(len(head),m.end()+550)] for m in hits[:40]]
        docs[b].append({"rel":rel,"official":official,"heading":in_heading,"upd":upd.group(1) if upd else None,"rpl":rpl.group(1) if rpl else None,"snippets":snippets})
decisions=[]
for r in missing:
    b=brand(r["name"])
    strengths=strength(r["name"])
    f=form(r["name"])
    options=[]
    for d in docs.get(b,[]):
        near=d["snippets"]
        strength_ok=bool(strengths) and all(any(re.search(r"(?<!\d)"+re.escape(v).replace(r"\.",r"[.,]")+r"\s*"+re.escape(u)+r"\b",w) for w in near) for v,u in strengths)
        form_verified=any(form_ok(f,w) for w in near)
        id_upd=bool(d["upd"] and r["upd_id"] and str(d["upd"]).lstrip("0")==str(r["upd_id"]).lstrip("0"))
        id_rpl=bool(d["rpl"] and r["rpl_id"] and str(d["rpl"]).lstrip("0")==str(r["rpl_id"]).lstrip("0"))
        conflict=bool(d["rpl"] and r["rpl_id"] and not id_rpl)
        # A document with a different base/combination name is unsafe.
        def wrong_combination(b,w):
            return b in ("draxxin","simparica") and any(re.search(r"\b"+b+r"\s+plus\b",x[:140]) for x in w)
        verdict="candidate"
        if not d["official"] or not d["heading"]:verdict="manual_not_verified_spc"
        elif conflict:verdict="manual_rpl_conflict"
        elif not strength_ok:verdict="manual_strength_not_confirmed"
        elif not form_verified:verdict="manual_form_not_confirmed"
        elif wrong_combination(b,near):verdict="manual_other_combination"
        elif id_upd:verdict="verified_upd_name_strength_form"
        elif id_rpl:verdict="verified_rpl_name_strength_form"
        else:verdict="manual_no_matching_registration_id"
        options.append({"pdf":d["rel"],"verdict":verdict,"official_spc":d["official"],"name_section":d["heading"],"strength_ok":strength_ok,"form_ok":form_verified,
         "upd_id_matches":id_upd,"rpl_id_matches":id_rpl})
    verified=[d for d in options if d["verdict"].startswith("verified_")]
    # Different versions of the SAME registration can coexist; choose none if multiple distinct files.
    if len(verified)==1:status="to_link"
    elif len(verified)>1:status="manual_multiple_approved_documents"
    elif options:status="manual_review"
    else:status="no_name_in_pdf_heading"
    decisions.append({"id":r["id"],"name":r["name"],"symbol":r["symbol"],"subiekt":r["subiekt"],"rpl":r["rpl_id"],"upd":r["upd_id"],"status":status,"candidates":options})
try:
    db.execute("BEGIN IMMEDIATE")
    for d in decisions:
        if d["status"]!="to_link":continue
        choice=next(x for x in d["candidates"] if x["verdict"].startswith("verified_"))
        old=db.execute("SELECT pdf_path FROM variants WHERE id=?",(d["id"],)).fetchone()[0]
        if old and good(old):
            d["status"]="existing_kept"
            continue
        db.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(choice["pdf"],))
        assert db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(choice["pdf"],d["id"])).rowcount==1
        d["status"]="linked"
        d["assigned_pdf"]=choice["pdf"]
    assert stable_hash()==before,"Clinical data change detected"
    assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
    assert not db.execute("PRAGMA foreign_key_check").fetchall()
    db.commit()
except:
    db.rollback()
    raise
remaining=sum(not good(x[0]) for x in db.execute("""SELECT pdf_path FROM variants WHERE scope IS NULL OR lower(scope) NOT LIKE '%archiwal%'"""))
result={"partia":41,"active_variants":len(rows),"pdfs_read":num_pdfs,"missing_before":len(missing),"missing_after":remaining,
 "counts":dict(Counter(x["status"] for x in decisions)),"clinical_unchanged":True,
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),"decisions":decisions}
(OUT/"partia41_wynik.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
with (OUT/"partia41_nazwy_subiekt_kandydaci.csv").open("w",encoding="utf-8-sig",newline="") as f:
    w=csv.writer(f,delimiter=";")
    w.writerow(["ID","Nazwa leku","Symbol Subiekta","Nazwa Subiekta","Status","PDF powiazany","PDF kandydaci"])
    for r in decisions:w.writerow([r["id"],r["name"],r["symbol"],r["subiekt"],r["status"],r.get("assigned_pdf","")," | ".join(x["pdf"]+" ["+x["verdict"]+"]" for x in r["candidates"])])
print("PARTIA41",json.dumps({k:v for k,v in result.items() if k!="decisions"},ensure_ascii=False))
db.close()
