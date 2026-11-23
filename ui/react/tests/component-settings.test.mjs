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

test("AG Grid preferences validate limits and survive reload",()=>{
  const chosen=normalizeComponents({
    table:{mode:"ag",rememberColumns:true},
    agGrid:{rowHeight:44,headerHeight:48,sortable:false,filters:true,animateRows:true},
  });
  assert.equal(chosen.table.mode,"ag");
  assert.deepEqual(chosen.agGrid,{rowHeight:44,headerHeight:48,sortable:false,filters:true,animateRows:true,resizableColumns:true,rowBuffer:10});
  const invalid=normalizeComponents({agGrid:{
    rowHeight:500,headerHeight:1,sortable:"false",filters:123,animateRows:null,
    resizableColumns:"yes",rowBuffer:1000,
  }});
  assert.deepEqual(invalid.agGrid,defaultComponents.agGrid);
});

test("PDF virtualisation and AG Grid performance preferences are validated",()=>{
  const chosen=normalizeComponents({
    pdf:{textLayer:false,annotations:false,preloadPages:3},
    agGrid:{resizableColumns:false,rowBuffer:5},
  });
  assert.equal(chosen.pdf.textLayer,false);
  assert.equal(chosen.pdf.annotations,false);
  assert.equal(chosen.pdf.preloadPages,3);
  assert.equal(chosen.agGrid.resizableColumns,false);
  assert.equal(chosen.agGrid.rowBuffer,5);
});
