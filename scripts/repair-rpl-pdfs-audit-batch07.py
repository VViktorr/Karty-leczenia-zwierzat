#!/usr/bin/env python3
"""Batch07: restore TWO damaged Polish RPL PDFs, audit ten product families.

Only use exact official RPL API endpoints and verify PDF body + extracted
product name before writing. NEVER overwrite a valid existing local PDF.
SQLite is read only; no medical data is changed without separate approval.
"""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError
import sqlite3
from io import BytesIO
from pypdf import PdfReader

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_RPL_PDF_szczepionki_partia_07.md"
REPAIRS={
 "32411":("data/ChPL/RPL/RPL_32411_Pneumospectin_RPL_ChPL.pdf","Pneumospectin"),
 "22647":("data/ChPL/RPL/RPL_22647_Rispoval_IBR-Marker_Vivum_liofilizat_i_rozpus_RPL_ChPL.pdf","Rispoval IBR-Marker Vivum"),
}
FAMILIES={
 "Biocan R": {"18679"},
 "Nobivac DHPPi": {"16737"},
 "Nobivac Tricat Trio": {"18939"},
 "Porcilis Ery-Parvo": {"19703"},
 "Rispoval 3": {"22684"},
 "Rispoval IBR Inactivatum": {"19789"},
 "Rispoval IBR Vivum": {"22647"},
 "IBR marker zywy": {"28639"},
 "Purevax RCPCh": {"100225078"},
 "Pneumospectin": {"32411"},
}
def validate(content,expected):
    if not (content[:5]==b"%PDF-" and b"%%EOF" in content[-2048:] and len(content)>15000):
        raise ValueError("Invalid PDF envelope/size")
    pdf=PdfReader(BytesIO(content),strict=False)
    if len(pdf.pages)<2:
        raise ValueError("PDF unexpectedly short")
    txt=" ".join((p.extract_text() or "") for p in pdf.pages[:3])
    normalized=" ".join(txt.lower().replace("-"," ").split())
    exp=" ".join(expected.lower().replace("-"," ").split())
    if exp not in normalized:
        raise ValueError("PDF did not contain expected product name: "+expected)
    return len(pdf.pages)
def main():
    lines=["# Partia 07: naprawa urzędowych PDF RPL i audyt 10 grup","",
           "Pliki PDF pobierane wyłącznie z urzędowych API RPL i podmieniane tylko po kontroli treści.",
           "Bez zmian dawkowania, karencji, statusu lub historii w SQLite.",""]
    replaced=[]
    unresolved=[]
    for rpl,(rel,name) in REPAIRS.items():
        target=ROOT/rel
        valid_old=False
        if target.is_file():
            try:
                valid_old=validate(target.read_bytes(),name)>0
            except Exception as exc:
                lines.append(f"- Istniejący PDF RPL {rpl}: nieprawidłowy ({type(exc).__name__})")
        if valid_old:
            lines.append(f"- RPL {rpl}: poprawny PDF już istnieje; nie podmieniono")
            continue
        errors=[]
        for domain in ("rejestry.ezdrowie.gov.pl","rejestrymedyczne.ezdrowie.gov.pl"):
            uri=f"https://{domain}/api/rpl/medicinal-products/{rpl}/characteristic"
            try:
                req=Request(uri,headers={"User-Agent":"Mozilla/5.0","Accept":"application/pdf"})
                with urlopen(req,timeout=30) as resp:
                    data=resp.read(12*1024*1024+1)
                pages=validate(data,name)
                target.write_bytes(data)
                replaced.append((rpl,rel,uri,pages,len(data)))
                lines.append(f"- **NAPRAWIONO** RPL {rpl}, {name}: {pages} stron, {len(data)} bajtów; źródło {uri}")
                break
            except (OSError,URLError,HTTPError,ValueError,Exception) as exc:
                errors.append(domain+": "+type(exc).__name__+" "+str(exc)[:110])
        else:
            unresolved.append(rpl)
            lines.append(f"- **NIE NAPRAWIONO** RPL {rpl}: "+"; ".join(errors))
    lines += ["","## Dziesięć grup — aktywne i archiwalne warianty","",
              "| Lek | Aktywne | Archiwalne | Dawka odsyła do ChPL | Brak PDF |","|---|---:|---:|---:|---:|"]
    with sqlite3.connect(f"file:{DB}?mode=ro",uri=True) as db:
        db.row_factory=sqlite3.Row
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        rows=db.execute("SELECT id,rpl_id,symbol,name,species,indication,route,dose,scope,pdf_path FROM variants").fetchall()
        totals=[0,0,0,0]
        for name,refs in FAMILIES.items():
            matching=[r for r in rows if str(r["rpl_id"]) in refs]
            active=[r for r in matching if "archiwal" not in str(r["scope"] or "").lower()]
            archived=len(matching)-len(active)
            refdose=sum(any(w in (r["dose"] or "").lower() for w in ("wg chpl","według chpl","zgodnie z chpl","wg ulotki","według ulotki")) for r in active)
            no_pdf=sum(not r["pdf_path"] for r in active)
            current=(len(active),archived,refdose,no_pdf)
            totals=[x+y for x,y in zip(totals,current)]
            lines.append(f"| {name} | {current[0]} | {current[1]} | {current[2]} | {current[3]} |")
            lines += ["",f"### {name} — szczegółowe warianty",""]
            for r in matching:
                status="archiwalny" if r not in active else "aktywny"
                lines.append(f"- ID {r['id']} ({status}), gatunek: {r['species']}, droga: {r['route']}, wskazanie: {r['indication']}, dawka: {r['dose']}, PDF: {r['pdf_path'] or 'BRAK'}")
        lines += ["","**Razem:** aktywne "+str(totals[0])+", archiwalne "+str(totals[1])+", odsyłacze w dawce "+str(totals[2])+", bez PDF "+str(totals[3])+".",""]
    lines += ["## Dalsze kroki","",
              "- Rispoval IBR Vivum: opracować osobne schematy wieku i dróg podania na podstawie ChPL; bez zgadywania.",
              "- Dla Purevax RCPCh sprawdzić obecny status rejestracyjny przed zastąpieniem linku do dokumentu.",
              "- Nie włączać archiwalnych wariantów do licznika aktywnych braków.",""]
    OUT.write_text("\n".join(lines),encoding="utf-8")
    print("SUCCESS batch07 PDF repaired",len(replaced),"unresolved",len(unresolved),"10 groups audited, SQLite integrity OK")
    for r in replaced:print("PDF_REPLACED",r)
    for r in unresolved:print("PDF_UNRESOLVED",r)
if __name__=="__main__":
    main()
