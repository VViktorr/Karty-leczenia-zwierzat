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
        r"https://(?:rejestry(?:medyczne)?\.ezdrowie\.gov\.pl/api/rpl/medicinal-products/\d+/(?:characteristic|leaflet)|medicines\.health\.europa\.eu/veterinary/(?:en|pl)/\d{12})(?![\w/])",
        val(value),
    )
    return match.group() if match else ""


def upd_ids(value):
    return set(re.findall(r"(?:medicines\.health\.europa\.eu/veterinary/(?:en|pl)/|\bUPD\s*[:=]?\s*)(\d{12})(?!\d)", val(value), re.IGNORECASE))


def permits(value):
    return set(re.findall(r"\b(?:EU|UE)/\d+/\d+/\d+", val(value), re.IGNORECASE))


def upd_source(row, decision, choice, status):
    if re.search(r"zawieszon|wycofan|archiwaln", status, re.IGNORECASE):
        return ""
    link = source_url(row[10])
    ids = upd_ids(row[10]) | upd_ids(row[9])
    if re.fullmatch(r"\d{12}", val(row[2])):
        ids.add(val(row[2]))
    if len(ids) != 1:
        return ""
    product_id = next(iter(ids))
    if decision.startswith("SELECTED_UPD"):
        if val(choice[4]) != product_id:
            return ""
    elif choice:
        return ""
    else:
        status_ids = upd_ids(status)
        same_permit = bool(permits(status) & (permits(row[9]) | permits(row[10])))
        if status_ids:
            if status_ids != ids:
                return ""
        elif not (status.startswith("Dane UPD") and same_permit):
            return ""
        if not link and not (status.startswith("Dane UPD") and same_permit):
            return ""
    return "https://medicines.health.europa.eu/veterinary/pl/" + product_id


workbook = load_workbook(sys.argv[1], read_only=True, data_only=True)
decisions = {(val(r[0]), val(r[1])): r for r in list(workbook["Decyzje użytkownika"].values)[1:] if r[0]}
variants = defaultdict(list)
for row in list(workbook["Warianty"].values)[1:]:
    if row[0] and row[3] and row[5]:
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
    matching = []
    if not excluded:
        for row in variants[key[0]]:
            source = source_url(row[10])
            if verified and rpl_id and document_id(row[2]) == document_id(rpl_id) and source:
                matching.append((row, source))
            elif (upd := upd_source(row, decision, choice, val(status))):
                matching.append((row, upd))
    name_in_register = val(choice[8]) if choice else (val(matching[0][0][1]) if matching else "")
    link = source_url(choice[11]) if choice else ""
    if not link and choice and decision.startswith("SELECTED_UPD") and re.fullmatch(r"\d{12}", val(choice[4])):
        link = "https://medicines.health.europa.eu/veterinary/pl/" + val(choice[4])
    if not link and matching:
        link = matching[0][1]
    if not link and verified and re.fullmatch(r"\d+", document_id(rpl_id)):
        link = "https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/" + document_id(rpl_id) + "/characteristic"
    entries.append(dict(
        Symbol=key[0], SubiektName=key[1], RegisterName=name_in_register, Source=link,
        Excluded=excluded, Variants=[
            dict(Species=val(row[3]), Indication=val(row[4]), Route=val(row[5]), Dosing=val(row[6]),
                 Meat=val(row[7]), Milk=val(row[8]), Restrictions=val(row[9]), Source=source)
            for row, source in matching
        ],
    ))

with open(sys.argv[2], "w", encoding="utf-8") as output:
    json.dump(entries, output, ensure_ascii=False, separators=(",", ":"))
