#!/usr/bin/env python3
"""Batch 05: ten medicine families, active-only gap audit, exact RPL PDF links."""
import sqlite3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_10_lekow_partia_05_aktywne_SQLite.md"
MEDICINES={
 "Cyclix": {"18835":"RPL_18835_Cyclix_250_mcg_ml_roztwór_do_wstrzykiwań_dla__RPL_ChPL.pdf"},
 "Fertigest": {"39496":"RPL_39496_Fertigest_RPL_ChPL.pdf"},
 "Ganapar": {"35323":"RPL_35323_Ganapar_RPL_ChPL.pdf"},
 "Kexxtone": {"100297400":None},
 "Pneumospectin": {"32411":"RPL_32411_Pneumospectin_RPL_ChPL.pdf"},
 "Porcilis Ery-Parvo": {"19703":"RPL_19703_Porcilis_Ery_Parvo_RPL_ChPL.pdf"},
 "Purevax RCPCh": {"100225078":None},
 "Receptal": {"17179":"RPL_17179_Receptal_RPL_ChPL.pdf"},
 "Rispoval IBR Vivum": {"22647":"RPL_22647_Rispoval_IBR-Marker_Vivum_liofilizat_i_rozpus_RPL_ChPL.pdf"},
 "Wellicox": {"30613":"RPL_30613_Wellicox_RPL_ChPL.pdf"},
}
MED_COLS=("id","symbol","name","rpl_id","upd_id","species","indication","route","dose",
          "edible_tissues","milk","restrictions","source","verified_at","card_text",
          "scope","withdrawal_short")
GENERIC=("wg chpl","według chpl","zgodnie z chpl","wg ulotki","zgodnie z ulotką","do weryfikacji","wg rpl")
def fmt(v):
    return str(v).replace("\r"," ").replace("\n"," ") if v is not None else "(brak)"
def archived(r):
    return "archiwal" in str(r["scope"] or "").casefold()
def no_number(dose):
    return not any(ch.isdigit() for ch in (dose or ""))
def main():
    pdfs={}
    warnings=[]
    for name,docs in MEDICINES.items():
        for rpl,filename in docs.items():
            if filename is None:
                pdfs[rpl]=None
                warnings.append(name+": brak zidentyfikowanego oficjalnego PDF w repozytorium; nie dołączano automatycznie")
                continue
            rel="data/ChPL/RPL/"+filename
            p=ROOT/rel
            valid=False
            if p.is_file():
                with p.open("rb") as stream:
                    valid=stream.read(5)==b"%PDF-"
            pdfs[rpl]=rel if valid else None
            if not valid:
                warnings.append(name+": dokument "+rel+" nie jest prawidłowym PDF; potrzebna podmiana")
    with sqlite3.connect(DB) as con:
        con.row_factory=sqlite3.Row
        con.execute("PRAGMA foreign_keys=ON")
        assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not con.execute("PRAGMA foreign_key_check").fetchall()
        before=con.execute("SELECT * FROM variants ORDER BY id").fetchall()
        original=[tuple(row[k] for k in MED_COLS) for row in before]
        counts={table:con.execute("SELECT count(*) FROM "+table).fetchone()[0] for table in ("products","variants","price_withdrawal_entries")}
        report=["# Partia 05 — 10 leków, tylko aktywne zaległości","",
          "Źródło: SQLite z winforms. Archiwalnych wariantów nie liczymy jako aktywne braki.",
          "Bez automatycznych zmian dawkowania, karencji, gatunków, wskazań lub statusów.",
          ""]
        stats=[]
        links=[]
        for name,refs in MEDICINES.items():
            rows=[r for r in before if str(r["rpl_id"]) in refs]
            if len(rows)>80:
                raise RuntimeError("Too many variants: "+name)
            active=[r for r in rows if not archived(r)]
            archive=[r for r in rows if archived(r)]
            missing=[r for r in active if no_number(r["dose"])]
            refs_only=[r for r in active if not no_number(r["dose"]) and any(x in str(r["dose"] or "").casefold() for x in GENERIC)]
            missing_pdf=[r for r in active if not r["pdf_path"]]
            stats.append((name,len(active),len(archive),len(missing),len(refs_only),len(missing_pdf)))
            report.extend(["## "+name,"",
                "Aktywne: "+str(len(active))+"; archiwalne: "+str(len(archive))+
                "; bez dawki liczbowej: "+str(len(missing))+
                "; dawka liczbowa z odesłaniem: "+str(len(refs_only))+
                "; bez PDF: "+str(len(missing_pdf))+".",""])
            if not rows:
                report.extend(["Brak wariantów po znanym ID RPL. Brak automatycznego dopasowania po nazwie.",""])
            for r in rows:
                rpl=str(r["rpl_id"])
                if not archived(r) and not r["pdf_path"] and pdfs.get(rpl):
                    links.append((r["id"],rpl,pdfs[rpl]))
                state="ARCHIWALNY" if archived(r) else ("BRAK LICZBOWEJ DAWKI" if no_number(r["dose"]) else ("DAWKA Z ODESŁANIEM" if any(x in str(r["dose"] or "").casefold() for x in GENERIC) else "DAWKA LICZBOWA"))
                report.extend(["### Wariant "+str(r["id"])+" — "+state,""])
                for k in ("symbol","name","species","indication","route","dose","edible_tissues","milk","scope","pdf_path"):
                    report.append("- "+k+": "+fmt(r[k]))
                report.append("")
            print("AUDIT",name,"active",len(active),"archive",len(archive),"missing numeric dose",len(missing),"with refs",len(refs_only))
        con.execute("BEGIN IMMEDIATE")
        for vid,rpl,pdf in links:
            con.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)",(pdf,))
            cur=con.execute("UPDATE variants SET pdf_path=? WHERE id=? AND rpl_id=? AND pdf_path IS NULL",(pdf,vid,rpl))
            assert cur.rowcount==1,(vid,rpl)
        after=con.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert len(before)==len(after)
        assert [tuple(r[k] for k in MED_COLS) for r in after]==original
        touched={x[0] for x in links}
        assert all(a["pdf_path"]==b["pdf_path"] for a,b in zip(after,before) if a["id"] not in touched)
        assert all(con.execute("SELECT pdf_path FROM variants WHERE id=?",(vid,)).fetchone()[0]==pdf for vid,_,pdf in links)
        for table,n in counts.items():
            assert con.execute("SELECT count(*) FROM "+table).fetchone()[0]==n
        assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not con.execute("PRAGMA foreign_key_check").fetchall()
        con.commit()
        summary=["## Zestawienie 10 leków","",
                 "| Lek | Aktywne | Archiwalne | Bez dawki liczbowej | Z odesłaniem | Brak PDF przed korektą |",
                 "|---|---:|---:|---:|---:|---:|"]
        for name,a,b,c,d,e in stats:
            summary.append("| "+name+" | "+str(a)+" | "+str(b)+" | "+str(c)+" | "+str(d)+" | "+str(e)+" |")
        summary += ["","Uzupełnione oryginalne PDF dla aktywnych wariantów: "+str(len(links))+".","",
                    "## Dokumenty wymagające naprawy",""]
        summary += ["- "+x for x in warnings]
        report[4:4]=summary+[""]
        OUT.write_text("\n".join(report),encoding="utf-8")
        print("SUCCESS 10 drugs; active",sum(x[1] for x in stats),"archive",sum(x[2] for x in stats),"missing doses",sum(x[3] for x in stats),"links",len(links),"integrity OK")
if __name__=="__main__":
    main()
