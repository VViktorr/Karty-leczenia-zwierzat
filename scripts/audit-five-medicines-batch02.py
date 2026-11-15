#!/usr/bin/env python3
"""Read-only audit of five medicine families referenced in the dose-quality report.

Families: Ataxxa, Biocan R, Duphalyte, Fertigest, Ganapar.
The original Polish RPL PDFs are validated; no medicine data is changed.
"""
from pathlib import Path
import sqlite3

ROOT = Path(__file__).resolve().parents[1]
DB = ROOT / "src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT = ROOT / "data/audyt_5_lekow_partia_02_SQLite.md"
FAMILIES = {
    "Ataxxa": {
        "36217": "RPL_36217_Ataxxa_RPL_ChPL.pdf",
        "36218": "RPL_36218_Ataxxa_RPL_ChPL.pdf",
        "36219": "RPL_36219_Ataxxa_RPL_ChPL.pdf",
        "36220": "RPL_36220_Ataxxa_RPL_ChPL.pdf",
    },
    "Biocan R": {"18679": "RPL_18679_Biocan_R_RPL_ChPL.pdf"},
    "Duphalyte": {"20218": "RPL_20218_Duphalyte_RPL_ChPL.pdf"},
    "Fertigest": {"39496": "RPL_39496_Fertigest_RPL_ChPL.pdf"},
    "Ganapar": {"35323": "RPL_35323_Ganapar_RPL_ChPL.pdf"},
}
FIELDS = ["id", "symbol", "name", "rpl_id", "species", "indication", "route",
          "dose", "edible_tissues", "milk", "restrictions", "source",
          "verified_at", "card_text", "scope", "pdf_path", "withdrawal_short"]
def safe(value):
    return str(value).replace("\r", " ").replace("\n", " ") if value is not None else "(brak)"

def main():
    with sqlite3.connect(f"file:{DB}?mode=ro", uri=True) as conn:
        conn.row_factory = sqlite3.Row
        assert conn.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        assert conn.execute("PRAGMA foreign_key_check").fetchall() == []
        report = [
            "# Audyt pięciu leków — partia 02",
            "",
            "Źródło: aktualna baza winforms, odczyt bez modyfikacji danych.",
            "Nie jest to ponowna weryfikacja ChPL ani autoryzacja automatycznej korekty dawkowania.",
            "",
        ]
        totals = {}
        for family, documents in FAMILIES.items():
            for rpl, fname in documents.items():
                path = ROOT / "data/ChPL/RPL" / fname
                with path.open("rb") as doc:
                    assert doc.read(5) == b"%PDF-", f"Not a valid official document: {path}"
            tokens = sorted(documents)
            placeholders = ",".join("?" for _ in tokens)
            rows = conn.execute(
                f"SELECT {','.join(FIELDS)} FROM variants WHERE rpl_id IN ({placeholders}) ORDER BY rpl_id, id", tokens
            ).fetchall()
            assert rows and len(rows) < 100, (family, len(rows))
            totals[family] = len(rows)
            report += [f"## {family} — {len(rows)} wariantów", ""]
            for rpl, fname in documents.items():
                report.append(f"- RPL {rpl}: `data/ChPL/RPL/{fname}`")
            report.append("")
            for row in rows:
                report += [f"### Wariant {row['id']}", ""]
                for field in FIELDS[1:]:
                    report.append(f"- **{field}:** {safe(row[field])}")
                expected = documents[str(row["rpl_id"])]
                pdf = row["pdf_path"]
                if pdf and Path(pdf).name != expected:
                    raise RuntimeError(f"Unexpected RPL PDF for variant {row['id']}: {pdf}")
                if not pdf:
                    report.append("- **UWAGA: brak powiązania PDF w SQLite.**")
                report.append("")
            print("AUDITED", family, len(rows), "RPL IDs", tokens)
        OUT.write_text("\n".join(report), encoding="utf-8")
        print("PASS: five medicines, variants", sum(totals.values()),
              "integrity_check=ok foreign_key_check=ok report=", OUT.relative_to(ROOT))
if __name__ == "__main__":
    main()
