export type DocumentKind = "invoices" | "receipts" | "warehouse" | "all";
export type DatePeriod = "all" | "today" | "week" | "month";
export interface DocumentRow {
  id: number; number: string; date: string | null;
  shortPayer: string; payer: string; gross: number | null;
  net: number | null; currency: string; relatedText: string;
}
export interface DocumentsResult { items: DocumentRow[]; truncated: boolean; }
export interface DocumentDetail {
  number: string; payer: string; recipient: string;
  rows: Array<{product: string; quantity: string; batch: string; species: string; route: string;}>;
}
interface RequestMessage { id: string; action: string; [key: string]: unknown; }
interface ResponseMessage { id: string; ok: boolean; data?: unknown; error?: string; }
interface WebView {
  postMessage(message: RequestMessage): void;
  addEventListener(type: "message", listener: (event: MessageEvent<ResponseMessage>) => void): void;
}
declare global { interface Window { chrome?: { webview?: WebView } } }
const pending = new Map<string, {
  resolve: (value: unknown) => void; reject: (reason: Error) => void;
  timeout: ReturnType<typeof setTimeout>;
}>();
let sequence = 0;
if (window.chrome?.webview) {
  window.chrome.webview.addEventListener("message", event => {
    const response = event.data;
    if (!response || typeof response.id !== "string") return;
    const request = pending.get(response.id);
    if (!request) return;
    pending.delete(response.id);
    clearTimeout(request.timeout);
    if (response.ok) request.resolve(response.data);
    else request.reject(new Error(response.error || "Nie udało się wykonać operacji."));
  });
}
export function isDesktop() { return !!window.chrome?.webview; }
export function invoke<T>(action: string, fields: Record<string, unknown> = {}): Promise<T> {
  const bridge = window.chrome?.webview;
  if (!bridge) return Promise.reject(new Error(
    "Dane Subiekta są dostępne po uruchomieniu desktopowego WebView2 z parametrem --react."
  ));
  const id = String(++sequence);
  return new Promise<T>((resolve, reject) => {
    const timeout = setTimeout(() => {
      pending.delete(id);
      reject(new Error("Serwer nie odpowiedział w wyznaczonym czasie."));
    }, 40000);
    pending.set(id, {resolve: value => resolve(value as T), reject, timeout});
    bridge.postMessage({id, action, ...fields});
  });
}
