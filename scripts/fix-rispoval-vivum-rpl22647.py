#!/usr/bin/env python3
"""Two guarded Rispoval IBR-Marker Vivum vaccine schedule corrections.

Official Polish RPL 22647 ChPL, sec. 3.9:
https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/22647/characteristic
Do not require the damaged local PDF: source verified independently.
Preserve existing paths, withdrawal, restrictions, other records.
"""
from pathlib import Path
import sqlite3
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
REPORT=ROOT/"data/audyt_Rispoval_IBR_Vivum_RPL22647_partia_08.md"
RPL="22647"
IDS={261,527}
OLD="2 ml IM lub 1 ml do każdego nozdrza; od 3 mies. IM/IN; dla cieląt 2 tyg.–3 mies. tylko IN; schemat 1 lub 2 dawki wg ChPL"
NEW=("Po rekonstytucji jedna dawka 2 ml: domięśniowo (IM) lub donosowo (IN, po 1 ml do każdego nozdrza). "
     "Cielęta w wieku 2 tygodni–3 miesięcy: pierwsza dawka donosowo, następnie w wieku 3 miesięcy dawka domięśniowo; "
     "przeciwciała matczyne mogą zmniejszać ochronę. "
     "Bydło od 3 miesięcy: jedna dawka podstawowa IM lub IN. "
     "Ochrona przed poronieniami u samic: dwie dawki Vivum IM w odstępie 3–5 tygodni "
     "(alternatywny schemat obejmuje Inactivatum według ChPL). "
     "W razie bezpośredniego narażenia na BHV-1: pierwsza dawka IN, druga IM po 3–5 tygodniach. "
     "Pierwsze szczepienie przypominające po 6 miesiącach; dalej Vivum IM co 6 miesięcy "
     "lub Inactivatum SC zgodnie ze wskazanym w ChPL schematem (odstęp 12 miesięcy). "
     "Szczepić tylko zdrowe zwierzęta; uwzględnić status przeciwciał matczynych i program zwalczania IBR.")
def main():
    with sqlite3.connect(DB) as db:
        db.row_factory=sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        before=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        target=[r for r in before if r["id"] in IDS]
        assert {r["id"] for r in target}==IDS
        for r in target:
            assert str(r["rpl_id"])==RPL and r["species"]=="Bydło"
            assert r["route"]=="IM/IN"
            assert "archiwal" not in str(r["scope"] or "").casefold()
            assert r["dose"] in (OLD,NEW),(r["id"],r["dose"])
            assert not r["card_text"] or r["card_text"].count(OLD)<=1
        untouched={r["id"]:dict(r) for r in before if r["id"] not in IDS}
        unchanged={r["id"]:{k:r[k] for k in r.keys() if k not in ("dose","card_text")} for r in target}
        changed=0
        db.execute("BEGIN IMMEDIATE")
        for r in target:
            if r["dose"]==NEW:
                continue
            card=r["card_text"]
            nc=card.replace(OLD,NEW) if card and OLD in card else card
            cur=db.execute("UPDATE variants SET dose=?,card_text=? WHERE id=? AND rpl_id=? AND dose=?",(NEW,nc,r["id"],RPL,OLD))
            assert cur.rowcount==1
            changed+=1
        after=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert len(after)==len(before)
        assert {r["id"]:dict(r) for r in after if r["id"] not in IDS}==untouched
        for r in after:
            if r["id"] in IDS:
                assert r["dose"]==NEW
                assert {k:r[k] for k in r.keys() if k not in ("dose","card_text")}==unchanged[r["id"]]
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
        REPORT.write_text("\n".join([
          "# Rispoval IBR-Marker Vivum — dwa warianty, RPL 22647","",
          "Źródło: oficjalne polskie ChPL, punkt 3.9: https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/22647/characteristic",
          "Zmiany tylko w dose i odpowiadającym card_text dla wariantów 261 i 527.",
          "Uzupełniono: 2 tyg.–3 mies., >=3 mies., program dla ciężarnych, bezpośrednia ekspozycja, przypomnienia Vivum/Inactivatum.",
          "UWAGA: lokalny PDF RPL 22647 pozostaje do odrębnej naprawy; nie podmieniano go ani nie nadawano fikcyjnego powiązania PDF.",
          "Zachowano wszystkie istniejące inne rekordy i karencje; SQLite integrity_check=ok, foreign_key_check=ok.",
          "",f"Poprawiono {changed} z 2 rekordów.","","## Schemat po korekcie","",NEW
        ]),encoding="utf-8")
        print(f"SUCCESS Rispoval Vivum corrected {changed} of 2; integrity_check=ok foreign_key_check=ok")
if __name__=="__main__":
    main()
