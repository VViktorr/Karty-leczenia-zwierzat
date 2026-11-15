#!/usr/bin/env python3
"""One-pass audit + conservative correction of ten RPL medicine families.

Source: CURRENT winforms SQLite and the checked-in Polish RPL PDFs. Never infer
a dose, withdrawal, species, indication or marketing authorization from text.
Only attach a missing official PDF where the variant's RPL ID is an exact match.
Write a durable per-variant audit to the repository. Abort on unexpected links.
"""
import sqlite3
from collections import Counter
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
OUT=ROOT/"data/audyt_10_lekow_partia_03_SQLite.md"
# Ten separate products with direct, not fuzzy, RPL ID matches.
MEDICINES={
    "Depo-Promone":{"16750":"RPL_16750_Depo-Promone_RPL_ChPL.pdf"},
    "Enrofloxan 10%":{"16704":"RPL_16704_Enrofloxan_10_roztwór_RPL_ChPL.pdf"},
    "Excenel":{"16539":"RPL_16539_Excenel_RPL_ChPL.pdf"},
    "Febrivac Dist":{"16827":"RPL_16827_Febrivac_Dist_liofilizat_rozpuszczalnik_do_sp_RPL_ChPL.pdf"},
    "IBR marker żywy":{"28639":"RPL_28639_IBR_marker_żywy_RPL_ChPL.pdf"},
    "Nerfasin":{"29972":"RPL_29972_Nerfasin_vet_RPL_ChPL.pdf"},
    "Nobivac DHPPi":{"16737":"RPL_16737_Nobivac_DHPPi_RPL_ChPL.pdf"},
    "Nobivac Tricat Trio":{"18939":"RPL_18939_Nobivac_Tricat_Trio_RPL_ChPL.pdf"},
    "Oxyflex XLA":{"16575":"RPL_16575_OXYFLEX_XLA_100_ML_126192_RPL_ChPL.pdf"},
    "Polocainum 5%":{"20473":"RPL_20473_Polocainum_Hydrochloricum_5_Cum_Adrenalino_0__RPL_ChPL.pdf"},
}
FIELDS=("id","symbol","name","rpl_id","species","indication","route","dose","edible_tissues","milk","restrictions","scope","pdf_path","withdrawal_short","source")
AUDIT_KEYS=("id","symbol","name","rpl_id","species","indication","route","dose","edible_tissues","milk","restrictions","scope","withdrawal_short","source")
def clean(value):
    return str(value).replace("\n"," ").replace("\r"," ") if value is not None else "(brak)"
def main():
    expected={}
    for family, docs in MEDICINES.items():
        for rpl,filename in docs.items():
            path=ROOT/"data/ChPL/RPL"/filename
            with path.open("rb") as f:
                assert f.read(5)==b"%PDF-",(family,str(path))
            expected[rpl]=("data/ChPL/RPL/"+filename,family)
    with sqlite3.connect(DB) as db:
        db.execute("PRAGMA foreign_keys=ON")
        db.row_factory=sqlite3.Row
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        before=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        before_data=[tuple(r[k] for k in AUDIT_KEYS) for r in before]
        counts={table:db.execute("SELECT count(*) FROM "+table).fetchone()[0] for table in ("products","variants","price_withdrawal_entries")}
        byid={r["id"]:r for r in before}
        lines=["# Partia 03 — 10 leków: audyt oraz bezpieczne uzupełnienie PDF RPL","",
               "Raport z bieżącej bazy SQLite gałęzi winforms; dawki i karencje zachowane bez zmian.",
               "Oryginalne PDF: katalog `data/ChPL/RPL/`. Warianty bez pewnego dopasowania nie są modyfikowane.",
               ""]
        touched=[]
        for rpl,(pdf,family) in expected.items():
            rows=[r for r in before if r["rpl_id"]==rpl]
            assert rows and len(rows)<50,(family,rpl,len(rows))
            lines += [f"## {family} — RPL {rpl}, {len(rows)} wariantów","",f"- Oficjalny plik: `{pdf}`",""]
            for r in rows:
                current=r["pdf_path"]
                if current and current!=pdf:
                    raise RuntimeError(f"Unexpected PDF for {family}, variant {r['id']}: {current!r}; no changes")
                if not current:
                    touched.append((r["id"],rpl,pdf))
                lines += [f"### Wariant {r['id']}",""]
                for key in FIELDS[1:]:
                    lines.append(f"- **{key}:** {clean(r[key])}")
                lines.append(f"- **PDF po korekcie:** `{pdf}`")
                dose=r["dose"] or ""
                if "wg ChPL" in dose or "według ChPL" in dose or "zgodnie z ChPL" in dose:
                    lines.append("- **Do dalszego opracowania:** opis zawiera odesłanie do ChPL; wymagane rozróżnienie wskazań, bez automatycznej zmiany dawki.")
                lines.append("")
            print("AUDIT",family,"RPL",rpl,"variants",len(rows))
        db.execute("BEGIN IMMEDIATE")
        for id_,rpl,pdf in touched:
            db.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)",(pdf,))
            cur=db.execute("UPDATE variants SET pdf_path=? WHERE id=? AND rpl_id=? AND pdf_path IS NULL",(pdf,id_,rpl))
            assert cur.rowcount==1,(id_,rpl)
        after=db.execute("SELECT * FROM variants ORDER BY id").fetchall()
        assert [tuple(r[k] for k in AUDIT_KEYS) for r in after]==before_data,"Unexpected alteration of medical fields"
        assert len(after)==len(before)
        touched_ids={r[0] for r in touched}
        assert all(a["pdf_path"]==b["pdf_path"] for a,b in zip(after,before) if a["id"] not in touched_ids)
        assert all(db.execute("SELECT pdf_path FROM variants WHERE id=?",(id_,)).fetchone()[0]==pdf for id_,_,pdf in touched)
        assert all(db.execute("SELECT count(*) FROM "+table).fetchone()[0]==v for table,v in counts.items())
        assert db.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not db.execute("PRAGMA foreign_key_check").fetchall()
        db.commit()
        lines[3]=f"Uzupełniono {len(touched)} powiązań PDF RPL; bez zmian dawkowania, karencji i statusów."
        OUT.write_text("\n".join(lines),encoding="utf-8")
        print("SUCCESS: 10 drugs",sum(1 for r in before if r["rpl_id"] in expected),"variants; added PDF links",len(touched),"integrity_check=ok foreign_key_check=ok")
if __name__=="__main__":
    main()
