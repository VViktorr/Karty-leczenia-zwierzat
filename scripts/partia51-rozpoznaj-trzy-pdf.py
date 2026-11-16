#!/usr/bin/env python3
import fitz, json, hashlib, re
from pathlib import Path
root=Path(__file__).resolve().parents[1]
paths=[
"data/ChPL/UPD/Ulotka-44168-2020-10-23-2702_W-2021-04-14.pdf",
"data/ChPL/UPD/anx_133354_pl.pdf",
"data/ChPL/UPD/anx_168157_pl.pdf"
]
results=[]
for p in paths:
 item={"path":p}
 try:
  f=root/p
  item["size"]=f.stat().st_size
  assert f.open("rb").read(5)==b"%PDF-"
  with fitz.open(f) as d:
   item["pages"]=len(d)
   text=" ".join(page.get_text(sort=True) for page in d)
   item["first_text"]=re.sub(r"\s+"," ",text[:3500])
   item["keywords"]={q:bool(re.search(q,text,re.I)) for q in ["ECOPORC","INGELVAC","EFFYDRAL","KRIPTAZEN","VELACTIS","AGRISEPT","TETRA.DELTA","PREGSURE","CIPER.PULVIZOO","PREQUENZA","CHARAKTERYSTYKA PRODUKTU","CHARAKTERYSTYKA WETERYNARYJNEGO","ULOTKA"]}
  item["sha256"]=hashlib.sha256(f.read_bytes()).hexdigest()
 except Exception as e:item["error"]=str(e)
 results.append(item)
target=root/"data/audyt_brakujacych_pdf/partia51_nowe_pdf_rozpoznanie.json"
target.write_text(json.dumps({"partia":51,"read_only":True,"documents":results},ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps([{"path":r["path"],"pages":r.get("pages"),"first_text":r.get("first_text","")[:850],"keywords":r.get("keywords"),"error":r.get("error")} for r in results],ensure_ascii=False))
