#!/usr/bin/env python3
"""Seven guarded vaccine corrections: Biocan R / Porcilis Ery+Parvo / Rispoval Inactivatum.

Official Polish product material:
Biocan R RPL18679 leaflet section 8,
Porcilis Ery+Parvo RPL19703 ChPL 3.9,
Rispoval IBR-Marker Inactivatum RPL19789 ChPL 3.9.
No alterations to withdrawals, documents, species, indications or other products.
"""
import sqlite3
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
REPORT=ROOT/"data/audyt_3_szczepionek_partia_09_ChPL.md"
URLS={
"18679":"https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/18679/leaflet",
"19703":"https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/19703/characteristic",
"19789":"https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/19789/characteristic",
}
NEW_BIO=("1 ml na zwierzę SC lub IM. Pierwsze szczepienie od ukończenia 3. miesiąca życia; "
         "jeżeli szczepiono wcześniej, doszczepić po ukończeniu 3. miesiąca, z zachowaniem co najmniej "
         "14 dni od poprzedniej dawki. Zwierzę pierwszy raz szczepione w wieku 3–12 miesięcy: "
         "powtórzyć po roku; szczepienia coroczne kontynuować przez co najmniej 2 lata. "
         "Dalsze szczepienia zgodnie z obowiązującymi przepisami weterynaryjnymi.")
NEW_POR=("2 ml IM w szyję za uchem. Pierwsze szczepienie loszek po osiągnięciu wieku 6 miesięcy "
         "i nie później niż 2 tygodnie przed pierwszym kryciem. Dla odporności przeciw różycy "
         "szczepienie podstawowe obejmuje dodatkową dawkę szczepionki monowalentnej "
         "4 tygodnie przed lub 4 tygodnie po Porcilis Ery+Parvo. "
         "Powtarzać Porcilis Ery+Parvo raz na rok, a szczepionkę monowalentną przeciw różycy "
         "podać 6 miesięcy po każdej dawce Porcilis Ery+Parvo.")
NEW_RIS=("2 ml podskórnie (SC) na dawkę, u bydła od 3. miesiąca życia. Szczepienie podstawowe: "
         "2 dawki w odstępie 3–5 tygodni. Po podstawowym szczepieniu Inactivatum: "
         "jedna dawka przypominająca co 6 miesięcy. Jeśli szczepienie podstawowe wykonano Vivum: "
         "pierwsza dawka Inactivatum po 6 miesiącach, następne co 12 miesięcy. "
         "Ochrona przed poronieniami: 2 dawki Inactivatum SC co 3–5 tygodni albo jedna dawka Vivum IM "
         "i Inactivatum SC po 6 miesiącach; szczepienie podstawowe lub przypominające "
         "zaleca się przeprowadzić nie później niż przed rozpoczęciem drugiego trymestru ciąży.")
OLD_BIO="1 ml jednorazowo od 12. tyg. życia; dawka przypominająca zgodnie z ChPL."
OLD_BIO_DOG="1 ml jednorazowo od 12. tyg. życia; dawka przypominająca co 1–3 lata zgodnie z przepisami i ChPL."
OLD_BIO_FERRET="1 ml jednorazowo od 12. tyg. życia."
OLD_POR=("2 ml/zwierzę; podać domięśniowo w szyję za uchem. Szczepienie podstawowe i przypominające "
         "według ChPL, z uwzględnieniem odrębnego schematu dla różycy.")
OLD_RIS="2 ml/zwierzę; bydło ≥3 mies.: 2 dawki co 3–5 tyg.; dalej dawki przypominające wg ChPL"
EXPECTED={
 2103:("18679",OLD_BIO,NEW_BIO),2104:("18679",OLD_BIO,NEW_BIO),
 2529:("18679",OLD_BIO_DOG,NEW_BIO),2530:("18679",OLD_BIO,NEW_BIO),
 2531:("18679",OLD_BIO_FERRET,NEW_BIO),
 696:("19703",OLD_POR,NEW_POR),1304:("19703",OLD_POR,NEW_POR),
 320:("19789",OLD_RIS,NEW_RIS),485:("19789",OLD_RIS,NEW_RIS),
}
def main():
    with sqlite3.connect(DB) as db:
        db.row_factory=sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        before=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        targets={r["id"]:r for r in before if r["id"] in EXPECTED}
        assert set(targets)==set(EXPECTED)
        for vid,(rpl,old,new) in EXPECTED.items():
            r=targets[vid]
            assert str(r["rpl_id"])==rpl,(vid,r["rpl_id"])
            assert "archiwal" not in str(r["scope"] or "").casefold()
            assert r["dose"] in (old,new),(vid,r["dose"])
            assert r["pdf_path"] and (ROOT/r["pdf_path"]).is_file()
            if rpl=="18679":
                assert "Pies" in r["species"] or "Psy" in r["species"] or "Kot" in r["species"] or "fret" in r["species"].lower(),(vid,r["species"])
            elif rpl=="19703":
                assert r["route"]=="IM" and "Świnie" in r["species"]
            else:
                assert r["route"]=="SC" and r["species"]=="Bydło"
            if r["card_text"]:
                assert r["card_text"].count(old)<=1,(vid,"multiple old dose copies")
        untouched={r["id"]:dict(r) for r in before if r["id"] not in EXPECTED}
        meta={vid:{k:r[k] for k in r.keys() if k not in ("dose","card_text")} for vid,r in targets.items()}
        total=db.execute("SELECT COUNT(*) FROM variants").fetchone()[0]
        db.execute("BEGIN IMMEDIATE")
        changed=0
        for vid,(rpl,old,new) in EXPECTED.items():
            r=targets[vid]
            if r["dose"]==new:continue
            card=r["card_text"]
            new_card=card.replace(old,new) if card and old in card else card
            n=db.execute("UPDATE variants SET dose=?,card_text=? WHERE id=? AND rpl_id=? AND dose=?",
                         (new,new_card,vid,rpl,old)).rowcount
            assert n==1,vid
            changed+=1
        after=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert len(after)==total
        assert {r["id"]:dict(r) for r in after if r["id"] not in EXPECTED}==untouched
        for r in after:
            if r["id"] in EXPECTED:
                assert r["dose"]==EXPECTED[r["id"]][2]
                assert {k:r[k] for k in r.keys() if k not in ("dose","card_text")}==meta[r["id"]]
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
        sections=["# Partia 09 — schematy 3 szczepionek z polskich ChPL","",
            "Dane źródłowe: "+", ".join(URLS.values()),"",
            "Bez zmian karencji, gatunków, dróg, dokumentów, wskazań i rekordów innych leków.",
            "Skorygowano "+str(changed)+" z "+str(len(EXPECTED))+" wariantów.",
            "","## Biocan R — RPL 18679, warianty 2103, 2104, 2529, 2530, 2531","",NEW_BIO,
            "","## Porcilis Ery+Parvo — RPL 19703, warianty 696 i 1304","",NEW_POR,
            "","## Rispoval IBR-Marker Inactivatum — RPL 19789, warianty 320 i 485","",NEW_RIS,
            "","Kontrole: integrity_check=ok; foreign_key_check=ok; pozostałe warianty bez zmian."]
        REPORT.write_text("\n".join(sections),encoding="utf-8")
        print("SUCCESS three vaccines: updated",changed,"of",len(EXPECTED),"variants; integrity_check=ok foreign_key_check=ok")
if __name__=="__main__":
    main()
