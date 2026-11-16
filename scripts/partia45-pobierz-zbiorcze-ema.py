#!/usr/bin/env python3
"""Partia 45: download official Polish EMA combined SPCs for missing groups.
Never overwrite existing PDFs, never alter clinical fields, never guess between brands.
"""
from pathlib import Path
from collections import Counter
import hashlib,json,re,sqlite3,unicodedata,urllib.request,urllib.error,time
import fitz

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
DIR=ROOT/"data/ChPL/UPD"
OUT.mkdir(exist_ok=True)
# URLs are directly verified Polish "Combined File of all Documents" links on official EMA product pages.
DOWNLOADS=[
 ("Previcox","600000000610","https://medicines.health.europa.eu/veterinary/pl/documents/download/bf3d5b46-3497-495b-b434-c1df7dc9cc13","ema-combined-previcox-pl-2024-02-29.pdf",["previcox","227 mg"],[1173]),
 ("Porcilis ColiClos","600000003319","https://medicines.health.europa.eu/veterinary/pl/documents/download/bcb9b7aa-28ce-4488-8443-a1cf5edf67b2","ema-combined-porcilis-coliclos-pl-2026-03-14.pdf",["porcilis coliclos"],[342,2700]),
 ("Vectra 3D","600000000151","https://medicines.health.europa.eu/veterinary/pl/documents/download/a14fb789-2104-4a15-8fa0-0bb8e73d6906","ema-combined-vectra-3d-pl-2026-03-13.pdf",["vectra 3d"],[]),
]
EXISTING=[
 ("Melovem","data/ChPL/UPD/ema-combined-v000152-melovem-vnra23478-2025-12-03-pl.pdf",["melovem","30 mg/ml"],[430,431,548,549,843,844],"600000004346")
]
def norm(s):
 s=unicodedata.normalize("NFKD",str(s or "").casefold().replace("ł","l").replace(",","."))
 return " ".join("".join(c for c in s if not unicodedata.combining(c)).split())
def valid_pdf(path):
 try:return path.is_file() and path.stat().st_size>10000 and path.open("rb").read(5)==b"%PDF-"
 except (OSError,ValueError):return False
def pdf_text(path):
 with fitz.open(path) as f:
  if f.is_encrypted:raise ValueError("encrypted")
  return norm(" ".join(page.get_text(sort=True) for page in f)),len(f)
def verify(path,terms):
 content,pages=pdf_text(path)
 for t in terms:
  v=norm(t)
  assert v in content,(path,"missing product/strength",t)
 assert ("charakterystyka" in content and ("przeciwwskazania" in content or "ostrzezenia" in content)),(path,"not Polish SPC")
 assert pages>=2,(path,"too few pages")
 return pages
def download(url,dst):
 if valid_pdf(dst):return "already_exists"
 assert url.startswith("https://medicines.health.europa.eu/veterinary/pl/documents/download/")
 headers={"User-Agent":"Mozilla/5.0 (compatible; Veterinary-SPC-Catalog-Audit/1.0)","Accept":"application/pdf,*/*"}
 last=None
 for attempt in range(3):
  try:
   req=urllib.request.Request(url,headers=headers)
   with urllib.request.urlopen(req,timeout=35) as response:
    final=response.geturl()
    assert final.startswith("https://medicines.health.europa.eu/"),final
    data=response.read(15_000_001)
   if len(data)>15_000_000 or not data.startswith(b"%PDF-"):raise ValueError("not a PDF or oversized")
   dst.write_bytes(data)
   return "downloaded"
  except Exception as exc:
   last=str(exc)
   time.sleep(2*(attempt+1))
 raise RuntimeError(last)

con=sqlite3.connect(DB)
con.row_factory=sqlite3.Row
con.execute("PRAGMA foreign_keys=ON")
assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
columns=[r["name"] for r in con.execute("PRAGMA table_info(variants)") if r["name"]!="pdf_path"]
def clinical_hash():
 h=hashlib.sha256()
 for row in con.execute("SELECT "+",".join('"'+c+'"' for c in columns)+" FROM variants ORDER BY id"):
  h.update(json.dumps(list(row),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=clinical_hash()
report={"partia":45,"documents":[],"links":[],"errors":[]}
docs=[]
for brand,upd,url,filename,terms,ids in DOWNLOADS:
 dst=DIR/filename
 try:
  result=download(url,dst)
  pages=verify(dst,terms)
  docs.append((brand,dst.relative_to(ROOT).as_posix(),ids,upd))
  report["documents"].append({"brand":brand,"url":url,"path":dst.relative_to(ROOT).as_posix(),"pages":pages,"sha256":hashlib.sha256(dst.read_bytes()).hexdigest(),"result":result})
 except Exception as exc:
  # Failed/unverified downloads never enter SQLite.
  if dst.exists() and not valid_pdf(dst):dst.unlink()
  report["errors"].append({"brand":brand,"url":url,"error":str(exc)})
for brand,rel,terms,ids,upd in EXISTING:
 try:
  p=ROOT/rel
  assert valid_pdf(p),rel
  pages=verify(p,terms)
  docs.append((brand,rel,ids,upd))
  report["documents"].append({"brand":brand,"path":rel,"pages":pages,"sha256":hashlib.sha256(p.read_bytes()).hexdigest(),"result":"existing_reviewed"})
 except Exception as exc:
  report["errors"].append({"brand":brand,"path":rel,"error":str(exc)})

try:
 con.execute("BEGIN IMMEDIATE")
 for brand,rel,ids,upd in docs:
  for id in ids:
   r=con.execute("""SELECT v.id,v.name,v.symbol,v.upd_id,v.pdf_path,p.name AS subiekt
    FROM variants v JOIN products p ON p.symbol=v.symbol WHERE v.id=?""",(id,)).fetchone()
   assert r is not None,(brand,id)
   label=norm(r["name"]+" "+r["subiekt"])
   assert str(r["upd_id"])==upd,(brand,id,"different UPD")
   if brand=="Melovem":
    assert "melovem" in label and "30 mg/ml" in label,(id,label)
   elif brand=="Previcox":
    assert "previcox" in label and "227 mg" in label,(id,label)
   elif brand=="Porcilis ColiClos":
    assert "porcilis" in label and ("coliclos" in label or "coli" in label),(id,label)
   elif brand=="Vectra 3D":assert "vectra" in label,(id,label)
   old=str(r["pdf_path"] or "")
   if old and valid_pdf(ROOT/old):
    state="existing_kept"
   else:
    con.execute("INSERT OR IGNORE INTO documents(path) VALUES (?)",(rel,))
    assert con.execute("UPDATE variants SET pdf_path=? WHERE id=?",(rel,id)).rowcount==1
    state="linked"
   report["links"].append({"brand":brand,"id":id,"medicine":r["name"],"subiekt":r["subiekt"],"symbol":r["symbol"],"upd_id":upd,"pdf":rel,"status":state})
 assert before==clinical_hash(),"clinical data modified"
 assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not con.execute("PRAGMA foreign_key_check").fetchall()
 con.commit()
except:
 con.rollback()
 raise
report["counts"]={"downloaded":sum(x["result"]=="downloaded" for x in report["documents"]),"linked":sum(x["status"]=="linked" for x in report["links"]),"failures":len(report["errors"])}
report["clinical_unchanged"]=True
report["sqlite_sha256"]=hashlib.sha256(DB.read_bytes()).hexdigest()
(OUT/"partia45_zbiorcze_ema_wynik.json").write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding="utf8")
print("PARTIA45",json.dumps({"counts":report["counts"],"errors":report["errors"],"linked":report["links"]},ensure_ascii=False))
con.close()
