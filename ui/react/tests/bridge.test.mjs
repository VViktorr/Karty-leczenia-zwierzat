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
  assert.ok(/^\\d+$/.test(sent._bridgeRequestId));
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
