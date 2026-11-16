#!/usr/bin/env python3
"""Partia 39: read-only comprehensive SQLite ChPL audit with Subiekt product names.

Report every active variant with missing, broken, or evidently incorrect PDF path.
Never change the database, its clinical fields, or document links.
"""
import csv, hashlib, json, re, sqlite3, unicodedata
from pathlib import Path
from collections import Counter,defaultdict

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_brakujacych_pdf"
OUT.mkdir(parents=True,exist_ok=True)
def norm(v):
    v=unicodedata.normalize("NFKD",str(v or "").casefold())
    return re.sub(r"\s+"," ","".join(c for c in v if not unicodedata.combining(c))).strip()
def good_pdf(rel):
    if not rel or not str(rel).strip():return False
    p=(ROOT/rel)
    try:return p.is_file() and p.stat().st_size>=1000 and p.open("rb").read(5)==b"%PDF-"
    except (ValueError, OSError):return False
def status_for(row):
    rel=row["pdf_path"]
    if not str(rel or "").strip():return "BRAK_POWIAZANIA"
    if not good_pdf(rel):return "PLIK_NIEISTNIEJE_LUB_USZKODZONY"
    # Certain errors are reliably detectable without PDF text: *different* registered product
    # advertised by the path. Do not use fuzzy full-name matching as a negative check.
    name=norm(row["variant_name"])
    path=norm(rel).replace("_"," ").replace("-"," ")
    if re.search(r"\bdraxxin\b",name) and "draxxin plus" in path and "draxxin plus" not in name:
        return "BLEDNY_LEK_DOKUMENT_DRAxxIN_PLUS"
    rplmatch=re.search(r"rpl_(\d+)_",str(rel),flags=re.I)
    rpl=str(row["rpl_id"] or "").strip()
    if rplmatch and rpl and rplmatch.group(1).lstrip("0")!=rpl.lstrip("0"):
        return "NIEZGODNY_ID_RPL_W_SCIEZCE"
    return "POWIAZANIE_TECHNICZNIE_OK"
db=sqlite3.connect(f"file:{DB}?mode=ro",uri=True)
db.row_factory=sqlite3.Row
assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
assert not db.execute("PRAGMA foreign_key_check").fetchall()
product_fields={r["name"] for r in db.execute("PRAGMA table_info(products)")}
assert {"symbol","name"}.issubset(product_fields),product_fields
stock_expression="p.stock" if "stock" in product_fields else "NULL"
unit_expression="p.unit" if "unit" in product_fields else "NULL"
products={r["symbol"]:dict(r) for r in db.execute("SELECT symbol,name FROM products")}
variant_fields={r["name"] for r in db.execute("PRAGMA table_info(variants)")}
assert {"symbol","name","scope","pdf_path","rpl_id","upd_id"}.issubset(variant_fields)
# All active records; products.name is original Subiekt name. May be shared across variants.
rows=[dict(r) for r in db.execute(f"""SELECT v.id AS variant_id,v.symbol,v.name AS variant_name,
 v.rpl_id,v.upd_id,v.pdf_path,v.scope,p.name AS subiekt_name,{stock_expression} AS stored_stock,{unit_expression} AS unit
 FROM variants v JOIN products p ON p.symbol=v.symbol
 WHERE v.scope IS NULL OR lower(v.scope) NOT LIKE '%archiwal%'
 ORDER BY lower(v.name),v.id""")]
assert len(rows)==2902,len(rows)
for r in rows:r["status"]=status_for(r)
missing=[r for r in rows if r["status"]!="POWIAZANIE_TECHNICZNIE_OK"]
counts=dict(Counter(r["status"] for r in rows))
files=list((ROOT/"data/ChPL").rglob("*.pdf"))
report={
 "phase":39,"read_only":True,"active_variants":len(rows),"all_variants":db.execute("SELECT COUNT(*) FROM variants").fetchone()[0],
 "subiekt_products":len(products),"available_pdf_files":len(files),
 "missing_or_flagged_variant_count":len(missing),
 "unique_variant_names":len({norm(r["variant_name"]) for r in missing}),
 "unique_subiekt_products":len({r["symbol"] for r in missing}),
 "status_counts":counts,
 "sqlite_sha256":hashlib.sha256(DB.read_bytes()).hexdigest(),
 "note":"Stan magazynowy pochodzi z zapisanej bazy katalogu, a nie z bieżącego połączenia SQL Subiekta.",
 "limitation":"Technicznie poprawne powiązanie nie gwarantuje zgodności mocy/postaci; każda treściowa weryfikacja wymaga analizy konkretnej ChPL."
}
fields=["variant_id","variant_name","symbol","subiekt_name","rpl_id","upd_id","status","pdf_path","stored_stock","unit"]
with (OUT/"partia39_braki_z_nazwami_subiekta.csv").open("w",encoding="utf-8-sig",newline="") as f:
    writer=csv.DictWriter(f,fieldnames=fields,extrasaction="ignore",delimiter=";")
    writer.writeheader()
    writer.writerows(missing)
with (OUT/"partia39_wszystkie_warianty_kontrola.csv").open("w",encoding="utf-8-sig",newline="") as f:
    writer=csv.DictWriter(f,fieldnames=fields,extrasaction="ignore",delimiter=";")
    writer.writeheader()
    writer.writerows(rows)
groups=defaultdict(list)
for r in missing:groups[norm(r["variant_name"])].append(r)
md=["# Partia 39 – audyt powiązań ChPL z nazwami Subiekta","",
    f"**Aktywne warianty:** {len(rows)}",
    f"**Brakujące lub podejrzane powiązania:** {len(missing)}",
    f"**Różne nazwy wariantów:** {report['unique_variant_names']}",
    f"**Kartoteki Subiekta:** {report['unique_subiekt_products']}",
    "",
    "**Uwaga:** stan magazynowy jest opcjonalny; brak kolumny stock w katalogu oznacza puste pole.",
    "Audyt jest tylko do odczytu. Nie potwierdza zgodności treści dla istniejących PDF, które technicznie się otwierają.",
    "",
    "## Rozkład przyczyn",""]
for k,v in sorted(counts.items()):md.append(f"- {k}: {v}")
md.extend(["","## Pełna lista leków i powiązanych kartotek Subiekta",""])
for name,items in sorted(groups.items()):
    display=items[0]["variant_name"] or "(brak nazwy wariantu)"
    md.append("### "+str(display))
    md.append("")
    md.append("| ID wariantu | Symbol Subiekta | Nazwa w Subiekcie | ID RPL / UPD | Przyczyna |")
    md.append("|---:|---|---|---|---|")
    for r in sorted(items,key=lambda r:(r["symbol"],r["variant_id"])):
        val=lambda x:str(x or "—").replace("|","\\|").replace("\n"," ")
        md.append(f"| {r['variant_id']} | {val(r['symbol'])} | {val(r['subiekt_name'])} | {val(r['rpl_id'])} / {val(r['upd_id'])} | {val(r['status'])} |")
    md.append("")
(OUT/"partia39_braki_z_nazwami_subiekta.md").write_text("\n".join(md),encoding="utf-8")
(OUT/"partia39_podsumowanie.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print("PARTIA39_AUDIT",json.dumps(report,ensure_ascii=False))
db.close()
