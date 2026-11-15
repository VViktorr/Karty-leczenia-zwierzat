#!/usr/bin/env python3
"""Batch 04: ten veterinary medicine families, audit + guarded Enrocin/Enroxil fixes.

Enrocin RPL 18715 and Enroxil RPL 18561 official current Polish ChPL section 3.9.
Only replace generic 'according to ChPL' dose when route/species/product match;
never infer a dose for an unverified variant. Original PDF associations are
read and reported, not overwritten. No builds and no deletion.
"""
from pathlib import Path
import sqlite3

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
REPORT=ROOT/"data/audyt_10_lekow_partia_04_SQLite.md"
FAMILIES={
"Enrocin 5%":({"100187159","18715"},"RPL_18715_Enrocin_5_inj_RPL_ChPL.pdf"),
"Enroxil 5%":({"100185611","18561"},"RPL_18561_Enroxil_5_RPL_ChPL.pdf"),
"Enrofloksacyna 10%":({"100172070","17207"},"RPL_17207_Enrofloksacyna_10_INJ_RPL_ChPL.pdf"),
"Pneumospectin":({"32411"},"RPL_32411_Pneumospectin_RPL_ChPL.pdf"),
"Porcilis Ery-Parvo":({"19703"},"RPL_19703_Porcilis_Ery_Parvo_RPL_ChPL.pdf"),
"Pyralgivet":({"18806"},"RPL_18806_Pyralgivet_500_mg_ml_roztwór_do_wstrzykiwań_d_RPL_ChPL.pdf"),
"Receptal":({"17179"},"RPL_17179_Receptal_RPL_ChPL.pdf"),
"Rispoval 3":({"22684"},"RPL_22684_Rispoval_3_-_BRSV_-_PI3_-_BVD_liofilizat_i_za_RPL_ChPL.pdf"),
"Rispoval IBR Inactivatum":({"19789"},"RPL_19789_Rispoval_IBR-Marker_Inactivatum_zawiesina_do__RPL_ChPL.pdf"),
"Rispoval IBR Vivum":({"22647"},"RPL_22647_Rispoval_IBR-Marker_Vivum_liofilizat_i_rozpus_RPL_ChPL.pdf"),
}
ENROCIN_IDS={1637,2337,1581,2338,1588,2339}
ENROXIL_IDS={1390,1393,2340}
OLD="Dawkowanie i sposób podania zgodnie z ChPL."
PIG_DOSE=("Domięśniowo: 2,5 mg enrofloksacyny/kg m.c. (0,5 ml/10 kg) raz dziennie przez 3 dni; "
          "przy zakażeniu przewodu pokarmowego lub posocznicy wywołanej E. coli: "
          "5 mg/kg (1 ml/10 kg) raz dziennie przez 3 dni. Maksymalnie 3 ml w jedno miejsce, iniekcja w kark.")
DOG_DOSE=("Podskórnie: 5 mg enrofloksacyny/kg m.c. (1 ml/10 kg) raz dziennie przez maksymalnie 5 dni; "
          "przy kolejnych iniekcjach zmieniać miejsce podania.")
DOSE_COLS=("id","symbol","name","rpl_id","upd_id","species","indication","route",
           "edible_tissues","milk","restrictions","source","verified_at","scope",
           "pdf_path","withdrawal_short")
def compact(value):
    return str(value).replace("\r"," ").replace("\n"," ") if value is not None else "(brak)"
def main():
    pdf_warnings=[]
    for family,(_,filename) in FAMILIES.items():
        pdf=ROOT/"data/ChPL/RPL"/filename
        is_pdf=False
        if pdf.is_file():
            with pdf.open("rb") as source:
                is_pdf=source.read(5)==b"%PDF-"
        if not is_pdf:
            if family in ("Enrocin 5%","Enroxil 5%"):
                raise RuntimeError(f"Cannot modify doses without valid local ChPL PDF: {pdf}")
            pdf_warnings.append(f"{family}: dokument {pdf.relative_to(ROOT)} nie ma prawidłowego nagłówka PDF lub nie istnieje; wymaga zastąpienia urzędowym PDF")
            print("WARNING_PDF",family,pdf)
    with sqlite3.connect(DB) as conn:
        conn.row_factory=sqlite3.Row
        conn.execute("PRAGMA foreign_keys=ON")
        assert conn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not conn.execute("PRAGMA foreign_key_check").fetchall()
        allrows=conn.execute("SELECT * FROM variants ORDER BY id").fetchall()
        before=[tuple(r[k] for k in DOSE_COLS) for r in allrows]
        other_before={r["id"]:(r["dose"],r["card_text"],r["pdf_path"]) for r in allrows if r["id"] not in ENROCIN_IDS|ENROXIL_IDS}
        counts={name:conn.execute("SELECT count(*) FROM "+name).fetchone()[0] for name in ("products","variants","price_withdrawal_entries")}
        report=["# Partia 04 — 10 leków, audyt i korekta dawki dwóch grup","",
                "Źródła: aktualna SQLite na winforms; dla Enrocin RPL 18715 i Enroxil RPL 18561 aktualne urzędowe ChPL (pkt 3.9).",
                "Pozostałych ośmiu leków nie zmieniano bez porównania wskazań z ChPL.",
                *([f"UWAGA: {w}" for w in pdf_warnings] or ["Weryfikacja nagłówków PDF: bez ostrzeżeń."]),
                ""]
        updates=[]
        all_target_ids=set()
        for family,(ids,filename) in FAMILIES.items():
            rows=[r for r in allrows if str(r["rpl_id"]) in ids]
            assert rows and len(rows)<60,(family,"No/current variants unexpectedly missing")
            all_target_ids.update(r["id"] for r in rows)
            report.extend([f"## {family}: {len(rows)} wariantów", "",f"Dokument: `data/ChPL/RPL/{filename}`.",""])
            for r in rows:
                vid=r["id"]; species=(r["species"] or "").casefold()
                route=(r["route"] or "").casefold()
                current=r["dose"] or ""
                status="bez zmiany"
                if (family=="Enrocin 5%" and vid in ENROCIN_IDS) or (family=="Enroxil 5%" and vid in ENROXIL_IDS):
                    if vid in ENROCIN_IDS:
                        assert family=="Enrocin 5%",(vid,family)
                    else:
                        assert family=="Enroxil 5%",(vid,family)
                    if "świn" in species or "swini" in species:
                        proposed=PIG_DOSE
                        valid_route=("im" in route or "domięś" in route or "domies" in route)
                    elif family=="Enroxil 5%" and ("pies" in species or "psy" in species):
                        proposed=DOG_DOSE
                        valid_route=("sc" in route or "podskór" in route or "podskor" in route)
                    else:
                        proposed=None; valid_route=False
                    if current==OLD and proposed and valid_route:
                        updates.append((vid,str(r["rpl_id"]),proposed,current,r["card_text"]))
                        status="gotowy do bezpiecznej korekty"
                    elif current==proposed and valid_route:
                        status="dawka już poprawiona"
                    else:
                        status="WYMAGA RĘCZNEJ WERYFIKACJI; dawka/droga/gatunek nie pasują do wzorca"
                report.append(f"### ID {vid}: {compact(r['name'])}")
                report.append("")
                for key in ("symbol","rpl_id","species","indication","route","dose","edible_tissues","milk","pdf_path","scope"):
                    report.append(f"- **{key}:** {compact(r[key])}")
                report.append(f"- **Wynik:** {status}")
                report.append("")
            print("AUDIT",family,len(rows))
        assert ENROCIN_IDS.issubset(all_target_ids),(ENROCIN_IDS-all_target_ids)
        assert ENROXIL_IDS.issubset(all_target_ids),(ENROXIL_IDS-all_target_ids)
        conn.execute("BEGIN IMMEDIATE")
        for vid,rpl,new,old,card in updates:
            if card:
                assert card.count(old)<=1,(vid,"ambiguous card_text")
                new_card=card.replace(old,new) if old in card else card
            else:
                new_card=card
            cur=conn.execute("UPDATE variants SET dose=?,card_text=? WHERE id=? AND rpl_id=? AND dose=?",
                             (new,new_card,vid,rpl,old))
            assert cur.rowcount==1,(vid,rpl)
            print("UPDATED_DOSE",vid,rpl)
        after=conn.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert [tuple(r[k] for k in DOSE_COLS) for r in after]==before
        assert len(after)==len(allrows)
        assert {r["id"]:(r["dose"],r["card_text"],r["pdf_path"]) for r in after if r["id"] not in ENROCIN_IDS|ENROXIL_IDS}==other_before
        for name,c in counts.items():
            assert conn.execute("SELECT count(*) FROM "+name).fetchone()[0]==c
        assert conn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not conn.execute("PRAGMA foreign_key_check").fetchall()
        conn.commit()
        report.insert(4,f"W tej partii poprawiono {len(updates)} wpisów bez liczbowej dawki; inne dawki i karencje pozostały bez zmian.")
        REPORT.write_text("\n".join(report),encoding="utf-8")
        print("SUCCESS: ten medicine families",len(all_target_ids),"variants; corrected doses",len(updates),
              "integrity_check=ok foreign_key_check=ok")
if __name__=="__main__":
    main()
