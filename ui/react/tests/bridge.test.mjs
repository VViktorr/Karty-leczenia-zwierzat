import test from "node:test";
import assert from "node:assert/strict";

const posted=[];
let deliver;
globalThis.window={chrome:{webview:{
  addEventListener(_type,listener){deliver=listener;},
  postMessage(message){posted.push(message);}
}}};
const {invoke}=await import("../src/bridge.ts");

test("record IDs cannot overwrite the RPC correlation ID",async()=>{
  const savedId="0123456789abcdef0123456789abcdef";
  const pending=invoke("savedCard",{id:savedId,trash:false});
  const sent=posted.at(-1);
  assert.equal(sent.id,savedId);
  assert.ok(/^[0-9]+$/.test(sent._bridgeRequestId));
  assert.notEqual(sent._bridgeRequestId,savedId);
  deliver({data:{id:sent._bridgeRequestId,ok:true,data:{savedId}}});
  assert.deepEqual(await pending,{savedId});
});

test("simultaneous saved-card requests resolve by correlation, not by card GUID",async()=>{
  const first=invoke("savedCard",{id:"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"});
  const a=posted.at(-1);
  const second=invoke("savedCard",{id:"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"});
  const b=posted.at(-1);
  assert.notEqual(a._bridgeRequestId,b._bridgeRequestId);
  deliver({data:{id:b._bridgeRequestId,ok:true,data:"second"}});
  deliver({data:{id:a._bridgeRequestId,ok:true,data:"first"}});
  assert.equal(await first,"first");
  assert.equal(await second,"second");
});

test("Subiekt catalogue search and selection send distinct read-only requests", async () => {
  const search = invoke("searchSubiektCatalogue", {query: "symbol"});
  const searchRequest = posted.at(-1);
  assert.equal(searchRequest.action, "searchSubiektCatalogue");
  assert.equal(searchRequest.query, "symbol");
  deliver({data: {id: searchRequest._bridgeRequestId, ok: true,
    data: [{symbol: "symbol", name: "Fixture"}]}});
  const [row] = await search;

  const open = invoke("openSubiektCatalogueProduct", {symbol: row.symbol});
  const openRequest = posted.at(-1);
  assert.equal(openRequest.action, "openSubiektCatalogueProduct");
  assert.equal(openRequest.symbol, row.symbol);
  assert.equal(Object.hasOwn(openRequest, "name"), false);
  const draft = {...row, rplId: "", description: "", revision: "", variants: []};
  deliver({data: {id: openRequest._bridgeRequestId, ok: true, data: draft}});
  assert.deepEqual(await open, draft);
});
