import {useSyncExternalStore} from "react";

export type PageMode = "pages" | "scroll";
export type ViewerMode = "react" | "native";
export type GridMode = "antd" | "ag";
export type NavigationMode = "top" | "side";
export type ShortcutAction = "back" | "next" | "summary" | "save";
export type ComponentPreferences = {
  pdf: {mode: PageMode; viewer: ViewerMode; zoom: number; rememberZoom: boolean};
  table: {mode: GridMode; rememberColumns: boolean};
  agGrid: {
    rowHeight: number;
    headerHeight: number;
    sortable: boolean;
    filters: boolean;
    animateRows: boolean;
  };
  cache: {staleSeconds: number; retry: boolean};
  shortcuts: {enabled: boolean; back: string; next: string; summary: string; save: string};
  navigation: NavigationMode;
};

export const defaultComponents: ComponentPreferences = {
  pdf: {mode: "scroll", viewer: "react", zoom: 100, rememberZoom: true},
  table: {mode: "antd", rememberColumns: true},
  agGrid: {rowHeight: 36, headerHeight: 40, sortable: true, filters: false, animateRows: false},
  cache: {staleSeconds: 15, retry: false},
  shortcuts: {enabled: true, back: "alt+left", next: "alt+right",
    summary: "alt+enter", save: "mod+s"},
  navigation: "side",
};

const key = "ReactKL.componentPreferences.v1";
const validShortcut = (value: unknown): value is string =>
  typeof value === "string" && value.length >= 3 && value.length <= 40 &&
  /^(?:(?:alt|ctrl|shift|meta|mod)\+)+(?:left|right|enter|s|d|p|f[1-9]|[a-z])$/i.test(value);
const enumValue = <T extends string>(candidate: unknown, choices: readonly T[], fallback: T): T =>
  choices.includes(candidate as T) ? candidate as T : fallback;
export function normalizeComponents(raw: unknown): ComponentPreferences {
  const data = raw && typeof raw === "object" && !Array.isArray(raw)
    ? raw as Record<string, unknown> : {};
  const obj = (value: unknown): Record<string, unknown> =>
    value && typeof value === "object" && !Array.isArray(value)
      ? value as Record<string, unknown> : {};
  const pdf = obj(data.pdf), table = obj(data.table), cache = obj(data.cache);
  const agGrid = obj(data.agGrid);
  const shortcuts = obj(data.shortcuts);
  const zoom = Number(pdf.zoom);
  const staleSeconds = Number(cache.staleSeconds);
  const defaults = defaultComponents;
  return {
    pdf: {
      mode: enumValue(pdf.mode, ["pages","scroll"], defaults.pdf.mode),
      viewer: enumValue(pdf.viewer, ["react","native"], defaults.pdf.viewer),
      zoom: [50,75,100,125,150,175,200].includes(zoom)?zoom:defaults.pdf.zoom,
      rememberZoom: typeof pdf.rememberZoom === "boolean" ? pdf.rememberZoom : defaults.pdf.rememberZoom,
    },
    table: {
      mode: enumValue(table.mode, ["antd","ag"], defaults.table.mode),
      rememberColumns: typeof table.rememberColumns === "boolean"
        ? table.rememberColumns : defaults.table.rememberColumns,
    },
    agGrid: {
      rowHeight: [28,32,36,40,44,48,52].includes(Number(agGrid.rowHeight))
        ? Number(agGrid.rowHeight) : defaults.agGrid.rowHeight,
      headerHeight: [32,36,40,44,48,52].includes(Number(agGrid.headerHeight))
        ? Number(agGrid.headerHeight) : defaults.agGrid.headerHeight,
      sortable: typeof agGrid.sortable === "boolean" ? agGrid.sortable : defaults.agGrid.sortable,
      filters: typeof agGrid.filters === "boolean" ? agGrid.filters : defaults.agGrid.filters,
      animateRows: typeof agGrid.animateRows === "boolean" ? agGrid.animateRows : defaults.agGrid.animateRows,
    },
    cache: {
      staleSeconds: [5,15,30,60].includes(staleSeconds)
        ? staleSeconds : defaults.cache.staleSeconds,
      retry: typeof cache.retry === "boolean" ? cache.retry : defaults.cache.retry,
    },
    shortcuts: {
      enabled: typeof shortcuts.enabled === "boolean" ? shortcuts.enabled : defaults.shortcuts.enabled,
      back: validShortcut(shortcuts.back)?shortcuts.back:defaults.shortcuts.back,
      next: validShortcut(shortcuts.next)?shortcuts.next:defaults.shortcuts.next,
      summary: validShortcut(shortcuts.summary)?shortcuts.summary:defaults.shortcuts.summary,
      save: validShortcut(shortcuts.save)?shortcuts.save:defaults.shortcuts.save,
    },
    navigation: enumValue(data.navigation, ["top","side"], defaults.navigation),
  };
}
function read(): ComponentPreferences {
  try { return normalizeComponents(JSON.parse(localStorage.getItem(key)??"null")); }
  catch { return normalizeComponents(null); }
}
let current = read();
const listeners = new Set<() => void>();
function notify() { for (const listener of listeners) listener(); }
export function useComponents(): ComponentPreferences {
  return useSyncExternalStore(
    (listener) => {listeners.add(listener);return () => {listeners.delete(listener);};},
    () => current, () => defaultComponents);
}
export function setComponents(next: ComponentPreferences) {
  current = normalizeComponents(next);
  try { localStorage.setItem(key, JSON.stringify(current)); } catch { /* session fallback */ }
  notify();
}
export function updateComponents<K extends "pdf" | "table" | "agGrid" | "cache" | "shortcuts">(
  section: K, patch: Partial<ComponentPreferences[K]>) {
  setComponents({...current, [section]: {...current[section], ...patch}});
}
export function currentComponents() { return current; }
export function resetComponents() { setComponents(defaultComponents); }
export function initializeNavigation(legacy: string | null) {
  try {
    if (localStorage.getItem(key)) return;
  } catch { return; }
  if (legacy === "classic" || legacy === "compact-tabs" ||
      legacy === "windows" || legacy === "material" || legacy === "aero") {
    setComponents({...current, navigation:"top"});
  }
}
if (typeof window !== "undefined") {
  window.addEventListener("storage", event => {
    if (event.key === key) {current = read();notify();}
  });
}
