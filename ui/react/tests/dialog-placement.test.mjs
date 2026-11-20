import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = readFileSync(new URL("../src/DialogPlacement.tsx", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: {module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022}
}).outputText.replace('import { useEffect } from "react";', "");
const {attachDialog} = await import("data:text/javascript;base64," +
  Buffer.from(compiled).toString("base64"));

class ElementFixture extends EventTarget {
  classList = new class extends Set { remove(...items) { items.forEach(item => this.delete(item)); } };
  dataset = {};
  children = [];
  style = {removeProperty: key => delete this.style[key]};
  offsetLeft = 100;
  offsetTop = 80;
  offsetWidth = 900;
  offsetHeight = 600;
  closest() { return null; }
  setAttribute() {}
  setPointerCapture() {}
  append(child) { this.children.push(child); child.parent = this; }
  remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); }
}

function pointer(element, type, x, y) {
  const event = Object.assign(new Event(type, {cancelable: true}),
    {pointerId: 1, button: 0, clientX: x, clientY: y});
  element.dispatchEvent(event);
}

test("dialog and PDF handles resize at zoom, persist per dialog, and clean up on cancel", () => {
  globalThis.HTMLElement = globalThis.Element = ElementFixture;
  globalThis.document = {createElement: () => new ElementFixture()};
  globalThis.ResizeObserver = class {observe() {} disconnect() {}};
  const saved = new Map();
  globalThis.localStorage = {getItem: key => saved.get(key), setItem: (key, value) => saved.set(key, value)};

  const wrapper = {clientWidth: 1024, clientHeight: 768, getBoundingClientRect: () => ({width: 819.2})};
  const modal = new ElementFixture(), content = new ElementFixture(), header = new ElementFixture();
  modal.classList.add("kl-dialog-treatment-pdf");
  modal.querySelector = selector => selector === ".ant-modal-content" ? content : header;
  const cleanup = attachDialog(modal, wrapper);
  assert.equal(content.children.length, 8);
  assert.equal(modal.children.length, 0);
  const corner = content.children.find(handle => handle.dataset.edge === "se");
  pointer(corner, "pointerdown", 600, 500);
  assert.ok(modal.classList.has("kl-modal-adjusting"));
  pointer(corner, "pointermove", 440, 380);
  pointer(corner, "pointerup", 440, 380);
  assert.equal(modal.style.width, "700px");
  assert.equal(modal.style.height, "450px");
  assert.ok(!modal.classList.has("kl-modal-adjusting"));
  pointer(header, "pointerdown", 200, 120);
  pointer(header, "pointermove", 240, 160);
  pointer(header, "pointerup", 240, 160);
  const key = "kl.dialogPlacement.v1.kl-dialog-treatment-pdf";
  assert.deepEqual(JSON.parse(saved.get(key)), {left: 150, top: 130, width: 700, height: 450});
  pointer(corner, "pointerdown", 600, 500);
  pointer(corner, "pointermove", 580, 480);
  pointer(corner, "pointercancel", 580, 480);
  assert.equal(modal.style.width, "700px");
  cleanup();
  assert.equal(content.children.length, 0);
  const closeAgain = attachDialog(modal, wrapper);
  assert.equal(modal.style.width, "700px");
  assert.equal(modal.style.left, "150px");
  closeAgain();
});
