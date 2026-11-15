#!/usr/bin/env python3
"""Guarded Ganapar cattle indication schedules from official Polish RPL 35323.

Source: https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/35323/characteristic
Only two existing active cattle variants (560, 561). Keep same IDs, indication
enumerations, route, withdrawal, species, product, source and PDF. Do not
fabricate variant IDs or modify sow/mare regimens. Reconcile card_text.
"""
import sqlite3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_Ganapar_RPL35323_schematy_2026-11-15.md"
IDS={560,561}
RPL="35323"
PDF="data/ChPL/RPL/RPL_35323_Ganapar_RPL_ChPL.pdf"
OLD="2 ml/zwierzę (150 µg). Zależnie od wskazania: schematy powtarzane i termin inseminacji według ChPL."
NEW=(
  "Bydło (krowy), IM: 2 ml/zwierzę (150 µg D-kloprostenolu) na podanie. "
  "SYNCHRONIZACJA RUI: 2 iniekcje w odstępie 11 dni; inseminacja po 72 i 96 h od drugiej iniekcji. "
  "WYWOŁANIE RUI: przy obecnym ciałku żółtym w 6.–18. dniu cyklu; ruja zwykle po 48–60 h, "
  "inseminacja po 72–96 h; jeśli nie wystąpi ruja, ponowić iniekcję po 11 dniach. "
  "PRZETRWAŁE CIAŁKO ŻÓŁTE / TORBIELE LUTEALNE: podać po rozpoznaniu, inseminować "
  "podczas pierwszej rui; jeśli ruja nie wystąpi, ponownie zbadać i ewentualnie "
  "podać po 11 dniach; inseminacja po 72–96 h od podania. "
  "ZAPALENIE BŁONY ŚLUZOWEJ MACICY / ROPOMACICZE: 1 iniekcja, w razie konieczności "
  "powtórzyć po 10 dniach. "
  "OPÓŹNIONA INWOLUCJA MACICY: 1 iniekcja, w razie potrzeby powtórzyć 1–2 razy "
  "w odstępach 24 h. "
  "WYWOŁANIE PORODU: jednorazowo po 270. dniu ciąży; poród zwykle po 30–60 h. "
  "TERAPEUTYCZNE WYWOŁANIE PORONIENIA: jednorazowo przed 150. dniem ciąży. "
  "WYDALENIE ZMUMIFIKOWANEGO PŁODU: jednorazowo; wydalenie zwykle po 3–4 dniach."
)
def main():
    with (ROOT/PDF).open("rb") as source:
        assert source.read(5)==b"%PDF-",PDF
    with sqlite3.connect(DB) as con:
        con.row_factory=sqlite3.Row
        con.execute("PRAGMA foreign_keys=ON")
        assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not con.execute("PRAGMA foreign_key_check").fetchall()
        allrows=con.execute("SELECT * FROM variants ORDER BY id").fetchall()
        current=[r for r in allrows if str(r["rpl_id"])==RPL]
        assert {r["id"] for r in current}=={560,561,562,563}
        target=[r for r in current if r["id"] in IDS]
        assert len(target)==2
        expected_indication=("Synchronizacja lub wywołanie rui, wywołanie porodu, zaburzenia czynności jajników "
          "(przetrwałe ciałko żółte, torbiele lutealne), zapalenie błony śluzowej macicy, "
          "ropomacicze, opóźnione obkurczanie macicy, wywołanie poronienia w pierwszej "
          "połowie ciąży i wydalenie zmumifikowanego płodu.")
        for r in target:
            assert r["species"] in ("Bydło","Bydło (krowy)"),r["species"]
            assert r["indication"]==expected_indication,r["id"]
            assert r["route"]=="IM" and str(r["rpl_id"])==RPL
            assert r["edible_tissues"]=="0 dni" and r["milk"]=="0 godzin"
            assert r["pdf_path"]==PDF
            assert "archiwal" not in str(r["scope"] or "").casefold()
            assert r["dose"] in (OLD,NEW),(r["id"],r["dose"])
            if r["card_text"]:
                assert r["card_text"].count(OLD)<=1,(r["id"],"ambiguous card dose")
        untouched={r["id"]:dict(r) for r in allrows if r["id"] not in IDS}
        metadata={r["id"]:{k:r[k] for k in r.keys() if k not in ("dose","card_text")} for r in target}
        counts={t:con.execute("SELECT COUNT(*) FROM "+t).fetchone()[0] for t in ("variants","products","price_withdrawal_entries")}
        changed=0
        con.execute("BEGIN IMMEDIATE")
        for r in target:
            if r["dose"]==NEW:
                continue
            card=r["card_text"]
            new_card=card.replace(OLD,NEW) if card and OLD in card else card
            cur=con.execute("UPDATE variants SET dose=?,card_text=? WHERE id=? AND rpl_id=? AND dose=?",
                            (NEW,new_card,r["id"],RPL,OLD))
            assert cur.rowcount==1,r["id"]
            changed+=1
        after=con.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert {r["id"]:dict(r) for r in after if r["id"] not in IDS}==untouched
        for r in after:
            if r["id"] in IDS:
                assert r["dose"]==NEW
                assert {k:r[k] for k in r.keys() if k not in ("dose","card_text")}==metadata[r["id"]]
        assert all(con.execute("SELECT COUNT(*) FROM "+t).fetchone()[0]==n for t,n in counts.items())
        assert con.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not con.execute("PRAGMA foreign_key_check").fetchall()
        con.commit()
        report=["# Ganapar RPL 35323 — schematy leczenia bydła","",
          "Oficjalna ChPL: https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/35323/characteristic",
          "Warianty 560 i 561 zachowane jako dwa dotychczasowe rekordy (wskazania rozróżnione w polu dawkowania).",
          "Nie tworzono odrębnych rekordów: wymagałoby to osobnej oceny schematu i powiązań danych.",
          "Nie zmieniono dawki podstawowej 2 ml, drogi IM, karencji tkanki 0 dni / mleko 0 h, gatunków ani wariantów koni i świń.",
          "",
          "Poprawione rekordy: "+str(changed)+" z 2.","",
          "## Szczegółowe schematy","",NEW,"",
          "## Bezpieczeństwo","",
          "Sprawdzono integralność i klucze obce SQLite, niezmienność wszystkich innych wariantów i metadanych.",
          "Nie stosować produktu dożylnie; u ciężarnych tylko gdy celem jest wywołanie porodu lub przerwanie ciąży."
        ]
        OUT.write_text("\n".join(report),encoding="utf-8")
        print("SUCCESS Ganapar RPL 35323 cattle variants changed",changed,"of 2; integrity_check=ok foreign_key_check=ok; all other variants preserved")
if __name__=="__main__":
    main()
