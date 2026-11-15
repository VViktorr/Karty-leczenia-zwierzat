#!/usr/bin/env python3
"""Read-only snapshot of Bioketan and Buserelin variants from current SQLite.
No ChPL doses are inferred; the report is for indication-specific human review.
"""
from pathlib import Path
import sqlite3

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
REPORT=ROOT/"data/audyt_Bioketan_Buserelin_warianty_SQLite.md"
REGISTERS={"20507":"Bioketan","18833":"Buserelin"}
COLUMNS=("id","symbol","name","rpl_id","species","indication","route","dose","edible_tissues","milk","restrictions","scope","pdf_path","source")
def printable(v):
    return str(v).replace("\n"," ").replace("\r"," ") if v is not None else "(brak)"
def main():
    with sqlite3.connect(f"file:{DB}?mode=ro",uri=True) as db:
        db.row_factory=sqlite3.Row
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert db.execute("PRAGMA foreign_key_check").fetchall()==[]
        lines=["# Bioketan i Buserelin — audyt wariantów SQLite","",
               "Raport z aktualnej bazy winforms. Nie zastępuje dokumentów urzędowych ani decyzji klinicznej.",""]
        counts={}
        for rpl,name in REGISTERS.items():
            rows=db.execute("SELECT "+",".join(COLUMNS)+" FROM variants WHERE rpl_id=? ORDER BY id",(rpl,)).fetchall()
            assert 0<len(rows)<50,(rpl,len(rows))
            pdf=ROOT/"data/ChPL/RPL"/("RPL_20507_Bioketan_RPL_ChPL.pdf" if rpl=="20507" else "RPL_18833_Buserelin_aniMedica_0_004_mg_ml_roztwór_do_ws_RPL_ChPL.pdf")
            assert pdf.is_file() and pdf.open("rb").read(5)==b"%PDF-",(rpl,pdf)
            counts[name]=len(rows)
            lines += [f"## {name} — RPL {rpl}; {len(rows)} wariantów","",f"Oryginalny PDF: `{pdf.relative_to(ROOT)}`.",""]
            for row in rows:
                lines += [f"### Wariant {row['id']}",""]
                for key in COLUMNS[1:]:
                    lines.append(f"- **{key}:** {printable(row[key])}")
                lines.append("")
                if not row["pdf_path"]:
                    print("PDF_LINK_MISSING",rpl,row["id"])
        REPORT.write_text("\n".join(lines),encoding="utf-8")
        print("READ_ONLY_AUDIT_OK",counts,"integrity_check=ok foreign_key_check=ok",REPORT)
if __name__=="__main__":
    main()
