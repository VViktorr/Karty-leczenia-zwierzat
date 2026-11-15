#!/usr/bin/env python3
"""Guarded corrections from Polish RPL for Bioketan, Buserelin and Carofertin.
Bioketan: reviewed, doses already correct; untouched.
Buserelin: repeat at 24 h only for mare OVULATION INDUCTION, not conception-rate indication.
Carofertin: precise peripartum timing for cows/heifers and mares.
Changes only dose and an embedded duplicate of the original dose in card_text.
"""
import sqlite3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
PDFS={
"20507":"data/ChPL/RPL/RPL_20507_Bioketan_RPL_ChPL.pdf",
"18833":"data/ChPL/RPL/RPL_18833_Buserelin_aniMedica_0_004_mg_ml_roztwór_do_ws_RPL_ChPL.pdf",
"17131":"data/ChPL/RPL/RPL_17131_Carofertin_RPL_ChPL.pdf",
}
OLD={
"18833":"10 ml (40 µg) jednorazowo; przy braku owulacji w 24 h powtórzyć wg ulotki",
"17131_by":"20–25 ml krowy/jałówki; IM/SC; dwa podania okołoporodowo wg ChPL",
"17131_ko":"20 ml klaczy; IM/SC; w okresie okołoporodowym dwa podania wg ChPL",
}
NEW={
"18833":"10 ml (40 µg) jednorazowo; wyłącznie przy indukcji owulacji: jeśli nie wystąpi ona w ciągu 24 godzin, powtórzyć iniekcję. Przy poprawie zaźrebień: dawka jednorazowa.",
"17131_by":"20–25 ml na krowę/jałówkę IM lub SC; 2 podania okołoporodowo: pierwsze 1–2 tyg. przed porodem albo 1–2 tyg. po porodzie, drugie 4–6 tyg. po porodzie. W terapii wspomagającej niepłodności z wypływami: 1–2 podania w odstępach 14 dni.",
"17131_ko":"20 ml na klacz IM lub SC; 2 podania okołoporodowo: pierwsze 1–2 tyg. przed porodem albo 1–2 tyg. po porodzie, drugie 4–6 tyg. po porodzie. W terapii wspomagającej niepłodności z wypływami: 1–2 podania w odstępach 14 dni.",
}
CHANGES={
247:("18833","Konie (klacze)","Indukcja owulacji lub poprawa współczynnika zaźrebień","18833"),
1886:("18833","Konie (klacze)","Indukcja owulacji lub poprawa współczynnika zaźrebień","18833"),
1890:("18833","Konie (klacze)","Indukcja owulacji lub poprawa współczynnika zaźrebień","18833"),
282:("17131","Bydło",None,"17131_by"),
777:("17131","Bydło",None,"17131_by"),
283:("17131","Konie",None,"17131_ko"),
778:("17131","Konie",None,"17131_ko"),
}
EXPECTED_COUNTS={"20507":4,"18833":12,"17131":6}
def main():
    for rpl,p in PDFS.items():
        with (ROOT/p).open("rb") as f:
            assert f.read(5)==b"%PDF-",(rpl,p)
    with sqlite3.connect(DB) as db:
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        for rpl,n in EXPECTED_COUNTS.items():
            assert db.execute("SELECT COUNT(*) FROM variants WHERE rpl_id=?",(rpl,)).fetchone()[0]==n,rpl
        before_count=db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]
        products=db.execute("SELECT COUNT(*) FROM products").fetchone()[0]
        before_others=db.execute("SELECT * FROM variants WHERE id NOT IN ("+",".join(str(i) for i in CHANGES)+") ORDER BY id").fetchall()
        baseline=db.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,edible_tissues,milk,
                    restrictions,source,verified_at,scope,pdf_path,withdrawal_short
                    FROM variants WHERE id IN ("""+",".join(str(i) for i in CHANGES)+") ORDER BY id").fetchall()
        db.execute("BEGIN IMMEDIATE")
        changed=0
        for id_,(rpl,species,indication,key) in CHANGES.items():
            row=db.execute("""SELECT rpl_id,species,indication,dose,card_text,pdf_path FROM variants WHERE id=?""",(id_,)).fetchone()
            assert row is not None,id_
            actual_rpl,actual_species,actual_indication,actual_dose,card,pdf=row
            assert (actual_rpl,actual_species)==(rpl,species),(id_,row)
            if indication is not None:
                assert actual_indication==indication,(id_,actual_indication)
            assert pdf==PDFS[rpl],(id_,pdf)
            assert actual_dose in (OLD[key],NEW[key]),(id_,actual_dose)
            if card:
                assert card.count(OLD[key])<=1,(id_,"ambiguous card_text")
            if actual_dose==OLD[key]:
                new_card=card.replace(OLD[key],NEW[key]) if card else card
                cur=db.execute("UPDATE variants SET dose=?, card_text=? WHERE id=? AND rpl_id=? AND dose=?",
                               (NEW[key],new_card,id_,rpl,OLD[key]))
                assert cur.rowcount==1,id_
                changed+=1
            print("VERIFIED",id_,rpl,species,"updated" if actual_dose==OLD[key] else "already-current")
        assert db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]==before_count
        assert db.execute("SELECT COUNT(*) FROM products").fetchone()[0]==products
        assert db.execute("SELECT * FROM variants WHERE id NOT IN ("+",".join(str(i) for i in CHANGES)+") ORDER BY id").fetchall()==before_others
        after=db.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,edible_tissues,milk,
                    restrictions,source,verified_at,scope,pdf_path,withdrawal_short
                    FROM variants WHERE id IN ("""+",".join(str(i) for i in CHANGES)+") ORDER BY id").fetchall()
        assert baseline==after, "Non-dose pharmacological data was unexpectedly modified"
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
        print(f"SUCCESS: {changed} updated out of 7 targets; 22 variants audited; integrity and foreign keys OK.")
if __name__=="__main__":
    main()
