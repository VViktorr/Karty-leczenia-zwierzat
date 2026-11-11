"""Export the catalogue workbook for the Windows application (requires openpyxl)."""
import json
import re
import sys
from collections import defaultdict

from openpyxl import load_workbook


def val(value):
    return "" if value is None else str(value).strip()


def document_id(value):
    value = val(value)
    return value[3:-1] if re.fullmatch(r"100\d{6}", value) else value


def source_url(value):
    match = re.search(
        r"https://(?:rejestry(?:medyczne)?\.ezdrowie\.gov\.pl/api/rpl/medicinal-products/\d+/characteristic|medicines\.health\.europa\.eu/veterinary/(?:en|pl)/\d{12})(?![\w/])",
        val(value),
    )
    return match.group() if match else ""


workbook = load_workbook(sys.argv[1], read_only=True, data_only=True)
decisions = {(val(r[0]), val(r[1])): r for r in list(workbook["Decyzje użytkownika"].values)[1:] if r[0]}
variants = defaultdict(list)
for row in list(workbook["Warianty"].values)[1:]:
    if row[0] and row[2] and row[3] and row[5] and source_url(row[10]):
        variants[val(row[0])].append(row)

entries = []
for symbol, name, _, _, _, _, rpl_id, status in list(workbook["Kartoteki"].values)[1:]:
    if not symbol or not name:
        continue
    key = val(symbol), val(name)
    choice = decisions.get(key)
    decision = val(choice[2]) if choice else ""
    excluded = decision == "NOT_MEDICINE" or val(status).startswith("To nie jest lek")
    verified = decision == "SELECTED_RPL" or (not choice and val(status).startswith(("Opracowano", "Częściowo opracowano")))
    matching = [
        row for row in variants[key[0]]
        if verified and not excluded and rpl_id and document_id(row[2]) == document_id(rpl_id)
    ]
    name_in_register = val(choice[8]) if choice else (val(matching[0][1]) if matching else "")
    link = source_url(choice[11]) if choice else ""
    if not link and choice and decision.startswith("SELECTED_UPD") and re.fullmatch(r"\d{12}", val(choice[4])):
        link = "https://medicines.health.europa.eu/veterinary/pl/" + val(choice[4])
    if not link and matching:
        link = source_url(matching[0][10])
    if not link and verified and re.fullmatch(r"\d+", document_id(rpl_id)):
        link = "https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/" + document_id(rpl_id) + "/characteristic"
    entries.append(dict(
        Symbol=key[0], SubiektName=key[1], RegisterName=name_in_register, Source=link,
        Excluded=excluded, Variants=[
            dict(Species=val(row[3]), Indication=val(row[4]), Route=val(row[5]), Dosing=val(row[6]),
                 Meat=val(row[7]), Milk=val(row[8]), Restrictions=val(row[9]), Source=source_url(row[10]))
            for row in matching
        ],
    ))

with open(sys.argv[2], "w", encoding="utf-8") as output:
    json.dump(entries, output, ensure_ascii=False, separators=(",", ":"))
