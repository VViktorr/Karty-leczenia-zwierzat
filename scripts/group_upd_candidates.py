"""Group candidate CSV rows for shared review. Does not confirm product IDs."""
import argparse
import csv
import json
import re
from collections import defaultdict
from pathlib import Path

PACKAGE = re.compile(
    r"(.*?)\s+A\s*['’]\s*\d+(?:[.,]\d+)?\s*(?:ML|L|G|KG)"
    r"(?:\s+([A-Z0-9/.-]+(?:\s+[A-Z0-9/.-]+)*))?", re.I
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidates", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.candidates.resolve() == args.output.resolve():
        parser.error("Choose a separate output file.")
    cards = {}
    with args.candidates.open(encoding="utf-8-sig", newline="") as source:
        for row in csv.DictReader(source, delimiter=";"):
            name, ids = cards.setdefault(row["Symbol"], (row["Kartoteka"], set()))
            if name != row["Kartoteka"]:
                parser.error(f"Conflicting names for symbol {row['Symbol']}")
            if row["ID UPD"]:
                ids.add(row["ID UPD"])
    groups = defaultdict(list)
    for symbol, (name, ids) in cards.items():
        match = PACKAGE.fullmatch(name.strip())
        if match and (not match[2] or all(any(c.isdigit() for c in token) for token in match[2].split())):
            key = " ".join(match[1].upper().split())
            groups[key].append((symbol, name, ids))
    with args.output.open("w", encoding="utf-8-sig", newline="") as output:
        writer = csv.writer(output, delimiter=";")
        writer.writerow(("Grupa robocza", "Liczba kartotek", "Kartoteki (JSON)",
                         "Liczba wspólnych ID UPD", "Wspólne ID UPD", "Status"))
        for key, members in sorted(groups.items()):
            if len(members) < 2:
                continue
            shared = set.intersection(*(ids for _, _, ids in members))
            writer.writerow((key, len(members), json.dumps(
                [{"symbol": symbol, "nazwa": name} for symbol, name, _ in members],
                ensure_ascii=False), len(shared), ", ".join(sorted(shared)),
                "Sprawdzić moc, postać i pozwolenie w Polsce"))


if __name__ == "__main__":
    main()
