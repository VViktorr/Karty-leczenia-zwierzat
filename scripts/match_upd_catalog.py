"""Rank UPD candidates for the Kartoteki sheet of a Subiekt catalogue."""
import argparse
import csv
import json
import re
import unicodedata
from collections import defaultdict
from pathlib import Path
from zipfile import ZipFile

from openpyxl import load_workbook

FORM = {
    "injection": r"\b(inj|injection|injectable|wstrzykiwan|iniekcja|rtu)\b",
    "pour-on": r"\b(pour on|polewania)\b",
    "intramammary": r"\b(intramammary|dowymieniow\w*)\b",
    "tablet": r"\b(tablet\w*|tabl|tbl|tab)\b",
    "paste": r"\b(pasta|paste)\b",
    "spray": r"\b(spray|aerozol)\b",
}


def normalize(text):
    text = unicodedata.normalize("NFKD", text.casefold().replace("ł", "l"))
    return "".join(c for c in text if not unicodedata.combining(c))


def signature(name):
    text = normalize(name)
    strength = set(re.findall(
        r"\b(\d+(?:[,.]\d+)?)\s*(mg|mcg|μg|g|iu)\s*/\s*(?:1\s*)?(ml|g|kg)\b",
        text,
    ))
    strength = {(v.replace(",", "."), "mcg" if u == "μg" else u, d)
                for v, u, d in strength}
    words = re.findall(r"[a-z]+", text)
    form = {key for key, pattern in FORM.items()
            if re.search(pattern, text.replace("-", " "))}
    return (words[0] if words else ""), strength, form


def candidates(archive):
    index = defaultdict(list)
    with ZipFile(archive) as source, source.open("upd-products.jsonl") as records:
        for line in records:
            if not line.strip():
                continue
            product = json.loads(line)
            if product.get("resourceType") != "MedicinalProductDefinition":
                continue
            for name in product.get("name", []):
                label = name.get("productName") or ""
                brand, strength, form = signature(label)
                if brand and product.get("id"):
                    countries = sorted({
                        code["code"]
                        for usage in name.get("countryLanguage", [])
                        for code in usage.get("country", {}).get("coding", [])
                        if code.get("code")
                    })
                    index[brand].append((product["id"], label, strength, form, ",".join(countries)))
    return index


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path, help="Katalog_lekow.xlsx")
    parser.add_argument("upd", type=Path, help="ZIP with upd-products.jsonl")
    parser.add_argument("output", type=Path, help="Candidate CSV")
    args = parser.parse_args()
    if args.output.resolve() in {args.catalog.resolve(), args.upd.resolve()}:
        parser.error("Output must be different from both inputs.")
    index = candidates(args.upd)
    book = load_workbook(args.catalog, read_only=True, data_only=True)
    try:
        rows = book["Kartoteki"].values
        headers = next(rows)
        symbol_col = headers.index("Symbol Subiekta")
        name_col = headers.index("Nazwa w Subiekcie")
        with args.output.open("w", encoding="utf-8-sig", newline="") as output:
            writer = csv.writer(output, delimiter=";")
            writer.writerow(("Symbol", "Kartoteka", "ID UPD", "Nazwa UPD",
                             "Kody kraju nazwy UPD", "Moc", "Postać", "Status"))
            for row in rows:
                if not row[symbol_col] or not row[name_col]:
                    continue
                symbol, label = row[symbol_col], str(row[name_col])
                brand, strength, form = signature(label)
                ranked = {}
                for upd_id, name, upd_strength, upd_form, countries in index.get(brand, ()):
                    if strength and upd_strength and strength.isdisjoint(upd_strength):
                        continue
                    if form and upd_form and form.isdisjoint(upd_form):
                        continue
                    same_strength = bool(strength and strength == upd_strength)
                    same_form = bool(form and form == upd_form)
                    score = 2 * same_strength + same_form
                    result = (score, name, countries, same_strength, same_form)
                    if upd_id not in ranked or result > ranked[upd_id]:
                        ranked[upd_id] = result
                if not ranked:
                    writer.writerow((symbol, label, "", "", "", "", "", "Brak kandydata"))
                    continue
                best = max(value[0] for value in ranked.values())
                for upd_id, (score, name, countries, same_strength, same_form) in sorted(ranked.items()):
                    if score == best:
                        writer.writerow((symbol, label, upd_id, name, countries,
                                         "Zgodna" if same_strength else "Do sprawdzenia",
                                         "Zgodna" if same_form else "Do sprawdzenia",
                                         "Kandydat do weryfikacji"))
    finally:
        book.close()
    print(args.output)


if __name__ == "__main__":
    main()
