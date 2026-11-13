"""Build the offline catalogue from the merged v10 workbook."""
import argparse
import gzip
import json
from decimal import Decimal
from pathlib import Path
import re
import sqlite3
from tempfile import TemporaryDirectory

from openpyxl import load_workbook


def worksheet(book, name):
    return list(book[name].values)[1:]


def build(workbook, repository, output):
    book = load_workbook(workbook, read_only=True, data_only=True)
    cards = worksheet(book, "Kartoteki")
    variants = worksheet(book, "Warianty")
    decisions = {str(row[0]): row[2] for row in worksheet(book, "Decyzje użytkownika")}
    pdfs = sorted(p.relative_to(repository).as_posix() for p in
                  (repository / "data/ChPL").rglob("*.pdf"))
    output.parent.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=output.parent) as temporary:
        database = Path(temporary) / output.name
        db = sqlite3.connect(database)
        db.executescript("""
            PRAGMA foreign_keys = ON;
            PRAGMA user_version = 1;
            CREATE TABLE products (
                symbol TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL,
                description TEXT, stock REAL NOT NULL, unit TEXT,
                withdrawal_from_subiekt TEXT, rpl_id TEXT, upd_id TEXT,
                status TEXT, decision TEXT
            );
            CREATE TABLE upd_search (id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL);
            CREATE TABLE documents (path TEXT PRIMARY KEY NOT NULL);
            CREATE TABLE variants (
                id INTEGER PRIMARY KEY,
                symbol TEXT NOT NULL REFERENCES products(symbol),
                name TEXT, rpl_id TEXT, upd_id TEXT, species TEXT,
                indication TEXT, route TEXT, dose TEXT, edible_tissues TEXT,
                milk TEXT, restrictions TEXT, source TEXT,
                verified_at TEXT, card_text TEXT, scope TEXT,
                pdf_path TEXT REFERENCES documents(path)
            );
            CREATE INDEX products_rpl ON products(rpl_id);
            CREATE INDEX products_upd ON products(upd_id);
            CREATE INDEX variants_symbol ON variants(symbol);
        """)
        def string(value):
            return None if value is None or value == "" else str(value)

        index = repository / "src/KartyLeczenia/Resources/Raw/upd-index.json.gz"
        with gzip.open(index, "rt", encoding="utf-8") as stream:
            upd = json.load(stream)
        with db:
            db.executemany("INSERT INTO upd_search VALUES (?,?)", (
                (entry["id"], " / ".join(n["productName"] or "" for n in entry["name"]))
                for entry in upd
            ))
            db.executemany("INSERT INTO documents VALUES (?)", ((p,) for p in pdfs))
            for row in cards:
                values = list(map(string, row[:9]))
                values[3] = float(Decimal(str(row[3] or 0).replace(",", ".")))
                db.execute("INSERT INTO products VALUES (?,?,?,?,?,?,?,?,?,?)",
                           (*values, decisions.get(values[0])))
            by_name = {Path(path).name: path for path in pdfs}
            by_register = {}
            for path in pdfs:
                key = re.match(r"(RPL|UPD)_(\d+)_", Path(path).name)
                if key:
                    by_register.setdefault(key.groups(), []).append(path)
            for row in variants:
                if "WARIANT USUNIĘTY" in str(row[4]).upper():
                    continue
                values = list(map(string, (*row, None)[:15]))
                source = values[11] or ""
                match = re.search(r"([^/\\]+?\.pdf)(?:\s|$|\))", source, re.I)
                pdf_path = by_name.get(match.group(1).strip()) if match else None
                if not pdf_path and not match:
                    keys = re.findall(r"/medicinal-products/(\d+)/characteristic", source)
                    candidates = by_register.get(("RPL", keys[0]), []) if len(keys) == 1 else []
                    pdf_path = candidates[0] if len(candidates) == 1 else None
                db.execute("INSERT INTO variants VALUES (NULL,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                           (*values, pdf_path))
        assert db.execute("PRAGMA integrity_check").fetchone() == ("ok",)
        assert db.execute("PRAGMA foreign_key_check").fetchone() is None
        print(db.execute("""
            SELECT (SELECT count(*) FROM products), count(*), count(DISTINCT symbol),
                   count(pdf_path), (SELECT count(*) FROM documents) FROM variants
        """).fetchone())
        db.close()
        book.close()
        database.replace(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workbook", type=Path)
    parser.add_argument("repository", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build(args.workbook, args.repository, args.output)
