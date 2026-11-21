import assert from "node:assert/strict";
import test from "node:test";
import {restoreGeometry} from "../src/dialogGeometry.ts";

test("dialog starts at its default position without valid stored geometry", () => {
  const defaults = {x:0, y:0, width:"100%", height:"auto"};
  assert.deepEqual(restoreGeometry(null, 1000, 800), defaults);
  assert.deepEqual(restoreGeometry("{invalid", 1000, 800), defaults);
});

test("dialog restores saved geometry and fits smaller viewports", () => {
  const geometry = {x:300, y:250, width:850, height:610};
  const stored = JSON.stringify(geometry);
  assert.deepEqual(restoreGeometry(stored, 1200, 900), geometry);
  assert.deepEqual(restoreGeometry(stored, 540, 420), {x:0, y:0, width:516, height:296});
});
