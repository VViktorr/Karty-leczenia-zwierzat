#!/usr/bin/env python3
"""Guarded ChPL fixes for Cyclix RPL 18835 + Fertigest RPL 39496.

Official ChPL:
https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/18835/characteristic
https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/39496/characteristic
Existing unrelated data are untouched. Combined indications for Cyclix remain
combined and clearly differentiated; no unsafe automatic split or guessed IDs.
"""
import sqlite3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
REPORT=ROOT/"data/audyt_korekta_Cyclix_Fertigest_partia_06.md"
PDFS={
    "18835":"data/ChPL/RPL/RPL_18835_Cyclix_250_mcg_ml_roztwór_do_wstrzykiwań_dla__RPL_ChPL.pdf",
    "39496":"data/ChPL/RPL/RPL_39496_Fertigest_RPL_ChPL.pdf",
}
CYC_IDS={200,204,222,226}
OLD_CYC="2 ml/szt. (500 µg kloprostenolu); schemat powtórzeń zależny od wskazania wg ulotki"
NEW_CYC=("2 ml IM na krowę (500 µg kloprostenolu), pojedyncza dawka. "
         "Indukcja porodu: po 270. dniu ciąży, nie wcześniej niż 10 dni przed przewidywaną datą wycielenia. "
         "Wywołanie poronienia: pojedyncza dawka między 5. a 150. dniem ciąży. "
         "Przed podaniem konieczne jest wybranie właściwego wskazania.")
OLD_FERT={
    859:"2,5 ml/zwierzę; protokół wg ChPL",
    862:"2,5 ml/zwierzę; termin wg programu inseminacji z ChPL",
}
NEW_FERT={
    859:("2,5 ml/zwierzę IM lub SC. Przy podwyższaniu wskaźnika zacieleń: podać od początku rui "
         "do inseminacji lub w jej trakcie. Przy synchronizacji: 2,5 ml w dniu 0, "
         "prostaglandyna F2α po 7 dniach, kolejna dawka 2,5 ml busereliny 48 godzin później; "
         "inseminacja 12–24 godziny po drugiej dawce busereliny."),
    862:("2,5 ml/zwierzę IM. Loszki: 115–120 godzin po zakończeniu synchronizacji progestagenem; "
         "lochy: 83–89 godzin po odsadzeniu. Pojedyncza inseminacja 30–33 godziny po wstrzyknięciu; "
         "jeśli nie ma jeszcze oznak rui, inseminować później po ich wystąpieniu."),
}
def main():
    for rpl,pdf in PDFS.items():
        with (ROOT/pdf).open("rb") as f:
            assert f.read(5)==b"%PDF-",pdf
    with sqlite3.connect(DB) as db:
        db.row_factory=sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        target=CYC_IDS|set(OLD_FERT)
        rows=db.execute("SELECT * FROM variants WHERE id IN ("+",".join("?" for _ in target)+")",sorted(target)).fetchall()
        assert {r["id"] for r in rows}==target
        def spec(r):
            if r["id"] in CYC_IDS:
                assert str(r["rpl_id"])=="18835" and r["indication"]=="Indukcja porodu lub poronienia"
                assert r["species"].startswith("Bydło") and r["route"]=="IM"
                assert r["edible_tissues"]=="1 dzień" and r["milk"]=="0 godzin"
                old,new=OLD_CYC,NEW_CYC
            else:
                assert str(r["rpl_id"])=="39496" and r["route"] in ("IM/SC","IM")
                if r["id"]==859:
                    assert r["species"]=="Bydło" and "synchronizacja" in r["indication"]
                if r["id"]==862:
                    assert r["species"]=="Świnie" and "owulacji" in r["indication"]
                old,new=OLD_FERT[r["id"]],NEW_FERT[r["id"]]
            assert r["pdf_path"]==PDFS[str(r["rpl_id"])]
            assert r["dose"] in (old,new),(r["id"],r["dose"])
            card=r["card_text"]
            assert not card or card.count(old)<=1,(r["id"],"ambiguous card")
            return old,new
        for r in rows: spec(r)
        before_count=db.execute("SELECT count(*) FROM variants").fetchone()[0]
        before_all=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        untouched={r["id"]:dict(r) for r in before_all if r["id"] not in target}
        unchanged_fields={r["id"]:{k:r[k] for k in r.keys() if k not in ("dose","card_text")} for r in rows}
        report=["# Partia 06 — Cyclix i Fertigest, precyzyjne schematy z ChPL","",
          "Podstawa: oryginalne polskie ChPL RPL 18835 (pkt 3.9) i RPL 39496 (pkt 4.9).",
          "Wszystkie wcześniejsze karencje, drogi, nazwy, warianty, statusy i dokumenty zachowano.",
          "W Cyclix 4 rekordy z połączonymi wskazaniami pozostają połączone, ale opis precyzuje odrębne okresy ciąży; rozdzielenie rekordów wymaga osobnej zmiany schematu danych.",
          ""]
        db.execute("BEGIN IMMEDIATE")
        updated=0
        for r in rows:
            old,new=spec(r)
            card=r["card_text"]
            new_card=card.replace(old,new) if card and old in card else card
            if r["dose"]==old:
                cur=db.execute("UPDATE variants SET dose=?,card_text=? WHERE id=? AND dose=?",
                               (new,new_card,r["id"],old))
                assert cur.rowcount==1,r["id"]
                updated+=1
            report += ["## Wariant "+str(r["id"]),"",
              "- Lek: "+r["name"],
              "- Gatunek: "+r["species"],
              "- Wskazanie: "+r["indication"],
              "- Dawkowanie po korekcie: "+new,
              "- PDF: "+r["pdf_path"],""]
        after_all=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert len(after_all)==before_count
        assert {r["id"]:dict(r) for r in after_all if r["id"] not in target}==untouched
        for r in after_all:
            if r["id"] in target:
                assert {k:r[k] for k in r.keys() if k not in ("dose","card_text")}==unchanged_fields[r["id"]]
                assert r["dose"]==spec(r)[1]
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
        report.insert(5,"Zmienione dawki: "+str(updated)+" z 6 docelowych wariantów.")
        REPORT.write_text("\n".join(report),encoding="utf-8")
        print("SUCCESS corrected",updated,"of 6 Cyclix/Fertigest variants; integrity_check=ok foreign_key_check=ok")
if __name__=="__main__":
    main()
