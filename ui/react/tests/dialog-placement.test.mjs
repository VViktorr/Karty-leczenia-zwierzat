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
  classList = new class extends Set { contains(item) { return this.has(item); } remove(...items) { items.forEach(item => this.delete(item)); } };
  dataset = {};
  children = [];
  style = {removeProperty: key => delete this.style[key]};
  offsetLeft = 100;
  offsetTop = 80;
  offsetWidth = 900;
  offsetHeight = 600;
  closest() { return null; }
  setAttribute() {}
  getAttribute() { return null; }
  removeAttribute() {}
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
  let repair;
  globalThis.MutationObserver = class {
    constructor(callback) { repair = callback; }
    observe() {}
    disconnect() {}
  };
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
  modal.classList.remove("kl-movable-modal");
  modal.style.width = "520px";
  repair();
  assert.ok(modal.classList.has("kl-movable-modal"));
  assert.equal(modal.style.width, "700px");
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
  const keyboardCorner = content.children.find(handle => handle.dataset.edge === "se");
  keyboardCorner.dispatchEvent(Object.assign(new Event("keydown", {cancelable: true}),
    {key: "ArrowLeft", shiftKey: false}));
  assert.equal(modal.style.width, "688px");
  assert.equal(JSON.parse(saved.get(key)).width, 688);
  closeAgain();
});


test("a dialog without a custom class can move, resize and restore its position", () => {
  const wrapper = {clientWidth: 1024, clientHeight: 768, getBoundingClientRect: () => ({width: 1024})};
  const modal = new ElementFixture(), content = new ElementFixture(), header = new ElementFixture();
  const title = {textContent: "Numery i liczba leczonych zwierząt"};
  modal.querySelector = selector => selector === ".ant-modal-content" ? content :
    selector.includes(".ant-modal-title") ? title : header;
  const cleanup = attachDialog(modal, wrapper);
  const corner = content.children.find(handle => handle.dataset.edge === "se");
  pointer(corner, "pointerdown", 500, 400);
  pointer(corner, "pointermove", 450, 380);
  pointer(corner, "pointerup", 450, 380);
  pointer(header, "pointerdown", 150, 120);
  pointer(header, "pointermove", 170, 150);
  pointer(header, "pointerup", 170, 150);
  cleanup();
  const closeAgain = attachDialog(modal, wrapper);
  assert.equal(modal.style.width, "850px");
  assert.equal(modal.style.left, "120px");
  assert.equal(modal.style.top, "110px");
  closeAgain();
});
