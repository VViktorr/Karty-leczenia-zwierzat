#!/usr/bin/env python3
"""Safely finalize five-medicine audit batch 02.

Ataxxa, Biocan R, Duphalyte, Fertigest, Ganapar are checked against
their registered source IDs and local PDF files. Only the eight absent
Duphalyte pdf_path values are modified. Pharmacological content remains intact.
"""
import sqlite3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DB=ROOT/"src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite"
FAMILIES={"Ataxxa":{"36217","36218","36219","36220"},
          "Biocan R":{"18679"},"Duphalyte":{"20218"},
          "Fertigest":{"39496"},"Ganapar":{"35323"}}
EXPECTED_COUNTS={"Ataxxa":4,"Biocan R":5,"Duphalyte":8,"Fertigest":5,"Ganapar":4}
DUPHALYTE_IDS={690,691,692,693,1939,1940,1941,1942}
PDF="data/ChPL/RPL/RPL_20218_Duphalyte_RPL_ChPL.pdf"

def main():
    with (ROOT/PDF).open("rb") as f:
        assert f.read(5)==b"%PDF-","Official RPL PDF absent or corrupt"
    with sqlite3.connect(DB) as conn:
        conn.execute("PRAGMA foreign_keys=ON")
        assert conn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not conn.execute("PRAGMA foreign_key_check").fetchall()
        allids=set()
        for name,ids in FAMILIES.items():
            rows=conn.execute("SELECT id FROM variants WHERE rpl_id IN ("+",".join("?" for _ in ids)+")",tuple(sorted(ids))).fetchall()
            assert len(rows)==EXPECTED_COUNTS[name],(name,len(rows))
            allids.update(x[0] for x in rows)
        assert len(allids)==sum(EXPECTED_COUNTS.values())
        rows=conn.execute("SELECT id,rpl_id,pdf_path FROM variants WHERE rpl_id='20218' ORDER BY id").fetchall()
        assert {r[0] for r in rows}==DUPHALYTE_IDS,(rows,DUPHALYTE_IDS)
        assert all(r[1]=="20218" and r[2] in (None,PDF) for r in rows),rows
        original_variants=conn.execute("SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,dose,edible_tissues,milk,restrictions,source,verified_at,card_text,scope,withdrawal_short FROM variants ORDER BY id").fetchall()
        original_counts={t:conn.execute("SELECT COUNT(*) FROM "+t).fetchone()[0] for t in ("products","variants","documents","price_withdrawal_entries")}
        original_other=conn.execute("SELECT id,pdf_path FROM variants WHERE rpl_id IS NOT '20218' ORDER BY id").fetchall()
        conn.execute("BEGIN IMMEDIATE")
        conn.execute("INSERT OR IGNORE INTO documents(path) VALUES(?)",(PDF,))
        changed=0
        for vid,rpl,current in rows:
            if current is None:
                cur=conn.execute("UPDATE variants SET pdf_path=? WHERE id=? AND rpl_id=? AND pdf_path IS NULL",(PDF,vid,rpl))
                assert cur.rowcount==1,vid
                changed+=1
        assert conn.execute("SELECT COUNT(*) FROM variants WHERE rpl_id='20218' AND pdf_path=?",(PDF,)).fetchone()[0]==8
        assert conn.execute("SELECT id,symbol,name,rpl_id,upd_id,species,indication,route,dose,edible_tissues,milk,restrictions,source,verified_at,card_text,scope,withdrawal_short FROM variants ORDER BY id").fetchall()==original_variants
        assert conn.execute("SELECT id,pdf_path FROM variants WHERE rpl_id IS NOT '20218' ORDER BY id").fetchall()==original_other
        for name in ("products","variants","price_withdrawal_entries"):
            assert conn.execute("SELECT COUNT(*) FROM "+name).fetchone()[0]==original_counts[name],name
        assert conn.execute("PRAGMA integrity_check").fetchone()[0]=="ok"
        assert not conn.execute("PRAGMA foreign_key_check").fetchall()
        conn.commit()
        print("SUCCESS: five-medicine batch02 checked; Duphalyte PDF links added:",changed,"/8; integrity_check=ok; foreign_key_check=ok")
if __name__=="__main__":
    main()
