#!/usr/bin/env python3
"""Read-only audit of all Calciphos RPL 20485 variants from current SQLite.

Review route, indication and dose together before changing any data.
No app build, SQL migration, or pharmacological changes.
"""
import json
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATABASE = ROOT / "src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
REPORT = ROOT / "data/audyt_Calciphos_warianty_SQLite.md"
EXPECTED_IDS = {1859, 1860, 1861, 1862, 1863, 1864, 771, 772, 773, 1697, 1698, 1699}

def main():
    with sqlite3.connect(f"file:{DATABASE}?mode=ro", uri=True) as con:
        con.row_factory = sqlite3.Row
        assert con.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        assert not con.execute("PRAGMA foreign_key_check").fetchall()
        rows = con.execute("""
            SELECT id, symbol, name, rpl_id, species, indication, route, dose,
                   edible_tissues, milk, restrictions, pdf_path, source, scope
            FROM variants WHERE rpl_id='20485' ORDER BY symbol,id
        """).fetchall()
        actual_ids = {r["id"] for r in rows}
        # Abort for manual review if additional or missing variants were found.
        assert actual_ids == EXPECTED_IDS, f"RPL20485 IDs changed; review required: {sorted(actual_ids)}"
        for row in rows:
            assert row["pdf_path"] and "20485" in row["pdf_path"], f"Missing official PDF for variant {row['id']}"
        lines = [
            "# Calciphos — audyt rzeczywistych wariantów w SQLite",
            "",
            "Źródło: aktualna baza na gałęzi winforms. **Raport techniczny, nie nowa ChPL.**",
            "",
            "ChPL: `data/ChPL/RPL/RPL_20485_Calciphos_RPL_ChPL.pdf`.",
            "",
            "W ChPL odrębne schematy dla ostrych niedoborów (0,6–0,8 ml/kg), chorób szkieletu",
            "(0,4–0,5 ml/kg) i stanów zapalnych/alergicznych (0,2–0,3 ml/kg).",
            "Zestawienie poniżej nie autoryzuje automatycznej zamiany dawek.",
            "",
        ]
        for row in rows:
            lines.extend([
                f"## Wariant {row['id']}: {row['name'] or row['symbol']}",
                "",
                f"- Symbol: `{row['symbol']}`",
                f"- Gatunek: {row['species'] or 'BRAK'}",
                f"- Wskazanie: {row['indication'] or 'BRAK'}",
                f"- Droga podania: {row['route'] or 'BRAK'}",
                f"- Dawka: {row['dose'] or 'BRAK'}",
                f"- Karencja tkanek: {row['edible_tissues'] or 'BRAK'}",
                f"- Mleko: {row['milk'] or 'BRAK'}",
                f"- Ograniczenia: {row['restrictions'] or 'BRAK'}",
                f"- PDF: `{row['pdf_path']}`",
                f"- Zakres/status wariantu: {row['scope'] or 'BRAK'}",
                "",
            ])
        REPORT.write_text("\n".join(lines),encoding="utf-8")
        print(f"PASS read-only; {len(rows)} variants; integrity_check=ok; foreign_key_check=ok")
        for row in rows:
            print("VARIANT",json.dumps({k:row[k] for k in ("id","species","indication","route","dose","scope")},ensure_ascii=False))
        print(f"REPORT={REPORT.relative_to(ROOT)}")
if __name__=="__main__":
    main()
