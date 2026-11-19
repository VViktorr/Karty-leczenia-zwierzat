export type DocumentKind = "invoices" | "receipts" | "warehouse" | "all";
export type DatePeriod = "all" | "today" | "currentWeek" | "currentMonth" |
  "currentQuarter" | "currentYear" | "week" | "month" | "customMonth" | "custom";
export interface DocumentRow {
  id: number; number: string; date: string | null;
  shortPayer: string; payer: string; gross: number | null;
  net: number | null; currency: string; relatedText: string;
}
export interface DocumentsResult { items: DocumentRow[]; truncated: boolean; cached: boolean; loadedAt: string; }
export interface DocumentDetail {
  number: string; payer: string; recipient: string;
  rows: Array<{product: string; quantity: string; batch: string; species: string; route: string;}>;
}
interface RequestMessage { id: string; action: string; [key: string]: unknown; }
export interface BackupProgress {stage:string;percent:number|null;detail:string;}
interface ResponseMessage { id: string; ok: boolean; data?: unknown; error?: string;
  event?: "progress";stage?:string;percent?:number|null;detail?:string; }
interface WebView {
  postMessage(message: RequestMessage): void;
  addEventListener(type: "message", listener: (event: MessageEvent<ResponseMessage>) => void): void;
}
declare global { interface Window { chrome?: { webview?: WebView } } }
const pending = new Map<string, {
  resolve: (value: unknown) => void; reject: (reason: Error) => void;
  timeout: ReturnType<typeof setTimeout>;
  onProgress?: (progress: BackupProgress) => void;
}>();
let sequence = 0;
if (window.chrome?.webview) {
  window.chrome.webview.addEventListener("message", event => {
    const response = event.data;
    if (!response || typeof response.id !== "string") return;
    const request = pending.get(response.id);
    if (!request) return;
    if (response.event === "progress") {
      request.onProgress?.({stage:response.stage ?? "",percent:response.percent ?? null,
        detail:response.detail ?? ""});
      return;
    }
    pending.delete(response.id);
    clearTimeout(request.timeout);
    if (response.ok) request.resolve(response.data);
    else request.reject(new Error(response.error || "Nie udało się wykonać operacji."));
  });
}
export function isDesktop() { return !!window.chrome?.webview; }
export function invoke<T>(action: string, fields: Record<string, unknown> = {},
  onProgress?: (progress: BackupProgress) => void, signal?: AbortSignal): Promise<T> {
  const bridge = window.chrome?.webview;
  if (!bridge) return Promise.reject(new Error(
    "Dane SQL i Subiekta są dostępne po uruchomieniu aplikacji KartyLeczenia.ReactKL.exe."
  ));
  if(signal?.aborted)return Promise.reject(new Error("Operacja anulowana."));
  const id = String(++sequence);
  return new Promise<T>((resolve, reject) => {
    let abort: (() => void)|null = null;
    const clean = () => {if(abort)signal?.removeEventListener("abort",abort);};
    const timeout = setTimeout(() => {
      pending.delete(id);clean();
      reject(new Error("Serwer nie odpowiedział w wyznaczonym czasie."));
    }, action === "backupDatabase" || action === "restoreDatabase" ? 2700000 :
      action === "installUpdate" ? 900000 :
      action === "purgeTrashedCard" ? 180000 :
      action === "analyseCataloguePdf" ? 120000 :
      action === "saveCardPdf" || action === "emailCardPdf" ? 180000 : 40000);
    pending.set(id, {
      resolve: value => {clean();resolve(value as T);},
      reject: error => {clean();reject(error);},
      timeout, onProgress
    });
    if(signal){
      abort=()=>{
        if(!pending.has(id))return;
        pending.delete(id);clearTimeout(timeout);clean();
        // Native host cancels the linked SQL request by original request id.
        // No response is expected for this fire-and-forget cancellation.
        try { bridge.postMessage({id:String(++sequence),action:"cancelRequest",requestId:id}); }
        catch { /* WebView2 can already be closed during unmount. */ }
        reject(new Error("Anulowano nieaktualne wyszukiwanie."));
      };
      signal.addEventListener("abort",abort,{once:true});
    }
    // The payload may itself contain an "id" (e.g. an archived card GUID).
    // Keep the transport correlation ID independent of any domain record ID.
    bridge.postMessage({id, action, ...fields, _bridgeRequestId:id});
  });
}
