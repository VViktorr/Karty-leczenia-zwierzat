#!/usr/bin/env python3
"""Partia 52b — archive unused PDF assets to separate archiwum-katalogu branch.

Critical order:
1) audit exact source DB + all 91 files (paths/hash)
2) copy to DISTINCT prefix on archive branch; commit and push archive
3) fetch remote archive and verify every hash there
4) only then prune unused documents table entries and git rm from winforms
5) write manifest; workflow commits winforms after this script finishes.
No clinical fields or variant records change.
"""
from pathlib import Path
from collections import defaultdict
import hashlib,json,sqlite3,subprocess,shutil
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
AUDIT=ROOT/"data/audyt_brakujacych_pdf/partia52_pdf_sprzatanie_plan.json"
REPORT=ROOT/"data/audyt_brakujacych_pdf/partia52b_przeniesienie_do_archiwum.json"
BRANCH="archiwum-katalogu"
DEST_PREFIX="data/archiwum-nieuzywanych-pdf/partia52-2026-11-16"
WORKTREE=Path("/tmp/partia52-archiwum-katalogu")
def run(*args,cwd=ROOT,output=False):
 r=subprocess.run(args,cwd=cwd,check=True,text=True,capture_output=output)
 return r.stdout.strip() if output else None
def digest(path):
 h=hashlib.sha256()
 with open(path,"rb") as f:
  for chunk in iter(lambda:f.read(1024*1024),b""):h.update(chunk)
 return h.hexdigest()
audit=json.loads(AUDIT.read_text(encoding="utf-8"))
assert audit["mode"]=="dry_run" and audit["pdf_unused_count"]==91
assert digest(DB)==audit["db_sha256"],"SQLite changed since audit; abort"
unused=audit["pdf_unused"]
assert len(unused)==91 and len({x["path"] for x in unused})==91
all_paths=set(x["path"] for x in unused)
cn=sqlite3.connect(DB)
cn.row_factory=sqlite3.Row
cn.execute("PRAGMA foreign_keys=ON")
assert cn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not cn.execute("PRAGMA foreign_key_check").fetchall()
variant_paths={str(r[0]).strip().replace("\\","/").removeprefix("./") for r in cn.execute("SELECT pdf_path FROM variants WHERE pdf_path IS NOT NULL AND trim(pdf_path)<>''")}
assert not all_paths.intersection(variant_paths),"Some proposed archival PDF is linked by a variant"
tracked=set(subprocess.check_output(["git","ls-files","-z","data/ChPL"],cwd=ROOT).decode("utf-8").strip("\0").split("\0"))
assert all_paths<=tracked
# Confirm exact files from audit, including bytes and hash, before mutation.
for x in unused:
 p=ROOT/x["path"]
 assert p.is_file() and p.stat().st_size==x["bytes"] and digest(p)==x["sha256"],("File changed",x["path"])
# Preserve all variants/clinical data 1:1 (hash only SQLite db fields before/after).
protected_cols=[r["name"] for r in cn.execute("PRAGMA table_info(variants)")]
def variant_hash():
 h=hashlib.sha256()
 q="SELECT "+",".join('"'+c+'"' for c in protected_cols)+" FROM variants ORDER BY id"
 for r in cn.execute(q):
  h.update(json.dumps(list(r),ensure_ascii=False,default=str).encode())
 return h.hexdigest()
before=variant_hash()
docs_before={r["path"] for r in cn.execute("SELECT path FROM documents")}
assert {x["path"] for x in unused if x["in_documents"]}<=docs_before
# Worktree branch should not collide or overwrite prior archiving.
run("git","fetch","origin",BRANCH)
if WORKTREE.exists():raise RuntimeError("Archive worktree path already exists, won't overwrite it")
run("git","worktree","add","-b","partia52-temp-archive",str(WORKTREE),"origin/"+BRANCH)
archive_commit=None
try:
 for x in unused:
  target=WORKTREE/DEST_PREFIX/x["path"]
  assert not target.exists(),("Archival destination already exists",str(target))
  target.parent.mkdir(parents=True,exist_ok=True)
  shutil.copy2(ROOT/x["path"],target)
  assert digest(target)==x["sha256"]
 index={"batch":"52b","source_branch":"winforms","source_db_sha256":audit["db_sha256"],
        "archival_branch":BRANCH,"archive_prefix":DEST_PREFIX,
        "files":[{"source":x["path"],"archive":DEST_PREFIX+"/"+x["path"],"bytes":x["bytes"],"sha256":x["sha256"],"was_in_documents":x["in_documents"]} for x in unused]}
 index_path=WORKTREE/DEST_PREFIX/"manifest.json"
 index_path.write_text(json.dumps(index,ensure_ascii=False,indent=2),encoding="utf-8")
 run("git","add","--",DEST_PREFIX,cwd=WORKTREE)
 run("git","-c","user.name=github-actions[bot]","-c","user.email=41898282+github-actions[bot]@users.noreply.github.com","commit","-m","archive: preserve 91 unlinked ChPL PDFs from winforms (batch 52)",cwd=WORKTREE)
 archive_commit=run("git","rev-parse","HEAD",cwd=WORKTREE,output=True)
 run("git","push","origin","HEAD:refs/heads/"+BRANCH,cwd=WORKTREE)
finally:
 run("git","worktree","remove","--force",str(WORKTREE))
 run("git","branch","-D","partia52-temp-archive")
# Re-fetch and verify the actual target branch objects remotely via git tree blob SHA,
# avoiding a false positive from local worktree copies.
run("git","fetch","origin",BRANCH)
assert run("git","rev-parse","origin/"+BRANCH,output=True)==archive_commit, "Remote archive branch differs from pushed commit"
for x in unused:
 archive_path=DEST_PREFIX+"/"+x["path"]
 data=subprocess.run(["git","show","origin/"+BRANCH+":"+archive_path],cwd=ROOT,check=True,capture_output=True).stdout
 assert hashlib.sha256(data).hexdigest()==x["sha256"],("Archive remote object mismatch",archive_path)
# All files are now safely archived. Delete unreferenced documents rows and git files.
deleted_docs=[]
try:
 cn.execute("BEGIN IMMEDIATE")
 for x in unused:
  if x["path"] in docs_before:
   assert cn.execute("SELECT count(*) FROM variants WHERE pdf_path=?",(x["path"],)).fetchone()[0]==0
   assert cn.execute("DELETE FROM documents WHERE path=?",(x["path"],)).rowcount==1
   deleted_docs.append(x["path"])
 assert variant_hash()==before, "Variants changed"
 assert cn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
 assert not cn.execute("PRAGMA foreign_key_check").fetchall()
 cn.commit()
except:
 cn.rollback();raise
for x in unused:
 run("git","rm","--",x["path"])
assert variant_hash()==before
leftovers=[r[0] for r in cn.execute("SELECT DISTINCT pdf_path FROM variants WHERE pdf_path IS NOT NULL AND trim(pdf_path)<>''") if not (ROOT/r[0]).is_file()]
assert not leftovers,("Dangling linked PDFs after move",leftovers)
report={
 "batch":52,"operation":"move_unlinked_pdfs_from_winforms_to_archiwum-katalogu",
 "source_branch":"winforms","archive_branch":BRANCH,"archive_commit":archive_commit,
 "archive_prefix":DEST_PREFIX,"source_sqlite_sha256":audit["db_sha256"],
 "result_sqlite_sha256":digest(DB),
 "archived_count":len(unused),"archived_bytes":sum(x["bytes"] for x in unused),
 "documents_rows_removed":len(deleted_docs),
 "remaining_pdf_files":len(tracked)-len(unused),
 "all_archive_remote_hashes_verified":True,"all_variant_records_unchanged":True,
 "active_pdf_links_preserved":True,"remaining_broken_variant_refs":0,
 "files":[{"from":x["path"],"to":DEST_PREFIX+"/"+x["path"],"sha256":x["sha256"],"bytes":x["bytes"]} for x in unused]
}
REPORT.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA52B",json.dumps({k:v for k,v in report.items() if k!="files"},ensure_ascii=False),flush=True)
cn.close()
