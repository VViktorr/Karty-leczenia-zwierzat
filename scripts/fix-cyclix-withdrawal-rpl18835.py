#!/usr/bin/env python3
"""Cyclix RPL 18835: align all variants' withdrawal fields with current Polish ChPL.
Official source (section 3.12): https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/18835/characteristic
No dose, indications, archival status, or other products are modified.
"""
import argparse
import sqlite3
import re
from pathlib import Path

DB = Path(__file__).resolve().parents[1] / "src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
RPL = "18835"
EXPECTED_IDS = set(range(197, 205)) | set(range(219, 227))
TISSUES = "1 dzień"
MILK = "0 godzin"
SHORT = "tk. 1 dzień, ml. 0 h"

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args()
    uri = f"file:{DB}?mode={'rw' if args.apply else 'ro'}"
    with sqlite3.connect(uri, uri=True) as db:
        db.execute("PRAGMA foreign_keys=ON")
        assert db.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        assert db.execute("PRAGMA foreign_key_check").fetchall() == []
        cols = {row[1] for row in db.execute("PRAGMA table_info(variants)")}
        assert {"id", "rpl_id", "edible_tissues", "milk", "withdrawal_short", "card_text", "dose"}.issubset(cols)
        assert db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='price_withdrawal_entries'").fetchone()
        ids = {v[0] for v in db.execute("SELECT id FROM variants WHERE rpl_id=?", (RPL,))}
        assert ids == EXPECTED_IDS, f"Unexpected RPL 18835 variants; no changes: {sorted(ids)}"
        before_products = db.execute("SELECT count(*) FROM products").fetchone()[0]
        before_variants = db.execute("SELECT count(*) FROM variants").fetchone()[0]
        before_other = db.execute("SELECT * FROM variants WHERE rpl_id IS NOT ? ORDER BY id", (RPL,)).fetchall()
        before_dose = db.execute(
            "SELECT id, rpl_id, species, indication, route, dose, source, pdf_path, scope FROM variants WHERE rpl_id=? ORDER BY id",
            (RPL,)
        ).fetchall()
        updates = 0
        if args.apply:
            db.execute("BEGIN IMMEDIATE")
        for id_, tissues, milk, short, card_text in db.execute(
            "SELECT id, edible_tissues, milk, withdrawal_short, card_text FROM variants WHERE rpl_id=? ORDER BY id", (RPL,)
        ).fetchall():
            # Replace only the established withdrawal wording inside cards.
            new_card = card_text
            if card_text:
                new_card = re.sub(r"(?i)(\\bTkanki(?: jadalne)?\\s*:\\s*)2\\s*dni\\b", r"\\g<1>1 dzień", new_card)
                new_card = re.sub(r"(?i)(\\bMleko\\s*:\\s*)(?:zero\\s+godzin|0\\s*h(?:odzin)?)\\b", r"\\g<1>0 godzin", new_card)
            if (tissues, milk, short, card_text) != (TISSUES, MILK, SHORT, new_card):
                updates += 1
            print(f"Cyclix variant {id_}: {tissues!r}, {milk!r}, {short!r} -> {TISSUES!r}, {MILK!r}, {SHORT!r}")
            if args.apply:
                db.execute("""UPDATE variants SET edible_tissues=?, milk=?, withdrawal_short=?, card_text=?
                              WHERE id=? AND rpl_id=?""", (TISSUES, MILK, SHORT, new_card, id_, RPL))
                db.execute("""INSERT INTO price_withdrawal_entries (id, compact_text)
                              VALUES (?,?) ON CONFLICT(id) DO UPDATE SET compact_text=excluded.compact_text""",
                           (id_, SHORT))
        if args.apply:
            assert db.execute("SELECT count(*) FROM variants").fetchone()[0] == before_variants
            assert db.execute("SELECT count(*) FROM products").fetchone()[0] == before_products
            assert db.execute("SELECT * FROM variants WHERE rpl_id IS NOT ? ORDER BY id", (RPL,)).fetchall() == before_other
            assert db.execute(
                "SELECT id, rpl_id, species, indication, route, dose, source, pdf_path, scope FROM variants WHERE rpl_id=? ORDER BY id",
                (RPL,)
            ).fetchall() == before_dose
            assert db.execute("""SELECT count(*) FROM variants v LEFT JOIN price_withdrawal_entries p ON p.id=v.id
                WHERE v.rpl_id=? AND (v.edible_tissues<>? OR v.milk<>? OR v.withdrawal_short<>?
                OR p.compact_text IS NULL OR p.compact_text<>?)""",
                (RPL, TISSUES, MILK, SHORT, SHORT)).fetchone()[0] == 0
            assert db.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
            assert db.execute("PRAGMA foreign_key_check").fetchall() == []
            db.commit()
            print(f"COMMITTED: checked 16 Cyclix variants, {updates} withdrawal-field changes; integrity OK")
        else:
            print(f"DRY RUN: checked 16 Cyclix variants, {updates} withdrawal-field differences")
if __name__ == "__main__":
    main()
