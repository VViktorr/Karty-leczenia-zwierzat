#!/usr/bin/env python3
"""Partia 44: fresh READ-ONLY audit of live SQLite and every checked-in ChPL.

Only report candidate PDF matches; never replace clinical data or existing links.
"""
import csv, hashlib, json, re, sqlite3, unicodedata
from collections import Counter
from pathlib import Path
import fitz

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(parents=True,exist_ok=True)

def norm(v):
    x=unicodedata.normalize("NFKD",str(v or "").casefold().replace("ł","l"))
    return re.sub(r"\s+"," ","".join(c for c in x if not unicodedata.combining(c)).replace(",",".")).strip()

def good(rel):
    if not str(rel or "").strip():return False
    p=ROOT/str(rel)
    try:return p.is_file() and p.stat().st_size>1000 and p.open("rb").read(5)==b"%PDF-"
    except (OSError, ValueError):return False

def strength(s):
    return sorted(set(re.findall(r"(?<!\d)\d+(?:\.\d+)?\s*(?:mg/ml|mcg/ml|mg|mcg|g/ml|g/l)\b",norm(s))))

def token_present(text,word):
    return bool(re.search(r"(?<![a-z0-9])"+re.escape(word)+r"(?![a-z0-9])",text))

def brand(name):
    s=norm(name).replace("_"," ")
    return re.split(r"\s+",s)[0] if s else ""

def conflict(name,doc):
    # Product combinations are not interchangeable.
    n=norm(name); d=norm(doc)
    for base,suffix in [("draxxin","plus"),("simparica","trio"),("naxcel","100")]:
        if base in n and (base+" "+suffix) in d and (base+" "+suffix) not in n:
            return True
    return False

con=sqlite3.connect(f"file:{DB}?mode=ro",uri=True)
con.row_factory=sqlite3.Row
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
rows=[dict(r) for r in con.execute("""
 SELECT v.id AS variant_id, v.symbol, v.name AS variant_name,
        v.rpl_id, v.upd_id, v.pdf_path, v.scope,
        p.name AS subiekt_name
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%'
 ORDER BY v.id""")]
for r in rows:
    r["status"]="POWIAZANIE_OK" if good(r["pdf_path"]) else (
        "BRAK_POWIAZANIA" if not str(r["pdf_path"] or "").strip()
        else "BRAK_PLIKU_LUB_USZKODZONY_PDF")
missing=[r for r in rows if r["status"]!="POWIAZANIE_OK"]
docs=[]
for p in sorted((ROOT/"data/ChPL").rglob("*.pdf")):
    rel=p.relative_to(ROOT).as_posix()
    entry={"path":rel,"readable":False}
    try:
        if not good(rel):raise ValueError("invalid PDF header")
        with fitz.open(p) as f:
            if f.is_encrypted:raise ValueError("encrypted")
            # Combined documents may have sections after page 1.
            full=norm(" ".join(page.get_text(sort=True) for page in f))
            entry["pages"]=len(f)
        entry["readable"]=len(full)>300
        entry["text"]=full
        entry["file_name"]=norm(p.stem.replace("_"," ").replace("-"," "))
        mu=re.search(r"/UPD_(\d+)_",rel,re.I)
        mr=re.search(r"/RPL_(\d+)_",rel,re.I)
        entry["upd"]=mu.group(1) if mu else ""
        entry["rpl"]=mr.group(1) if mr else ""
        entry["official"]=("charakterystyka" in full and "przeciwwskazania" in full) or (
            "summary of product characteristics" in full and "contraindications" in full)
    except Exception as ex:
        entry["error"]=str(ex)
    docs.append(entry)

def id_match(a,b):
    return bool(a and b and str(a).lstrip("0")==str(b).lstrip("0"))

def score_candidate(r,d):
    if not d["readable"]:return None
    n=norm(r["variant_name"]+" "+r["subiekt_name"])
    b=brand(r["variant_name"]) or brand(r["subiekt_name"])
    pdf=d["text"]; fn=d["file_name"]
    idok=id_match(r["upd_id"],d.get("upd")) or id_match(r["rpl_id"],d.get("rpl"))
    nameok=bool(b and len(b)>3 and token_present(pdf,b))
    fileok=bool(b and len(b)>3 and token_present(fn,b))
    # Avoid huge false-positive lists caused by incidental medicine mentions.
    if not idok and not fileok:return None
    wanted=strength(n)
    present=[v for v in wanted if re.search(r"(?<!\d)"+re.escape(v).replace(r"\ ",r"\s*")+r"\b",pdf)]
    all_strength=bool(wanted) and len(present)==len(wanted)
    blocked=conflict(n,fn)
    status=("POTENCJALNY_ID_NAZWA_MOC" if idok and (nameok or fileok) and all_strength and d["official"] and not blocked
            else "POTENCJALNY_ID_NAZWA" if idok and (nameok or fileok) and d["official"] and not blocked
            else "SPRAWDZ_RECZNIE")
    return {"pdf":d["path"],"status":status,"id_match":idok,
            "name_in_pdf":nameok,"name_in_filename":fileok,
            "strengths_from_catalog":wanted,"strengths_found":present,
            "official_spc":bool(d.get("official")),"potential_conflict":blocked}

for r in missing:
    opts=[v for d in docs if (v:=score_candidate(r,d))]
    opts.sort(key=lambda c:(
        not c["id_match"], c["status"]!="POTENCJALNY_ID_NAZWA_MOC",
        c["status"]!="POTENCJALNY_ID_NAZWA",c["pdf"]))
    r["candidates"]=opts
    r["candidate_count"]=len(opts)
    r["recommended_pdf"]=opts[0]["pdf"] if opts else ""
    r["recommended_status"]=opts[0]["status"] if opts else "BRAK_KANDYDATA"

counts=Counter(r["status"] for r in rows)
report={
 "partia":44,"read_only":True,"active_variants":len(rows),
 "missing_or_broken":len(missing),"status_counts":dict(counts),
 "pdf_files":len(docs),"readable_pdf_files":sum(d["readable"] for d in docs),
 "unique_subiekt_symbols_missing":len({r["symbol"] for r in missing}),
 "candidate_status_counts":dict(Counter(r["recommended_status"] for r in missing)),
 "priority_products":{key:sum(key in norm(r["variant_name"]+" "+r["subiekt_name"]) for r in missing)
  for key in ["melovem","meloxidyl","previcox","porcilis"]},
 "db_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "note":"All PDF suggestions are candidates only. No SQLite write; matches require section-level review of drug name, strength, form, species and aliases. Existing good links are preserved.",
 "missing":missing,
 "unreadable_pdfs":[{"path":d["path"],"error":d.get("error","not enough extractable text")} for d in docs if not d["readable"]]
}
fields=["variant_id","variant_name","subiekt_name","symbol","rpl_id","upd_id","status","pdf_path","candidate_count","recommended_pdf","recommended_status"]
with (OUT/"partia44_braki_z_nazwami_subiekta.csv").open("w",encoding="utf-8-sig",newline="") as f:
    w=csv.DictWriter(f,fieldnames=fields,extrasaction="ignore",delimiter=";")
    w.writeheader()
    w.writerows(missing)
(OUT/"partia44_pelny_audyt.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf8")
summary={k:v for k,v in report.items() if k not in ("missing","unreadable_pdfs")}
(OUT/"partia44_podsumowanie.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf8")
print("PARTIA44",json.dumps(summary,ensure_ascii=False))
con.close()
