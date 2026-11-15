#!/usr/bin/env python3
"""Exact active-only audit from current SQLite (read-only), 10 next RPL families."""
import re
import sqlite3
from pathlib import Path
from collections import Counter
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
SOURCE=ROOT/"data/audyt_weryfikacji_SQLite_2026-11-15.md"
OUT=ROOT/"data/audyt_aktywnych_zaleglosci_partia_10_2026-11-15.md"
FAMILIES={
 "Febrivac Dist":{"16827"},"IBR Marker zywy":{"28639"},"Nerfasin":{"29972"},
 "Nobivac DHPPi":{"16737"},"Nobivac Tricat Trio":{"18939"},
 "Oxyflex XLA":{"16575"},"Polocainum 5%":{"20473"},"Pyralgivet":{"18806"},
 "Rispoval 3":{"22684"},"Wellicox":{"30613"},
}
TERMS=("wg chpl","według chpl","zgodnie z chpl","wg ulotki","według ulotki","zgodnie z ulotką","do weryfikacji","wg rpl")
def archived(r):
    return "archiwal" in str(r["scope"] or "").casefold()
def hasnum(t):
    return any(ch.isdigit() for ch in str(t or ""))
def mentions(t):
    return any(term in str(t or "").casefold() for term in TERMS)
def pdfstatus(r):
    value=r["pdf_path"]
    if not value:return "brak powiazania"
    relative=Path(value)
    if relative.is_absolute() or ".." in relative.parts:return "niebezpieczna sciezka"
    file=ROOT/relative
    if not file.is_file():return "brak pliku"
    with file.open("rb") as f:
        if f.read(5)!=b"%PDF-":return "nieprawidlowy naglowek"
    return "poprawny naglowek PDF"
def flat(v):
    return str(v if v is not None else "(brak)").replace("|","/").replace("\n"," ").replace("\r"," ")
def main():
    source=SOURCE.read_text(encoding="utf-8")
    section=source.split("## Warianty z nieprecyzyjnym dawkowaniem",1)[1].split("## Kartoteki z poprzedniego audytu",1)[0]
    ids={int(x) for x in re.findall(r"^\|\s*(\d+)\s*\|",section,re.M)}
    assert len(ids)==100,("original audit list inconsistent",len(ids))
    assert len(FAMILIES)==10
    with sqlite3.connect(f"file:{DB}?mode=ro",uri=True) as db:
        db.row_factory=sqlite3.Row
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        rows=db.execute("SELECT id,symbol,name,rpl_id,species,indication,route,dose,scope,pdf_path FROM variants ORDER BY id").fetchall()
        byid={r["id"]:r for r in rows}
        assert ids.issubset(byid),("missing original IDs",sorted(ids-set(byid)))
        active=[r for r in rows if not archived(r)]
        initial=[byid[x] for x in sorted(ids)]
        initial_active=[r for r in initial if not archived(r)]
        initial_archive=[r for r in initial if archived(r)]
        orig_refs=[r for r in initial_active if mentions(r["dose"])]
        orig_no_num=[r for r in initial_active if not hasnum(r["dose"])]
        all_refs=[r for r in active if mentions(r["dose"])]
        all_no_num=[r for r in active if not hasnum(r["dose"])]
        status={r["id"]:pdfstatus(r) for r in active}
        bad_pdf=[r for r in active if status[r["id"]]!="poprawny naglowek PDF"]
        lines=["# Audyt aktualnych aktywnych zaleglosci — partia 10","",
          "Zrodlo: biezaca SQLite z winforms; oryginalna lista 100 wariantow z raportu 15.11.",
          "**Wazne:** tekst 'wg ChPL' w dawce nie dowodzi bledu. Pelna ocena wymaga wskazania i urzedowej ChPL.",
          "","## Precyzyjne zestawienie","",
          f"- SQLite: **{len(rows)}** wariantow; aktywne **{len(active)}**, archiwalne **{len(rows)-len(active)}**.",
          f"- Poczatkowe 100: aktywne **{len(initial_active)}**; archiwalne **{len(initial_archive)}**.",
          f"- Aktywne z poczatkowych 100: brak liczby w dawce **{len(orig_no_num)}**; nadal odeslanie w dawce **{len(orig_refs)}**.",
          f"- Wszystkie aktywne: brak liczby w dawce **{len(all_no_num)}**; odeslanie w dawce **{len(all_refs)}**.",
          f"- Aktywne z niekompletnym lub nieprawidlowym PDF **{len(bad_pdf)}**.","",
          "## Pierwotne 100 — tylko aktywne","",
          "| ID | Lek | Gatunek | RPL | Dawka liczba | Odeslanie | PDF |",
          "|---:|---|---|---|---|---|---|"]
        for r in initial_active:
            lines.append(f"| {r['id']} | {flat(r['name'])} | {flat(r['species'])} | {flat(r['rpl_id'])} | {'tak' if hasnum(r['dose']) else 'BRAK'} | {'tak' if mentions(r['dose']) else 'nie'} | {status[r['id']]} |")
        lines += ["","## Archiwalne wpisy z pierwotnej listy","",", ".join(str(r["id"]) for r in initial_archive),"",
          "## Dziesiec kolejnych lekow — priorytetowe warianty","",
          "| Lek | Aktywne | Archiwalne | Odeslania | Bez dawki liczbowej | Problemy PDF |",
          "|---|---:|---:|---:|---:|---:|"]
        for name,rplset in FAMILIES.items():
            group=[r for r in rows if str(r["rpl_id"]) in rplset]
            a=[r for r in group if not archived(r)]
            lines.append(f"| {name} | {len(a)} | {len(group)-len(a)} | {sum(mentions(r['dose']) for r in a)} | {sum(not hasnum(r['dose']) for r in a)} | {sum(status[r['id']]!='poprawny naglowek PDF' for r in a)} |")
        for name,rplset in FAMILIES.items():
            lines += ["","### "+name,""]
            for r in rows:
                if str(r["rpl_id"]) not in rplset:continue
                lines.append(f"- {r['id']} ({'archiwalny' if archived(r) else 'aktywny'}): {flat(r['species'])}, {flat(r['indication'])}, {flat(r['route'])}, dawka: {flat(r['dose'])}; PDF: {flat(r['pdf_path'])}")
        lines += ["","## Aktywne niekompletne PDF",""]
        for r in bad_pdf:
            lines.append(f"- {r['id']}: {flat(r['name'])} (RPL {flat(r['rpl_id'])}): {status[r['id']]}; {flat(r['pdf_path'])}")
        lines += ["","## Kontrole","", "integrity_check=ok; foreign_key_check=ok; SQLite bez zmian; kompilacji nie uruchamiano."]
        OUT.write_text("\n".join(lines)+"\n",encoding="utf-8")
        print("SUCCESS exact counts: original active",len(initial_active),"archived",len(initial_archive),
              "original active references",len(orig_refs),"original without numeric dose",len(orig_no_num),
              "global active references",len(all_refs),"global no-number",len(all_no_num),
              "active pdf problems",len(bad_pdf),"integrity_check=ok foreign_key_check=ok")
if __name__=="__main__":
    main()
