#!/usr/bin/env python3
"""Attach original Polish RPL PDFs to variants with matching RPL IDs only.

Safely operates on the *current* SQLite file. Does not edit drug doses,
withdrawal periods, source citations, product status, or archival records.
Use --apply to commit changes to SQLite. Default is read-only check.
"""
import argparse
from pathlib import Path
import sqlite3

ROOT = Path(__file__).resolve().parents[1]
DB = ROOT / "src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
PDFS = {
    "18835": "data/ChPL/RPL/RPL_18835_Cyclix_250_mcg_ml_roztwór_do_wstrzykiwań_dla__RPL_ChPL.pdf",
    "20507": "data/ChPL/RPL/RPL_20507_Bioketan_RPL_ChPL.pdf",
    "18833": "data/ChPL/RPL/RPL_18833_Buserelin_aniMedica_0_004_mg_ml_roztwór_do_ws_RPL_ChPL.pdf",
    "17131": "data/ChPL/RPL/RPL_17131_Carofertin_RPL_ChPL.pdf",
}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="apply safe PDF link updates")
    args = parser.parse_args()
    for rpl, relative in PDFS.items():
        path = ROOT / relative
        if not path.is_file() or path.open("rb").read(5) != b"%PDF-":
            raise RuntimeError(f"Missing/not-PDF for RPL {rpl}: {relative}")
    with sqlite3.connect(f"file:{DB}?mode={'rw' if args.apply else 'ro'}", uri=True) as conn:
        conn.execute("PRAGMA foreign_keys=ON")
        assert conn.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        assert not conn.execute("PRAGMA foreign_key_check").fetchall()
        before = conn.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,
            indication,route,dose,edible_tissues,milk,restrictions,source,
            verified_at,card_text,scope,withdrawal_short
            FROM variants ORDER BY id""").fetchall()
        product_count = conn.execute("SELECT count(*) FROM products").fetchone()[0]
        variant_count = len(before)
        if args.apply:
            conn.execute("BEGIN IMMEDIATE")
        total = 0
        for rpl, relative in PDFS.items():
            ids = [r[0] for r in conn.execute(
                "SELECT id FROM variants WHERE rpl_id=? AND pdf_path IS NULL ORDER BY id",
                (rpl,))]
            assert len(ids) <= 30, (rpl, ids)
            print(f"RPL {rpl}: {len(ids)} PDF links missing, IDs={ids}")
            total += len(ids)
            if args.apply and ids:
                conn.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)", (relative,))
                conn.executemany(
                    "UPDATE variants SET pdf_path=? WHERE id=? AND rpl_id=? AND pdf_path IS NULL",
                    [(relative, id_, rpl) for id_ in ids])
                assert all(conn.execute(
                    "SELECT pdf_path FROM variants WHERE id=?", (id_,)
                ).fetchone()[0] == relative for id_ in ids)
        if args.apply:
            after = conn.execute("""SELECT id,symbol,name,rpl_id,upd_id,species,
                indication,route,dose,edible_tissues,milk,restrictions,source,
                verified_at,card_text,scope,withdrawal_short
                FROM variants ORDER BY id""").fetchall()
            assert after == before, "Unexpected modification of pharmacological fields"
            assert conn.execute("SELECT count(*) FROM products").fetchone()[0] == product_count
            assert conn.execute("SELECT count(*) FROM variants").fetchone()[0] == variant_count
            assert conn.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
            assert not conn.execute("PRAGMA foreign_key_check").fetchall()
            conn.commit()
            print(f"Successfully linked {total} PDF documents; integrity checks passed.")
        else:
            print(f"Read-only preview: {total} missing links. Run with --apply to attach.")

if __name__ == "__main__":
    main()
