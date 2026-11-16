#!/usr/bin/env python3
"""Batch 52 audit: dry-run PDF inventory vs SQLite (no changes)."""
import sqlite3,hashlib,json,collections,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf/partia52_pdf_sprzatanie_plan.json"
cn=sqlite3.connect(f"file:{DB}?mode=ro",uri=True)
cn.row_factory=sqlite3.Row
assert cn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
tables=[r["name"] for r in cn.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
rows=[dict(r) for r in cn.execute("SELECT id,name,scope,pdf_path FROM variants ORDER BY id")]
def norm(p):
 return str(p or "").strip().replace("\\","/").removeprefix("./")
variant_refs=collections.defaultdict(list)
for r in rows:
 if norm(r["pdf_path"]):variant_refs[norm(r["pdf_path"])].append({"id":r["id"],"scope":r["scope"]})
docs=[dict(x) for x in cn.execute("SELECT * FROM documents")]
registered={norm(x["path"]) for x in docs if norm(x["path"])}
# Examine all SQLite tables and columns that may contain path references, not just variants.
other_ref_counts={}
foreign_keys={}
for table in tables:
 foreign_keys[table]=[dict(r) for r in cn.execute('PRAGMA foreign_key_list("'+table.replace('"','""')+'")')]
 schema=[dict(r) for r in cn.execute('PRAGMA table_info("'+table.replace('"','""')+'")')]
 for col in schema:
  if (col["type"] or "").upper() in ("TEXT","VARCHAR","CHAR","CLOB",""):
   # DB paths in other columns may be descriptions; record any PDF-looking fields for manual review.
   q='SELECT "'+col["name"].replace('"','""')+'" FROM "'+table.replace('"','""')+'" WHERE "'+col["name"].replace('"','""')+'" LIKE \'%.pdf%\' LIMIT 40'
   try: vals=[str(r[0])[:500] for r in cn.execute(q) if r[0]]
   except sqlite3.Error: vals=[]
   if vals and not (table=="variants" and col["name"]=="pdf_path") and not (table=="documents" and col["name"]=="path"):
    other_ref_counts[table+"."+col["name"]]={"count_sample":len(vals),"examples":vals[:5]}
files=subprocess.check_output(["git","ls-files","-z","data/ChPL"],cwd=ROOT).decode().split("\0")
pdfs=[p for p in files if p.lower().endswith(".pdf")]
size_total=sum((ROOT/p).stat().st_size for p in pdfs)
unused=[]
for p in pdfs:
 if p in variant_refs:continue
 f=ROOT/p
 unused.append({"path":p,"bytes":f.stat().st_size,
   "sha256":hashlib.sha256(f.read_bytes()).hexdigest(),"in_documents":p in registered})
# sha duplicates
by_hash=collections.defaultdict(list)
for p in pdfs:
 by_hash[hashlib.sha256((ROOT/p).read_bytes()).hexdigest()].append(p)
duplicates=[v for v in by_hash.values() if len(v)>1]
missing_refs=[{"path":p,"num_variants":len(refs)} for p,refs in variant_refs.items() if not (ROOT/p).is_file()]
invalid_pdfs=[p for p in pdfs if (ROOT/p).open("rb").read(5)!=b"%PDF-"]
unused_docs=sorted(registered-set(variant_refs))
report={
 "batch":52,"mode":"dry_run","db_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "pdf_total":len(pdfs),"pdf_bytes_total":size_total,
 "variant_count":len(rows),"variant_pdf_references":sum(map(len,variant_refs.values())),
 "unique_variant_pdf_paths":len(variant_refs),
 "pdf_unused_count":len(unused),"pdf_unused_bytes":sum(x["bytes"] for x in unused),
 "pdf_unused":unused,"duplicate_sha_groups":duplicates,
 "documents_count":len(docs),"documents_without_variant_refs_count":len(unused_docs),
 "documents_without_variant_refs":unused_docs,
 "other_sqlite_pdf_text_columns":other_ref_counts,
 "sqlite_foreign_keys":{k:v for k,v in foreign_keys.items() if v},
 "missing_variant_reference_paths":missing_refs,
 "invalid_pdf_magic_paths":invalid_pdfs
}
OUT.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PDF_AUDIT",json.dumps({k:v for k,v in report.items() if k not in ("pdf_unused","duplicate_sha_groups","documents_without_variant_refs","sqlite_foreign_keys")},ensure_ascii=False)[:11000])
print("UNUSED_FIRST",json.dumps(unused[:30],ensure_ascii=False))
print("DUPLICATES",json.dumps(duplicates[:40],ensure_ascii=False))
cn.close()
