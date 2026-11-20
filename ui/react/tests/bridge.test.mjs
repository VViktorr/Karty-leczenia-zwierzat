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

test("cancelled document read cannot consume the response for the next view", async () => {
  const controller = new AbortController();
  const read = invoke("readDocument", {documentId: 123}, undefined, controller.signal);
  const original = posted.at(-1);
  controller.abort();
  await assert.rejects(read, /Anulowano/);
  assert.equal(posted.at(-1).action, "cancelRequest");
  assert.equal(posted.at(-1).requestId, original._bridgeRequestId);

  const next = invoke("searchDocuments", {kind: "invoices"});
  const request = posted.at(-1);
  deliver({data:{id:original._bridgeRequestId,ok:false,error:"SQL timeout"}});
  const list = {items: []};
  deliver({data:{id:request._bridgeRequestId,ok:true,data:list}});
  assert.deepEqual(await next, list);
});

test("SQL update confirmation carries the release shown to the operator", async () => {
  const releaseId = "d7908b62-1012-4db5-9f1d-139cb5461791";
  const check = invoke("checkUpdate");
  const checkRequest = posted.at(-1);
  deliver({data: {id: checkRequest._bridgeRequestId, ok: true,
    data: {release: {releaseId, version: "3.1.0", newer: true}, currentVersion: "3.0.5"}}});
  const {release} = await check;
  assert.equal(posted.at(-1).action, "checkUpdate");

  const install = invoke("installUpdate", {releaseId: release.releaseId});
  const installRequest = posted.at(-1);
  assert.equal(installRequest.action, "installUpdate");
  assert.equal(installRequest.releaseId, releaseId);
  deliver({data: {id: installRequest._bridgeRequestId, ok: true, data: {started: true}}});
  assert.deepEqual(await install, {started: true});
});

test("RPL PDF name uses the entry ID and excludes the date and later identifiers", async () => {
  const {rplEntryFromPdfName} = await import("../src/cataloguePdfMetadata.ts");
  assert.equal(rplEntryFromPdfName("Ulotka-46833-2025-10-29-6195_W-2025-11-18.pdf"), "46833");
  assert.equal(rplEntryFromPdfName("charakterystyka-46833-2025-10-29.pdf"), "46833");
  assert.equal(rplEntryFromPdfName("Skan-2025-10-29.pdf"), undefined);
});

test("compact withdrawal retains both conditions and leaves missing values empty", async () => {
  const {compactWithdrawal} = await import("../src/cataloguePdfMetadata.ts");
  assert.equal(compactWithdrawal("tekst tkanek", "● warunek A\n● warunek B"),
    "tk. tekst tkanek · ml. warunek A; warunek B");
  assert.equal(compactWithdrawal("", ""), "");
});
