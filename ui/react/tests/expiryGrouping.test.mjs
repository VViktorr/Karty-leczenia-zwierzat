import test from "node:test";
import assert from "node:assert/strict";
import {groupExpiryLots} from "../src/expiryGrouping.ts";

const lot=(movementId,overrides={})=>({
  name:"Lek testowy",productId:18,movementId,
  warehouseId:1,warehouseName:"Magazyn 1",
  batch:"SER-1",expires:"2027-02-20T00:00:00",
  received:"2026-11-17T00:00:00",
  remaining:2,deliveriesConfirmed:true,...overrides
});

test("same confirmed product, warehouse, batch and expiry combine, with expandable originals",()=>{
  const rows=groupExpiryLots([
    lot(11,{remaining:2.5}),
    lot(12,{remaining:1.25,received:"2026-11-18T00:00:00"}),
    lot(13,{expires:"2027-03-01T00:00:00",remaining:3})
  ],true);
  assert.equal(rows.length,2);
  assert.equal(rows[0].deliveryCount,2);
  assert.equal(rows[0].children.length,2);
  assert.equal(rows[0].remaining,3.75);
  assert.equal(rows[0].received,null);
  assert.equal(rows[0].multipleReceivedDates,true);
  assert.equal(rows[1].remaining,3);
  assert.equal(rows[1].children,undefined);
});

test("different warehouse, product or expiry never combine",()=>{
  const rows=groupExpiryLots([
    lot(11),lot(12,{warehouseId:2,warehouseName:"Magazyn 2"}),
    lot(13,{productId:29}),lot(14,{expires:"2027-04-01T00:00:00"})
  ],true);
  assert.equal(rows.length,4);
  assert.ok(rows.every(row=>!row.children));
});

test("unconfirmed and absent lot metadata never fabricate aggregate stock",()=>{
  const rows=groupExpiryLots([
    lot(11,{deliveriesConfirmed:false}),lot(12,{deliveriesConfirmed:false}),
    lot(13,{batch:""}),lot(14,{batch:""}),
    lot(15,{expires:null}),lot(16,{expires:null}),
    lot(0),lot(0),lot(21,{remaining:0}),lot(22,{remaining:-2})
  ],true);
  assert.equal(rows.length,10);
  assert.ok(rows.every(row=>!row.children));
});

test("same movement ID never counted twice within an aggregate",()=>{
  const rows=groupExpiryLots([lot(11,{remaining:3}),lot(11,{remaining:3}),lot(12,{remaining:2})],true);
  assert.equal(rows.length,1);
  assert.equal(rows[0].children.length,2);
  assert.equal(rows[0].remaining,5);
});

test("disabled grouping preserves source quantities, ordering and every row",()=>{
  const source=[lot(11,{remaining:3}),lot(12,{remaining:5})];
  const rows=groupExpiryLots(source,false);
  assert.equal(rows.length,2);
  assert.deepEqual(rows.map(row=>row.remaining),[3,5]);
  assert.ok(rows.every(row=>!row.children));
});

test("same received date is preserved and independent groups get unique IDs",()=>{
  const rows=groupExpiryLots([
    lot(11),lot(12),lot(13,{batch:"SER-2"}),lot(14,{batch:"SER-2"})
  ],true);
  assert.equal(rows.length,2);
  assert.equal(rows[0].received,"2026-11-17T00:00:00");
  assert.equal(rows[0].multipleReceivedDates,false);
  assert.notEqual(rows[0].key,rows[1].key);
});
