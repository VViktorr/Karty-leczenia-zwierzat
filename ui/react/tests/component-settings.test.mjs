import {test} from "node:test";
import {strict as assert} from "node:assert";
import {defaultComponents,normalizeComponents} from "../src/componentSettings.ts";

test("invalid stored component preferences fall back to safe defaults",()=>{
  const result=normalizeComponents({
    pdf:{mode:"something",viewer:"external",zoom:999,rememberZoom:"true"},
    table:{mode:"foo",rememberColumns:0},
    cache:{staleSeconds:9999,retry:"yes"},
    shortcuts:{back:"javascript:evil()",next:"x".repeat(300),summary:"alt+enter",save:"mod+s"},
    navigation:"unknown"
  });
  assert.deepEqual(result,defaultComponents);
});

test("all supported PDF and keyboard preferences survive normalisation",()=>{
  const result=normalizeComponents({
    pdf:{mode:"pages",viewer:"native",zoom:125,rememberZoom:false},
    table:{mode:"ag",rememberColumns:false},
    cache:{staleSeconds:30,retry:true},
    shortcuts:{enabled:false,back:"alt+shift+left",next:"alt+shift+right",
      summary:"mod+enter",save:"mod+shift+s"},
    navigation:"top"
  });
  assert.equal(result.pdf.mode,"pages");
  assert.equal(result.pdf.viewer,"native");
  assert.equal(result.pdf.zoom,125);
  assert.equal(result.table.mode,"ag");
  assert.equal(result.cache.staleSeconds,30);
  assert.equal(result.shortcuts.enabled,false);
  assert.equal(result.shortcuts.next,"alt+shift+right");
  assert.equal(result.navigation,"top");
});
