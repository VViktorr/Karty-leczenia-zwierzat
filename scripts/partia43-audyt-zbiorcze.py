#!/usr/bin/env python3
"""Partia 43: non-mutating document-section audit of three user-uploaded Polish EMA combined PDFs."""
from pathlib import Path
import sqlite3, json, re, csv, unicodedata, hashlib
from collections import defaultdict
import fitz
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(parents=True,exist_ok=True)
PDFS={
 "librela":"data/ChPL/UPD/ema-combined-v5180-librela-vra0000340267-2026-07-02-pl.pdf",
 "draxxin":"data/ChPL/UPD/ema-combined-v77-draxxin-vnra19587-vnra19588-2025-04-25-pl.pdf",
 "loxicom":"data/ChPL/UPD/ema-combined-v141-loxicom-vra0000340368-2026-11-03-pl.pdf"}
def norm(s):
 s=unicodedata.normalize("NFKD",str(s or "").lower().replace("ł","l"))
 return re.sub(r"\s+"," ","".join(c for c in s if not unicodedata.combining(c)).replace(",",".")).strip()
con=sqlite3.connect(f"file:{DB}?mode=ro",uri=True)
con.row_factory=sqlite3.Row
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not con.execute("PRAGMA foreign_key_check").fetchall()
records={}
for brand,path in PDFS.items():
 p=ROOT/path
 assert p.is_file() and p.read_bytes()[:5]==b"%PDF-",path
 with fitz.open(p) as d:
  assert not d.is_encrypted and len(d)>1
  pages=len(d)
  # Full exact text from each PDF, not filename-based assertions.
  texts=[norm(page.get_text(sort=True)) for page in d]
 raw=" ".join(texts)
 assert len(raw)>1000,(brand,"image-only scan")
 assert "charakterystyka" in raw[:20000] and "przeciwwskazania" in raw and "karencji" in raw,(brand,"not official ChPL")
 headings=[]
 for pg,t in enumerate(texts):
  for match in re.finditer(r"\b(?:nazwa weterynaryjnego produktu leczniczego|nazwa produktu leczniczego weterynaryjnego|nazwa produktu leczniczego)\b",t):
   segment=t[max(0,match.start()-20):match.start()+230]
   if brand in segment:
    headings.append({"page":pg+1,"context":segment})
 positions=[]
 for pg,t in enumerate(texts):
  for m in list(re.finditer(r"(?<!\w)"+brand+r"(?!\w)",t))[:80]:
   positions.append({"page":pg+1,"context":t[max(0,m.start()-80):m.end()+420]})
 records[brand]={"pdf":path,"pages":pages,"text_chars":len(raw),"headings":headings[:30],"contexts":positions[:50],"concentrations":sorted(set(re.findall(r"(?<!\d)\d+(?:\.\d+)?\s*mg\s*/\s*ml",raw)))[:70],"pdf_sha256":hashlib.sha256(p.read_bytes()).hexdigest()}
rows=[dict(x) for x in con.execute("""SELECT v.id,v.name,v.symbol,v.rpl_id,v.upd_id,v.pdf_path,p.name AS subiekt,v.scope FROM variants v JOIN products p ON p.symbol=v.symbol WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%' ORDER BY v.id""")]
related=[]
for r in rows:
 name=norm(r["name"])+" "+norm(r["subiekt"])
 for brand in PDFS:
  if re.search(r"\b"+brand+r"\b",name) and not (brand=="draxxin" and "draxxin plus" in name):
   related.append({**r,"brand":brand})
result={"partia":43,"read_only":True,"documents":records,"related_variants":related,"missing_related":[r for r in related if not str(r["pdf_path"] or "").strip()],"counts":{"related":len(related),"unlinked":sum(not str(r["pdf_path"] or "").strip() for r in related)}}
(OUT/"partia43_analiza_trzech_zbiorczych_pdf.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
with (OUT/"partia43_warianty_subiekt.csv").open("w",encoding="utf-8-sig",newline="") as f:
 w=csv.writer(f,delimiter=";")
 w.writerow(["Lek","Wariant ID","Nazwa wariantu","Nazwa Subiekta","Symbol","RPL","UPD","Dotychczasowy PDF"])
 for r in related:w.writerow([r["brand"],r["id"],r["name"],r["subiekt"],r["symbol"],r["rpl_id"],r["upd_id"],r["pdf_path"]])
print("PARTIA43_SUMMARY",json.dumps({k:v for k,v in result.items() if k not in ("documents","related_variants","missing_related")},ensure_ascii=False))
for k,v in records.items():
 print("PARTIA43_DOCUMENT",json.dumps({"brand":k,"pages":v["pages"],"headings":v["headings"][:20],"contexts":v["contexts"][:8]},ensure_ascii=False))
con.close()
