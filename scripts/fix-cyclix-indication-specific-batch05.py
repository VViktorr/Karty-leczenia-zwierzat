#!/usr/bin/env python3
"""Cyclix RPL 18835: exact indication-specific schedule from current official Polish leaflet.

Source: https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/18835/leaflet
Only 12 variants with unambiguous indication are changed. Four variants
combining labour induction and pregnancy termination stay unchanged for splitting.
"""
import sqlite3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
PDF="data/ChPL/RPL/RPL_18835_Cyclix_250_mcg_ml_roztwór_do_wstrzykiwań_dla__RPL_ChPL.pdf"
OLD="2 ml/szt. (500 µg kloprostenolu); schemat powtórzeń zależny od wskazania wg ulotki"
NEW={
    "Ruja / synchronizacja": "2 ml IM na krowę (500 µg kloprostenolu) jednorazowo; jeżeli nie obserwuje się objawów rui, drugą dawkę można podać po 11 dniach.",
    "Zapalenie błony śluzowej macicy": "2 ml IM na krowę (500 µg kloprostenolu) jednorazowo; przy czynnym ciałku żółtym w razie potrzeby powtórzyć po 10–14 dniach.",
    "Torbiele lutealne": "2 ml IM na krowę (500 µg kloprostenolu) — jedna dawka.",
}
TARGET={197,198,199,201,202,203,219,220,221,223,224,225}
COMBINED={200,204,222,226}
def main():
    with (ROOT/PDF).open("rb") as f:
        assert f.read(5)==b"%PDF-"
    with sqlite3.connect(DB) as db:
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        rows=db.execute("SELECT id,rpl_id,species,indication,route,dose,card_text,pdf_path,edible_tissues,milk FROM variants WHERE rpl_id='18835' ORDER BY id").fetchall()
        assert {x[0] for x in rows}==TARGET|COMBINED
        for vid,rpl,species,indication,route,dose,card,pdf,tissues,milk in rows:
            assert rpl=="18835" and species.startswith("Bydło") and route=="IM"
            assert pdf==PDF and tissues=="1 dzień" and milk=="0 godzin"
            if vid in TARGET:
                assert indication in NEW,(vid,indication)
                assert dose in (OLD,NEW[indication]),(vid,dose)
                if card:
                    assert card.count(OLD)<=1,(vid,"multiple dose spans")
            else:
                assert indication=="Indukcja porodu lub poronienia" and dose==OLD,(vid,indication,dose)
        before=db.execute("SELECT * FROM variants WHERE rpl_id != '18835' OR rpl_id IS NULL ORDER BY id").fetchall()
        baseline=db.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,edible_tissues,milk,
                          restrictions,source,verified_at,scope,pdf_path,withdrawal_short FROM variants WHERE rpl_id='18835' ORDER BY id""").fetchall()
        nproducts=db.execute("SELECT COUNT(*) FROM products").fetchone()[0]
        nvariants=db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]
        db.execute("BEGIN IMMEDIATE")
        updates=0
        for vid,rpl,species,indication,route,dose,card,pdf,tissues,milk in rows:
            if vid not in TARGET or dose==NEW.get(indication):
                continue
            newcard=card.replace(OLD,NEW[indication]) if card and OLD in card else card
            cur=db.execute("UPDATE variants SET dose=?,card_text=? WHERE id=? AND rpl_id=? AND dose=?",
                           (NEW[indication],newcard,vid,rpl,OLD))
            assert cur.rowcount==1,vid
            updates+=1
        assert db.execute("SELECT * FROM variants WHERE rpl_id != '18835' OR rpl_id IS NULL ORDER BY id").fetchall()==before
        assert db.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,edible_tissues,milk,
                          restrictions,source,verified_at,scope,pdf_path,withdrawal_short FROM variants WHERE rpl_id='18835' ORDER BY id""").fetchall()==baseline
        assert db.execute("SELECT COUNT(*) FROM products").fetchone()[0]==nproducts
        assert db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]==nvariants
        assert db.execute("SELECT COUNT(*) FROM variants WHERE rpl_id='18835' AND dose=?",(OLD,)).fetchone()[0]==4
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
        print(f"SUCCESS: Cyclix 12 exact indication variants, changed {updates}; 4 ambiguous pregnancy variants preserved; integrity OK")
if __name__=="__main__":
    main()
