import { useEffect } from "react";

type Bounds = {left:number;top:number;width:number;height:number};
const storagePrefix="kl.dialogPlacement.v1.";

function dialogKey(modal:HTMLElement):string {
  // One set of window preferences for each dialog type, not each patient/product.
  // Stable IDs keep positions separate even when a patient, medicine or PDF
  // filename changes in the dialog title between openings.
  const explicit=Array.from(modal.classList).find(name=>name.startsWith("kl-dialog-"));
  if(explicit)return storagePrefix+explicit;
  const title=modal.querySelector<HTMLElement>(
    ".ant-modal-title, .ant-modal-confirm-title")?.textContent?.trim()??"Okno";
  const kind=title.split(/\s+[—–]\s+/)[0].replace(/\s+/g," ").slice(0,100);
  let hash=2166136261;
  for(const character of kind){
    hash^=character.charCodeAt(0);
    hash=Math.imul(hash,16777619);
  }
  return storagePrefix+(hash>>>0).toString(16);
}
function read(key:string):Bounds|null {
  try {
    const parsed:unknown=JSON.parse(localStorage.getItem(key)??"null");
    if(!parsed||typeof parsed!=="object")return null;
    const obj=parsed as Partial<Bounds>;
    if(![obj.left,obj.top,obj.width,obj.height].every(x=>typeof x==="number"&&Number.isFinite(x)))return null;
    if((obj.width??0)<320||(obj.height??0)<190)return null;
    return obj as Bounds;
  }catch{return null;}
}
const edges = ["n", "ne", "e", "se", "s", "sw", "w", "nw"] as const;
type Edge = typeof edges[number];
const minimum = {width: 320, height: 190};
const edgeMargin = 6;

function fit(bounds: Bounds, wrapper: HTMLElement): Bounds {
  const width = Math.min(Math.max(minimum.width, bounds.width), Math.max(1, wrapper.clientWidth - 2 * edgeMargin));
  const height = Math.min(Math.max(minimum.height, bounds.height), Math.max(1, wrapper.clientHeight - 2 * edgeMargin));
  return {width, height,
    left: Math.max(edgeMargin, Math.min(bounds.left, wrapper.clientWidth - width - edgeMargin)),
    top: Math.max(edgeMargin, Math.min(bounds.top, wrapper.clientHeight - height - edgeMargin))};
}

export function attachDialog(modal: HTMLElement, wrapper: HTMLElement) {
  const header = modal.querySelector<HTMLElement>(
    ".ant-modal-header, .ant-modal-confirm-title");
  const content = modal.querySelector<HTMLElement>(".ant-modal-content");
  if (!content) return;
  const key = dialogKey(modal);
  const originalWidth = modal.style.width;
  const scale = () => wrapper.getBoundingClientRect().width / wrapper.clientWidth || 1;
  let preferred = read(key) ?? {
    left: modal.offsetLeft, top: modal.offsetTop,
    width: modal.offsetWidth, height: content.offsetHeight
  };
  let bounds = fit(preferred, wrapper);

  function place(value: Bounds) {
    bounds = value;
    Object.assign(modal.style, {
      left: value.left + "px", top: value.top + "px",
      width: value.width + "px", height: value.height + "px"
    });
  }

  const view = modal.ownerDocument?.defaultView;
  let gesture: {target: HTMLElement; pointer: number; edge?: Edge;
    x: number; y: number; origin: Bounds; zoom: number} | undefined;
  const start = (event: PointerEvent) => {
    if (gesture || event.button !== 0 || !(event.currentTarget instanceof HTMLElement) ||
      (event.target !== event.currentTarget && event.target instanceof Element &&
        event.target.closest("button,input,select,textarea,a,[role=button]"))) return;
    const target = event.currentTarget;
    gesture = {target, pointer: event.pointerId, edge: target.dataset.edge as Edge | undefined,
      x: event.clientX, y: event.clientY, origin: bounds, zoom: scale()};
    try { target.setPointerCapture(event.pointerId); }
    catch { /* Continue through the top-level pointer listeners. */ }
    view?.addEventListener("pointermove", move, true);
    view?.addEventListener("pointerup", finish, true);
    view?.addEventListener("pointercancel", finish, true);
    modal.classList.add("kl-modal-adjusting");
    event.preventDefault();
    event.stopPropagation();
  };

  const move = (event: PointerEvent) => {
    if (!gesture || event.pointerId !== gesture.pointer) return;
    const {edge, origin, zoom} = gesture;
    const dx = (event.clientX - gesture.x) / zoom;
    const dy = (event.clientY - gesture.y) / zoom;
    if (!edge) {
      place(fit({...origin, left: origin.left + dx, top: origin.top + dy}, wrapper));
      return;
    }
    let {left, top, width, height} = origin;
    const minWidth = Math.min(minimum.width, Math.max(1, wrapper.clientWidth - 2 * edgeMargin));
    const minHeight = Math.min(minimum.height, Math.max(1, wrapper.clientHeight - 2 * edgeMargin));
    if (edge.includes("w")) {
      left = Math.max(edgeMargin, Math.min(left + dx, origin.left + width - minWidth));
      width += origin.left - left;
    }
    if (edge.includes("e"))
      width = Math.max(minWidth, Math.min(width + dx, wrapper.clientWidth - left - edgeMargin));
    if (edge.includes("n")) {
      top = Math.max(edgeMargin, Math.min(top + dy, origin.top + height - minHeight));
      height += origin.top - top;
    }
    if (edge.includes("s"))
      height = Math.max(minHeight, Math.min(height + dy, wrapper.clientHeight - top - edgeMargin));
    place({left, top, width, height});
  };

  const finish = (event: PointerEvent) => {
    if (!gesture || event.pointerId !== gesture.pointer) return;
    const {origin} = gesture;
    gesture = undefined;
    removeWindowListeners();
    modal.classList.remove("kl-modal-adjusting");
    if (event.type !== "pointerup") {
      place(fit(preferred, wrapper));
      return;
    }
    if (origin.left === bounds.left && origin.top === bounds.top &&
      origin.width === bounds.width && origin.height === bounds.height) return;
    preferred = bounds;
    try { localStorage.setItem(key, JSON.stringify(preferred)); }
    catch { /* Keep the active geometry. */ }
  };

  function removeWindowListeners() {
    view?.removeEventListener("pointermove", move, true);
    view?.removeEventListener("pointerup", finish, true);
    view?.removeEventListener("pointercancel", finish, true);
  }

  const handles = edges.map(edge => {
    const handle = document.createElement("span");
    handle.className = "kl-modal-resize";
    handle.dataset.edge = edge;
    handle.setAttribute("aria-hidden", "true");
    content.append(handle);
    return handle;
  });
  const corner = handles[3];
  const keyboardTargets = header ? [header, corner] : [corner];
  const originalHeaderTabIndex = header?.getAttribute("tabindex");
  const originalHeaderTitle = header?.getAttribute("title");
  if (header) {
    header.tabIndex = 0;
    header.title = "Przeciągnij lub użyj strzałek, aby przesunąć okno";
  }
  corner.tabIndex = 0;
  corner.removeAttribute("aria-hidden");
  corner.setAttribute("role", "button");
  corner.setAttribute("aria-label", "Zmień rozmiar okna strzałkami");
  corner.title = "Przeciągnij lub użyj strzałek, aby zmienić rozmiar";
  const adjustByKey = (event: KeyboardEvent) => {
    if (event.target !== event.currentTarget || gesture) return;
    const step = event.shiftKey ? 40 : 12;
    const dx = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
    const dy = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
    if (!dx && !dy) return;
    event.preventDefault();
    const next = event.currentTarget === corner
      ? {...bounds, width: Math.min(bounds.width + dx, wrapper.clientWidth - bounds.left - edgeMargin),
          height: Math.min(bounds.height + dy, wrapper.clientHeight - bounds.top - edgeMargin)}
      : {...bounds, left: bounds.left + dx, top: bounds.top + dy};
    preferred = fit(next, wrapper);
    place(preferred);
    try { localStorage.setItem(key, JSON.stringify(preferred)); }
    catch { /* Keep the active geometry. */ }
  };
  keyboardTargets.forEach(target => target.addEventListener("keydown", adjustByKey));
  const targets = header ? [header, ...handles] : handles;
  const events = [
    ["pointerdown", start], ["pointermove", move], ["pointerup", finish],
    ["pointercancel", finish], ["lostpointercapture", finish]
  ] as const;
  targets.forEach(target => events.forEach(([name, listener]) =>
    target.addEventListener(name, listener)));
  modal.classList.add("kl-movable-modal");
  place(bounds);

  const refit = new ResizeObserver(() => {
    if (!gesture && wrapper.clientWidth && wrapper.clientHeight) place(fit(preferred, wrapper));
  });
  refit.observe(wrapper);
  return () => {
    refit.disconnect();
    removeWindowListeners();
    targets.forEach(target => events.forEach(([name, listener]) =>
      target.removeEventListener(name, listener)));
    keyboardTargets.forEach(target => target.removeEventListener("keydown", adjustByKey));
    if (header) {
      if (originalHeaderTabIndex == null) header.removeAttribute("tabindex");
      else header.setAttribute("tabindex", originalHeaderTabIndex);
      if (originalHeaderTitle == null) header.removeAttribute("title");
      else header.setAttribute("title", originalHeaderTitle);
    }
    handles.forEach(handle => handle.remove());
    modal.classList.remove("kl-movable-modal", "kl-modal-adjusting");
    for (const property of ["left", "top", "height"]) modal.style.removeProperty(property);
    if (originalWidth) modal.style.width = originalWidth;
    else modal.style.removeProperty("width");
  };

}

export default function DialogPlacement() {
  useEffect(() => {
    const live = new Map<HTMLElement, () => void>();
    const scan = () => {
      for (const [modal, cleanup] of live) {
        if (!modal.isConnected) { cleanup(); live.delete(modal); }
      }
      document.querySelectorAll<HTMLElement>(".ant-modal-wrap .ant-modal").forEach(modal => {
        if (live.has(modal)) return;
        const wrapper = modal.closest<HTMLElement>(".ant-modal-wrap");
        if (!wrapper?.clientWidth || !wrapper.clientHeight || !modal.offsetWidth) return;
        const cleanup = attachDialog(modal, wrapper);
        if (cleanup) live.set(modal, cleanup);
      });
    };
    const observer = new MutationObserver(changes => {
      if (changes.some(change => change.type === "childList" ||
        (change.target instanceof HTMLElement &&
          (change.target.matches(".ant-modal-wrap") ||
            (change.target.matches(".ant-modal") && !live.has(change.target))))))
        scan();
    });
    observer.observe(document.body, {childList: true, subtree: true,
      attributes: true, attributeFilter: ["class", "style"]});
    scan();
    return () => {
      observer.disconnect();
      live.forEach(cleanup => cleanup());
    };
  }, []);
  return null;
}
