#!/usr/bin/env python3
"""Partia 42: four newly uploaded official Polish EMA combined ChPLs.

Verify the relevant name+strength in the same product-heading context, check dosage
form and target species where explicit, keep all clinical content untouched.
Only link unique, clearly matching Polish official ChPL files.
"""
import csv,hashlib,json,re,sqlite3,unicodedata
from pathlib import Path
from collections import Counter
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(exist_ok=True)
PDFS={
 "naxcel":"data/ChPL/UPD/ema-combined-v79-naxcel-vnra14173-2024-04-22-pl.pdf",
 "metacam":"data/ChPL/UPD/ema-combined-v33-metacam-vra0153-2024-10-31-pl.pdf",
 "stronghold":"data/ChPL/UPD/ema-combined-v50-stronghold-vnra18893-2025-03-05-pl.pdf",
 "simparica":"data/ChPL/UPD/ema-combined-v3991-simparica-vnra18772-2025-02-17-pl.pdf"
}
def n(s):
    v=unicodedata.normalize("NFKD",str(s or "").lower())
    return re.sub(r"\s+"," ","".join(c for c in v if not unicodedata.combining(c)).replace(",",".")).strip()
def good(rel):
    if not str(rel or "").strip():return False
    p=ROOT/rel
    try:return p.is_file() and p.stat().st_size>6000 and p.open("rb").read(5)==b"%PDF-"
    except (OSError,ValueError):return False
def strong(name):
    return [(a,u) for a,u in re.findall(r"(?<![a-z0-9])(\d+(?:\.\d+)?)\s*(mg/ml|mg)\b",n(name))]
def has_concentration(fragment,a,u):
    # The same name-strength block, with punctuation normalized. Avoid a 20mg match on 120mg.
    return bool(re.search(r"(?<!\d)"+re.escape(a)+r"\s*"+re.escape(u)+r"\b",fragment))
def proper_brand(r):
    v=n(r["name"])
    sv=n(r["subiekt"])
    for name in PDFS:
        if name=="simparica" and ("simparica trio" in v or "simparica trio" in sv):return None
        if v.startswith(name+" ") or v==name or sv.startswith(name+" ") or sv==name:
            return name
    return None
def target_species_match(brand,r,block):
    # Apply species checks only if specified by the catalog.
    name=n(r["name"])+" "+n(r["subiekt"])
    if brand=="naxcel" or "bydlo" in name or "bydl" in name:
        return ("bydlo" in block or "bydlat" in block or "cattle" in block)
    if "kot" in name:return ("kot" in block or "kotow" in block or "cat" in block)
    if "pies" in name or "psow" in name:return ("psow" in block or "pies" in block or "dogs" in block)
    return True
def form_match(brand,r,block):
    v=n(r["name"])+" "+n(r["subiekt"])
    if brand=="naxcel" or " inj" in v or "wstrzykiwan" in v:
        return "wstrzykiwan" in block or "injection" in block
    if "tab" in v or brand=="simparica":return "tablet" in block
    if "pipet" in v or "spot" in v or brand=="stronghold":return "nakrapian" in block or "spot-on" in block
    return True
def measure_for(r,brand):
    v=strong(r["name"])
    if v:return v
    if brand=="naxcel":
        return strong(r["subiekt"])[:1]
    if brand=="metacam":
        # Subiekt 2% = 20mg/ml, 0.5% = 5mg/ml
        sub=n(r["subiekt"])
        if "2%" in sub:return [("20","mg/ml")]
        if "0.5%" in sub:return [("5","mg/ml")]
    if brand=="stronghold":
        return strong(r["subiekt"])[:1]
    return []
db=sqlite3.connect(DB)
db.row_factory=sqlite3.Row
db.execute("PRAGMA foreign_keys=ON")
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
no_pdf_cols=[x["name"] for x in db.execute("PRAGMA table_info(variants)") if x["name"]!="pdf_path"]
def clinical_hash():
    h=hashlib.sha256()
    for r in db.execute("SELECT "+",".join('"'+c+'"' for c in no_pdf_cols)+" FROM variants ORDER BY id"):
        h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
    return h.hexdigest()
before=clinical_hash()
documents={}
for drug,rel in PDFS.items():
    assert good(rel),rel
    with fitz.open(ROOT/rel) as pdf:
        assert pdf.page_count>1 and not pdf.is_encrypted
        s=n("\n".join(p.get_text(sort=True) for p in pdf))
        pages=pdf.page_count
    # Official product-specific EMA document, not an arbitrary text mentioning the brand.
    assert len(s)>1000,(drug,"image-only pdf")
    assert ("charakterystyka" in s[:35000] and "przeciwwskazania" in s and "karencji" in s),(drug,"not complete Polish SPC")
    # Product names and several strengths often in one SPC. Read up to first 2500 chars after
    # each exact product-name occurrence; do not accept a medicine only in the annex index.
    matches=list(re.finditer(r"(?<![a-z0-9])"+re.escape(drug)+r"(?![a-z0-9])",s))
    assert matches,(drug,"product name missing")
    windows=[s[max(0,m.start()-50):m.end()+1800] for m in matches[:200]]
    if drug=="simparica":
        windows=[w for w in windows if not re.search(r"simparica\s+trio",w[:120])]
    documents[drug]={"path":rel,"pages":pages,"text_chars":len(s),"matches":len(matches),"windows":windows}
rows=[dict(r) for r in db.execute("""SELECT v.id,v.name,v.symbol,v.rpl_id,v.upd_id,v.pdf_path,p.name AS subiekt
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%' ORDER BY v.id""")]
decisions=[]
for r in rows:
    drug=proper_brand(r)
    if not drug:continue
    strengths=measure_for(r,drug)
    doc=documents[drug]
    # Require a single individual product heading or a paragraph describing exactly this
    # strength. Prevent numerical matches from unrelated product sections.
    local=[w for w in doc["windows"] if strengths and all(has_concentration(w,a,u) for a,u in strengths)]
    match=[w for w in local if target_species_match(drug,r,w) and form_match(drug,r,w)]
    # Keep consistent product authorization where present. EMA combined names in filenames
    # often don't have UPD IDs, so do not infer an ID mismatch from filename.
    if good(r["pdf_path"]):
        status="existing_pdf_kept"
    elif not strengths:status="manual_no_explicit_strength"
    elif not local:status="manual_strength_not_near_name"
    elif not match:status="manual_species_or_form_not_confirmed"
    else:status="verified_to_link"
    decisions.append({"id":r["id"],"name":r["name"],"subiekt":r["subiekt"],"symbol":r["symbol"],"rpl":r["rpl_id"],"upd":r["upd_id"],"old_pdf":r["pdf_path"],"new_pdf":doc["path"],"strengths":strengths,"status":status,"local_name_strength_hits":len(local),"form_species_hits":len(match)})
try:
    db.execute("BEGIN IMMEDIATE")
    for d in decisions:
        if d["status"]!="verified_to_link":continue
        old=db.execute("SELECT pdf_path FROM variants WHERE id=?",(d["id"],)).fetchone()[0]
        assert not good(old)
        db.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)",(d["new_pdf"],))
        assert db.execute("UPDATE variants SET pdf_path=? WHERE id=?",(d["new_pdf"],d["id"])).rowcount==1
        d["status"]="linked"
    assert clinical_hash()==before,"clinical data changed"
    assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
    assert not db.execute("PRAGMA foreign_key_check").fetchall()
    db.commit()
except:
    db.rollback()
    raise
result={"partia":42,"documents":{k:{f:v for f,v in d.items() if f!="windows"} for k,d in documents.items()},
  "statuses":dict(Counter(d["status"] for d in decisions)),"linked_by_brand":dict(Counter(d["new_pdf"].split("/")[-1].split("-")[2] if "-" in d["new_pdf"] else d["new_pdf"] for d in decisions if d["status"]=="linked")),
  "clinical_unchanged":True,"sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),"decisions":decisions}
(OUT/"partia42_nowe_zbiorcze_pdf_wynik.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
with (OUT/"partia42_nowe_zbiorcze_pdf_nazwy_subiekt.csv").open("w",encoding="utf-8-sig",newline="") as f:
    w=csv.writer(f,delimiter=";")
    w.writerow(["ID","Nazwa wariantu","Nazwa w Subiekcie","Symbol","Status","Nowy PDF","Dopasowane moce"])
    for d in decisions:
        w.writerow([d["id"],d["name"],d["subiekt"],d["symbol"],d["status"],d["new_pdf"]," ".join(a+" "+u for a,u in d["strengths"])])
print("PARTIA42",json.dumps({k:v for k,v in result.items() if k!="decisions"},ensure_ascii=False))
db.close()
