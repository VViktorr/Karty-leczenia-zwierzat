import dayjs from "dayjs";
import "dayjs/locale/pl";
import datePickerPl from "antd/es/date-picker/locale/pl_PL";
import {isPoultry} from "./cataloguePdfMetadata";
import Modal from "./AppModal";
import {ListFormattingPanel,useListFormatting} from "./ListFormatting";
import {useListPagination} from "./ListPagination";
import {moveColumn as reorderColumn,useTableMouseLayout} from "./TableMouseLayout";
import { useEffect, useMemo, useState, useRef } from "react";
import {
  Alert, Button, Card, Checkbox, ConfigProvider, DatePicker, Descriptions, Dropdown, Empty,
  Form, Input, InputNumber, Segmented, Select, Space, Spin, Switch, Table,
  Tag, Tabs, Typography, Progress, theme
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke, isDesktop } from "./bridge";
import CataloguePage from "./CataloguePage";
import {useExpiryTableLayout} from "./ExpiryTableLayout";
import {useListColumnLayout} from "./ListColumnLayout";
import {SubiektDatabaseSettings,ProgramDatabaseSettings} from "./DatabaseSettings";
import { defaultPricePrint, normalizePriceSettings, withdrawalText,
  PricePrintSettingsPanel, PricePrintPreview } from "./PricePrint";
import type { PricePrintOptions, PricePrintRow } from "./PricePrint";
import type { BackupProgress } from "./bridge";
import type { DocumentKind, DocumentRow, DocumentsResult, DatePeriod } from "./bridge";

type LayoutName = "classic" | "modern" | "fluent" | "compact-tabs" | "compact-sidebar";
type PaletteName = "light" | "dark" | "sea";
type Section = "documents" | "prices" | "catalogue" | "treatment" | "saved" | "expiry" | "settings";
interface DraftRow {
  index: number; product: string; replacement: string; referenceSymbol?: string; descriptionGroup?: string; countGroup?: string; symbol: string; indication: string; quantity: string; batch: string;
  species: string; route: string; dosing: string; withdrawal: string;
  description: string; animals: string; diagnosis: string; section: string; selected: boolean;
}
interface Draft {
  draftId: string; number: string; payer: string; recipient: string;
  hasPayerId: boolean; rows: DraftRow[];
}
interface TreatmentVariantChoice {
  id:number;species:string;indication:string;route:string;dose:string;
  tissues:string;milk:string;eggs:string;remarks:string;source:string;pdf:string;
}
interface TreatmentVariantList {
  product:string;symbol:string;variants:TreatmentVariantChoice[];
}
interface TreatmentSuggestion {
  index:number;symbol:string;status:string;message:string;
  variantId:number|null;species:string;route:string;dosing:string;
  withdrawal:string;source:string;hasPdf:boolean;pdfPath:string;groups:SpeciesGroup[];variantCount:number;hasCatalogueVariants:boolean;nonMedicine:boolean;
}
interface TreatmentSuggestionList {
  items:TreatmentSuggestion[];
  compatibility:Array<{index:number;groups:SpeciesGroup[]}>;
}
type SpeciesGroup="all"|"cattle"|"swine"|"poultry"|"dog"|"cat";
interface SavedChoice {symbol:string;species:string;route:string;}
interface SavedChoiceList {items:SavedChoice[];}
const speciesGroups:Array<{value:SpeciesGroup;label:string}>=[
  {value:"all",label:"Wszystkie gatunki"},
  {value:"cattle",label:"Bydło (także cielęta)"},
  {value:"swine",label:"Świnie"},
  {value:"poultry",label:"Drób"},
  {value:"dog",label:"Pies"},
  {value:"cat",label:"Kot"}
];
interface TreatmentBulkResult {
  applied:Array<{index:number;species:string;route:string;dosing:string;
    withdrawal:string;indication:string;description:string}>;
  skipped:string[];
}
interface PriceLevel { number: number; name: string; }
interface MedicinePrice {
  symbol: string; name: string; gross: number | null; withdrawal: string; latestBatch: string;
}
interface MedicineExpiry {
  name: string; batch: string; expires: string | null; received: string | null; remaining: number;
  warehouseName: string; deliveriesConfirmed: boolean; productId: number; movementId: number;
}
interface SavedCard { id: string; date: string; client: string; document: string; number: string; }
interface SaveResult {duplicate: boolean; duplicateId: string | null; number: string | null; id: string | null; html: string | null;}
interface StoredCard {
  fields: Record<string, string>;
  rows: Array<Record<string, string>>;
  revision: string;
}
const archivedEditableColumns=[
  {key:"Seria",title:"Seria",width:140},
  {key:"Gatunek",title:"Gatunek",width:145},
  {key:"Droga podania",title:"Droga podania",width:160},
  {key:"Dawkowanie i okres stosowania",title:"Dawkowanie",width:250},
  {key:"Karencja",title:"Karencja",width:250},
  {key:"Liczba zwierząt",title:"Liczba zwierząt",width:135},
  {key:"Opis zwierząt / numer siedziby stada",title:"Opis zwierząt",width:230},
  {key:"Rozpoznanie",title:"Rozpoznanie",width:210},
  {key:"Zamiennik produktu",title:"Zamiennik",width:180},
  {key:"Sekcja (II lub III)",title:"Nabycie produktu",width:150},
  {key:"Zalecenia / uwagi",title:"Uwagi",width:230}
] as const;
const archivedEditableFields=new Set(["Wyniki badań",
  "Zgłoszenie — data i godzina","Wykonanie — data i godzina"]);
interface SqlSettings {
  configured: boolean; warning?: string; server: string; database: string;
  windowsAuthentication: boolean; userName: string; trustCertificate: boolean;
  drugGroup: string; warehouseId: number | null;
}

const sections: Array<{id: Section; name: string}> = [
  {id: "documents", name: "Dokumenty"}, {id: "prices", name: "Cennik leków"},
  {id: "catalogue", name: "Katalog leków / ChPL"},
  {id: "treatment", name: "Karta leczenia"}, {id: "saved", name: "Zapisane karty"},
  {id: "expiry", name: "Terminy ważności"}, {id: "settings", name: "Ustawienia"}
];
function saved<T extends string>(key: string, values: readonly T[], fallback: T): T {
  try { const value = localStorage.getItem(key); return values.find(item => item === value) ?? fallback; }
  catch { return fallback; }
}
const layouts = ["modern", "classic", "fluent", "compact-tabs", "compact-sidebar"] as const;
const layoutLabels: Record<LayoutName,string> = {
  modern:"Modern — menu boczne", classic:"Classic — zakładki", fluent:"Fluent — Windows 11",
  "compact-tabs":"Zakładki kompaktowe", "compact-sidebar":"Menu boczne kompaktowe"
};
const palettes = ["light", "dark", "sea"] as const;
const currency = (n: number | null, unit = "zł") =>
  n === null ? "—" : new Intl.NumberFormat("pl-PL", {minimumFractionDigits: 2, maximumFractionDigits: 2})
    .format(n) + " " + unit;
function localDateTime() {
  const d = new Date();
  const local = new Date(d.getTime() - d.getTimezoneOffset()*60000);
  return local.toISOString().slice(0,16);
}
const date = (value: string | null) => value
  ? new Intl.DateTimeFormat("pl-PL").format(new Date(value)) : "—";
function ErrorNotice({error}: {error: string}) {
  return error ? <Alert className="notice" type="error" showIcon message={error}/> : null;
}
function useRemote<T>(action: string, payload: Record<string, unknown>, deps: unknown[],
  enabled = true, sameKeyOnly = false): {
  data: T | null; busy: boolean; error: string; reload: () => void;
} {
  const [response, setResponse] = useState<{key:string;data:T} | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);
  const forceRefresh = useRef(false);
  // Keys are primitive strings/numbers, not serialized SQL credentials.
  const key = JSON.stringify(deps);
  useEffect(() => {
    let expired = false;
    if (!enabled) {setBusy(false);return;}
    const controller = new AbortController();
    const refresh = forceRefresh.current;
    forceRefresh.current = false;
    setBusy(true); setError("");
    invoke<T>(action, refresh ? {...payload, refresh:true} : payload, undefined, controller.signal)
      .then(result => { if (!expired) setResponse({key,data:result}); })
      .catch(reason => { if (!expired) setError(reason instanceof Error ? reason.message : String(reason)); })
      .finally(() => { if (!expired) setBusy(false); });
    return () => { expired = true; controller.abort(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [action, key, tick, enabled]);
  const data = response && (!sameKeyOnly || response.key===key) ? response.data : null;
  return {data, busy, error, reload: () => {
    forceRefresh.current = true;
    setTick(n => n + 1);
  }};
}

function Documents({active, compact, onOpen}: {active: boolean; compact: boolean; onOpen: (draft: Draft) => void}) {
  const tableRef=useRef<HTMLDivElement>(null);
  const [contextMenu,setContextMenu]=useState<DocumentRow|"header"|null>(null);
  useEffect(()=>{if(!active||!compact)setContextMenu(null);},[active,compact]);
  const [kind, setKind] = useState<DocumentKind>("invoices");
  const [period, setPeriod] = useState<DatePeriod>("all");
  const [pageSize,setPageSize] = useState<25|50|100|"all">(()=>{
    try {
      const stored=localStorage.getItem("kl.documents.pageSize");
      return stored==="all"?"all":stored==="50"?50:stored==="100"?100:25;
    }catch{return 25;}
  });
  const [page,setPage]=useState(1);
  const [query, setQuery] = useState("");
  const [search, setSearch] = useState("");
  const [selectedId,setSelectedId] = useState<number|null>(null);
  const [startDate,setStartDate] = useState(() => localDateTime().slice(0,10));
  const [endDate,setEndDate] = useState(() => localDateTime().slice(0,10));
  const [selectedMonth,setSelectedMonth] = useState(() => localDateTime().slice(0,7));
  const [readError, setReadError] = useState("");
  const [reading, setReading] = useState(false);
  const openedDocument = useRef<AbortController | null>(null);
  useEffect(() => {
    if (!active) setReading(false);
    return () => openedDocument.current?.abort();
  }, [active]);
  useEffect(() => {
    const timer = setTimeout(() => setSearch(query.trim()), 300);
    return () => clearTimeout(timer);
  }, [query]);
  const from = period==="customMonth" ? selectedMonth+"-01" : period==="custom" ? startDate : "";
  const until = period==="custom" ? endDate : "";
  const {data, busy, error, reload} = useRemote<DocumentsResult>(
    "searchDocuments", {kind, period, query: search, startDate:from, endDate:until},
    [kind, period, search, from, until], true, true);
  useEffect(()=>{setPage(1);},[kind,period,search,from,until]);
  const allPageSize=Math.max(500,data?.items.length??0);
  const effectivePageSize=pageSize==="all"?allPageSize:pageSize;
  function changePageSize(value:25|50|100|"all"){
    setPageSize(value);setPage(1);
    try{localStorage.setItem("kl.documents.pageSize",String(value));}catch{}
  }
  const open = async (row: DocumentRow) => {
    if (openedDocument.current) return;
    const request = new AbortController();
    openedDocument.current = request;
    setReading(true); setReadError("");
    try {
      const draft = await invoke<Draft>("readDocument",
        {documentId: row.id, number: row.number}, undefined, request.signal);
      if (!request.signal.aborted) onOpen(draft);
    } catch (error) {
      if (!request.signal.aborted) setReadError(error instanceof Error ? error.message : String(error));
    } finally {
      openedDocument.current = null;
      if (!request.signal.aborted) setReading(false);
    }
  };
  useEffect(() => {
    if (!active) return;
    const key = (event: KeyboardEvent) => {
      if (event.key === "F5") { event.preventDefault(); reload(); return; }
      if(event.altKey && !event.ctrlKey && !event.shiftKey){
        const chosen = event.code==="Digit1"?"invoices":
          event.code==="Digit2"?"receipts":event.code==="Digit4"?"warehouse":null;
        if(chosen){event.preventDefault();setKind(chosen);return;}
      }
      if(event.key==="Enter" && event.target instanceof HTMLElement &&
        event.target.closest("tr[data-row-key]")){
        const id=Number(event.target.closest("tr[data-row-key]")?.getAttribute("data-row-key"));
        const row=data?.items.find(item=>item.id===id);
        if(row){event.preventDefault();void open(row);}
      }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, [active,reload,data,reading]);
  const columns: ColumnsType<DocumentRow> = [
    {title:"Data",dataIndex:"date",width:125,render:date,
      sorter:(a,b)=>(a.date||"").localeCompare(b.date||"")},
    {title:"Numer",dataIndex:"number",width:175,
      sorter:(a,b)=>a.number.localeCompare(b.number)},
    {title:"Kontrahent",dataIndex:"shortPayer",ellipsis:true,
      render:(value:string,row)=><span title={row.payer}>{value||row.payer}</span>},
    {title:"Brutto",dataIndex:"gross",align:"right",width:145,
      render:(value:number|null,row)=>currency(value,row.currency||"zł"),
      sorter:(a,b)=>(a.gross??0)-(b.gross??0)},
    {title:"Powiązania",dataIndex:"relatedText",ellipsis:true,width:230}
  ];
  const listLayout=useListColumnLayout("documents",columns,active);
  const columnMenu=[
    {key:"columns",label:"Ustaw kolumny",onClick:listLayout.openColumns},
    {key:"fit",label:"Dopasuj szerokości",onClick:()=>{if(tableRef.current)listLayout.fitColumns(tableRef.current);}},
    {key:"reset",label:"Przywróć domyślne kolumny",onClick:listLayout.resetColumns}
  ];
  async function copyDocumentText(value:string){
    try{await navigator.clipboard.writeText(value);setReadError("");}
    catch{setReadError("Nie udało się skopiować tekstu do schowka.");}
  }
  const rowMenu=contextMenu&&contextMenu!=="header"?[
    {key:"open",label:"Otwórz dokument",onClick:()=>{void open(contextMenu);}},
    {key:"number",label:"Kopiuj numer",onClick:()=>{void copyDocumentText(contextMenu.number);}},
    {key:"payer",label:"Kopiuj nazwę kontrahenta",onClick:()=>{
      void copyDocumentText(contextMenu.payer||contextMenu.shortPayer);
    }}
  ]:columnMenu;
  const periods: Array<{label:string;value:DatePeriod}> = [
    {label:"(nieokreślony)",value:"all"},
    {label:"Bieżący dzień",value:"today"},
    {label:"Bieżący tydzień",value:"currentWeek"},
    {label:"Bieżący miesiąc",value:"currentMonth"},
    {label:"Bieżący kwartał",value:"currentQuarter"},
    {label:"Bieżący rok",value:"currentYear"},
    {label:"Ostatnie 7 dni",value:"week"},
    {label:"Ostatnie 30 dni",value:"month"},
    {label:"Dowolny miesiąc",value:"customMonth"},
    {label:"Dowolny okres",value:"custom"}
  ];
  return <>
    {!compact&&<div className="page-heading"><div>
      <Typography.Title level={3}>Dokumenty Subiekta GT</Typography.Title>
      <Typography.Text type="secondary">Lista z jednego magazynu. F5 pobiera świeże dane SQL.</Typography.Text>
    </div><Space wrap>
      <Button onClick={listLayout.openColumns}>Kolumny · Ctrl+Shift+L</Button>
      <Button onClick={reload} loading={busy}>Odśwież · F5</Button>
    </Space></div>}
    <Card className={compact?"compact-documents":undefined}>
      <div className={"document-filters"+(compact?" compact-document-toolbar":"")}>
        <Segmented block size={compact?"middle":"large"} value={kind} onChange={value=>{setKind(value as DocumentKind);setSelectedId(null);}}
          options={[{label:compact?"Faktury":"Faktury · Alt+1",value:"invoices"},
            {label:compact?"Paragony":"Paragony · Alt+2",value:"receipts"},
            {label:compact?"WZ":"WZ · Alt+4",value:"warehouse"},
            {label:"Wszystkie",value:"all"}]}/>
        <Input.Search allowClear size={compact?"middle":"large"} value={query}
          onChange={e=>setQuery(e.target.value)}
          onSearch={text=>{setSearch(text.trim());}}
          onPressEnter={()=>{setSearch(query.trim());reload();}}
          placeholder="Numer lub kontrahent"/>
        <Select value={period} size={compact?"middle":"large"} onChange={next=>{setPeriod(next);setSelectedId(null);}}
          options={periods}/>
        {compact&&<Space size={6} className="compact-document-actions">
          {data?.truncated&&<Tag title="Pokazano 500 dokumentów. Zawęź wyszukiwanie."
            color="blue">Limit: 500</Tag>}
          <Button onClick={reload} loading={busy} title="Odśwież · F5">Odśwież ↻</Button>
          <Dropdown menu={{items:columnMenu}} trigger={["click"]}>
            <Button aria-label="Ustawienia tabeli dokumentów">⋯</Button>
          </Dropdown>
        </Space>}
        {period==="customMonth"&&<Input type="month" aria-label="Wybrany miesiąc"
          value={selectedMonth} onChange={e=>setSelectedMonth(e.target.value)}/>}
        {period==="custom"&&<>
          <Input type="date" aria-label="Od dnia" value={startDate} onChange={e=>setStartDate(e.target.value)}/>
          <Input type="date" aria-label="Do dnia" value={endDate} onChange={e=>setEndDate(e.target.value)}/>
        </>}
      </div>
      <ErrorNotice error={readError || (!data ? error : "")}/>
      {data&&(data.warning||error)&&<Alert className="notice" type="warning" showIcon
        message="Nie udało się odświeżyć dokumentów. Pokazuję ostatnią wczytaną listę."
        description={data.warning||error}/>}
      {!compact&&data?.truncated&&<Alert className="notice" type="info" showIcon
        message="Pokazano 500 dokumentów. Zawęź wyszukiwanie."/>}
      <Dropdown trigger={compact?["contextMenu"]:[]} open={compact&&contextMenu!==null}
        onOpenChange={visible=>{if(!visible)setContextMenu(null);}}
        menu={{items:rowMenu,onClick:()=>setContextMenu(null)}}>
        <div ref={tableRef} onContextMenuCapture={event=>{
          if(!compact||!(event.target instanceof Element))return;
          const header=event.target.closest(".ant-table-thead th");
          const id=event.target.closest("tr[data-row-key]")?.getAttribute("data-row-key");
          const row=data?.items.find(item=>String(item.id)===id);
          if(header||row){event.preventDefault();setContextMenu(header?"header":row!);if(row)setSelectedId(row.id);}
          else event.stopPropagation();
        }}>
      <Table {...listLayout.tableProps} rowKey="id" loading={(busy&&!data)||reading} dataSource={data?.items??[]} columns={listLayout.columns}
        rowClassName={row=>row.id===selectedId?"ant-table-row-selected":""}
        size={compact?"small":"middle"} scroll={{x:900,y:compact?"calc(100vh - 250px)":"calc(100vh - 390px)"}} pagination={{
          current:page,pageSize:effectivePageSize,position:["bottomRight"],
          pageSizeOptions:[25,50,100,allPageSize],
          showSizeChanger:{
            "aria-label":"Liczba wyświetlanych dokumentów",
            options:[{label:"25 dokumentów",value:25},{label:"50 dokumentów",value:50},
              {label:"100 dokumentów",value:100},
              {label:"Wszystkie pobrane",value:allPageSize}]
          },
          onChange:(next,size)=>{
            if(size===effectivePageSize) setPage(next);
            else if(size===25||size===50||size===100) changePageSize(size);
            else if(size===allPageSize) changePageSize("all");
          }
        }}
        onRow={row=>({tabIndex:0,onClick:()=>setSelectedId(row.id),
          onDoubleClick:()=>{void open(row);}})}
        locale={{emptyText:<Empty description="Brak dokumentów"/>}}/>
        </div>
      </Dropdown>
      <Typography.Text type="secondary">
        {data?.loadedAt?"Ostatni odczyt "+new Date(data.loadedAt).toLocaleTimeString("pl-PL",{hour:"2-digit",minute:"2-digit"})+
          (data.cached?" · lista z pamięci":" · odświeżono z SQL")+" · ":""}
        {data?.items.length??0} dokumentów. {pageSize==="all"
          ? "Pokazano wszystkie pobrane pozycje (limit SQL: 500). "
          : ""}Dwuklik lub Enter na wierszu otwiera kartę.
      </Typography.Text>
    </Card>
    {listLayout.dialog}
  </>;
}

const editable: Array<{key: keyof DraftRow; field: string; title: string}> = [
  {key:"batch",field:"Seria",title:"Seria"},
  {key:"replacement",field:"Zamiennik produktu",title:"Zamiennik produktu"},
  {key:"species",field:"Gatunek",title:"Gatunek"},
  {key:"route",field:"Droga podania",title:"Droga podania"},
  {key:"dosing",field:"Dawkowanie i okres stosowania",title:"Dawkowanie"},
  {key:"withdrawal",field:"Karencja",title:"Karencja"},
  {key:"animals",field:"Liczba zwierząt",title:"Liczba zwierząt"},
  {key:"description",field:"Opis zwierząt / numer siedziby stada",title:"Opis zwierząt"},
  {key:"diagnosis",field:"Rozpoznanie",title:"Rozpoznanie"},
  {key:"section",field:"Sekcja (II lub III)",title:"Nabycie produktu"}
];
const treatmentColumns=[{value:"selected",label:"Druk"},{value:"product",label:"Produkt"},
  {value:"section",label:"Nabycie produktu"},{value:"variant",label:"Wariant ChPL"},
  {value:"quantity",label:"Ilość"},{value:"indication",label:"Wskazanie z ChPL"},
  ...editable.filter(({key})=>key!=="section").map(({key,title})=>({value:key,label:title}))];
const treatmentColumnKeys=treatmentColumns.map(column=>column.value);
function treatmentColumnKey(column:ColumnsType<DraftRow>[number]):string {
  return String(column.key??("dataIndex" in column?column.dataIndex:""));
}
function restoreColumnOrder():string[] {
  try{
    const savedOrder=JSON.parse(localStorage.getItem("kl.treatmentColumnOrder")??"null");
    if(Array.isArray(savedOrder))
      return ["selected","product",...Array.from(new Set(
        savedOrder.map(value=>value==="suggestion"?"variant":value).filter((value:unknown)=>typeof value==="string"&&
          treatmentColumnKeys.includes(value)&&value!=="selected"&&value!=="product")
      )),...treatmentColumnKeys.filter(key=>key!=="selected"&&key!=="product"&&
        !savedOrder.includes(key)&&!(key==="variant"&&savedOrder.includes("suggestion")))];
  }catch{/* Reset invalid legacy settings. */}
  return treatmentColumnKeys;
}
function restoreColumnWidths():Record<string,number> {
  try{
    const values:unknown=JSON.parse(localStorage.getItem("kl.treatmentColumnWidths")??"null");
    if(values && typeof values==="object" && !Array.isArray(values))
      return Object.fromEntries(Object.entries(values).filter(([key,width])=>
        treatmentColumnKeys.includes(key)&&typeof width==="number"&&
        Number.isFinite(width)&&width>=60&&width<=800));
  }catch{/* Use default widths. */}
  return {};
}
function TreatmentOptions({compact, label, children}: {
  compact: boolean; label: string; children: React.ReactNode;
}) {
  return compact
    ? <details className="treatment-options"><summary>{label}</summary>{children}</details>
    : <>{children}</>;
}

function Treatment({draft, compact, onDocuments, onOpenSaved}: {
  draft: Draft | null; compact: boolean; onDocuments: () => void; onOpenSaved: (id:string)=>void;
}) {
  const [rows, setRows] = useState<DraftRow[]>(draft?.rows ?? []);
  const treatmentControls = useRef<HTMLDivElement>(null);
  const [tableHeight, setTableHeight] = useState(180);
  useEffect(()=>{
    const controls=treatmentControls.current;
    const main=controls?.closest<HTMLElement>("main");
    if(!compact||!controls||!main)return;
    const measure=()=>{
      const padding=getComputedStyle(main);
      const available=main.clientHeight-controls.offsetHeight
        - parseFloat(padding.paddingTop)-parseFloat(padding.paddingBottom)-76;
      setTableHeight(Math.max(180,Math.floor(available)));
    };
    const observer=new ResizeObserver(measure);
    observer.observe(controls);
    observer.observe(main);
    measure();
    return()=>observer.disconnect();
  },[compact,draft]);
  const [showColumns,setShowColumns]=useState(false);
  const treatmentMouse = useTableMouseLayout("treatment");
  const treatmentFormatting = useListFormatting("treatment");
  const [columnOrder,setColumnOrder]=useState<string[]>(restoreColumnOrder);
  const [columnWidths,setColumnWidths]=useState<Record<string,number>>(restoreColumnWidths);
  useEffect(()=>{
    try{
      localStorage.setItem("kl.treatmentColumnOrder",JSON.stringify(columnOrder));
      localStorage.setItem("kl.treatmentColumnWidths",JSON.stringify(columnWidths));
    }catch{/* Session settings still work. */}
  },[columnOrder,columnWidths]);
  function moveColumn(key:string,direction:-1|1){
    if(key==="selected"||key==="product")return;
    setColumnOrder(current=>{
      const index=current.indexOf(key),other=index+direction;
      if(index<2||other<2||other>=current.length)return current;
      const next=[...current];[next[index],next[other]]=[next[other],next[index]];
      return next;
    });
  }
  const [visibleColumns,setVisibleColumns]=useState<string[]>(()=>{
    try{
      const stored=JSON.parse(localStorage.getItem("kl.treatmentColumns")??"null");
      if(Array.isArray(stored)){
        const valid=stored.map(value=>value==="suggestion"?"variant":value).filter((value:unknown)=>typeof value==="string"&&
          treatmentColumnKeys.includes(value));
        return Array.from(new Set([...valid,"selected","product"]));
      }
    }catch{/* Use original columns if settings are unavailable. */}
    return treatmentColumnKeys;
  });
  useEffect(()=>{
    try{localStorage.setItem("kl.treatmentColumns",JSON.stringify(visibleColumns));}
    catch{/* Current choice remains active for this session. */}
  },[visibleColumns]);
  useEffect(()=>{
    const key=(event:KeyboardEvent)=>{
      if(event.ctrlKey&&event.shiftKey&&event.code==="KeyL"){
        event.preventDefault();setShowColumns(current=>!current);
      }
    };
    window.addEventListener("keydown",key);
    return()=>window.removeEventListener("keydown",key);
  },[]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [savedNumber, setSavedNumber] = useState("");
  const [duplicateId,setDuplicateId]=useState<string|null>(null);
  const [reported, setReported] = useState("");
  const [performed, setPerformed] = useState(() => localDateTime());
  const [dateStep,setDateStep]=useState<{reported:string;performed:string}|null>(null);
  const [saveReview,setSaveReview]=useState<{
    dates:{reported:string;performed:string};
    warnings:{row:DraftRow;issues:{message:string;field?:keyof DraftRow}[]}[];
  }|null>(null);
  const [savingSql,setSavingSql]=useState(false);
  const pendingSaveDates=useRef<{reported:string;performed:string}|null>(null);
  const [animalEdit,setAnimalEdit]=useState<{
    candidates:DraftRow[]; checked:number[]; description:string; count:string; beforeSave?:boolean;
  }|null>(null);
  const [referenceEdit,setReferenceEdit]=useState<{
    index:number; value:string; items:{symbol:string;name:string}[];
  }|null>(null);
  const [referenceBusy,setReferenceBusy]=useState(false);
  const [findings, setFindings] = useState("");
  const [preview, setPreview] = useState<string | null>(null);
  const [previewSavedId,setPreviewSavedId]=useState<string|null>(null);
  const [pdfBusy,setPdfBusy]=useState<"pdf"|"mail"|null>(null);
  const [pdfMessage,setPdfMessage]=useState("");
  const previewFrame = useRef<HTMLIFrameElement>(null);
  const [variantModal,setVariantModal]=useState<{
    index:number;product:string;symbol:string;variants:TreatmentVariantChoice[];
  }|null>(null);
  const [variantBusy,setVariantBusy]=useState(false);
  const [variantError,setVariantError]=useState("");
  const [variantPdfUrl,setVariantPdfUrl]=useState<string|null>(null);
  const [variantPdfName,setVariantPdfName]=useState("");
  const [suggestions,setSuggestions]=useState<TreatmentSuggestion[]>([]);
  const [suggestBusy,setSuggestBusy]=useState(false);
  const [suggestError,setSuggestError]=useState("");
  const [suggestReviewOpen,setSuggestReviewOpen]=useState(false);
  const [applyBusy,setApplyBusy]=useState(false);
  const [autofillMessage,setAutofillMessage]=useState("");
  const [suggestTick,setSuggestTick]=useState(0);
  const [suggestionGroup,setSuggestionGroup]=useState<SpeciesGroup>();
  const [speciesGroup,setSpeciesGroup]=useState<SpeciesGroup>("cattle");
  const [compatibility,setCompatibility]=useState<Record<number,SpeciesGroup[]>>({});
  const [compatibilityDraftId,setCompatibilityDraftId]=useState("");
  const desiredChecks=useRef<Map<number,boolean>>(new Map());
  const [remember,setRemember]=useState(()=>{
    try{return localStorage.getItem("ReactKL.rememberTreatmentChoices")==="true";}
    catch{return false;}
  });
  const [savedChoices,setSavedChoices]=useState<Record<string,SavedChoice>>({});
  const [choiceError,setChoiceError]=useState("");
  const [selectionBusy,setSelectionBusy]=useState(false);
  const [animalDescriptions,setAnimalDescriptions]=useState<string[]>([]);
  const [animalError,setAnimalError]=useState("");
  const [animalBusy,setAnimalBusy]=useState(false);
  const [groupDescription,setGroupDescription]=useState("");
  const [groupCount,setGroupCount]=useState("");
  const [groupSpecies,setGroupSpecies]=useState("");
  const [groupSection,setGroupSection]=useState("II");
  const selectionQueue=useRef<Promise<void>>(Promise.resolve());
  const selectionVersion=useRef(0);
  const autoApplied=useRef(new Set<string>());
  useEffect(() => {
    autoApplied.current.clear();
    setRows(draft?.rows ?? []);setSavedNumber("");setVariantModal(null);
    pendingSaveDates.current=null;setSaveReview(null);
    setDuplicateId(null);setDateStep(null);setAnimalEdit(null);setReferenceEdit(null);
    setReported("");setPerformed(localDateTime());
    desiredChecks.current=new Map((draft?.rows??[]).map(row=>[row.index,row.selected]));
    setSpeciesGroup("cattle");setCompatibilityDraftId("");setCompatibility({});
    setSavedChoices({});setAnimalDescriptions([]);setAnimalError("");
    setGroupDescription("");setGroupCount("");setGroupSpecies("");setGroupSection("II");
  }, [draft]);
  useEffect(()=>{
    if(!draft){setSuggestions([]);return;}
    let cancelled=false;
    setSuggestBusy(true);setSuggestError("");setSuggestions([]);
    // The selection effect queues the new group on the host in this render.
    void Promise.resolve().then(()=>selectionQueue.current)
      .then(()=>cancelled?undefined:invoke<TreatmentSuggestionList>("treatmentSuggestions",{
        draftId:draft.draftId,speciesGroup
      }))
      .then(result=>{
        if(cancelled||!result)return;
        setSuggestionGroup(speciesGroup);
        setSuggestions(result.items);
        const groups:Record<number,SpeciesGroup[]>={};
        for(const item of result.compatibility??[])groups[item.index]=item.groups;
        setCompatibility(groups);
        setCompatibilityDraftId(draft.draftId);
      })
      .catch(e=>{if(!cancelled){setSuggestions([]);setSuggestError(
        e instanceof Error?e.message:String(e));}})
      .finally(()=>{if(!cancelled)setSuggestBusy(false);});
    return()=>{cancelled=true;};
  },[draft,suggestTick,speciesGroup]);
  useEffect(()=>{
    if(!draft||!remember){setSavedChoices({});setChoiceError("");return;}
    let active=true;
    void invoke<SavedChoiceList>("readTreatmentChoices",{draftId:draft.draftId})
      .then(data=>{
        if(!active)return;
        const preferences:Record<string,SavedChoice>={};
        for(const item of data.items)preferences[item.symbol.trim().toUpperCase()]=item;
        setSavedChoices(preferences);setChoiceError("");
      })
      .catch(error=>{if(active)setChoiceError(error instanceof Error?error.message:String(error));});
    return()=>{active=false;};
  },[draft?.draftId,remember]);

  useEffect(()=>{
    if(!draft||compatibilityDraftId!==draft.draftId)return;
    const belongs=(index:number)=>speciesGroup==="all"||
      (compatibility[index]??[]).includes(speciesGroup);
    const planned=rows.map(row=>({...row,selected:belongs(row.index)&&
      (desiredChecks.current.get(row.index)??row.selected)}));
    setRows(planned);setSavedNumber("");setSelectionBusy(true);
    // Serialize snapshots: a slower SQL/host response must not overwrite a
    // newer species filter selection when the user switches groups quickly.
    const version=++selectionVersion.current;
    const payload={draftId:draft.draftId,
      selections:planned.map(row=>({index:row.index,symbol:row.symbol,selected:row.selected}))};
    selectionQueue.current=selectionQueue.current
      .catch(()=>{/* An earlier failed snapshot must not block the next one. */})
      .then(async()=>{
        await invoke("setDraftSelections",payload);
      })
      .catch(error=>{
        if(version===selectionVersion.current)
          setError(error instanceof Error?error.message:String(error));
      })
      .finally(()=>{
        if(version===selectionVersion.current)setSelectionBusy(false);
      });
    // Changing the filter uses catalogue compatibility already loaded once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  },[speciesGroup,compatibility,compatibilityDraftId,draft]);

  useEffect(()=>{
    if(!draft?.hasPayerId){setAnimalDescriptions([]);return;}
    let active=true;
    void invoke<{items:string[]}>("readAnimalDescriptions",{draftId:draft.draftId})
      .then(data=>{if(active){setAnimalDescriptions(data.items);setAnimalError("");}})
      .catch(error=>{if(active)setAnimalError(error instanceof Error?error.message:String(error));});
    return()=>{active=false;};
  },[draft?.draftId,draft?.hasPayerId]);

  const groupSpeciesOptions=Array.from(new Set(rows.map(row=>row.species.trim())
    .filter(Boolean))).sort((left,right)=>left.localeCompare(right,"pl"));
  function applyGroupAnimals(){
    if(!draft||!groupSpecies.trim())return;
    const target=rows.filter(row=>row.selected&&row.species.trim()===groupSpecies&&
      (row.section==="III"?"III":"II")===groupSection);
    if(!target.length){setAnimalError("Nie ma zaznaczonych pozycji tego gatunku i sekcji.");return;}
    const description=groupDescription.trim(),count=groupCount.trim();
    if(!description&&!count){setAnimalError("Wprowadź opis zwierząt lub liczbę leczonych.");return;}
    setRows(current=>current.map(row=>target.some(item=>item.index===row.index)
      ? {...row,...(description?{description}:{}),...(count?{animals:count}:{})}:row));
    setSavedNumber("");setAnimalError("");
  }
  function openAnimalEdit(row:DraftRow){
    if(!row.selected){
      setError("Zaznacz pozycję do wydruku, aby wpisać wspólne dane zwierząt.");
      return;
    }
    const candidates=rows.filter(item=>item.selected &&
      (item.section==="III")===(row.section==="III") &&
      item.species.trim().toLocaleLowerCase("pl")===row.species.trim().toLocaleLowerCase("pl"));
    setError("");
    setAnimalEdit({candidates,checked:candidates.filter(item=>
      (item.descriptionGroup??"")===(row.descriptionGroup??"")).map(item=>item.index),
      description:row.description,count:row.animals});
  }
  function startSave(currentRows=rows){
    if(pendingSaveDates.current)return;
    const selected=currentRows.filter(row=>row.selected);
    if(!selected.length){
      setError("Zaznacz przynajmniej jedną pozycję w kolumnie Druk.");
      return;
    }
    const description=(row:DraftRow)=>!row.descriptionGroup && row.description.trim().toLocaleLowerCase("pl")===
      row.species.trim().toLocaleLowerCase("pl") ? "" : row.description.trim();
    const missing=selected.find(row=>!description(row)||!row.animals.trim());
    setError("");
    if(!missing){
      setDateStep({reported,performed});
      return;
    }
    const candidates=selected.filter(row=>row.species===missing.species && row.section===missing.section &&
      description(row)===description(missing) && row.animals.trim()===missing.animals.trim() &&
      row.descriptionGroup===missing.descriptionGroup && row.countGroup===missing.countGroup);
    setAnimalEdit({candidates,checked:candidates.map(row=>row.index),
      description:description(missing),count:missing.animals,beforeSave:true});
  }
  async function applyAnimalEdit(){
    if(!animalEdit||!draft||!animalEdit.checked.length)return;
    const description=animalEdit.description.trim(), animals=animalEdit.count.trim();
    if(!animals||(animalEdit.beforeSave&&!description))return;
    const group=window.crypto.randomUUID();
    const updated=rows.map(row=>animalEdit.checked.includes(row.index)
      ? {...row,description,animals,descriptionGroup:group,countGroup:group}:row);
    setRows(updated);
    setAnimalEdit(null);setSavedNumber("");setAnimalError("");
    if(animalEdit.beforeSave){startSave(updated);return;}
    if(!draft.hasPayerId||!description)return;
    try{
      await invoke("saveAnimalDescription",{draftId:draft.draftId,description});
      setAnimalDescriptions(current=>[description,...current.filter(value=>value!==description)].slice(0,100));
    }catch(error){setAnimalError(error instanceof Error?error.message:String(error));}
  }
  async function openReference(row:DraftRow){
    if(referenceBusy)return;
    setReferenceBusy(true);setError("");
    try{
      const {items}=await invoke<{items:{symbol:string;name:string}[]}>("treatmentReferences",{});
      setReferenceEdit({index:row.index,value:row.referenceSymbol??"",
        items:items.filter(item=>item.symbol.toUpperCase()!==row.symbol.trim().toUpperCase())});
    }catch(error){setError(error instanceof Error?error.message:String(error));}
    finally{setReferenceBusy(false);}
  }
  async function applyReference(){
    if(!draft||!referenceEdit||referenceBusy)return;
    const {index,value}=referenceEdit;
    const row=rows.find(item=>item.index===index);
    if(row && (row.referenceSymbol??"")===value && (value||!row.replacement)){
      setReferenceEdit(null);return;
    }
    setReferenceBusy(true);setError("");
    try{
      const {groups,...reference}=await invoke<{
        referenceSymbol:string;replacement:string;groups:SpeciesGroup[];
      }>("setTreatmentReference",{draftId:draft.draftId,index,referenceSymbol:value});
      setRows(current=>current.map(item=>item.index===index
        ? {...item,...reference,species:"",route:"",dosing:"",withdrawal:"",indication:""}:item));
      setCompatibility(current=>({...current,[index]:groups}));
      setSuggestTick(tick=>tick+1);
      setReferenceEdit(null);setSavedNumber("");
    }catch(error){setReferenceEdit(null);setError(error instanceof Error?error.message:String(error));}
    finally{setReferenceBusy(false);}
  }
  async function rememberAnimalDescription(){
    if(!draft?.hasPayerId||!groupDescription.trim()||animalBusy)return;
    setAnimalBusy(true);setAnimalError("");
    try{
      const description=groupDescription.trim();
      await invoke("saveAnimalDescription",{draftId:draft.draftId,description});
      setAnimalDescriptions(current=>[description,...current.filter(x=>x!==description)].slice(0,100));
    }catch(error){setAnimalError(error instanceof Error?error.message:String(error));}
    finally{setAnimalBusy(false);}
  }

  function changeRemember(next:boolean){
    setRemember(next);
    try{localStorage.setItem("ReactKL.rememberTreatmentChoices",String(next));}
    catch{/* Current session still works without storage. */}
  }
  useEffect(()=>()=>{if(variantPdfUrl)URL.revokeObjectURL(variantPdfUrl);},[variantPdfUrl]);
  function requestSave(dates:{reported:string;performed:string}) {
    const selected=rows.filter(row=>row.selected);
    if(!selected.length) {
      setError("Zaznacz przynajmniej jedną pozycję w kolumnie Druk. Jeśli filtr gatunku ukrywa dostępne pozycje, wybierz Wszystkie.");
      return;
    }
    if(!dates.performed){
      setError("Podaj datę i godzinę wykonania czynności weterynaryjnych.");
      return;
    }
    const warnings=selected.map(row=>{
      const issues:{message:string; field?:keyof DraftRow}[]=[];
      if(!row.batch.trim()) issues.push({message:"Nie wpisano serii",field:"batch"});
      if(!row.animals.trim()) issues.push({message:"Nie wpisano liczby zwierząt",field:"animals"});
      if(!row.withdrawal.trim()) issues.push({message:"Brak danych o karencji",field:"withdrawal"});
      const candidate=suggestions.find(item=>item.index===row.index);
      if(!candidate || ["review","ambiguous","notFound","noMatch","missingSymbol"].includes(candidate.status))
        issues.push({message:"Sprawdź wariant ChPL"});
      return {row,issues};
    }).filter(item=>item.issues.length);
    if(warnings.length)setSaveReview({dates,warnings});
    else void saveCard(dates);
  }
  async function saveCard(dates:{reported:string;performed:string},confirmDuplicate = false) {
    if (!draft || busy || applyBusy || selectionBusy) return;
    setBusy(true);setSavingSql(true);setError("");
    try {
      const result = await invoke<SaveResult>("saveDraft", {
        draftId:draft.draftId, ...dates, findings, confirmDuplicate, rows
      });
      if (result.duplicate) {
        if(result.duplicateId)setDuplicateId(result.duplicateId);
        else setError("Wykryto istniejącą kartę, ale brak jej identyfikatora w archiwum SQL.");
      } else {
        setSavedNumber(result.number ?? "");
        if (result.id) {
          setPreviewSavedId(result.id);
          setPdfMessage("");
          if(result.html)setPreview(result.html);
        }
      }
    } catch (e) {setError(e instanceof Error ? e.message : String(e));}
    finally {setSavingSql(false);setBusy(false);}
  }
  async function pdfFromPreview(mail:boolean) {
    if(!draft||pdfBusy)return;
    setPdfBusy(mail?"mail":"pdf");setPdfMessage("");
    try{
      const request=previewSavedId
        ? {id:previewSavedId,trash:false}
        : {draftId:draft.draftId,reported,performed,findings,rows};
      const result=await invoke<{cancelled:boolean;fileName:string;recipient:string}>(
        mail?"emailCardPdf":"saveCardPdf",request);
      if(!result.cancelled)setPdfMessage(mail
        ? "Przygotowano wiadomość z PDF „"+result.fileName+
          "”. Adres płatnika: "+(result.recipient||"brak — wprowadź ręcznie")+
          ". Przed wysłaniem sprawdź wiadomość w programie pocztowym."
        : "Zapisano plik PDF: "+result.fileName);
    }catch(e){setPdfMessage(e instanceof Error?e.message:String(e));}
    finally{setPdfBusy(null);}
  }
  async function printPreview() {
    if (!draft || busy || applyBusy || selectionBusy) return;
    setBusy(true);setError("");
    try {
      const data = await invoke<{html:string}>("previewDraft", {
        draftId:draft.draftId, reported, performed, findings, rows
      });
      setPreviewSavedId(null);setPdfMessage("");
      setPreview(data.html); setError("");
    } catch(e) {setError(e instanceof Error ? e.message : String(e));}
    finally {setBusy(false);}
  }
  const readySuggestions=suggestions.filter(s=>{
    if(suggestionGroup!==speciesGroup || s.status!=="ready" || s.variantId==null)return false;
    const row=rows.find(x=>x.index===s.index);
    return !!row && row.selected && (row.referenceSymbol||row.symbol).trim().toUpperCase()===s.symbol.toUpperCase() &&
      !row.dosing?.trim() && !row.withdrawal?.trim();
  });
  async function applySuggestions(candidates=readySuggestions, autoGroup?:SpeciesGroup){
    if(!draft || !candidates.length || applyBusy)return;
    setApplyBusy(true);setAutofillMessage("");setError("");
    try{
      const result=await invoke<TreatmentBulkResult>("applyTreatmentSuggestions",{
        draftId:draft.draftId, rows, speciesGroup, autoGroup,
        candidates:candidates.slice(0,300).map(s=>({index:s.index,variantId:s.variantId,
          species:s.species,route:s.route,dosing:s.dosing,withdrawal:s.withdrawal,
          source:s.source,hasPdf:s.hasPdf,pdfPath:s.pdfPath}))
      });
      const applied=new Map(result.applied.map(item=>[item.index,item]));
      setRows(current=>current.map(row=>{
        const item=applied.get(row.index);
        const original=rows.find(value=>value.index===row.index);
        return item ? {...row,...item,description:row.description===original?.description &&
          !row.descriptionGroup?item.description:row.description} : row;
      }));
      setSuggestReviewOpen(false);
      setAutofillMessage(result.applied.length+
        (autoGroup?" jednoznacznych wariantów uzupełniono automatycznie. ":" jednoznacznych wariantów wstawiono do edytora. ")+
        "Sprawdź dawkowanie i karencję przed zapisem karty."+
        (result.skipped.length?" Pominięto: "+result.skipped.join(" "):""));
      setSuggestTick(value=>value+1);
      setSavedNumber("");
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setApplyBusy(false);}
  }

  useEffect(()=>{
    if(!draft||compatibilityDraftId!==draft.draftId||suggestBusy||selectionBusy||
      applyBusy||busy||variantBusy||referenceBusy||suggestReviewOpen||saveReview||dateStep||pendingSaveDates.current||animalEdit?.beforeSave)return;
    const candidates=readySuggestions.filter(item=>{
      const key=JSON.stringify([item.index,item.symbol,item.variantId]);
      return !autoApplied.current.has(key) &&
        (speciesGroup==="all"||item.groups?.includes(speciesGroup));
    }).slice(0,300);
    if(!candidates.length)return;
    for(const item of candidates)
      autoApplied.current.add(JSON.stringify([item.index,item.symbol,item.variantId]));
    void applySuggestions(candidates,speciesGroup);
  });

  async function openVariants(row:DraftRow){
    if(!draft || variantBusy || applyBusy)return;
    setVariantBusy(true);setVariantError("");setError("");
    try{
      const found=await invoke<TreatmentVariantList>("treatmentVariants",{
        draftId:draft.draftId,index:row.index
      });
      if(!found.variants.length)throw new Error(
        "W katalogu SQL brak wariantów dla tego produktu. Sprawdź jego kartotekę.");
      setVariantModal({index:row.index,product:found.product,
        symbol:found.symbol,variants:found.variants});
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setVariantBusy(false);}
  }
  async function viewVariantPdf(path:string){
    if(!path||variantBusy)return;
    setVariantBusy(true);setVariantError("");
    try{
      const pdf=await invoke<{base64:string;name:string}>("readCataloguePdf",{path});
      const binary=atob(pdf.base64);
      const bytes=new Uint8Array(binary.length);
      for(let i=0;i<binary.length;i++)bytes[i]=binary.charCodeAt(i);
      const url=URL.createObjectURL(new Blob([bytes],{type:"application/pdf"}));
      setVariantPdfName(pdf.name);
      setVariantPdfUrl(url);
    }catch(e){setVariantError(e instanceof Error?e.message:String(e));}
    finally{setVariantBusy(false);}
  }
  async function applyVariant(id:number){
    if(!draft || !variantModal || variantBusy)return;
    setVariantBusy(true);setVariantError("");
    try{
      const result=await invoke<Pick<DraftRow,"species"|"route"|"dosing"|"withdrawal"|"indication"|"description">>(
        "applyTreatmentVariant",{draftId:draft.draftId,
          index:variantModal.index,variantId:id,remember});
      if(remember && !rows.find(row=>row.index===variantModal.index)?.referenceSymbol){
        const chosen=variantModal.variants.find(item=>item.id===id);
        if(chosen){
          const symbol=variantModal.symbol.trim().toUpperCase();
          setSavedChoices(current=>({...current,[symbol]:{
            symbol,species:chosen.species,route:chosen.route
          }}));
        }
      }
      setRows(current=>current.map(row=>row.index===variantModal.index
        ? {...row,...result,description:row.descriptionGroup?row.description:result.description} : row));
      setVariantModal(null);
      setSuggestions(current=>current.map(s=>s.index===variantModal.index
        ? {...s,status:"existingData",message:"Wariant wybrany ręcznie."} : s));
      setSavedNumber("");
      setError(!result.withdrawal?.trim()
        ? "Wybrany wariant nie zawiera pełnej karencji w katalogu SQL. Zweryfikuj ChPL i uzupełnij karencję ręcznie przed zapisem karty, jeżeli jest wymagana."
        : "");
    }catch(e){setVariantError(e instanceof Error?e.message:String(e));}
    finally{setVariantBusy(false);}
  }
  const variantColumns:ColumnsType<TreatmentVariantChoice> = [
          {title:"Gatunek",dataIndex:"species",width:170,
            render:(species:string,row:TreatmentVariantChoice)=>
              <Space direction="vertical" size={2}>
                <Button type="primary" size="small" disabled={variantBusy ||
                  !species?.trim() || !row.route?.trim() || !row.dose?.trim()}
                  style={{whiteSpace:"normal",height:"auto",textAlign:"left"}}
                  onClick={()=>{void applyVariant(row.id);}}>{species?.trim() || "Brak gatunku"}</Button>
                {remember && variantModal?.symbol &&
                  savedChoices[variantModal.symbol.trim().toUpperCase()]?.species===species &&
                  savedChoices[variantModal.symbol.trim().toUpperCase()]?.route===row.route &&
                  <Tag color="blue">Ostatni wybór</Tag>}
              </Space>},
          {title:"Droga podania",dataIndex:"route",width:140},
          {title:"Dawkowanie",dataIndex:"dose",width:230,ellipsis:true},
          {title:"Karencja — tkanki",dataIndex:"tissues",width:180,ellipsis:true},
          {title:"Karencja — mleko",dataIndex:"milk",width:145,ellipsis:true,
            render:(value:string,row)=>isPoultry(row.species) ? "—" : value},
          {title:"Karencja — jaja",dataIndex:"eggs",width:145,ellipsis:true},
          {title:"Wskazanie",dataIndex:"indication",width:225,ellipsis:true},
          {title:"PDF ChPL",dataIndex:"pdf",width:130,
            render:(value:string)=>value
              ? <Button size="small" disabled={variantBusy}
                  onClick={()=>{void viewVariantPdf(value);}}>Otwórz ChPL</Button>
              : "Brak"}
        ];
  const variantLayout=useListColumnLayout("treatment-variant",variantColumns,false,"species");
  const variantsPagination=useListPagination("treatment-variants",variantModal?.variants.length??0,12);
  if (!draft) return <Card>
    <Empty description="Wybierz dokument w zakładce Dokumenty">
      <Button type="primary" onClick={onDocuments}>Przejdź do dokumentów</Button>
    </Empty></Card>;
  const update = async (index: number, key: keyof DraftRow, value: string | boolean) => {
    const field = key === "selected" ? "Drukuj" : editable.find(x => x.key === key)?.field;
    if(key==="selected" && typeof value==="boolean")
      desiredChecks.current.set(index,value);
    if (!field || applyBusy) return;
    setRows(current => current.map(row => row.index === index ? {...row,[key]:value} : row));
    if(["species","route","dosing","withdrawal"].includes(String(key))){
      setSuggestions(current=>current.map(s=>s.index===index
        ? {...s,status:"existingData",message:"Zmieniono dane w edytorze — sprawdź wariant ręcznie."} : s));
    }
    setSavedNumber("");
    try { await invoke("updateDraft", {draftId:draft.draftId,index,field,
      ...(key === "selected" ? {checked:value} : {value})}); }
    catch (e) {
      setError("Nie udało się zsynchronizować pola. Wpis zachowano w edytorze: "+
        (e instanceof Error ? e.message : String(e)));
    }
  };
  const isCompatible=(row:DraftRow)=>speciesGroup==="all" ||
    (compatibility[row.index]??[]).includes(speciesGroup);
  const rowSuggestions=new Map((suggestionGroup===speciesGroup?suggestions:[]).map(item=>[item.index,item]));
  const isUnambiguous=(row:DraftRow)=>{
    const item=rowSuggestions.get(row.index);
    return !!item && !item.nonMedicine && item.status!=="review" &&
      (item.status==="ready"||item.variantCount===1);
  };
  const sortedRows=rows.slice().sort((a,b)=>
    Number(isUnambiguous(a))-Number(isUnambiguous(b))||
    Number(isCompatible(b))-Number(isCompatible(a))||a.index-b.index);
  const columns: ColumnsType<DraftRow> = [
    {title:"Druk",dataIndex:"selected",width:63,render:(value:boolean,row) =>
      <Checkbox checked={value}
        disabled={applyBusy||selectionBusy||!isCompatible(row)}
        title={!isCompatible(row)?"Brak wariantu ChPL dla wybranego gatunku":undefined}
        onChange={e => {void update(row.index,"selected",e.target.checked);}}/>},
    {title:"Produkt",dataIndex:"product",width:240,ellipsis:true,fixed:"left",
      render:(value:string,row:DraftRow)=><span title={value}>
        {value}{row.replacement?.trim()&&<><br/><Typography.Text type="secondary">
          Zamiennik: {row.replacement}</Typography.Text></>}
      </span>},
    {title:"Wariant ChPL",key:"variant",width:150,
      render:(_value:unknown,row:DraftRow)=>{
        const item=rowSuggestions.get(row.index);
        if(!item)return "—";
        const label=item.nonMedicine?"To nie lek":
          item.variantCount===0?"Brak":
          item.status==="review"?"Do uzupełnienia":
          isUnambiguous(row)?"Jednoznaczny":"Kilka wariantów";
        const color=label==="Jednoznaczny"?"green":
          item.variantCount>0?"gold":"default";
        if(item.nonMedicine||!item.hasCatalogueVariants)
          return <Tag title={item.nonMedicine?"Produkt oznaczony w katalogu jako nieleczniczy":item.message}>{label}</Tag>;
        return <Button size="small" variant="outlined" color={color}
          title={item.message} disabled={variantBusy||applyBusy||referenceBusy}
          onClick={()=>{void openVariants(row);}}>{label}</Button>;
      }},
    {title:"Ilość",dataIndex:"quantity",width:100},
    {title:"Wskazanie z ChPL",dataIndex:"indication",width:220,ellipsis:true,
      render:(value:string)=><span title={value}>{value||"—"}</span>},
    ...editable.map(({key,title}) => ({
      title, dataIndex:key, width:key==="section" ? 150 : key==="dosing" || key==="withdrawal" ? 245 : 170,
      render:(value:string,row:DraftRow) => key==="replacement"
        ? <Button size="small" disabled={applyBusy||busy||referenceBusy} title={value}
            onClick={()=>{void openReference(row);}}>{value||"Wybierz…"}</Button>
        : key==="section"
        ? <Checkbox aria-label={"Nabycie produktu — "+row.product}
            title="Potwierdzenie nabycia produktu (sekcja III)"
            checked={value==="III"} disabled={applyBusy||busy}
            onChange={event=>{void update(row.index,"section",event.target.checked?"III":"II");}}/>
        : <Input.TextArea autoSize={{minRows:1,maxRows:3}}
        aria-label={title} data-treatment-field={key} value={value}
        title={key==="description"?"Dwuklik otwiera dane zwierząt":undefined}
        onDoubleClick={key==="description"?()=>openAnimalEdit(row):undefined}
        onKeyDown={key==="description"?event=>{
          if(event.key==="F4"){event.preventDefault();openAnimalEdit(row);}
        }:undefined} disabled={applyBusy||busy}
        onChange={e => {
          const next=e.target.value;
          setRows(current => current.map(item => item.index===row.index ? {...item,[key]:next} : item));
        }}
        onBlur={e => {void update(row.index,key,e.target.value);}}/>
    }))
  ];
  return <>
    <div ref={treatmentControls} className="treatment-controls">
    <div className="page-heading"><div>
      <Typography.Title level={3}>Karta leczenia — {draft.number}</Typography.Title>
      <Typography.Text type="secondary">{draft.payer.split("\n")[0]}</Typography.Text>
    </div><Space>
      <Button onClick={onDocuments}>Dokumenty</Button>
      <Button onClick={()=>setShowColumns(true)}>Kolumny · Ctrl+Shift+L</Button>
      <Button onClick={()=>{void printPreview();}} loading={busy&&!savingSql} disabled={busy||applyBusy||selectionBusy}>Podgląd wydruku</Button>
      <Button type="primary" onClick={()=>startSave()} loading={savingSql} disabled={busy||applyBusy||selectionBusy||referenceBusy}>Zapisz kartę do SQL</Button>
    </Space></div>
    <div className="treatment-tools">
    <TreatmentOptions compact={compact} label={"Podpowiedzi ChPL ("+readySuggestions.length+")"}>
    <Card className="treatment-autofill">
      <Space wrap>
        <Typography.Text strong>Podpowiedzi z katalogu SQL</Typography.Text>
        <Typography.Text type="secondary">
          {suggestBusy?"Sprawdzam symbole produktów...":
            readySuggestions.length+" jednoznacznych wariantów do uzupełnienia"}
        </Typography.Text>
        <Button loading={suggestBusy} disabled={applyBusy}
          onClick={()=>setSuggestTick(v=>v+1)}>Odśwież propozycje</Button>
        <Button type="primary" disabled={!readySuggestions.length||suggestBusy||applyBusy}
          onClick={()=>setSuggestReviewOpen(true)}>
          Przejrzyj i uzupełnij ({readySuggestions.length})
        </Button>
      </Space>
      <Typography.Text type="secondary">
        Warianty niejednoznaczne, bez potwierdzonej ChPL albo z już wpisanymi danymi
        wymagają ręcznego wyboru. Nic nie jest automatycznie zapisywane do SQL.
      </Typography.Text>
      {suggestError&&<Alert type="warning" showIcon className="notice"
        message={"Nie udało się pobrać propozycji: "+suggestError}/>}
      {autofillMessage&&<Alert type="info" showIcon className="notice"
        message={autofillMessage}/>}
    </Card>
    </TreatmentOptions>
    <Card className="treatment-species">
      <Space wrap>
        <Typography.Text strong>Filtr gatunku</Typography.Text>
        <Select value={speciesGroup} style={{minWidth:210}}
          disabled={suggestBusy||selectionBusy||applyBusy}
          options={speciesGroups} onChange={value=>setSpeciesGroup(value)}/>
        <Checkbox checked={remember} onChange={event=>changeRemember(event.target.checked)}>
          Zapamiętuj gatunki i drogi podania w SQL
        </Checkbox>
        <Typography.Text type="secondary">
          Niezgodne leki są odznaczane i przenoszone na koniec. Poprzedni wybór wraca po zmianie filtra.
        </Typography.Text>
      </Space>
      {choiceError&&<Alert type="warning" className="notice" showIcon message={choiceError}/>}
    </Card>
    <TreatmentOptions compact={compact} label="Wspólny opis i liczba zwierząt">
    <Card className="treatment-animals">
      <Typography.Text strong>Wspólny opis i liczba zwierząt</Typography.Text>
      <Typography.Paragraph type="secondary">
        Uzupełnij zaznaczone pozycje tego samego gatunku i sekcji. Nie zmienia to
        dawkowania, karencji ani wyboru wariantu ChPL.
      </Typography.Paragraph>
      <Space wrap>
        <Select style={{minWidth:170}} value={groupSpecies||undefined}
          placeholder="Wybierz gatunek" onChange={setGroupSpecies}
          options={groupSpeciesOptions.map(value=>({value,label:value}))}/>
        <Select value={groupSection} onChange={setGroupSection}
          options={[{value:"II",label:"Sekcja II"},{value:"III",label:"Sekcja III"}]}/>
        <Select showSearch allowClear style={{minWidth:260}} value={groupDescription||undefined}
          placeholder="Opis / numery zwierząt" onChange={value=>setGroupDescription(value??"")}
          options={animalDescriptions.map(value=>({value,label:value}))}
          notFoundContent="Brak zapamiętanych opisów"/>
        <Input style={{width:170}} value={groupDescription}
          placeholder="Nowy opis" onChange={e=>setGroupDescription(e.target.value)}/>
        <Input style={{width:145}} value={groupCount}
          placeholder="Liczba zwierząt" onChange={e=>setGroupCount(e.target.value)}/>
        <Button onClick={applyGroupAnimals} disabled={!groupSpecies||applyBusy||selectionBusy}>
          Zastosuj do grupy
        </Button>
        <Button loading={animalBusy} disabled={!draft.hasPayerId||!groupDescription.trim()}
          onClick={()=>{void rememberAnimalDescription();}}>Zapamiętaj opis w SQL</Button>
      </Space>
      {animalError&&<Alert className="notice" type="warning" showIcon message={animalError}/>}
      {!draft.hasPayerId&&<Typography.Text type="secondary">
        Zapamiętywanie wymaga płatnika z identyfikatorem Subiekta.
      </Typography.Text>}
    </Card>
    </TreatmentOptions>
    </div>
    <Card className="treatment-meta">
      <Space wrap>
        <label>Wyniki badań <Input value={findings} onChange={e=>setFindings(e.target.value)} style={{width:340}}/></label>
      </Space>
    </Card>
    {savedNumber && <Alert className="notice" type="success" showIcon message={"Zapisano kartę "+savedNumber}/>}
    <ErrorNotice error={error}/>
    </div>
    <Card className="treatment-rows"><Table<DraftRow> {...treatmentMouse.tableProps} rowKey="index" dataSource={sortedRows}
      columns={treatmentFormatting.apply(treatmentMouse.decorate(
        columns.filter(column=>visibleColumns.includes(treatmentColumnKey(column)))
        .sort((a,b)=>columnOrder.indexOf(treatmentColumnKey(a))-
          columnOrder.indexOf(treatmentColumnKey(b)))
        .map(column=>({...column,width:columnWidths[treatmentColumnKey(column)]??column.width})), treatmentColumnKey,
        (key,width)=>setColumnWidths(previous=>({...previous,[key]:width})),
        (source,target)=>setColumnOrder(previous=>reorderColumn(previous,source,target)),
        60, 800, ["selected","product"]), treatmentColumnKey)} size="small"
      rowClassName={row=>isCompatible(row)?"":"ant-table-row-disabled"}
      scroll={{x:1900,y:compact?tableHeight:"calc(100vh - 415px)"}} pagination={false}/>
      <Typography.Text type="secondary">Wybranych do wydruku: {rows.filter(r => r.selected).length} z {rows.length}.
        {speciesGroup!=="all"&&" Niezgodnych: "+rows.filter(row=>!isCompatible(row)).length+"."}</Typography.Text>
    </Card>
    <Modal className="kl-dialog-treatment-reference" open={referenceEdit!==null}
      title="Zamiennik produktu" okText="Wybierz" cancelText="Anuluj"
      confirmLoading={referenceBusy} onOk={()=>{void applyReference();}}
      onCancel={()=>{if(!referenceBusy)setReferenceEdit(null);}}>
      {referenceEdit&&<Select showSearch aria-label="Zamiennik produktu"
        style={{width:"100%"}} value={referenceEdit.value}
        onChange={value=>setReferenceEdit({...referenceEdit,value})}
        filterOption={(input,option)=>input.toLocaleLowerCase("pl").split(/[\s-]+/).every(word=>
          String(option?.label??"").toLocaleLowerCase("pl").includes(word))}
        options={[{value:"",label:"Preparat z dokumentu"},...referenceEdit.items.map(item=>({
          value:item.symbol,label:item.name+" · "+item.symbol
        }))]}/>}
    </Modal>
    <Modal className="kl-dialog-treatment-animals" open={animalEdit!==null}
      title="Numery i liczba leczonych zwierząt" okText="Zastosuj" cancelText="Anuluj"
      okButtonProps={{disabled:!animalEdit?.checked.length||!animalEdit.count.trim()||
        (animalEdit.beforeSave&&!animalEdit.description.trim())}} onOk={()=>{void applyAnimalEdit();}}
      onCancel={()=>setAnimalEdit(null)}>
      {animalEdit&&<Space direction="vertical" style={{width:"100%"}}>
        {animalEdit.candidates.length>1&&<Checkbox.Group value={animalEdit.checked}
          onChange={checked=>setAnimalEdit({...animalEdit,checked})}>
          <Space direction="vertical">{animalEdit.candidates.map(item=>
            <Checkbox key={item.index} value={item.index}>
              {item.index+1}. {item.product}{item.batch&&" · "+item.batch}
            </Checkbox>)}</Space>
        </Checkbox.Group>}
        <label>Numery zwierząt / opis grupy <Input.TextArea required={animalEdit.beforeSave} rows={4} value={animalEdit.description}
          onChange={e=>setAnimalEdit({...animalEdit,description:e.target.value})}/></label>
        <label>Liczba leczonych zwierząt (wymagane) <Input required value={animalEdit.count}
          onChange={e=>setAnimalEdit({...animalEdit,count:e.target.value})}/></label>
      </Space>}
    </Modal>
    <Modal className="kl-dialog-treatment-dates" open={dateStep!==null}
      title="Daty i godziny karty leczenia" onCancel={()=>setDateStep(null)}
      afterClose={()=>{
        const dates=pendingSaveDates.current;
        pendingSaveDates.current=null;
        if(dates)requestSave(dates);
      }}
      footer={<Space wrap>
        <Button onClick={()=>setDateStep(null)}>Anuluj</Button>
        <Button type="primary" disabled={!dateStep?.performed} onClick={()=>{
          if(!dateStep?.performed||pendingSaveDates.current)return;
          pendingSaveDates.current=dateStep;
          setReported(dateStep.reported);setPerformed(dateStep.performed);
          setDateStep(null);
        }}>Zatwierdź daty i kontynuuj</Button>
      </Space>}>
      {dateStep&&<Space direction="vertical" style={{width:"100%"}}>
        <label>Data i godzina zgłoszenia (opcjonalnie)
          <DatePicker value={dateStep.reported?dayjs(dateStep.reported).locale("pl"):null}
            showTime={{format:"HH:mm"}} needConfirm format="DD.MM.YYYY HH:mm"
            locale={{...datePickerPl,lang:{...datePickerPl.lang,ok:"Zatwierdź"}}}
            style={{width:"100%"}}
            onChange={value=>setDateStep(current=>current&&{
              ...current,reported:value?.format("YYYY-MM-DDTHH:mm")??""
            })}/></label>
        <label>Data i godzina wykonania czynności weterynaryjnych
          <DatePicker value={dateStep.performed?dayjs(dateStep.performed).locale("pl"):null}
            showTime={{format:"HH:mm"}} needConfirm allowClear={false} format="DD.MM.YYYY HH:mm"
            locale={{...datePickerPl,lang:{...datePickerPl.lang,ok:"Zatwierdź"}}}
            style={{width:"100%"}}
            onChange={value=>setDateStep(current=>current&&{
              ...current,performed:value?.format("YYYY-MM-DDTHH:mm")??""
            })}/></label>
      </Space>}
    </Modal>
    <Modal className="kl-dialog-treatment-warnings" open={saveReview!==null}
      title="Sprawdź dane przed zapisem" width={700}
      okText="Potwierdzam — zapisz kartę" cancelText="Anuluj"
      onCancel={()=>setSaveReview(null)} onOk={()=>{
        if(!saveReview)return;
        const {dates}=saveReview;
        setSaveReview(null);
        void saveCard(dates);
      }}>
      <Alert className="notice" type="warning" showIcon
        message="Sprawdź wymienione pozycje przed kontynuowaniem."/>
      <div style={{maxHeight:350,overflowY:"auto",marginTop:12}}>
        {saveReview?.warnings.map(item=><div key={item.row.index}
          style={{padding:"9px 0",borderBottom:"1px solid var(--ant-color-border-secondary, #ddd)"}}>
          <Typography.Text strong>{item.row.index+1}. {item.row.product||"Bez nazwy produktu"}</Typography.Text>
          <ul style={{margin:"5px 0",paddingLeft:20}}>
            {item.issues.map(issue=><li key={issue.message}>{issue.message}</li>)}
          </ul>
          <Button type="link" size="small" onClick={()=>{
            setSaveReview(null);
            const field=item.issues.find(issue=>issue.field)?.field;
            if(field)setVisibleColumns(current=>current.includes(field)?current:[...current,field]);
            window.setTimeout(()=>{
              const row=document.querySelector('.treatment-rows tr[data-row-key="'+item.row.index+'"]');
              const input=field?row?.querySelector<HTMLElement>('[data-treatment-field="'+field+'"]'):null;
              (input??row)?.scrollIntoView({behavior:"smooth",block:"center",inline:"nearest"});
              input?.focus({preventScroll:true});
            },250);
          }}>Przejdź do pozycji</Button>
        </div>)}
      </div>
    </Modal>
    <Modal className="kl-dialog-treatment-duplicate" open={duplicateId!==null}
      title="Karta z tego dokumentu już istnieje" onCancel={()=>setDuplicateId(null)}
      footer={<Space wrap>
        <Button onClick={()=>setDuplicateId(null)}>Anuluj</Button>
        <Button onClick={()=>{
          const id=duplicateId;setDuplicateId(null);
          if(id)onOpenSaved(id);
        }}>Otwórz / zaktualizuj istniejącą</Button>
        <Button type="primary" onClick={()=>{setDuplicateId(null);void saveCard({reported,performed},true);}}>
          Zapisz kolejną kartę
        </Button>
      </Space>}>
      <Typography.Paragraph>
        Możesz otworzyć dotychczasową kartę i poprawić jej treść bez zmiany numeru,
        zapisać kolejną kartę z nowym numerem lub anulować operację.
      </Typography.Paragraph>
    </Modal>
    <Modal className="kl-dialog-treatment-columns" open={showColumns} title="Parametry listy — karta leczenia"
      onCancel={()=>setShowColumns(false)} onOk={()=>setShowColumns(false)}
      okText="Gotowe" cancelButtonProps={{style:{display:"none"}}}>
      <Tabs items={[
        {key:"columns",label:"Kolumny",children:<>
      <Typography.Paragraph type="secondary">
        Wybierz widoczne kolumny. Produkt i zaznaczenie do wydruku pozostają zawsze dostępne.
        Ustawienie jest zapamiętywane na tym stanowisku.
      </Typography.Paragraph>
      <Checkbox.Group style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:8}}
        value={visibleColumns} options={treatmentColumns.map(option=>({
          ...option,disabled:option.value==="selected"||option.value==="product"
        }))}
        onChange={values=>setVisibleColumns(Array.from(new Set([
          ...values.map(String),"selected","product"
        ])))}/>
      <Typography.Paragraph style={{marginTop:14}} type="secondary">
        Kolejność i szerokość kolumn (piksele). „Druk” i „Produkt” są przypięte na początku.
      </Typography.Paragraph>
      <div style={{maxHeight:300,overflowY:"auto"}}>
        {columnOrder.filter(key=>visibleColumns.includes(key)).map(key=>{
          const index=columnOrder.indexOf(key);
          const label=treatmentColumns.find(item=>item.value===key)?.label??key;
          const defaultWidth=columns.find(col=>treatmentColumnKey(col)===key)?.width;
          return <div key={key} style={{display:"flex",alignItems:"center",gap:8,marginBottom:6}}>
            <span style={{flex:1}}>{label}</span>
            <InputNumber aria-label={"Szerokość kolumny "+label} min={60} max={800}
              style={{width:105}} value={columnWidths[key]??Number(defaultWidth??170)}
              onChange={value=>{if(value!=null)setColumnWidths(current=>({
                ...current,[key]:Math.max(60,Math.min(800,value))
              }));}}/>
            <Button size="small" aria-label={"Przesuń kolumnę "+label+" w lewo"}
              disabled={index<=2} onClick={()=>moveColumn(key,-1)}>←</Button>
            <Button size="small" aria-label={"Przesuń kolumnę "+label+" w prawo"}
              disabled={index<2||index>=columnOrder.length-1}
              onClick={()=>moveColumn(key,1)}>→</Button>
          </div>;
        })}
      </div>
      <Button size="small" style={{marginTop:16}}
        onClick={()=>{
          setVisibleColumns(treatmentColumnKeys);
          setColumnOrder(treatmentColumnKeys);
          setColumnWidths({});
          treatmentMouse.resetHeight();
        }}>Przywróć domyślne kolumny</Button>
        </>},
        {key:"format",label:"Formatowanie",children:<ListFormattingPanel formatting={treatmentFormatting}
          columns={treatmentColumns.map(({value,label})=>({key:value,label}))}/>}
      ]}/>
    </Modal>
    <Modal className="kl-dialog-treatment-review" width="90vw" open={suggestReviewOpen}
      title="Potwierdź uzupełnienie jednoznacznych wariantów ChPL"
      onCancel={()=>{if(!applyBusy)setSuggestReviewOpen(false);}}
      footer={<Space>
        <Button disabled={applyBusy} onClick={()=>setSuggestReviewOpen(false)}>Anuluj</Button>
        <Button type="primary" loading={applyBusy}
          disabled={!readySuggestions.length}
          onClick={()=>{void applySuggestions();}}>
          Zastosuj do edytora ({readySuggestions.length})
        </Button>
      </Space>}>
      <ErrorNotice error={variantError}/>
      <Alert type="warning" className="notice" showIcon
        message="Sprawdź każdą dawkę i karencję przed zatwierdzeniem"
        description="Dopasowanie następuje wyłącznie po symbolu Subiekta. Wpisy ręczne nie będą nadpisywane, a zapis karty w SQL pozostaje oddzielną czynnością."/>
      <Table<TreatmentSuggestion> rowKey="index" size="small"
        dataSource={readySuggestions} pagination={false}
        scroll={{x:1160,y:350}}
        columns={[
          {title:"Produkt",width:240,render:(_v,item)=>
            rows.find(x=>x.index===item.index)?.product||item.symbol},
          {title:"Gatunek",dataIndex:"species",width:120},
          {title:"Droga",dataIndex:"route",width:100},
          {title:"Dawkowanie",dataIndex:"dosing",width:260},
          {title:"Karencja",dataIndex:"withdrawal",width:270},
          {title:"Źródło",dataIndex:"source",width:155},
          {title:"PDF ChPL",dataIndex:"pdfPath",width:155,
            render:(path:string)=>path
              ? <Button size="small" disabled={variantBusy}
                  onClick={()=>{void viewVariantPdf(path);}}>Otwórz ChPL</Button>
              : "Brak"}
        ]}/>
    </Modal>
    <Modal className="kl-dialog-treatment-variant" width="90vw" open={variantModal!==null}
      title={"Dobór wariantu leku — "+(variantModal?.product||"")}
      onCancel={()=>{if(!variantBusy)setVariantModal(null);}}
      footer={<Button disabled={variantBusy} onClick={()=>setVariantModal(null)}>
        Anuluj
      </Button>}>
      <Alert className="notice" type="warning" showIcon
        message="Zweryfikuj gatunek, dawkowanie i karencję z aktualną urzędową ChPL."
        description="Wybór zastąpi dotychczasowy gatunek, drogę podania i dawkowanie; pełną karencję zmieni tylko, gdy katalog zawiera jej dane. Nic nie jest automatycznie zapisywane do archiwum kart."/>
      {remember&&variantModal?.symbol&&savedChoices[variantModal.symbol.trim().toUpperCase()]&&
        <Alert className="notice" type="info" showIcon
          message={"Ostatnio wybrano: "+
            savedChoices[variantModal.symbol.trim().toUpperCase()].species+" / "+
            savedChoices[variantModal.symbol.trim().toUpperCase()].route}
          description="Zapamiętany wybór to wskazówka. Potwierdź konkretny aktualny wariant ChPL, aby wypełnić kartę."/>}
      <ErrorNotice error={variantError}/>
      <Button size="small" onClick={variantLayout.openColumns}>Kolumny wariantów</Button>
      <Table<TreatmentVariantChoice> {...variantLayout.tableProps} rowKey="id" loading={variantBusy}
        dataSource={variantModal?.variants??[]} size="small"
        pagination={variantsPagination} scroll={{x:"max-content",y:340}}
        columns={variantLayout.columns}/>
    </Modal>
    {variantLayout.dialog}
    <Modal className="kl-dialog-treatment-pdf" width="94vw" open={variantPdfUrl!==null}
      title={"Dokument ChPL — "+variantPdfName}
      onCancel={()=>setVariantPdfUrl(null)}
      footer={<Button onClick={()=>setVariantPdfUrl(null)}>Zamknij dokument</Button>}>
      {variantPdfUrl&&<iframe src={variantPdfUrl} title="ChPL wybranego wariantu"
        referrerPolicy="no-referrer"
        style={{width:"100%",height:"76vh",border:0,background:"#fff"}}/>}
    </Modal>
    <Modal className="kl-dialog-treatment-preview" width="92vw" open={preview!==null} title="Podgląd karty leczenia" onCancel={()=>setPreview(null)}
      footer={<Space wrap>
        <Button onClick={()=>setPreview(null)}>Zamknij</Button>
        <Button loading={pdfBusy==="pdf"} disabled={!!pdfBusy}
          onClick={()=>{void pdfFromPreview(false);}}>Zapisz PDF</Button>
        <Button loading={pdfBusy==="mail"} disabled={!!pdfBusy}
          onClick={()=>{void pdfFromPreview(true);}}>Wyślij mailem</Button>
        <Button type="primary" onClick={()=>previewFrame.current?.contentWindow?.print()}>Drukuj</Button>
      </Space>}>
      {pdfMessage&&<Alert className="notice" type="info" showIcon message={pdfMessage}/>}
      {preview && <iframe ref={previewFrame} title="Podgląd karty leczenia" srcDoc={preview}
        style={{width:"100%",height:"72vh",border:0,background:"#fff"}}/>}
    </Modal>
  </>;
}

function Prices({onCatalogue}: {onCatalogue: (symbol: string, direct?: boolean) => void}) {
  const [activePriceSymbol,setActivePriceSymbol]=useState("");
  const levels=useRemote<PriceLevel[]>("priceLevels",{},[]);
  const [level,setLevel]=useState<number|null>(null);
  const [net,setNet]=useState(false);
  useEffect(()=>{
    if(level!==null||!levels.data?.length)return;
    const retail=levels.data.find(item=>item.name.toLocaleLowerCase("pl").includes("detaliczna"));
    setLevel((retail??levels.data[0]).number);
  },[levels.data,level]);
  // The C# bridge calculates the six catalogue withdrawal formats from one
  // cached SQL snapshot; switching display formats below is client-only.
  const prices=useRemote<PricePrintRow[]>("prices",{level,net},[level,net],level!==null);
  const [settings,setSettings]=useState<PricePrintOptions>(defaultPricePrint);
  const printSettingsWrites=useRef<Promise<unknown>>(Promise.resolve());
  const [settingsBusy,setSettingsBusy]=useState(true);
  const [settingsError,setSettingsError]=useState("");
  const [showSettings,setShowSettings]=useState(false);
  const [showPreview,setShowPreview]=useState(false);
  const [filter,setFilter]=useState("");
  const [onlyMissing,setOnlyMissing]=useState(false);
  const [excluded,setExcluded]=useState<Set<string>>(()=>new Set());
  useEffect(()=>{
    let live=true;
    void invoke<PricePrintOptions>("getPricePrintSettings")
      .then(data=>{if(live)setSettings(normalizePriceSettings(data));})
      .catch(error=>{if(live)setSettingsError(error instanceof Error?error.message:String(error));})
      .finally(()=>{if(live)setSettingsBusy(false);});
    return()=>{live=false;};
  },[]);
  const all=prices.data??[];
  const q=filter.trim().toLocaleLowerCase("pl");
  const rows=all.filter(row=>
    (!q||row.name.toLocaleLowerCase("pl").includes(q)||row.symbol.toLocaleLowerCase("pl").includes(q))
    &&(!onlyMissing||row.needsCompletion));
  const selected=rows.filter(row=>!excluded.has(row.symbol));
  const missingCount=all.filter(row=>row.needsCompletion).length;
  const toggle=(symbol:string,value:boolean)=>setExcluded(current=>{
    const next=new Set(current);
    if(value)next.delete(symbol);else next.add(symbol);
    return next;
  });
  function changeFormat(patch:Partial<PricePrintOptions>){
    const next={...settings,...patch};
    setSettings(next);
    // Chain writes so rapid changes cannot be persisted in reverse order.
    // This does not access either database: settings are per-station JSON.
    printSettingsWrites.current=printSettingsWrites.current.catch(()=>{}).then(()=>
      invoke("savePricePrintSettings",{settings:next})
    ).catch(error=>setSettingsError(error instanceof Error?error.message:String(error)));
  }
  const priceHeading=net?"Cena netto":"Cena brutto";
  const columns:ColumnsType<PricePrintRow>=[
    {key:"selected",title:"Druk",width:65,render:(_value,row)=>
      <Checkbox checked={!excluded.has(row.symbol)}
        onChange={e=>toggle(row.symbol,e.target.checked)}/>},
    {title:"Lek",dataIndex:"name",width:260,
      sorter:(a,b)=>a.name.localeCompare(b.name,"pl")},
    {title:priceHeading,dataIndex:"gross",width:140,align:"right",
      render:(n:number|null)=>currency(n)},
    ...(settings.batch?[{title:"Ostatnia seria",dataIndex:"latestBatch",
      width:140} as ColumnsType<PricePrintRow>[number]]:[]),
    {title:"Karencja z katalogu",key:"withdrawal",width:440,ellipsis:true,
      render:(_value,row)=> {
        const text=withdrawalText(row,settings);
        return <span title={text||"Brak karencji w wybranym formacie katalogu SQL"}>
          {text||<Typography.Text type="secondary">—</Typography.Text>}
        </span>;
      }},
    {title:"Katalog",key:"status",width:135,
      render:(_value,row)=>row.needsCompletion
        ? <Button size="small" onClick={()=>onCatalogue(row.symbol,true)}>Uzupełnij</Button>
        : <Tag color="green">Warianty SQL</Tag>}
  ];
  const listLayout=useListColumnLayout("prices",columns);
  const pagination=useListPagination("prices",rows.length,40);
  return <>
    <div className="page-heading"><div>
      <Typography.Title level={3}>Cennik leków</Typography.Title>
      <Typography.Text type="secondary">
        Ceny i stany z Subiekta; karencje z katalogu SQL KartyLeczenia.
      </Typography.Text>
    </div><Space wrap>
      <Button onClick={listLayout.openColumns}>Kolumny · Ctrl+Shift+L</Button>
      <Button onClick={prices.reload} disabled={level===null} loading={prices.busy}>Odśwież z SQL</Button>
      <Button onClick={()=>setShowSettings(true)} disabled={settingsBusy||!all.length}>
        Ustawienia wydruku
      </Button>
      <Button type="primary" disabled={settingsBusy||!selected.length} onClick={()=>setShowPreview(true)}>
        Drukuj ({selected.length})
      </Button>
    </Space></div>
    <Card>
      <div className="document-filters">
        <Select loading={levels.busy} value={level} placeholder="Poziom ceny"
          onChange={setLevel} options={(levels.data??[]).map(x=>({label:x.name,value:x.number}))}/>
        <Input placeholder="Szukaj leku lub symbolu" value={filter}
          allowClear onChange={e=>setFilter(e.target.value)} style={{minWidth:190}}/>
        <Space><Switch checked={net} onChange={setNet}/> Cena netto</Space>
        <Select value={settings.withdrawalFormat}
          onChange={v=>changeFormat({withdrawalFormat:v})}
          style={{minWidth:190}}
          options={[{label:"Karencja pełna",value:0},
            {label:"Karencja wg wariantów",value:1},
            {label:"Karencja wg gatunków",value:2}]}/>
        <Select value={settings.routeCodes?"codes":"full"}
          onChange={v=>changeFormat({routeCodes:v==="codes"})}
          options={[{label:"Drogi — skróty",value:"codes"},
            {label:"Drogi — pełne nazwy",value:"full"}]} style={{minWidth:180}}/>
      </div>
      <ErrorNotice error={levels.error||(level===null?"":prices.error)||settingsError}/>
      <Space wrap style={{marginBottom:12}}>
        <Button type={onlyMissing?"primary":"default"}
          onClick={()=>setOnlyMissing(prev=>!prev)}>
          Do uzupełnienia ({missingCount})
        </Button>
        <Button onClick={()=>onCatalogue(rows.some(row=>row.symbol===activePriceSymbol) ? activePriceSymbol : "")}>
          Dodaj / edytuj w katalogu
        </Button>
        <Button onClick={()=>setExcluded(current=>{
          const next=new Set(current);rows.forEach(row=>next.delete(row.symbol));return next;
        })}>Zaznacz widoczne</Button>
        <Button onClick={()=>setExcluded(current=>{
          const next=new Set(current);rows.forEach(row=>next.add(row.symbol));return next;
        })}>Odznacz widoczne</Button>
      </Space>
      <Table {...listLayout.tableProps} rowKey={(row,index)=>row.symbol||String(index)} loading={prices.busy&&level!==null}
        onRow={row=>({onClick:()=>setActivePriceSymbol(row.symbol)})}
        dataSource={rows} columns={listLayout.columns}
        pagination={pagination}
        size="middle" scroll={{x:1180,y:"calc(100vh - 425px)"}}/>
      <Typography.Text type="secondary">
        {selected.length} z {rows.length} widocznych pozycji do wydruku.
        Bufor danych magazynowych: 30 sekund; karencje z SQL: maksymalnie 1 minuta.
        Zmiana formatu karencji i filtra nie odczytuje ponownie SQL.
      </Typography.Text>
    </Card>
    {listLayout.dialog}
    {showSettings&&<PricePrintSettingsPanel initial={settings}
      onSave={setSettings} onClose={()=>setShowSettings(false)}
      onPreview={values=>{
        setSettings(values);setShowSettings(false);setShowPreview(true);
      }}/>}
    {showPreview&&<PricePrintPreview rows={selected}
      settings={settings} heading={priceHeading} onClose={()=>setShowPreview(false)}/>}
  </>;
}

function Expiry() {
  const remote=useRemote<MedicineExpiry[]>("expiries",{},[]);
  const [range,setRange]=useState("all");
  const [query,setQuery]=useState("");
  const [receivedFrom,setReceivedFrom]=useState("");
  const [receivedTo,setReceivedTo]=useState("");
  // Local filters and sorting never cause an additional SQL query.
  const today=localDateTime().slice(0,10);
  const dayNumber=(text:string)=>Math.floor(Date.parse(text+"T00:00:00Z")/86400000);
  const days=(row:MedicineExpiry)=>row.expires
    ? dayNumber(row.expires.slice(0,10))-dayNumber(today) : null;
  const normalized=query.trim().toLocaleLowerCase("pl");
  const rows=(remote.data??[]).filter(row=>{
    if(normalized && ![row.name,row.batch,row.warehouseName].some(value=>
      value.toLocaleLowerCase("pl").includes(normalized)))return false;
    if(receivedFrom && (!row.received || row.received.slice(0,10)<receivedFrom))return false;
    if(receivedTo && (!row.received || row.received.slice(0,10)>receivedTo))return false;
    const delta=days(row);
    return range==="all" || (range==="expired" ? delta!==null && delta<0 :
      delta!==null && delta>=0 && delta<=Number(range));
  }).sort((a,b)=>{
    if(!a.expires && !b.expires)return a.name.localeCompare(b.name,"pl");
    if(!a.expires)return 1;
    if(!b.expires)return -1;
    return a.expires.localeCompare(b.expires)||a.name.localeCompare(b.name,"pl");
  });
  const expiryColumns:ColumnsType<MedicineExpiry> = [
          {key:"name",title:"Lek",dataIndex:"name",sorter:(a,b)=>a.name.localeCompare(b.name,"pl")},
          {key:"batch",title:"Seria",dataIndex:"batch",width:150},
          {key:"received",title:"Data przyjęcia",dataIndex:"received",width:160,render:date,
            sorter:(a,b)=>(a.received??"").localeCompare(b.received??"")},
          {key:"expires",title:"Data ważności",dataIndex:"expires",width:160,render:date,
            defaultSortOrder:"ascend",
            sorter:(a,b)=>(a.expires??"9999").localeCompare(b.expires??"9999")},
          {key:"remaining",title:"Pozostało",dataIndex:"remaining",align:"right",width:125},
          {key:"warehouseName",title:"Magazyn",dataIndex:"warehouseName",width:160},
          {key:"status",title:"Status",width:145,render:(_,row)=>days(row)===null ?
            <Tag>Brak daty</Tag> : days(row)!<0 ? <Tag color="red">Przeterminowane</Tag> :
              days(row)!<=30 ? <Tag color="orange">Do 30 dni</Tag> : <Tag color="green">Aktualne</Tag>}
  ];
  const expiryLayout=useExpiryTableLayout(expiryColumns);
  const pagination=useListPagination("expiry",rows.length,40);
  return <>
    <div className="page-heading"><div>
      <Typography.Title level={3}>Terminy ważności leków</Typography.Title>
      <Typography.Text type="secondary">Filtry działają lokalnie, bez obciążania serwera SQL.</Typography.Text>
    </div><Space><Button onClick={expiryLayout.openColumns}>Kolumny · Ctrl+Shift+L</Button>
      <Button onClick={remote.reload} loading={remote.busy}>Odśwież z SQL</Button></Space></div>
    <Card>
      <Space wrap style={{marginBottom:12}}>
        <Segmented className="expiry-filters" value={range} onChange={setRange} options={[
          {label:"Wszystkie",value:"all"}, {label:"Przeterminowane",value:"expired"},
          {label:"Do 30 dni",value:"30"}, {label:"Do 60 dni",value:"60"}]}/>
        <Input allowClear placeholder="Lek lub seria" value={query}
          onChange={e=>setQuery(e.target.value)} style={{width:240}}/>
        <label>Przyjęto od <Input type="date" value={receivedFrom} onChange={e=>setReceivedFrom(e.target.value)}/></label>
        <label>do <Input type="date" value={receivedTo} onChange={e=>setReceivedTo(e.target.value)}/></label>
        <Button onClick={()=>{setQuery("");setReceivedFrom("");setReceivedTo("");setRange("all");}}>
          Wyczyść filtry
        </Button>
      </Space>
      <ErrorNotice error={remote.error}/>
      <Table {...expiryLayout.tableProps} rowKey={(x,index)=>x.productId+"-"+x.movementId+"-"+index}
        loading={remote.busy} dataSource={rows}
        pagination={pagination} scroll={{x:1040,y:"calc(100vh - 400px)"}}
        columns={expiryLayout.columns}/>
      <Typography.Text type="secondary">
        Wyświetlono {rows.length} pozycji. Dane magazynowe są buforowane maksymalnie 30 sekund. „Odśwież z SQL” pomija bufor.
      </Typography.Text>
    </Card>
    {expiryLayout.dialog}
  </>;
}

function SavedCards({initialCardId,onInitialCardOpened}:{
  initialCardId:string|null;onInitialCardOpened:()=>void;
}) {
  const [trash,setTrash]=useState(false);
  const [query,setQuery]=useState("");
  const [search,setSearch]=useState("");
  const [treatmentDate,setTreatmentDate]=useState("");
  useEffect(()=>{
    const timer=setTimeout(()=>setSearch(query.trim()),300);
    return()=>clearTimeout(timer);
  },[query]);
  const [selected,setSelected]=useState<SavedCard | null>(null);
  const [card,setCard]=useState<StoredCard|null>(null);
  const [editDraft,setEditDraft]=useState<StoredCard|null>(null);
  const [editing,setEditing]=useState(false);
  const [editBusy,setEditBusy]=useState(false);
  const [editConfirm,setEditConfirm]=useState(false);
  const [printHtml,setPrintHtml]=useState<string|null>(null);
  const [previewBusy,setPreviewBusy]=useState<string|null>(null);
  const [printTarget,setPrintTarget]=useState<SavedCard|null>(null);
  const [pdfBusy,setPdfBusy]=useState<string|null>(null);
  const printFrame=useRef<HTMLIFrameElement>(null);
  const [error,setError]=useState("");
  const [trashTarget,setTrashTarget]=useState<{row:SavedCard; restore:boolean}|null>(null);
  const [trashBusy,setTrashBusy]=useState(false);
  const [trashError,setTrashError]=useState("");
  const [purgeTarget,setPurgeTarget]=useState<SavedCard|null>(null);
  const [purgeConfirmation,setPurgeConfirmation]=useState("");
  const [purgeBusy,setPurgeBusy]=useState(false);
  const [exportingId,setExportingId]=useState<string|null>(null);
  const [purgeError,setPurgeError]=useState("");
  const [notice,setNotice]=useState("");
  const remote=useRemote<SavedCard[]>("savedCards",
    {trash,query:search,date:treatmentDate},[trash,search,treatmentDate]);
  useEffect(()=>{
    if(!initialCardId)return;
    let active=true;
    setTrash(false);setSelected(null);setCard(null);setEditDraft(null);setEditing(false);
    void invoke<StoredCard>("savedCard",{id:initialCardId,trash:false})
      .then(loaded=>{
        if(!active)return;
        setSelected({
          id:initialCardId,number:loaded.fields["Nr karty"]||"",
          date:loaded.fields["Wykonanie — data i godzina"]||"",
          client:loaded.fields["Właściciel i adres"]||"",
          document:loaded.fields["Dokument źródłowy"]||""
        });
        setCard(loaded);setError("");onInitialCardOpened();
      })
      .catch(reason=>{
        if(active){setError(reason instanceof Error?reason.message:String(reason));
          onInitialCardOpened();}
      });
    return()=>{active=false;};
    // eslint-disable-next-line react-hooks/exhaustive-deps
  },[initialCardId]);
  const open=async (row: SavedCard) => {
    setEditing(false);setEditDraft(null);setCard(null);setSelected(row);
    try{
      const loaded=await invoke<StoredCard>("savedCard",{id:row.id,trash});
      setCard(loaded);setError("");
    }catch(e){setSelected(null);setError(e instanceof Error?e.message:String(e));}
  };
  function discardSaved(){
    const finish=()=>{setSelected(null);setCard(null);setEditDraft(null);setEditing(false);};
    const changed=editing&&card&&editDraft&&
      JSON.stringify({fields:card.fields,rows:card.rows})!==
      JSON.stringify({fields:editDraft.fields,rows:editDraft.rows});
    if(changed)Modal.confirm({
      className:"kl-dialog-saved-card-discard", title:"Odrzucić niezapisane zmiany karty?",
      content:"Zmiany w edytorze zostaną odrzucone. Karta zapisana w SQL pozostanie bez zmian.",
      okText:"Odrzuć zmiany",cancelText:"Pozostań",onOk:finish
    });
    else finish();
  }
  function beginEdit(){
    if(trash||!card)return;
    setEditDraft({revision:card.revision,fields:{...card.fields},
      rows:card.rows.map(row=>({...row}))});
    setEditing(true);setError("");setNotice("");
  }
  function editHeader(key:string,value:string){
    setEditDraft(current=>current?{...current,fields:{...current.fields,[key]:value}}:current);
  }
  function editRow(index:number,key:string,value:string){
    setEditDraft(current=>current?{...current,
      rows:current.rows.map((row,i)=>i===index?{...row,[key]:value}:row)}:current);
  }
  async function updateExisting(){
    if(!selected||!card||!editDraft||editBusy||trash)return;
    setEditBusy(true);setError("");setNotice("");
    try{
      await invoke("updateSavedCard",{
        id:selected.id,revision:card.revision,
        fields:editDraft.fields,rows:editDraft.rows
      });
      setEditConfirm(false);setEditDraft(null);setEditing(false);
      setNotice("Zaktualizowano istniejącą kartę w SQL, zachowując jej numer.");
      remote.reload();
      try{
        const refreshed=await invoke<StoredCard>("savedCard",{id:selected.id,trash:false});
        setCard(refreshed);
      }catch(e){
        setSelected(null);setCard(null);
        setError("Zapisano kartę. Nie udało się jej ponownie odczytać: "+
          (e instanceof Error?e.message:String(e)));
        return;
      }
      await printSaved(selected);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setEditBusy(false);}
  }
  function confirmUpdate(){
    if(!selected||!editDraft||editBusy)return;
    setError("");setEditConfirm(true);
  }
  async function printSaved(row: SavedCard) {
    if(previewBusy)return;
    setPreviewBusy(row.id);setError("");
    try {
      const result=await invoke<{html:string}>("renderSavedCard",{id:row.id,trash});
      setPrintTarget(row);setPrintHtml(result.html);
    } catch(e) {setError(e instanceof Error?e.message:String(e));}
    finally{setPreviewBusy(null);}
  }
  async function cardPdf(row:SavedCard,mail:boolean){
    if(pdfBusy)return;
    setPdfBusy(row.id);setError("");setNotice("");
    try{
      const result=await invoke<{cancelled:boolean;fileName:string;recipient:string}>(
        mail?"emailCardPdf":"saveCardPdf",{id:row.id,trash});
      if(!result.cancelled)setNotice(mail
        ? "Przygotowano wiadomość z załącznikiem „"+result.fileName+
          "”. Adres płatnika: "+(result.recipient||"brak w Subiekcie — wpisz ręcznie")+
          ". Sprawdź wiadomość w programie pocztowym przed wysłaniem."
        : "Zapisano kartę jako PDF: "+result.fileName);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setPdfBusy(null);}
  }
  async function exportJson(row:SavedCard){
    if(exportingId)return;
    setExportingId(row.id);setError("");setNotice("");
    try{
      const result=await invoke<{cancelled:boolean;fileName:string}>(
        "exportSavedCardJson",{id:row.id,trash});
      if(!result.cancelled)setNotice("Wyeksportowano kartę do pliku "+result.fileName+".");
    }catch(error){setError(error instanceof Error?error.message:String(error));}
    finally{setExportingId(null);}
  }
  async function purgeCard(){
    const expected=purgeTarget?.number || purgeTarget?.document || "";
    if(!purgeTarget || !expected || purgeConfirmation!==expected || purgeBusy)return;
    setPurgeBusy(true);setPurgeError("");setError("");setNotice("");
    try{
      const result=await invoke<{deleted:boolean;cancelled:boolean}>("purgeTrashedCard",{
        id:purgeTarget.id,confirmation:purgeConfirmation
      });
      if(result.deleted){
        setPurgeTarget(null);setPurgeConfirmation("");
        setNotice("Karta została trwale usunięta z kosza SQL.");
        remote.reload();
      }else if(result.cancelled){
        setPurgeTarget(null);setPurgeConfirmation("");
        setNotice("Usuwanie anulowane przez Windows Hello.");
      }
    }catch(e){setPurgeError(e instanceof Error?e.message:String(e));}
    finally{setPurgeBusy(false);}
  }
  function changeTrash(row:SavedCard) {
    setTrashError("");
    setTrashTarget({row,restore:trash});
  }

  async function confirmTrash() {
    if (!trashTarget || trashBusy) return;
    const {row,restore} = trashTarget;
    setTrashBusy(true);setTrashError("");setNotice("");setError("");
    try {
      const result=await invoke<{updated:boolean}>("trashCard",{id:row.id,trash:restore});
      remote.reload();
      if (!result.updated) {
        setTrashError("Stan karty już się zmienił. Lista została odświeżona.");
        return;
      }
      setTrashTarget(null);
      setNotice(restore?"Przywrócono kartę z kosza.":"Przeniesiono kartę do kosza.");
    } catch (error) {
      setTrashError(error instanceof Error?error.message:String(error));
    } finally {setTrashBusy(false);}
  }

  const savedColumns:ColumnsType<SavedCard>=[

          {title:"Numer karty",dataIndex:"number",width:150},
          {title:"Data",dataIndex:"date",render:date,width:145},
          {title:"Kontrahent",dataIndex:"client",ellipsis:true},
          {title:"Dokument",dataIndex:"document",width:175},
          {key:"actions",title:"Czynności",width:650,render:(_,row)=><Space wrap>
            <Button size="small" onClick={()=>{void open(row);}}>Otwórz</Button>
            <Button size="small" loading={previewBusy===row.id}
              onClick={()=>{void printSaved(row);}}>Podgląd karty</Button>
            <Button size="small" loading={pdfBusy===row.id}
              onClick={()=>{void cardPdf(row,false);}}>Zapisz PDF</Button>
            <Button size="small" loading={pdfBusy===row.id}
              onClick={()=>{void cardPdf(row,true);}}>Wyślij mailem</Button>
            <Button size="small" loading={exportingId===row.id}
              onClick={()=>{void exportJson(row);}}>Eksport JSON</Button>
            <Button size="small" danger={!trash} onClick={()=>changeTrash(row)}>{trash?"Przywróć":"Kosz"}</Button>
            {trash&&<Button size="small" danger onClick={()=>{setPurgeTarget(row);setPurgeConfirmation("");setPurgeError("");setNotice("");}}>
              Usuń trwale
            </Button>}
          </Space>}

  ];
  const savedLayout=useListColumnLayout("saved-cards",savedColumns);
  const pagination=useListPagination("saved-cards",remote.data?.length??0,35);
  return <>
    <div className="page-heading"><Typography.Title level={3}>Zapisane karty leczenia</Typography.Title>
      <Space><Segmented value={trash?"trash":"active"} onChange={v=>setTrash(v==="trash")}
        options={[{label:"Archiwum",value:"active"},{label:"Kosz",value:"trash"}]}/>
      <Button onClick={savedLayout.openColumns}>Kolumny · Ctrl+Shift+L</Button>
      <Button onClick={remote.reload}>Odśwież</Button></Space></div>
    <Card>
      <Space wrap style={{marginBottom:12}}>
        <Input.Search allowClear value={query} onChange={e=>setQuery(e.target.value)}
          onSearch={value=>{
            const trimmed=value.trim();
            if(trimmed===search)remote.reload();
            else setSearch(trimmed);
          }}
          placeholder="Numer K/…/rok, klient lub dokument"
          style={{minWidth:300}} className="saved-search"/>
        <label>Data wykonania <Input type="date" aria-label="Filtr daty wykonania"
          value={treatmentDate} onChange={e=>setTreatmentDate(e.target.value)}/></label>
        {treatmentDate&&<Button onClick={()=>setTreatmentDate("")}>Wyczyść datę</Button>}
      </Space>
      <ErrorNotice error={error || remote.error}/>
      {notice&&<Alert className="notice" type="info" showIcon message={notice}/>}
      <Table {...savedLayout.tableProps} rowKey="id" loading={remote.busy} dataSource={remote.data ?? []}
        scroll={{x:800,y:"calc(100vh - 385px)"}} pagination={pagination}
        onRow={row=>({onDoubleClick:()=>{void open(row);}})}
        columns={savedLayout.columns}/>
    </Card>
    {savedLayout.dialog}
    <Modal className="kl-dialog-card-trash" open={trashTarget!==null}
      title={trashTarget?.restore?"Przywrócić kartę?":"Przenieść kartę do kosza?"}
      okText={trashTarget?.restore?"Przywróć":"Przenieś"} cancelText="Anuluj"
      confirmLoading={trashBusy} cancelButtonProps={{disabled:trashBusy}}
      closable={!trashBusy} maskClosable={!trashBusy} keyboard={!trashBusy}
      onCancel={()=>{if(!trashBusy)setTrashTarget(null);}}
      onOk={()=>{void confirmTrash();}}>
      {trashError&&<Alert className="notice" type="error" showIcon message={trashError}/>}
      <Typography.Paragraph>{trashTarget?.row.number||trashTarget?.row.document}</Typography.Paragraph>
    </Modal>
    <Modal className="kl-dialog-permanent-delete" open={purgeTarget!==null} title="Trwale usunąć kartę leczenia?"
      onCancel={()=>{if(!purgeBusy){setPurgeTarget(null);setPurgeConfirmation("");}}}
      closable={!purgeBusy} maskClosable={!purgeBusy} keyboard={!purgeBusy}
      footer={<Space>
        <Button disabled={purgeBusy} onClick={()=>{setPurgeTarget(null);setPurgeConfirmation("");}}>Anuluj</Button>
        <Button type="primary" danger loading={purgeBusy}
          disabled={!purgeTarget || purgeConfirmation!==(purgeTarget.number||purgeTarget.document)}
          onClick={()=>{void purgeCard();}}>Usuń trwale z SQL</Button>
      </Space>}>
      {purgeError&&<Alert className="notice" type="error" showIcon message={purgeError}/>}
      <Alert className="notice" type="warning" showIcon
        message="Nieodwracalna operacja — wymaga Windows Hello i uprawnień administratora SQL"
        description="Usuwana jest tylko wskazana karta znajdująca się już w koszu. Po usunięciu nie będzie jej można przywrócić z kosza."/>
      <Typography.Paragraph>
        Karta: <strong>{purgeTarget?.number||purgeTarget?.document}</strong>
        {purgeTarget?.client&&<> · {purgeTarget.client}</>}
      </Typography.Paragraph>
      <Typography.Paragraph>Wpisz dokładnie numer karty, aby potwierdzić:</Typography.Paragraph>
      <Input autoComplete="off" value={purgeConfirmation} disabled={purgeBusy}
        onChange={e=>setPurgeConfirmation(e.target.value)}
        placeholder={purgeTarget?.number||purgeTarget?.document||""}/>
    </Modal>
    <Modal className="kl-dialog-saved-card" open={!!selected} title={selected?.number||"Karta leczenia"}
      width={1100} closable={!editBusy} keyboard={!editBusy} maskClosable={!editBusy}
      footer={<Space wrap>
        {editing ? <>
          <Button disabled={editBusy} onClick={()=>{setEditing(false);setEditDraft(null);setError("");}}>
            Anuluj edycję
          </Button>
          <Button type="primary" loading={editBusy} onClick={confirmUpdate}>
            Zaktualizuj kartę w SQL
          </Button>
        </> : <>
          {!trash&&<Button onClick={beginEdit} disabled={!card}>Edytuj zapisaną kartę</Button>}
          {selected&&<Button disabled={!card} loading={previewBusy===selected.id}
            onClick={()=>{void printSaved(selected);}}>Podgląd karty</Button>}
          {selected&&<Button disabled={!!pdfBusy} onClick={()=>{void cardPdf(selected,false);}}>Zapisz PDF</Button>}
          {selected&&<Button disabled={!!pdfBusy} onClick={()=>{void cardPdf(selected,true);}}>Wyślij mailem</Button>}
          <Button onClick={discardSaved}>Zamknij</Button>
        </>}
      </Space>}
      onCancel={()=>{if(!editBusy)discardSaved();}}>
      {!card ? <Spin/> : <>
        {editing&&<Alert className="notice" type="warning" showIcon
          message="Edycja istniejącej karty — bez zmiany numeru"
          description="Zmiany pól klinicznych wymagają ponownej weryfikacji ChPL. Dane źródłowe Subiekta, numer karty i pieczątki pozostają zachowane."/>}
        {!editConfirm&&<ErrorNotice error={error}/>}
        {notice&&<Alert className="notice" type="info" showIcon message={notice}/>}
        <Descriptions bordered column={2} size="small"
          items={Object.entries(editing&&editDraft?editDraft.fields:card.fields)
            .map(([key,value])=>({key,label:key,children:
              editing&&archivedEditableFields.has(key)
                ? <Input.TextArea aria-label={key} value={value} autoSize={{minRows:1,maxRows:3}}
                    placeholder={key.includes("data i godzina")?"dd.MM.rrrr gg:mm":undefined}
                    onChange={event=>editHeader(key,event.target.value)}/>
                : value
            }))}/>
        <Table size="small" dataSource={editing&&editDraft?editDraft.rows:card.rows}
          pagination={false} scroll={{x:2450,y:"50vh"}}
          rowKey={(_,index)=>String(index)} columns={[
            {title:"Druk",dataIndex:"Drukuj",width:65,
              render:(value:string,_row:Record<string,string>,index:number)=>
                editing?<Checkbox checked={value==="True"}
                  onChange={event=>editRow(index,"Drukuj",String(event.target.checked))}/>
                  : value==="True"?"Tak":"Nie"},
            {title:"Lek — Subiekt",dataIndex:"Produkt",width:240,ellipsis:true},
            ...archivedEditableColumns.map(({key,title,width})=>({
              title,dataIndex:key,width,
              render:(value:string,_row:Record<string,string>,index:number)=>
                editing
                  ? key==="Sekcja (II lub III)"
                    ? <Checkbox checked={value==="III"}
                        aria-label={"Nabycie produktu — pozycja "+(index+1)}
                        title="Potwierdzenie nabycia produktu (sekcja III)"
                        onChange={event=>editRow(index,key,event.target.checked?"III":"II")}/>
                    : <Input.TextArea aria-label={title+" pozycji "+(index+1)}
                        autoSize={{minRows:1,maxRows:3}} value={value??""}
                        onChange={event=>editRow(index,key,event.target.value)}/>
                  : <span>{key==="Sekcja (II lub III)"?(value==="III"?"Tak":"Nie"):value||"—"}</span>
            }))
          ]}/>
      </>}
    </Modal>
    <Modal className="kl-dialog-update-saved-card" open={editConfirm}
      title="Zaktualizować istniejącą kartę?" okText="Zaktualizuj kartę" cancelText="Wróć do edycji"
      confirmLoading={editBusy} cancelButtonProps={{disabled:editBusy}}
      closable={!editBusy} maskClosable={!editBusy} keyboard={!editBusy}
      onCancel={()=>{if(!editBusy){setEditConfirm(false);setError("");}}}
      onOk={()=>{void updateExisting();}}>
      <ErrorNotice error={error}/>
      <Typography.Paragraph>
        Przed zatwierdzeniem sprawdź dawkowanie, karencję, gatunki i daty wykonania.
        Karta zachowa dotychczasowy numer.
      </Typography.Paragraph>
    </Modal>
    <Modal className="kl-dialog-saved-preview" width="92vw" open={printHtml!==null} title="Podgląd zapisanej karty leczenia"
      onCancel={()=>setPrintHtml(null)} footer={<Space wrap>
        <Button onClick={()=>setPrintHtml(null)}>Zamknij</Button>
        {printTarget&&<Button loading={pdfBusy===printTarget.id}
          onClick={()=>{void cardPdf(printTarget,false);}}>Zapisz PDF</Button>}
        {printTarget&&<Button loading={pdfBusy===printTarget.id}
          onClick={()=>{void cardPdf(printTarget,true);}}>Wyślij mailem</Button>}
        <Button type="primary" onClick={()=>printFrame.current?.contentWindow?.print()}>Drukuj</Button>
      </Space>}>
      <ErrorNotice error={error}/>
      {notice&&<Alert className="notice" type="info" showIcon message={notice}/>}
      {printHtml&&<iframe ref={printFrame} title="Wydruk zapisanej karty" srcDoc={printHtml}
        style={{width:"100%",height:"72vh",border:0,background:"#fff"}}/>}
    </Modal>
  </>;
}

interface ClinicData {
  name:string;address:string;doctor:string;phone:string;revision:string;
  useClinicStamp:boolean;useDoctorStamp:boolean;
  hasClinicStamp:boolean;hasDoctorStamp:boolean;
}
function CardStampSizeSettings() {
  const [height,setHeight]=useState<number|null>(null);
  const [loaded,setLoaded]=useState(false);
  const [saving,setSaving]=useState(false);
  const [message,setMessage]=useState("");
  const [error,setError]=useState("");
  useEffect(()=>{
    let active=true;
    void invoke<{doctorStampHeightMm:number|null}>("getCardPrintSettings")
      .then(result=>{if(active){setHeight(result.doctorStampHeightMm);setLoaded(true);}})
      .catch(error=>{if(active)setError(String(error instanceof Error?error.message:error));});
    return()=>{active=false;};
  },[]);
  async function save(){
    setSaving(true);setMessage("");setError("");
    try{
      await invoke("saveCardPrintSettings",{doctorStampHeightMm:height});
      setMessage("Zapisano wysokość pieczątki na tym stanowisku.");
    }catch(error){setError(String(error instanceof Error?error.message:error));}
    finally{setSaving(false);}
  }
  return <div style={{marginBottom:16}}>
    <Space wrap>
      <label htmlFor="doctor-stamp-height">Wysokość pieczątki lekarza (mm)</label>
      <InputNumber id="doctor-stamp-height" value={height} min={1} max={150} step={0.5}
        placeholder="Oryginalna" style={{width:120}} disabled={!loaded||saving}
        onChange={value=>{setHeight(value);setMessage("");}}/>
      <Button htmlType="button" onClick={()=>{void save();}} loading={saving}
        disabled={!loaded||(height!==null&&(height<1||height>150))}>Zapisz wysokość</Button>
    </Space>
    <div><Typography.Text type="secondary">Puste pole przywraca oryginalny rozmiar PNG.</Typography.Text></div>
    {message&&<Typography.Text type="success">{message}</Typography.Text>}
    <ErrorNotice error={error}/>
  </div>;
}

function ClinicSettingsPanel() {
  const [form]=Form.useForm();
  const [busy,setBusy]=useState(false);
  const [notice,setNotice]=useState("");
  const [error,setError]=useState("");
  const [revision,setRevision]=useState<string|null>(null);
  const [clinicStamp,setClinicStamp]=useState(false);
  const [doctorStamp,setDoctorStamp]=useState(false);
  const [selectedClinic,setSelectedClinic]=useState("");
  const [selectedDoctor,setSelectedDoctor]=useState("");
  const [removeClinic,setRemoveClinic]=useState(false);
  const [removeDoctor,setRemoveDoctor]=useState(false);
  async function refresh(){
    setBusy(true);setError("");setNotice("");setRevision(null);
    try {
      const current=await invoke<ClinicData>("getClinic");
      form.setFieldsValue(current);
      setRevision(current.revision);
      setClinicStamp(current.hasClinicStamp);
      setDoctorStamp(current.hasDoctorStamp);
      setSelectedClinic("");setSelectedDoctor("");
      setRemoveClinic(false);setRemoveDoctor(false);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  useEffect(()=>{void refresh();},[form]);
  async function chooseStamp(kind:"clinic"|"doctor"){
    setBusy(true);setError("");setNotice("");
    try{
      const result=await invoke<{cancelled:boolean;name?:string}>("chooseClinicStamp",{kind});
      if(!result.cancelled){
        if(kind==="clinic"){
          setSelectedClinic(result.name??"Wybrano plik PNG");
          setRemoveClinic(false);form.setFieldValue("useClinicStamp",true);
        }else{
          setSelectedDoctor(result.name??"Wybrano plik PNG");
          setRemoveDoctor(false);form.setFieldValue("useDoctorStamp",true);
        }
      }
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  function removeStamp(kind:"clinic"|"doctor"){
    if(kind==="clinic"){
      setRemoveClinic(true);setSelectedClinic("");form.setFieldValue("useClinicStamp",false);
    }else{
      setRemoveDoctor(true);setSelectedDoctor("");form.setFieldValue("useDoctorStamp",false);
    }
  }
  const save=async(values:Record<string,unknown>)=>{
    if(!revision)return;
    setBusy(true);setError("");setNotice("");
    try{
      const result=await invoke<{saved:boolean;revision:string;
        hasClinicStamp:boolean;hasDoctorStamp:boolean}>("saveClinic",{
          ...values,revision,removeClinicStamp:removeClinic,removeDoctorStamp:removeDoctor
        });
      setRevision(result.revision);
      setClinicStamp(result.hasClinicStamp);setDoctorStamp(result.hasDoctorStamp);
      setSelectedClinic("");setSelectedDoctor("");setRemoveClinic(false);setRemoveDoctor(false);
      setNotice("Wspólne dane lecznicy i lekarza zapisane w SQL Server.");
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  };
  return <Card className="settings-card">
    <Typography.Title level={4}>Lecznica i lekarz — wspólne dane SQL</Typography.Title>
    <Typography.Paragraph type="secondary">
      Dane są przechowywane w bazie KartyLeczenia i wykorzystywane przy wydruku
      na każdym stanowisku. Przy pierwszym połączeniu pustą kartotekę SQL można
      uzupełnić z wcześniejszych ustawień komputera.
    </Typography.Paragraph>
    <ErrorNotice error={error}/>
    {notice&&<Alert type="success" showIcon className="notice" message={notice}/>}
    <Form form={form} layout="vertical" onFinish={values=>{void save(values);}}>
      <Form.Item label="Nazwa lecznicy" name="name" rules={[{required:true}]}>
        <Input/>
      </Form.Item>
      <Form.Item label="Adres" name="address" rules={[{required:true}]}>
        <Input.TextArea autoSize/>
      </Form.Item>
      <Form.Item label="Lekarz i numer prawa wykonywania zawodu" name="doctor" rules={[{required:true}]}>
        <Input/>
      </Form.Item>
      <Form.Item label="Telefon" name="phone"><Input/></Form.Item>
      <Form.Item name="useClinicStamp" label="Pieczątka lecznicy na karcie" valuePropName="checked">
        <Switch checkedChildren="PNG" unCheckedChildren="Tekst"/>
      </Form.Item>
      <Space wrap style={{marginBottom:16}}>
        <Button disabled={busy||!revision} onClick={()=>{void chooseStamp("clinic");}}>
          Wybierz PNG lecznicy
        </Button>
        <Typography.Text type="secondary">{selectedClinic||(!removeClinic&&clinicStamp?"Zapisana pieczątka SQL":"Brak pliku")}</Typography.Text>
        <Button danger disabled={busy||(!clinicStamp&&!selectedClinic)||removeClinic}
          onClick={()=>removeStamp("clinic")}>Usuń pieczątkę</Button>
      </Space>
      <Form.Item name="useDoctorStamp" label="Pieczątka lekarza na karcie" valuePropName="checked">
        <Switch checkedChildren="PNG" unCheckedChildren="Tekst"/>
      </Form.Item>
      <Space wrap style={{marginBottom:16}}>
        <Button disabled={busy||!revision} onClick={()=>{void chooseStamp("doctor");}}>
          Wybierz PNG lekarza
        </Button>
        <Typography.Text type="secondary">{selectedDoctor||(!removeDoctor&&doctorStamp?"Zapisana pieczątka SQL":"Brak pliku")}</Typography.Text>
        <Button danger disabled={busy||(!doctorStamp&&!selectedDoctor)||removeDoctor}
          onClick={()=>removeStamp("doctor")}>Usuń pieczątkę</Button>
      </Space>
      <CardStampSizeSettings/>
      <Space wrap>
        <Button onClick={()=>{void refresh();}} disabled={busy}>Odśwież z SQL</Button>
        <Button type="primary" htmlType="submit" loading={busy} disabled={!revision}>
          Zapisz wspólne dane lecznicy
        </Button>
      </Space>
    </Form>
  </Card>;
}
interface BackupOptions {
  destinationFolder:string;workingFolder:string;naming:string;customName:string;
  overwrite:boolean;compress:boolean;
}
interface BackupResult { cancelled:boolean;path?:string;sqlVerified?:boolean;message?:string; }
function BackupPanel() {
  const [form]=Form.useForm<BackupOptions>();
  const [busy,setBusy]=useState(false);
  const [step,setStep]=useState<BackupProgress | null>(null);
  const [error,setError]=useState("");
  const [result,setResult]=useState("");
  const [verified,setVerified]=useState(false);
  useEffect(()=>{
    invoke<BackupOptions>("getBackupSettings").then(data=>form.setFieldsValue(data))
      .catch(e=>setError(String(e)));
  },[form]);
  const browse=async()=>{
    try {
      const result=await invoke<{folder:string}>("chooseArchiveFolder");
      if(result.folder)form.setFieldValue("destinationFolder",result.folder);
    } catch(e){setError(e instanceof Error?e.message:String(e));}
  };
  const save=async()=>{
    setError("");setResult("");
    try {await form.validateFields();await invoke("saveBackupSettings",form.getFieldsValue(true));setResult("Zapisano ustawienia archiwizacji.");}
    catch(e){setError(e instanceof Error?e.message:String(e));}
  };
  const run=async()=>{
    setBusy(true);setError("");setResult("");setStep(null);
    try {
      await form.validateFields();
      await invoke("saveBackupSettings",form.getFieldsValue(true));
      const outcome=await invoke<BackupResult>("backupDatabase",{},setStep);
      if(outcome.cancelled)setResult("Archiwizacja anulowana w Windows Hello.");
      else {setResult("Plik: "+outcome.path+"\n"+outcome.message);setVerified(!!outcome.sqlVerified);}
    } catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  };
  return <Card className="settings-card">
    <Typography.Title level={4}>Archiwizacja pełnej bazy SQL</Typography.Title>
    <Typography.Paragraph type="secondary">
      Jedna pełna kopia .bak na pendrivie, folder roboczy serwera, kontrola SHA-256
      i czyszczenie po pomyślnym przeniesieniu. Wymagane potwierdzenie Windows Hello.
    </Typography.Paragraph>
    <ErrorNotice error={error}/>
    {result&&<Alert className="notice" type={verified?"success":"info"} showIcon
      message={<span style={{whiteSpace:"pre-wrap"}}>{result}</span>}/>}
    <Form form={form} layout="vertical" initialValues={{naming:"daily",overwrite:true,compress:false,customName:"KartyLeczenia"}}>
      <Form.Item label="Folder archiwum — pendrive" name="destinationFolder" rules={[{required:true,message:"Wybierz folder"}]}>
        <Input placeholder="E:\archiwum" addonAfter={<Button type="link" size="small" onClick={()=>{void browse();}}>Przeglądaj</Button>}/>
      </Form.Item>
      <Form.Item label="Folder roboczy serwera SQL (puste = domyślny folder SQL)" name="workingFolder">
        <Input placeholder="C:\SQLBackups"/>
      </Form.Item>
      <Form.Item name="naming" label="Nazwa archiwum">
        <Select options={[
          {label:"Nazwa i data",value:"daily"}, {label:"Stała nazwa",value:"fixed"},
          {label:"Nazwa i miesiąc",value:"monthly"}, {label:"Własna nazwa",value:"custom"}]}/>
      </Form.Item>
      <Form.Item label="Własna nazwa pliku" name="customName"><Input/></Form.Item>
      <Form.Item name="overwrite" valuePropName="checked"><Checkbox>Nadpisuj archiwa o tej samej nazwie dopiero po weryfikacji</Checkbox></Form.Item>
      <Form.Item name="compress" valuePropName="checked"><Checkbox>Kompresja SQL Server</Checkbox></Form.Item>
      <Space><Button onClick={()=>{void save();}} disabled={busy}>Zapisz ustawienia</Button>
        <Button type="primary" loading={busy} onClick={()=>{void run();}}>Archiwizuj teraz</Button></Space>
    </Form>
    {busy && <div className="backup-progress">
      <Typography.Text strong>{step?.stage || "Przygotowanie archiwizacji"}</Typography.Text>
      <Progress percent={step?.percent??0} status="active"
        format={step?.percent==null?()=> "…":undefined}/>
      <Typography.Text type="secondary">{step?.detail||"Trwa operacja SQL..."}</Typography.Text>
    </div>}
  </Card>;
}

interface RestorePickerResult {
  cancelled:boolean;fileName?:string;path?:string;bytes?:number;modified?:string;
}
interface RestoreOutcome {
  cancelled:boolean;database?:string;sourcePath?:string;
  safetyBackupPath?:string|null;message?:string;
}
function RestorePanel() {
  const [file,setFile]=useState<RestorePickerResult|null>(null);
  const [confirmation,setConfirmation]=useState("");
  const [busy,setBusy]=useState(false);
  const [step,setStep]=useState<BackupProgress|null>(null);
  const [error,setError]=useState("");
  const [result,setResult]=useState("");
  async function browse(){
    if(busy)return;
    setError("");setResult("");setConfirmation("");
    try {
      const selected=await invoke<RestorePickerResult>("chooseRestoreFile");
      if(!selected.cancelled)setFile(selected);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
  }
  function requestRestore(){
    if(!file?.path||confirmation!=="KartyLeczenia"||busy)return;
    Modal.confirm({
      className:"kl-dialog-restore-confirm", title:"Zastąpić całą bazę KartyLeczenia?",
      content:<div>
        <Typography.Paragraph strong>Ta operacja przerwie dostęp pozostałych stanowisk i nadpisze aktualne dane bazy programowej.</Typography.Paragraph>
        <Typography.Paragraph>Plik: {file.fileName}</Typography.Paragraph>
        <Typography.Paragraph>Program sprawdzi kopię i utworzy dodatkowe archiwum aktualnej bazy na serwerze SQL przed jej zastąpieniem. Wymagane jest Windows Hello i uprawnienie sysadmin SQL.</Typography.Paragraph>
        <Typography.Paragraph>Nie dotyczy bazy Subiekta GT.</Typography.Paragraph>
      </div>,
      okText:"Tak, odtwórz bazę",
      okType:"danger",
      cancelText:"Anuluj",
      onOk:async()=>{
        setBusy(true);setError("");setResult("");setStep(null);
        try {
          const restored=await invoke<RestoreOutcome>("restoreDatabase",{
            path:file.path,confirmation
          },setStep);
          if(restored.cancelled){
            setResult("Odtwarzanie anulowane. Baza SQL nie została zmieniona.");
          }else{
            setResult((restored.message||"Odtwarzanie zakończone.")+
              (restored.safetyBackupPath?"\nKopia sprzed odtwarzania: "+restored.safetyBackupPath:"")+
              "\nZalecane ponowne uruchomienie ReactKL i odświeżenie pozostałych stanowisk.");
            setFile(null);setConfirmation("");
          }
        }catch(e){setError(e instanceof Error?e.message:String(e));}
        finally{setBusy(false);}
      }
    });
  }
  return <Card className="settings-card">
    <Typography.Title level={4}>Odtwarzanie archiwum SQL</Typography.Title>
    <Alert className="notice" type="warning" showIcon
      message="Operacja administracyjna — zastępuje całą bazę programową KartyLeczenia"
      description="Wykonuj na komputerze z SQL po zakończeniu pracy pozostałych stanowisk. Nie odtwarza danych Subiekta. Plik .bak musi być pełną kopią właściwej bazy."/>
    <ErrorNotice error={error}/>
    {result&&<Alert className="notice" type="success" showIcon
      message={<span style={{whiteSpace:"pre-wrap"}}>{result}</span>}/>}
    <Space direction="vertical" style={{width:"100%"}} size={13}>
      <Space wrap>
        <Button disabled={busy} onClick={()=>{void browse();}}>Wybierz archiwum .bak</Button>
        <Typography.Text>{file?.fileName??"Nie wybrano pliku"}</Typography.Text>
      </Space>
      {file?.path&&<Typography.Text type="secondary" copyable>
        {file.path} ({((file.bytes??0)/1048576).toFixed(1)} MB)
      </Typography.Text>}
      <div>
        <Typography.Text strong>Wpisz KartyLeczenia, aby odblokować odtwarzanie:</Typography.Text>
        <Input style={{maxWidth:330,display:"block",marginTop:7}}
          value={confirmation} autoComplete="off"
          placeholder="KartyLeczenia"
          onChange={event=>setConfirmation(event.target.value)}
          disabled={busy||!file?.path}/>
      </div>
      <Button danger type="primary" loading={busy}
        disabled={!file?.path||confirmation!=="KartyLeczenia"}
        onClick={requestRestore}>Odtwórz bazę SQL z archiwum</Button>
    </Space>
    {busy&&<div className="backup-progress">
      <Typography.Text strong>{step?.stage??"Przygotowanie odtwarzania"}</Typography.Text>
      <Progress status="active" percent={step?.percent??0}
        format={step?.percent==null?()=>"…":undefined}/>
      <Typography.Text type="secondary">{step?.detail||"Nie zamykaj aplikacji."}</Typography.Text>
    </div>}
  </Card>;
}

interface UpdateRelease {
  releaseId:string;version:string;currentVersion:string;sha256:string;
  newer:boolean;publishedAtUtc:string;
}
interface UpdateResult {release:UpdateRelease|null;currentVersion:string;}
interface InstallUpdateStatus {
  installed:boolean; isServer:boolean; pending:boolean; installedVersion:string;
  server:string; publicationError:string|null;
}
function UpdatePanel(){
  const [data,setData]=useState<UpdateResult|null>(null);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState("");
  const [notice,setNotice]=useState("");
  const [status,setStatus]=useState<InstallUpdateStatus|null>(null);
  useEffect(()=>{
    void invoke<InstallUpdateStatus>("getUpdateStatus").then(setStatus).catch(()=>{});
  },[]);
  async function publish(){
    setBusy(true);setError("");setNotice("");
    try{
      await invoke("publishPendingUpdate");
      setStatus(await invoke<InstallUpdateStatus>("getUpdateStatus"));
      setNotice("Zainstalowany instalator został opublikowany w SQL.");
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  async function check(){
    setBusy(true);setError("");setNotice("");
    try{
      const result=await invoke<UpdateResult>("checkUpdate");
      setData(result);
      setNotice(!result.release
        ? "Na serwerze nie opublikowano jeszcze instalatora ReactKL."
        : result.release.newer ? "Dostępna jest nowsza wersja ReactKL."
          : "Masz aktualną wersję lub nowszą od tej opublikowanej w SQL.");
    }catch(e){setError(e instanceof Error?e.message:String(e));setData(null);}
    finally{setBusy(false);}
  }
  async function install(){
    if(!data?.release?.newer||busy)return;
    setBusy(true);setError("");
    try{
      await invoke("installUpdate",{releaseId:data.release.releaseId});
    }catch(error){setError(error instanceof Error?error.message:String(error));}
    finally{setBusy(false);}
  }
  return <Card className="settings-card">
    <Typography.Title level={4}>Aktualizacje ReactKL z SQL Server</Typography.Title>
    <Typography.Paragraph type="secondary">
      Ten sam mechanizm co poprzednio: instalator trafia do SQL na komputerze serwera,
      a stanowiska sprawdzają aktualizacje przy uruchomieniu. Numer wersji jest wpisywany ręcznie.
    </Typography.Paragraph>
    <ErrorNotice error={error}/>
    {status && !status.installed && <Alert className="notice" type="info" showIcon
      message="Tryb testowy — aktualizacje SQL są dostępne po zainstalowaniu ReactKL."/>}
    {status?.publicationError && <Alert className="notice" type="warning" showIcon
      message={"Publikacja na serwerze: "+status.publicationError}/>}
    {notice&&<Alert className="notice" showIcon
      type={data?.release?.newer?"info":"success"} message={notice}/>}
    <Space direction="vertical" size={12}>
      <Typography.Text>Obecna wersja: <strong>{data?.currentVersion??status?.installedVersion??"—"}</strong></Typography.Text>
      {data?.release&&<div>
        <Typography.Text>Opublikowana wersja: <strong>{data.release.version}</strong></Typography.Text>
        <div><Typography.Text type="secondary" style={{fontSize:12}}>
          Publikacja: {new Date(data.release.publishedAtUtc).toLocaleString("pl-PL")}
        </Typography.Text></div>
      </div>}
      <Space wrap>
        <Button loading={busy} disabled={status?.installed===false}
          onClick={()=>{void check();}}>Sprawdź aktualizację w SQL</Button>
        {status?.isServer && status.pending &&
          <Button type="primary" loading={busy} onClick={()=>{void publish();}}>
            Opublikuj instalator na serwerze SQL
          </Button>}
        {!status?.isServer && <Button type="primary" loading={busy}
          disabled={!data?.release?.newer} onClick={install}>Pobierz i zainstaluj</Button>}
      </Space>
    </Space>
  </Card>;
}

function SettingsPage({onSubiektSaved}: {onSubiektSaved: () => void}) {
  const [active,setActive]=useState(()=>{
    try{return localStorage.getItem("kl.settings.activeTab")||"subiekt";}
    catch{return "subiekt";}
  });
  function choose(key:string){
    setActive(key);
    try{localStorage.setItem("kl.settings.activeTab",key);}
    catch{/* Workstation settings remain optional. */}
  }
  return <>
    <div className="page-heading">
      <Typography.Title level={3}>Ustawienia — Karty Leczenia</Typography.Title>
    </div>
    <Tabs activeKey={active} onChange={choose} items={[
      {key:"subiekt",label:"Subiekt GT",children:<SubiektDatabaseSettings onSaved={onSubiektSaved}/>},
      {key:"program",label:"Baza Karty Leczenia",children:<ProgramDatabaseSettings/>},
      {key:"administrator",label:"Konto administratora SQL",children:<ProgramDatabaseSettings administrator/>},
      {key:"backup",label:"Archiwizacja",children:<Space direction="vertical" size={18}
        style={{width:"100%"}}>
        <Alert type="info" className="notice" showIcon
          message="Archiwizacja dotyczy bazy KartyLeczenia — nie bazy Subiekta"
          description="Kopia korzysta z połączenia „Baza Karty Leczenia”. Do odtwarzania używane jest „Konto administratora SQL”."/>
        <BackupPanel/>
        <RestorePanel/>
      </Space>},
      {key:"clinic",label:"Dane lecznicy",children:<ClinicSettingsPanel/>},
      {key:"updates",label:"Aktualizacje",children:<UpdatePanel/>}
    ]}/>
  </>;
}

export default function App() {
  const [runningVersion,setRunningVersion]=useState("");
  useEffect(()=>{
    if(!isDesktop())return;
    let active=true;
    void invoke<{version:string}>("getProgramVersion")
      .then(result=>{if(active)setRunningVersion(result.version);})
      .catch(()=>{if(active)setRunningVersion("niedostępna");});
    return()=>{active=false;};
  },[]);
  const [layout,setLayout]=useState<LayoutName>(()=>saved("kl.layout",layouts,"modern"));
  const compact=layout==="compact-tabs"||layout==="compact-sidebar";
  const topTabs=layout==="classic"||layout==="compact-tabs";
  const [palette,setPalette]=useState<PaletteName>(()=>saved("kl.palette",palettes,"light"));
  const [uiScale,setUiScale]=useState<number>(()=>{
    try{const value=Number(localStorage.getItem("kl.uiScale"));return Number.isFinite(value)&&value>=80&&value<=200?value:100;}
    catch{return 100;}
  });
  const [section,setSection]=useState<Section>("documents");
  const [subiektRevision,setSubiektRevision]=useState(0);
  const [savedCardToOpen,setSavedCardToOpen]=useState<string|null>(null);
  const [draft,setDraft]=useState<Draft|null>(null);
  const [catalogueDirty,setCatalogueDirty]=useState(false);
  const [catalogueEntry,setCatalogueEntry]=useState<{symbol:string;direct:boolean}|null>(null);
  const navigate=(next:Section)=>{
    if(next===section)return;
    if(section==="catalogue"&&catalogueDirty){
      Modal.confirm({
        className:"kl-dialog-catalogue-navigation", title:"Niezapisane zmiany w katalogu",
        content:"Czy chcesz opuścić edytor i odrzucić niezapisane dane?",
        okText:"Odrzuć zmiany",cancelText:"Pozostań",
        onOk:()=>{setCatalogueDirty(false);setSection(next);}
      });
      return;
    }
    setSection(next);
  };
  useEffect(()=>{try{localStorage.setItem("kl.layout",layout);}catch{/* no storage */}},[layout]);
  useEffect(()=>{try{localStorage.setItem("kl.palette",palette);}catch{/* no storage */}},[palette]);
  useEffect(()=>{
    // Chromium WebView2 CSS zoom scales React and Ant Design controls together;
    // reset on unmount, without changing Windows DPI or persisted SQL settings.
    const previous=document.documentElement.style.zoom;
    document.documentElement.style.zoom=uiScale+"%";
    try{localStorage.setItem("kl.uiScale",String(uiScale));}catch{/* no storage */}
    return()=>{document.documentElement.style.zoom=previous;};
  },[uiScale]);
  const primary=palette==="sea"?"#15836f":palette==="dark"?"#78a5fa":"#1768d8";
  const appearance=useMemo(()=>({
    algorithm:palette==="dark"?theme.darkAlgorithm:theme.defaultAlgorithm,
    token:{colorPrimary:primary,borderRadius:topTabs?4:layout==="modern"?12:8, controlHeight:compact?26:32,
      fontFamily:"Segoe UI,system-ui,sans-serif",fontSize:compact?13:14},
    components:{Segmented:{itemSelectedBg:primary,itemSelectedColor:"#fff"},
      Table:{headerBg:palette==="dark"?"#273447":"#f3f6fa"}}
  }),[palette,layout,primary,topTabs,compact]);
  const nav=<nav aria-label="Nawigacja Karty Leczenia" className="main-nav">
    {sections.map(x=><button key={x.id} type="button"
      className={"nav-item "+(section===x.id?"is-active":"")}
      onClick={()=>navigate(x.id)}>{x.name}</button>)}
  </nav>;
  const controls=<div className="appearance-controls">
    <Select aria-label="Układ" value={layout} onChange={setLayout} options={layouts.map(value=>({value,label:layoutLabels[value]}))}/>
    <Select aria-label="Motyw" value={palette} onChange={setPalette} options={[
      {label:"Jasny",value:"light"}, {label:"Ciemny",value:"dark"}, {label:"Morski",value:"sea"}]}/>
    <Select aria-label="Skalowanie interfejsu" value={uiScale} style={{minWidth:100}}
      onChange={setUiScale} options={[80,90,100,110,125,150,175,200].map(value=>({value,label:value+"%"}))}/>
  </div>;
  const content=section==="documents"?null:
    section==="treatment"?<Treatment draft={draft} compact={compact} onDocuments={()=>setSection("documents")}
      onOpenSaved={id=>{setSavedCardToOpen(id);setSection("saved");}}/>:
    section==="prices"?<Prices onCatalogue={(symbol,direct=false)=>{setCatalogueEntry({symbol,direct});setSection("catalogue");}}/>:
    section==="catalogue"?<CataloguePage onDirtyChange={setCatalogueDirty}
      initialEntry={catalogueEntry} onPickerOpened={()=>setCatalogueEntry(null)}/>:
    section==="expiry"?<Expiry/>:
    section==="saved"?<SavedCards initialCardId={savedCardToOpen}
      onInitialCardOpened={()=>setSavedCardToOpen(null)}/>:<SettingsPage onSubiektSaved={()=>setSubiektRevision(revision=>revision+1)}/>;
  return <ConfigProvider theme={appearance}>
    <div className={"app-shell layout-"+layout+" palette-"+palette+(compact?" layout-compact":"")}>
      {!topTabs&&<aside className="sidebar">
        <div className="sidebar-brand"><div className="brand-mark">KL</div>
          <div><strong>Karty Leczenia</strong><small>ReactKL · SQL</small></div></div>
        {nav}
        <div className="sidebar-footer">Interfejs React + WebView2<br/>Dane pozostają w SQL Server</div>
      </aside>}
      <div className="workspace">
        <header className="topbar">
          <div className="topbar-title"><strong>{topTabs?"Karty Leczenia":sections.find(x=>x.id===section)?.name}</strong></div>
          {controls}
        </header>
        {topTabs&&<div className="top-navigation">{nav}</div>}
        <main className="main-content">
          {!isDesktop()&&<Alert type="info" className="notice" showIcon
            message="Podgląd przeglądarkowy. Połączenie SQL działa w aplikacji Windows ReactKL."/>}
          <div hidden={section!=="documents"} style={section==="documents"?{display:"contents"}:undefined}>
            <Documents key={subiektRevision} compact={compact} active={section==="documents"}
              onOpen={next=>{setDraft(next);setSection("treatment");}}/>
          </div>
          {content}
        </main>
        <footer className="statusline"><span>Karty Leczenia{runningVersion&&" · Wersja "+runningVersion}</span>
          <span>Układ {layoutLabels[layout]} · motyw {palette} · skala {uiScale}%</span></footer>
      </div>
    </div>
  </ConfigProvider>;
}
