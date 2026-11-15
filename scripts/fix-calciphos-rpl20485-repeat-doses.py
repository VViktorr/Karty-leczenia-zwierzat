#!/usr/bin/env python3
"""Correct repeat-dose guidance for 12 Calciphos RPL 20485 acute-deficiency variants.

Source: official Polish ChPL in data/ChPL/RPL/RPL_20485_Calciphos_RPL_ChPL.pdf,
section 3.9; audited variants data/audyt_Calciphos_warianty_SQLite.md.
Requires exact prior dose and matching indication; changes no other drugs.
"""
import argparse
from pathlib import Path
import sqlite3

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
PDF="data/ChPL/RPL/RPL_20485_Calciphos_RPL_ChPL.pdf"
IDS={771,772,773,1697,1698,1699,1859,1860,1861,1862,1863,1864}
OLD="0,6–0,8 ml/kg m.c.; w razie potrzeby dawki powtarzane wg ChPL"
NEW=("0,6–0,8 ml/kg m.c.; przy braku poprawy ponowne podanie "
     "nie wcześniej niż po 6 godzinach, kolejne nie wcześniej niż po 24 godzinach; "
     "liczbę podań i sposób podania dostosować do stanu zwierzęcia zgodnie z ChPL")

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--apply",action="store_true")
    args=ap.parse_args()
    assert (ROOT/PDF).read_bytes()[:5]==b"%PDF-", "Missing official PDF"
    with sqlite3.connect(f"file:{DB}?mode={'rw' if args.apply else 'ro'}",uri=True) as db:
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert db.execute("PRAGMA foreign_key_check").fetchall()==[]
        rows=db.execute("""SELECT id,rpl_id,species,indication,route,dose,card_text,pdf_path
                           FROM variants WHERE rpl_id='20485' ORDER BY id""").fetchall()
        assert {r[0] for r in rows}==IDS, "Unexpected variants: review required"
        assert len(rows)==12
        assert {r[4] for r in rows}=={"IM","IV","SC"}, "Unexpected routes"
        for r in rows:
            vid,rpl,species,indication,route,dose,card,pdf=r
            assert species=="Bydło" and indication=="Ostra hipokalcemia lub hipomagnezemia",(vid,species,indication)
            assert dose in (OLD,NEW),(vid,dose)
            assert pdf==PDF,(vid,pdf)
            if card and OLD in card:
                assert card.count(OLD)==1,(vid,"ambiguous card_text")
        before_products=db.execute("SELECT count(*) FROM products").fetchone()[0]
        before_count=db.execute("SELECT count(*) FROM variants").fetchone()[0]
        before_other=db.execute("SELECT * FROM variants WHERE rpl_id IS NOT '20485' ORDER BY id").fetchall()
        before_pharm=db.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,
                         edible_tissues,milk,restrictions,source,verified_at,scope,pdf_path,withdrawal_short
                         FROM variants WHERE rpl_id='20485' ORDER BY id""").fetchall()
        changes=sum(r[5]!=NEW for r in rows)
        print(f"Calciphos matching variants={len(rows)}, dose updates={changes}")
        if args.apply:
            db.execute("BEGIN IMMEDIATE")
            for r in rows:
                vid,_,_,_,_,dose,card,_=r
                updated_card=card.replace(OLD,NEW) if card and OLD in card else card
                db.execute("""UPDATE variants SET dose=?,card_text=?
                           WHERE id=? AND rpl_id='20485' AND dose=?""",(NEW,updated_card,vid,dose))
                assert db.execute("SELECT changes()").fetchone()[0]==1,vid
            assert db.execute("SELECT count(*) FROM variants").fetchone()[0]==before_count
            assert db.execute("SELECT count(*) FROM products").fetchone()[0]==before_products
            assert db.execute("SELECT * FROM variants WHERE rpl_id IS NOT '20485' ORDER BY id").fetchall()==before_other
            assert db.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,
                         edible_tissues,milk,restrictions,source,verified_at,scope,pdf_path,withdrawal_short
                         FROM variants WHERE rpl_id='20485' ORDER BY id""").fetchall()==before_pharm
            assert db.execute("SELECT count(*) FROM variants WHERE rpl_id='20485' AND dose=?",(NEW,)).fetchone()[0]==12
            assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
            assert db.execute("PRAGMA foreign_key_check").fetchall()==[]
            db.commit()
            print("SUCCESS: 12 variants verified; changes committed; integrity checks OK")
        else:
            print("DRY RUN: no database changes")
if __name__=="__main__":
    main()
