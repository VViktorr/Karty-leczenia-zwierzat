"""Preview and import simple VetLek withdrawal periods (requires pyodbc).

Set REACTKL_SQL_CONNECTION to an ODBC connection string for KartyLeczenia.
Preview: python Import-VetLekShortWithdrawal.py VetLek-katalog.json review.csv
Apply:   python Import-VetLekShortWithdrawal.py VetLek-katalog.json review.csv --apply
Set approved to 1 only on rows marked ready after checking the source.
"""

import argparse
import csv
import json
import os
import re
from pathlib import Path

FIELDS = (
    "approved", "reason", "variant_id", "rpl", "species", "route", "commodity",
    "source_text", "short_text", "document", "vetlek_text",
)
COMMODITIES = {"tkanki jadalne": "tissues", "tkanki": "tissues",
               "mleko": "milk", "jaja": "eggs"}
PERIOD = re.compile(r"(0|[1-9][0-9]{0,5})\s*(dzień|dni|doby|dób|doba|h|godz\.?|godzin[ay]?)", re.I)


def normalized(value):
    return " ".join((value or "").casefold().split())


SPECIES_ALIASES = {
    "świnie": "świnia", "kury": "kura", "indyki": "indyk",
    "konie": "koń", "gołębie": "gołąb", "psy": "pies", "koty": "kot",
    "owce": "owca", "kozy": "koza", "króliki": "królik",
    "cielę": "cielęta", "cielęta bydła": "cielęta", "bydło (cielęta)": "cielęta",
    "świnie (prosięta)": "prosięta", "owce (jagnięta)": "jagnięta",
    "krowy w okresie zasuszenia": "krowy zasuszone",
    "bydło (krowy w okresie zasuszenia)": "krowy zasuszone",
    "bydło (krowy zasuszone)": "krowy zasuszone",
    "bydło (krowy w laktacji)": "krowy w laktacji",
}
SPECIES_PARENTS = {
    "cielęta": "bydło", "krowy zasuszone": "bydło", "krowy w laktacji": "bydło",
    "prosięta": "świnia", "jagnięta": "owca",
}
ROUTE_ALIASES = {
    "im": "domięśniowo", "domięśniowa": "domięśniowo",
    "iv": "dożylnie", "dożylna": "dożylnie",
    "sc": "podskórnie", "podskórna": "podskórnie",
    "po": "doustnie", "doustna": "doustnie",
    "imm": "dowymieniowo", "dowymieniowa": "dowymieniowo",
    "doustnie w wodzie do picia": "w wodzie do picia",
    "doustna — woda do picia": "w wodzie do picia",
    "doustnie, przez wodę do picia": "w wodzie do picia",
    "po (woda do picia)": "w wodzie do picia",
    "doustnie w wodzie": "w wodzie do picia",
    "polewanie (pour-on)": "pour-on",
}


def species_key(value):
    key = normalized(value)
    return SPECIES_ALIASES.get(key, key)


def route_key(value):
    key = normalized(value)
    match = re.fullmatch(r"(.+) [(]([a-z]+)[)]", key)
    if match:
        name, code = match.groups()
        canonical = ROUTE_ALIASES.get(name, name)
        if canonical == ROUTE_ALIASES.get(code):
            return canonical
    return ROUTE_ALIASES.get(key, key)


def period(value):
    match = PERIOD.fullmatch(normalized(value).rstrip("."))
    if match:
        count, unit = match.groups()
        return int(count), "days" if unit.startswith("d") else "hours"
    return None


def vetlek_periods(text, species):
    """Accept only standalone, labelled periods under an exact species heading."""
    result = {}
    current_species = None
    for line in text.splitlines():
        line = line.strip()
        heading = species_key(line.rstrip(":"))
        if heading in species or SPECIES_PARENTS.get(heading) in species:
            current_species = heading
            continue
        label, sep, value = line.partition(":")
        commodity = COMMODITIES.get(normalized(label))
        if not current_species or not sep or not commodity or not period(value):
            # Reject subsequent values until the next explicit species heading.
            if line:
                current_species = None
            continue
        result.setdefault((current_species, commodity), set()).add(period(value))
    return result


def rows(cursor, catalog):
    products = {product["id"]: product for product in catalog["products"]}
    for variant_id, rpl, species, route, tissues, milk, eggs in cursor.execute("""
        SELECT v.id, COALESCE(NULLIF(v.rpl_id,N''),p.rpl_id),v.species,v.route,
               v.edible_tissues,v.milk,v.eggs
        FROM dbo.catalog_variants v JOIN dbo.catalog_products p ON p.symbol=v.symbol
        ORDER BY v.id
    """):
        registry_id = str(rpl or "").strip()
        product_id = registry_id if registry_id.startswith("rpl:") else "rpl:" + registry_id
        product = products.get(product_id, {})
        section = catalog["summaries"].get(product_id, {}).get("sec", {}).get("karencja", {})
        text = section.get("x", "")
        routes = {route_key(item) for item in product.get("rt", [])}
        species_names = {species_key(item) for item in product.get("sp", [])}
        values = vetlek_periods(text, species_names)
        for commodity, full in zip(("tissues", "milk", "eggs"), (tissues, milk, eggs)):
            if not full:
                continue
            candidate = period(full)
            if not candidate:
                label, sep, value = full.partition(":")
                if sep and COMMODITIES.get(normalized(label)) == commodity:
                    candidate = period(value)
            matches = values.get((species_key(species), commodity))
            if not text:
                reason = "missing_vetlek_source"
            elif (species_key(species) not in species_names and
                  SPECIES_PARENTS.get(species_key(species)) not in species_names):
                reason = "species_requires_review"
            elif routes != {route_key(route)} or not route_key(route):
                reason = "route_requires_review"
            elif not candidate or matches != {candidate}:
                reason = "withdrawal_requires_review"
            else:
                reason = "ready"
            short = ""
            if reason == "ready":
                count, unit = candidate
                short = f"{count} " + ("h" if unit == "hours" else ("dzień" if count == 1 else "dni"))
            yield dict(approved="", reason=reason, variant_id=str(variant_id), rpl=product_id,
                       species=species or "", route=route or "", commodity=commodity,
                       source_text=full, short_text=short,
                       document=str(section.get("d", "")), vetlek_text=text)


INSERT = """
INSERT dbo.catalog_variant_withdrawal
    (variant_id,commodity,short_text,status,duration,unit,source_text)
SELECT v.id, ?, ?, 'period', ?, ?, ?
FROM dbo.catalog_variants v WITH (UPDLOCK,HOLDLOCK)
WHERE v.id=? AND COALESCE(v.species,N'') COLLATE Latin1_General_100_BIN2=?
    AND COALESCE(v.route,N'') COLLATE Latin1_General_100_BIN2=?
    AND COALESCE(CASE ? WHEN 'tissues' THEN v.edible_tissues
                       WHEN 'milk' THEN v.milk ELSE v.eggs END,N'')
        COLLATE Latin1_General_100_BIN2=?
    AND NOT EXISTS (SELECT 1 FROM dbo.catalog_variant_withdrawal w WITH (UPDLOCK,HOLDLOCK)
                    WHERE w.variant_id=v.id AND w.commodity=?)
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("review", type=Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    import pyodbc

    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    with pyodbc.connect(os.environ["REACTKL_SQL_CONNECTION"]) as db:
        cursor = db.cursor()
        database, table = cursor.execute("""
            SELECT DB_NAME(),OBJECT_ID(N'dbo.catalog_variant_withdrawal',N'U')
        """).fetchone()
        if database != "KartyLeczenia" or table is None:
            raise SystemExit("Wybierz bazę KartyLeczenia z tabelą krótkich karencji.")
        current = list(rows(cursor, catalog))
        if not args.apply:
            with args.review.open("x", encoding="utf-8-sig", newline="") as file:
                writer = csv.DictWriter(file, fieldnames=FIELDS, delimiter=";")
                writer.writeheader()
                writer.writerows(current)
            print(f"Wiersze raportu: {len(current)}; do zatwierdzenia: "
                  f"{sum(row['reason'] == 'ready' for row in current)}")
            return
        ready = {(row["variant_id"], row["commodity"]): row for row in current if row["reason"] == "ready"}
        approved = set()
        with args.review.open(encoding="utf-8-sig", newline="") as file:
            for row in csv.DictReader(file, delimiter=";"):
                if row.get("approved") != "1":
                    continue
                key = row["variant_id"], row["commodity"]
                expected = ready.get(key)
                if not expected or any(row.get(field) != expected[field] for field in FIELDS[1:]):
                    raise SystemExit(f"Wpis {key} zmienił się lub wymaga weryfikacji. Wygeneruj nowy raport.")
                approved.add(key)
        inserted = 0
        for key in sorted(approved):
            row = ready[key]
            commodity = row["commodity"]
            count, unit = period(row["short_text"])
            cursor.execute(INSERT, commodity, row["short_text"], count, unit,
                           row["source_text"], int(row["variant_id"]), row["species"],
                           row["route"], commodity, row["source_text"], commodity)
            inserted += cursor.rowcount
        print(f"Dodano: {inserted}; pominięto istniejące lub zmienione: {len(approved) - inserted}")


if __name__ == "__main__":
    main()
