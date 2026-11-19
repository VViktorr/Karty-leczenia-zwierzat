import { useEffect, useMemo, useState, useRef } from "react";
import {
  Alert, Button, Card, Checkbox, ConfigProvider, DatePicker, Descriptions, Empty,
  Form, Input, InputNumber, Modal, Segmented, Select, Space, Spin, Switch, Table,
  Tag, Typography, Progress, theme
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke, isDesktop } from "./bridge";
import CataloguePage from "./CataloguePage";
import type { BackupProgress } from "./bridge";
import type { DocumentKind, DocumentRow, DocumentsResult, DatePeriod } from "./bridge";

type LayoutName = "classic" | "modern" | "fluent";
type PaletteName = "light" | "dark" | "sea";
type Section = "documents" | "prices" | "catalogue" | "treatment" | "saved" | "expiry" | "settings";
interface DraftRow {
  index: number; product: string; symbol: string; indication: string; quantity: string; batch: string;
  species: string; route: string; dosing: string; withdrawal: string;
  description: string; animals: string; diagnosis: string; section: string; selected: boolean;
}
interface Draft {
  draftId: string; number: string; payer: string; recipient: string; rows: DraftRow[];
}
interface TreatmentVariantChoice {
  id:number;species:string;indication:string;route:string;dose:string;
  tissues:string;milk:string;remarks:string;source:string;pdf:string;
}
interface TreatmentVariantList {
  product:string;symbol:string;variants:TreatmentVariantChoice[];
}
interface PriceLevel { number: number; name: string; }
interface MedicinePrice {
  symbol: string; name: string; gross: number | null; withdrawal: string; latestBatch: string;
}
interface MedicineExpiry {
  name: string; batch: string; expires: string | null; remaining: number;
  warehouseName: string; deliveriesConfirmed: boolean; productId: number; movementId: number;
}
interface SavedCard { id: string; date: string; client: string; document: string; number: string; }
interface SaveResult {duplicate: boolean; duplicateId: string | null; number: string | null; id: string | null;}
interface StoredCard { fields: Record<string, string>; rows: Array<Record<string, string>>; }
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
const layouts = ["modern", "classic", "fluent"] as const;
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
function useRemote<T>(action: string, payload: Record<string, unknown>, deps: unknown[]): {
  data: T | null; busy: boolean; error: string; reload: () => void;
} {
  const [data, setData] = useState<T | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [tick, setTick] = useState(0);
  // Keys are primitive strings/numbers, not serialized SQL credentials.
  const key = JSON.stringify(deps);
  useEffect(() => {
    let expired = false;
    setBusy(true); setError("");
    invoke<T>(action, payload)
      .then(result => { if (!expired) setData(result); })
      .catch(reason => { if (!expired) setError(reason instanceof Error ? reason.message : String(reason)); })
      .finally(() => { if (!expired) setBusy(false); });
    return () => { expired = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [action, key, tick]);
  return {data, busy, error, reload: () => setTick(n => n + 1)};
}

function Documents({onOpen}: {onOpen: (draft: Draft) => void}) {
  const [kind, setKind] = useState<DocumentKind>("invoices");
  const [period, setPeriod] = useState<DatePeriod>("month");
  const [query, setQuery] = useState("");
  const [search, setSearch] = useState("");
  const [readError, setReadError] = useState("");
  const [reading, setReading] = useState(false);
  useEffect(() => {
    const timer = setTimeout(() => setSearch(query.trim()), 300);
    return () => clearTimeout(timer);
  }, [query]);
  const {data, busy, error, reload} = useRemote<DocumentsResult>(
    "searchDocuments", {kind, period, query: search}, [kind, period, search]);
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      if (event.key === "F5") { event.preventDefault(); reload(); }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, [reload]);
  const columns: ColumnsType<DocumentRow> = [
    {title: "Data", dataIndex: "date", width: 125, render: date,
      sorter: (a, b) => (a.date || "").localeCompare(b.date || "")},
    {title: "Numer", dataIndex: "number", width: 175,
      sorter: (a, b) => a.number.localeCompare(b.number)},
    {title: "Kontrahent", dataIndex: "shortPayer", ellipsis: true,
      render: (value: string, row) => <span title={row.payer}>{value || row.payer}</span>},
    {title: "Brutto", dataIndex: "gross", align: "right", width: 145,
      render: (value: number | null, row) => currency(value, row.currency || "zł"),
      sorter: (a, b) => (a.gross ?? 0) - (b.gross ?? 0)},
    {title: "Powiązania", dataIndex: "relatedText", ellipsis: true, width: 230}
  ];
  const open = async (row: DocumentRow) => {
    setReading(true); setReadError("");
    try {
      const draft = await invoke<Draft>("readDocument", {documentId: row.id, number: row.number});
      onOpen(draft);
    } catch (e) { setReadError(String(e instanceof Error ? e.message : e)); }
    finally { setReading(false); }
  };
  return <>
    <div className="page-heading"><div>
      <Typography.Title level={3}>Dokumenty Subiekta GT</Typography.Title>
      <Typography.Text type="secondary">Faktury, paragony i WZ z aktualnej bazy.</Typography.Text>
    </div><Button onClick={reload} loading={busy}>Odśwież · F5</Button></div>
    <Card>
      <div className="document-filters">
        <Segmented block size="large" value={kind} onChange={value => setKind(value as DocumentKind)}
          options={[{label:"Faktury",value:"invoices"}, {label:"Paragony",value:"receipts"},
            {label:"WZ",value:"warehouse"}, {label:"Wszystkie",value:"all"}]}/>
        <Input.Search allowClear size="large" value={query}
          onChange={e => setQuery(e.target.value)} placeholder="Numer lub kontrahent"/>
        <Select value={period} size="large" onChange={setPeriod} options={[
          {label:"Ostatnie 30 dni", value:"month"}, {label:"Ostatnie 7 dni",value:"week"},
          {label:"Dzisiaj",value:"today"}, {label:"Wszystkie",value:"all"}]}/>
      </div>
      <ErrorNotice error={error || readError}/>
      {data?.truncated && <Alert className="notice" type="info" showIcon message="Pokazano 200 dokumentów. Zawęź wyszukiwanie."/>}
      <Table rowKey="id" loading={busy || reading} dataSource={data?.items ?? []} columns={columns}
        size="middle" scroll={{x:900,y:"calc(100vh - 390px)"}} pagination={{pageSize:25,showSizeChanger:true}}
        onRow={row => ({onDoubleClick: () => {void open(row);}})}
        locale={{emptyText:<Empty description="Brak dokumentów"/>}}/>
      <Typography.Text type="secondary">Dwuklik otwiera kartę leczenia w React.</Typography.Text>
    </Card>
  </>;
}

const editable: Array<{key: keyof DraftRow; field: string; title: string}> = [
  {key:"batch",field:"Seria",title:"Seria"},
  {key:"species",field:"Gatunek",title:"Gatunek"},
  {key:"route",field:"Droga podania",title:"Droga podania"},
  {key:"dosing",field:"Dawkowanie i okres stosowania",title:"Dawkowanie"},
  {key:"withdrawal",field:"Karencja",title:"Karencja"},
  {key:"animals",field:"Liczba zwierząt",title:"Liczba zwierząt"},
  {key:"description",field:"Opis zwierząt / numer siedziby stada",title:"Opis zwierząt"},
  {key:"diagnosis",field:"Rozpoznanie",title:"Rozpoznanie"},
  {key:"section",field:"Sekcja (II lub III)",title:"Sekcja"}
];
function Treatment({draft, onDocuments}: {draft: Draft | null; onDocuments: () => void}) {
  const [rows, setRows] = useState<DraftRow[]>(draft?.rows ?? []);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [savedNumber, setSavedNumber] = useState("");
  const [reported, setReported] = useState(() => localDateTime());
  const [performed, setPerformed] = useState(() => localDateTime());
  const [findings, setFindings] = useState("");
  const [preview, setPreview] = useState<string | null>(null);
  const previewFrame = useRef<HTMLIFrameElement>(null);
  const [variantModal,setVariantModal]=useState<{
    index:number;product:string;symbol:string;variants:TreatmentVariantChoice[];
  }|null>(null);
  const [variantBusy,setVariantBusy]=useState(false);
  const [variantError,setVariantError]=useState("");
  useEffect(() => {
    setRows(draft?.rows ?? []);setSavedNumber("");setVariantModal(null);
  }, [draft]);
  async function saveCard(confirmDuplicate = false) {
    if (!draft || busy) return;
    setBusy(true); setError("");
    try {
      const result = await invoke<SaveResult>("saveDraft", {
        draftId:draft.draftId, reported, performed, findings, confirmDuplicate, rows
      });
      if (result.duplicate) {
        Modal.confirm({
          title:"Karta z tego dokumentu już istnieje",
          content:"Czy na pewno utworzyć kolejną kartę leczenia z tego samego dokumentu?",
          okText:"Zapisz kolejną", cancelText:"Anuluj",
          onOk:() => {void saveCard(true);}
        });
      } else setSavedNumber(result.number ?? "");
    } catch (e) {setError(e instanceof Error ? e.message : String(e));}
    finally {setBusy(false);}
  }
  async function printPreview() {
    if (!draft || busy) return;
    setBusy(true);
    try {
      const data = await invoke<{html:string}>("previewDraft", {
        draftId:draft.draftId, reported, performed, findings, rows
      });
      setPreview(data.html); setError("");
    } catch(e) {setError(e instanceof Error ? e.message : String(e));}
    finally {setBusy(false);}
  }
  async function openVariants(row:DraftRow){
    if(!draft || variantBusy)return;
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
  async function applyVariant(id:number){
    if(!draft || !variantModal || variantBusy)return;
    setVariantBusy(true);setVariantError("");
    try{
      const result=await invoke<Pick<DraftRow,"species"|"route"|"dosing"|"withdrawal"|"indication">>(
        "applyTreatmentVariant",{draftId:draft.draftId,
          index:variantModal.index,variantId:id});
      setRows(current=>current.map(row=>row.index===variantModal.index
        ? {...row,...result} : row));
      setVariantModal(null);setError("");
    }catch(e){setVariantError(e instanceof Error?e.message:String(e));}
    finally{setVariantBusy(false);}
  }
  if (!draft) return <Card>
    <Empty description="Wybierz dokument w zakładce Dokumenty">
      <Button type="primary" onClick={onDocuments}>Przejdź do dokumentów</Button>
    </Empty></Card>;
  const update = async (index: number, key: keyof DraftRow, value: string | boolean) => {
    const field = key === "selected" ? "Drukuj" : editable.find(x => x.key === key)?.field;
    if (!field) return;
    const previous = rows;
    setRows(current => current.map(row => row.index === index ? {...row,[key]:value} : row));
    try { await invoke("updateDraft", {draftId:draft.draftId,index,field,
      ...(key === "selected" ? {checked:value} : {value})}); setError(""); }
    catch (e) { setRows(previous); setError(e instanceof Error ? e.message : String(e)); }
  };
  const columns: ColumnsType<DraftRow> = [
    {title:"Druk",dataIndex:"selected",width:63,render:(value:boolean,row) =>
      <Checkbox checked={value} onChange={e => {void update(row.index,"selected",e.target.checked);}}/>},
    {title:"Produkt",dataIndex:"product",width:240,ellipsis:true,fixed:"left"},
    {title:"Wariant ChPL",key:"variant",width:150,
      render:(_value:unknown,row:DraftRow)=><Button size="small"
        disabled={!row.symbol || variantBusy}
        onClick={()=>{void openVariants(row);}}>
        Wybierz wariant
      </Button>},
    {title:"Ilość",dataIndex:"quantity",width:100},
    {title:"Wskazanie z ChPL",dataIndex:"indication",width:220,ellipsis:true,
      render:(value:string)=><span title={value}>{value||"—"}</span>},
    ...editable.map(({key,title}) => ({
      title, dataIndex:key, width:key==="section" ? 125 : key==="dosing" || key==="withdrawal" ? 245 : 170,
      render:(value:string,row:DraftRow) => key==="section"
        ? <Select aria-label={"Sekcja karty leczenia — "+row.product}
            value={value==="III"?"III":"II"} style={{width:"100%"}}
            options={[{label:"II",value:"II"},{label:"III",value:"III"}]}
            onChange={next=>{void update(row.index,"section",next);}}/>
        : <Input.TextArea autoSize={{minRows:1,maxRows:3}}
        aria-label={title} value={value}
        onChange={e => {
          const next=e.target.value;
          setRows(current => current.map(item => item.index===row.index ? {...item,[key]:next} : item));
        }}
        onBlur={e => {void update(row.index,key,e.target.value);}}/>
    }))
  ];
  return <>
    <div className="page-heading"><div>
      <Typography.Title level={3}>Karta leczenia — {draft.number}</Typography.Title>
      <Typography.Text type="secondary">{draft.payer.split("\n")[0]}</Typography.Text>
    </div><Space>
      <Button onClick={onDocuments}>Dokumenty</Button>
      <Button onClick={()=>{void printPreview();}} loading={busy}>Podgląd wydruku</Button>
      <Button type="primary" onClick={()=>{void saveCard();}} loading={busy}>Zapisz kartę do SQL</Button>
    </Space></div>
    <Card className="treatment-meta">
      <Space wrap>
        <label>Data zgłoszenia <Input type="datetime-local" value={reported} onChange={e=>setReported(e.target.value)}/></label>
        <label>Data wykonania <Input type="datetime-local" value={performed} onChange={e=>setPerformed(e.target.value)}/></label>
        <label>Wyniki badań <Input value={findings} onChange={e=>setFindings(e.target.value)} style={{width:340}}/></label>
      </Space>
    </Card>
    {savedNumber && <Alert className="notice" type="success" showIcon message={"Zapisano kartę "+savedNumber}/>}
    <ErrorNotice error={error}/>
    <Card><Table<DraftRow> rowKey="index" dataSource={rows} columns={columns} size="small"
      scroll={{x:1900,y:"calc(100vh - 415px)"}} pagination={false}/>
      <Typography.Text type="secondary">Wybranych do wydruku: {rows.filter(r => r.selected).length} z {rows.length}</Typography.Text>
    </Card>
    <Modal width="90vw" open={variantModal!==null}
      title={"Dobór wariantu leku — "+(variantModal?.product||"")}
      onCancel={()=>{if(!variantBusy)setVariantModal(null);}}
      footer={<Button disabled={variantBusy} onClick={()=>setVariantModal(null)}>
        Anuluj
      </Button>}>
      <Alert className="notice" type="warning" showIcon
        message="Zweryfikuj gatunek, dawkowanie i karencję z aktualną urzędową ChPL."
        description="Wybór zastąpi dotychczasowy gatunek, drogę podania i dawkowanie; pełną karencję zmieni tylko, gdy katalog zawiera jej dane. Nic nie jest automatycznie zapisywane do archiwum kart."/>
      <ErrorNotice error={variantError}/>
      <Table<TreatmentVariantChoice> rowKey="id" loading={variantBusy}
        dataSource={variantModal?.variants??[]} size="small"
        pagination={{pageSize:12}} scroll={{x:1190,y:340}}
        columns={[
          {title:"Gatunek",dataIndex:"species",width:130},
          {title:"Droga podania",dataIndex:"route",width:140},
          {title:"Dawkowanie",dataIndex:"dose",width:230,ellipsis:true},
          {title:"Karencja — tkanki",dataIndex:"tissues",width:180,ellipsis:true},
          {title:"Karencja — mleko",dataIndex:"milk",width:145,ellipsis:true},
          {title:"Wskazanie",dataIndex:"indication",width:225,ellipsis:true},
          {title:"PDF ChPL",dataIndex:"pdf",width:110,
            render:(value:string)=>value?"Przypisany":"Brak"},
          {title:"Wybór",width:130,
            render:(_value:unknown,row:TreatmentVariantChoice)=>
              <Button type="primary" size="small" disabled={variantBusy ||
                !row.species?.trim() || !row.route?.trim() || !row.dose?.trim()}
                onClick={()=>{void applyVariant(row.id);}}>Wybierz</Button>}
        ]}/>
    </Modal>
    <Modal width="92vw" open={preview!==null} title="Podgląd karty leczenia" onCancel={()=>setPreview(null)}
      footer={<Space><Button onClick={()=>setPreview(null)}>Zamknij</Button>
        <Button type="primary" onClick={()=>previewFrame.current?.contentWindow?.print()}>Drukuj</Button></Space>}>
      {preview && <iframe ref={previewFrame} title="Podgląd karty leczenia" srcDoc={preview}
        style={{width:"100%",height:"72vh",border:0,background:"#fff"}}/>}
    </Modal>
  </>;
}

function Prices() {
  const levels = useRemote<PriceLevel[]>("priceLevels",{},[]);
  const [level,setLevel]=useState<number | null>(null);
  const [net,setNet]=useState(false);
  useEffect(() => { if (level === null && levels.data?.length) setLevel(levels.data[0].number); },[levels.data,level]);
  const prices=useRemote<MedicinePrice[]>("prices",{level,net},[level,net]);
  const [filter,setFilter]=useState("");
  const rows=(prices.data ?? []).filter(p => p.name.toLocaleLowerCase("pl").includes(filter.toLocaleLowerCase("pl")));
  return <>
    <div className="page-heading"><Typography.Title level={3}>Cennik leków</Typography.Title>
      <Button onClick={prices.reload}>Odśwież</Button></div>
    <Card><div className="document-filters">
      <Select loading={levels.busy} value={level} placeholder="Poziom ceny" onChange={setLevel}
        options={(levels.data ?? []).map(x => ({label:x.name,value:x.number}))}/>
      <Input placeholder="Szukaj leku" value={filter} allowClear onChange={e => setFilter(e.target.value)}/>
      <Space><Switch checked={net} onChange={setNet}/> Cena netto</Space>
    </div>
    <ErrorNotice error={levels.error || (level === null ? "" : prices.error)}/>
    <Table rowKey={(row,index) => row.symbol || String(index)} loading={prices.busy && level !== null} dataSource={rows}
      pagination={{pageSize:40,showSizeChanger:true}} size="middle" scroll={{x:950,y:"calc(100vh - 390px)"}}
      columns={[
        {title:"Lek",dataIndex:"name",sorter:(a,b)=>a.name.localeCompare(b.name)},
        {title:net?"Cena netto":"Cena brutto",dataIndex:"gross",width:160,align:"right",
          render:(n:number|null)=>currency(n)},
        {title:"Ostatnia seria",dataIndex:"latestBatch",width:140},
        {title:"Karencja",dataIndex:"withdrawal",width:320,ellipsis:true}
      ]}/>
    </Card>
  </>;
}

function Expiry() {
  const remote=useRemote<MedicineExpiry[]>("expiries",{},[]);
  const [range,setRange]=useState("all");
  const now=Date.now();
  const days=(row:MedicineExpiry)=>row.expires ? Math.ceil((new Date(row.expires).getTime()-now)/86400000) : null;
  const rows=(remote.data ?? []).filter(row => range==="all" ||
    (range==="expired" ? days(row)!==null && days(row)!<0 :
      days(row)!==null && days(row)!>=0 && days(row)!<=Number(range)));
  return <>
    <div className="page-heading"><Typography.Title level={3}>Terminy ważności leków</Typography.Title>
      <Button onClick={remote.reload} loading={remote.busy}>Odśwież</Button></div>
    <Card>
      <Segmented className="expiry-filters" value={range} onChange={setRange} options={[
        {label:"Wszystkie",value:"all"}, {label:"Przeterminowane",value:"expired"},
        {label:"Do 30 dni",value:"30"}, {label:"Do 60 dni",value:"60"}]}/>
      <ErrorNotice error={remote.error}/>
      <Table rowKey={(x,index)=>x.productId+"-"+x.movementId+"-"+index}
        loading={remote.busy} dataSource={rows}
        pagination={{pageSize:40,showSizeChanger:true}} scroll={{x:900,y:"calc(100vh - 400px)"}}
        columns={[
          {title:"Lek",dataIndex:"name",sorter:(a,b)=>a.name.localeCompare(b.name)},
          {title:"Seria",dataIndex:"batch",width:180},
          {title:"Data ważności",dataIndex:"expires",width:160,render:date,
            sorter:(a,b)=>(a.expires ?? "").localeCompare(b.expires ?? "")},
          {title:"Pozostało",dataIndex:"remaining",align:"right",width:140},
          {title:"Magazyn",dataIndex:"warehouseName",width:175},
          {title:"Status",width:145,render:(_,row)=>days(row)===null ?
            <Tag>Brak daty</Tag> : days(row)!<0 ? <Tag color="red">Przeterminowane</Tag> :
              days(row)!<=30 ? <Tag color="orange">Do 30 dni</Tag> : <Tag color="green">Aktualne</Tag>}
        ]}/>
    </Card>
  </>;
}

function SavedCards() {
  const [trash,setTrash]=useState(false);
  const [query,setQuery]=useState("");
  const [selected,setSelected]=useState<SavedCard | null>(null);
  const [card,setCard]=useState<StoredCard|null>(null);
  const [printHtml,setPrintHtml]=useState<string|null>(null);
  const printFrame=useRef<HTMLIFrameElement>(null);
  const [error,setError]=useState("");
  const [purgeTarget,setPurgeTarget]=useState<SavedCard|null>(null);
  const [purgeConfirmation,setPurgeConfirmation]=useState("");
  const [purgeBusy,setPurgeBusy]=useState(false);
  const [purgeError,setPurgeError]=useState("");
  const [notice,setNotice]=useState("");
  const remote=useRemote<SavedCard[]>("savedCards",{trash,query},[trash,query]);
  const open=async (row: SavedCard) => {
    try {setSelected(row);setCard(await invoke<StoredCard>("savedCard",{id:row.id,trash}));setError("");}
    catch(e){setSelected(null);setError(e instanceof Error?e.message:String(e));}
  };
  async function printSaved(row: SavedCard) {
    try {
      const result=await invoke<{html:string}>("renderSavedCard",{id:row.id,trash});
      setPrintHtml(result.html);setError("");
    } catch(e) {setError(e instanceof Error?e.message:String(e));}
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
  const changeTrash=(row:SavedCard)=>{
    Modal.confirm({title:trash?"Przywrócić kartę?":"Przenieść kartę do kosza?",
      content:row.number || row.document,okText:trash?"Przywróć":"Przenieś",cancelText:"Anuluj",
      onOk:async()=>{await invoke("trashCard",{id:row.id,trash});remote.reload();}});
  };
  return <>
    <div className="page-heading"><Typography.Title level={3}>Zapisane karty leczenia</Typography.Title>
      <Space><Segmented value={trash?"trash":"active"} onChange={v=>setTrash(v==="trash")}
        options={[{label:"Archiwum",value:"active"},{label:"Kosz",value:"trash"}]}/>
      <Button onClick={remote.reload}>Odśwież</Button></Space></div>
    <Card>
      <Input.Search allowClear value={query} onChange={e=>setQuery(e.target.value)}
        placeholder="Szukaj karty lub klienta" className="saved-search"/>
      <ErrorNotice error={error || remote.error}/>
      {notice&&<Alert className="notice" type="info" showIcon message={notice}/>}
      <Table rowKey="id" loading={remote.busy} dataSource={remote.data ?? []}
        scroll={{x:800,y:"calc(100vh - 385px)"}} pagination={{pageSize:35}}
        onRow={row=>({onDoubleClick:()=>{void open(row);}})}
        columns={[
          {title:"Numer karty",dataIndex:"number",width:150},
          {title:"Data",dataIndex:"date",render:date,width:145},
          {title:"Kontrahent",dataIndex:"client",ellipsis:true},
          {title:"Dokument",dataIndex:"document",width:175},
          {title:"Czynności",width:230,render:(_,row)=><Space>
            <Button size="small" onClick={()=>{void open(row);}}>Otwórz</Button>
            <Button size="small" onClick={()=>{void printSaved(row);}}>Drukuj</Button>
            <Button size="small" danger={!trash} onClick={()=>changeTrash(row)}>{trash?"Przywróć":"Kosz"}</Button>
            {trash&&<Button size="small" danger onClick={()=>{setPurgeTarget(row);setPurgeConfirmation("");setPurgeError("");setNotice("");}}>
              Usuń trwale
            </Button>}
          </Space>}
        ]}/>
    </Card>
    <Modal open={purgeTarget!==null} title="Trwale usunąć kartę leczenia?"
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
    <Modal open={!!selected} title={selected?.number||"Karta leczenia"}
      width={1100} footer={<Button onClick={()=>{setSelected(null);setCard(null);}}>Zamknij</Button>}
      onCancel={()=>{setSelected(null);setCard(null);}}>
      {!card ? <Spin/> : <>
        <Descriptions bordered column={2} size="small"
          items={Object.entries(card.fields).map(([key,value])=>({key,label:key,children:value}))}/>
        <Table size="small" dataSource={card.rows} pagination={false} scroll={{x:1300}}
          rowKey={(_,index)=>String(index)} columns={[
            {title:"Lek",dataIndex:"Produkt",width:230},
            {title:"Seria",dataIndex:"Seria",width:140},
            {title:"Gatunek",dataIndex:"Gatunek",width:130},
            {title:"Dawkowanie",dataIndex:"Dawkowanie i okres stosowania",width:260},
            {title:"Karencja",dataIndex:"Karencja",width:260},
            {title:"Rozpoznanie",dataIndex:"Rozpoznanie",width:180}
          ]}/>
      </>}
    </Modal>
    <Modal width="92vw" open={printHtml!==null} title="Podgląd zapisanej karty leczenia"
      onCancel={()=>setPrintHtml(null)} footer={<Space>
        <Button onClick={()=>setPrintHtml(null)}>Zamknij</Button>
        <Button type="primary" onClick={()=>printFrame.current?.contentWindow?.print()}>Drukuj</Button>
      </Space>}>
      {printHtml&&<iframe ref={printFrame} title="Wydruk zapisanej karty" srcDoc={printHtml}
        style={{width:"100%",height:"72vh",border:0,background:"#fff"}}/>}
    </Modal>
  </>;
}

function ClinicSettingsPanel() {
  const [form]=Form.useForm();
  const [busy,setBusy]=useState(false);
  const [notice,setNotice]=useState("");
  const [error,setError]=useState("");
  useEffect(()=>{
    invoke("getClinic").then(data=>form.setFieldsValue(data)).catch(e=>setError(String(e)));
  },[form]);
  const save=async(values:Record<string,unknown>)=>{
    setBusy(true);setError("");setNotice("");
    try{await invoke("saveClinic",values);setNotice("Dane lecznicy i lekarza zapisane.");}
    catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  };
  return <Card className="settings-card">
    <Typography.Title level={4}>Lecznica i lekarz</Typography.Title>
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
      <Button type="primary" htmlType="submit" loading={busy}>Zapisz dane lecznicy</Button>
    </Form>
  </Card>;
}
interface ArchiveConfig {
  configured:boolean;server:string;windowsAuthentication:boolean;userName:string;trustCertificate:boolean;
}
function ArchiveSettingsPanel() {
  const [form]=Form.useForm();
  const [error,setError]=useState("");
  const [notice,setNotice]=useState("");
  const [busy,setBusy]=useState(false);
  useEffect(()=>{
    invoke<ArchiveConfig>("getArchiveSettings").then(data=>form.setFieldsValue({...data,password:"",rememberPassword:false}))
      .catch(e=>setError(String(e)));
  },[form]);
  async function save(values:Record<string,unknown>) {
    setError("");setNotice("");setBusy(true);
    try { await invoke("saveArchiveSettings",values);setNotice("Połączenie z archiwum kart SQL sprawdzone i zapisane."); }
    catch(e){setError(e instanceof Error?e.message:String(e));}
    finally {setBusy(false);}
  }
  return <Card className="settings-card">
    <Typography.Title level={4}>Archiwum kart SQL</Typography.Title>
    <Typography.Text type="secondary">Baza programowa KartyLeczenia — niezależna od bazy Subiekta.</Typography.Text>
    <ErrorNotice error={error}/>
    {notice&&<Alert type="success" className="notice" showIcon message={notice}/>}
    <Form form={form} layout="vertical" onFinish={values=>{void save(values);}}>
      <Form.Item label="Serwer bazy KartyLeczenia" name="server" rules={[{required:true}]}><Input/></Form.Item>
      <Form.Item label="Uwierzytelnianie Windows" name="windowsAuthentication" valuePropName="checked"><Switch/></Form.Item>
      <Form.Item label="Login SQL" name="userName"><Input/></Form.Item>
      <Form.Item label="Hasło SQL" name="password"><Input.Password autoComplete="new-password"/></Form.Item>
      <Form.Item name="rememberPassword" valuePropName="checked"><Checkbox>Zapamiętaj hasło (DPAPI)</Checkbox></Form.Item>
      <Form.Item name="trustCertificate" valuePropName="checked"><Checkbox>Zaufaj certyfikatowi serwera</Checkbox></Form.Item>
      <Button type="primary" htmlType="submit" loading={busy}>Sprawdź i zapisz archiwum</Button>
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
      title:"Zastąpić całą bazę KartyLeczenia?",
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
let checkedOnStartup=false;
function AutomaticUpdateCheck(){
  useEffect(()=>{
    if(checkedOnStartup || !isDesktop())return;
    checkedOnStartup=true;
    void invoke<InstallUpdateStatus>("getUpdateStatus").then(async status=>{
      if(status.isServer || !status.installed)return; // Portable previews never launch old SQL installers.
      const result=await invoke<UpdateResult>("checkUpdate");
      if(!result.release?.newer)return;
      Modal.confirm({
        title:"Dostępna aktualizacja KartyLeczenia",
        content:"Wersja "+result.release.version+
          " jest dostępna na serwerze SQL. Obecna wersja: "+result.currentVersion+
          ". Czy pobrać instalator i zaktualizować program?",
        okText:"Pobierz i zainstaluj",cancelText:"Później",
        onOk:async()=>{await invoke("installUpdate");}
      });
    }).catch(()=>{/* Settings offers an explicit retry if SQL is temporarily unavailable. */});
  },[]);
  return null;
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
  function install(){
    if(!data?.release?.newer||busy)return;
    Modal.confirm({
      title:"Uruchomić aktualizację ReactKL?",
      content:<div>
        <Typography.Paragraph>
          Zainstalowana wersja: <strong>{data.currentVersion}</strong>
        </Typography.Paragraph>
        <Typography.Paragraph>
          Nowa wersja w SQL: <strong>{data.release.version}</strong>
        </Typography.Paragraph>
        <Typography.Paragraph>
          Program pobierze instalator do folderu tymczasowego, sprawdzi SHA-256
          i zgodność wersji, a następnie otworzy instalator Windows i zamknie ReactKL.
        </Typography.Paragraph>
      </div>,
      okText:"Pobierz i uruchom instalator",
      cancelText:"Anuluj",
      onOk:async()=>{
        setBusy(true);setError("");setNotice("");
        try{
          await invoke("installUpdate");
          setNotice("Uruchomiono instalator ReactKL.");
        }catch(e){setError(e instanceof Error?e.message:String(e));}
        finally{setBusy(false);}
      }
    });
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
      <Typography.Text>Obecna wersja: <strong>{data?.currentVersion??"—"}</strong></Typography.Text>
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

function SettingsPage() {
  const [form]=Form.useForm();
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState("");
  const [notice,setNotice]=useState("");
  useEffect(()=>{
    invoke<SqlSettings>("getSettings").then(data=>{
      form.setFieldsValue({...data,rememberPassword:false,password:""});
      if(data.warning)setNotice(data.warning);
    }).catch(e=>setError(String(e)));
  },[form]);
  const save=async(values:Record<string,unknown>)=>{
    setBusy(true);setError("");setNotice("");
    try{await invoke("saveSettings",values);setNotice("Połączenie sprawdzone i ustawienia zapisane.");}
    catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  };
  return <>
    <div className="page-heading"><Typography.Title level={3}>Ustawienia połączenia Subiekta GT</Typography.Title></div>
    <Card className="settings-card">
      <Alert className="notice" showIcon type="info"
        message="Konfiguracja SQL jest zachowana z poprzedniej instalacji; nowy interfejs działa wyłącznie w React."/>
      <ErrorNotice error={error}/>
      {notice && <Alert className="notice" type="success" showIcon message={notice}/>}
      <Form form={form} layout="vertical" onFinish={values=>{void save(values);}}>
        <Form.Item name="server" label="Serwer SQL" rules={[{required:true,message:"Podaj serwer"}]}>
          <Input placeholder="NAZWA-SERWERA\INSTANCJA"/>
        </Form.Item>
        <Form.Item name="database" label="Baza podmiotu Subiekta" rules={[{required:true,message:"Podaj bazę"}]}>
          <Input/>
        </Form.Item>
        <Form.Item name="windowsAuthentication" label="Uwierzytelnianie Windows" valuePropName="checked">
          <Switch/>
        </Form.Item>
        <Form.Item name="userName" label="Login SQL"><Input autoComplete="username"/></Form.Item>
        <Form.Item name="password" label="Hasło SQL (pozostaw puste, aby zachować zapamiętane)"><Input.Password autoComplete="new-password"/></Form.Item>
        <Form.Item name="rememberPassword" valuePropName="checked"><Checkbox>Zapamiętaj hasło (ochrona DPAPI Windows)</Checkbox></Form.Item>
        <Form.Item name="trustCertificate" valuePropName="checked"><Checkbox>Zaufaj certyfikatowi znanego serwera SQL</Checkbox></Form.Item>
        <Form.Item name="drugGroup" label="Grupa leków"><Input/></Form.Item>
        <Form.Item name="warehouseId" label="Identyfikator magazynu"><InputNumber min={1} style={{width:"100%"}}/></Form.Item>
        <Button type="primary" htmlType="submit" loading={busy}>Sprawdź i zapisz połączenie</Button>
      </Form>
    </Card>
    <ClinicSettingsPanel/>
    <ArchiveSettingsPanel/>
    <BackupPanel/>
    <RestorePanel/>
    <UpdatePanel/>
  </>;
}

export default function App() {
  const [layout,setLayout]=useState<LayoutName>(()=>saved("kl.layout",layouts,"modern"));
  const [palette,setPalette]=useState<PaletteName>(()=>saved("kl.palette",palettes,"light"));
  const [section,setSection]=useState<Section>("documents");
  const [draft,setDraft]=useState<Draft|null>(null);
  const [catalogueDirty,setCatalogueDirty]=useState(false);
  const navigate=(next:Section)=>{
    if(next===section)return;
    if(section==="catalogue"&&catalogueDirty){
      Modal.confirm({
        title:"Niezapisane zmiany w katalogu",
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
  const primary=palette==="sea"?"#15836f":palette==="dark"?"#78a5fa":"#1768d8";
  const appearance=useMemo(()=>({
    algorithm:palette==="dark"?theme.darkAlgorithm:theme.defaultAlgorithm,
    token:{colorPrimary:primary,borderRadius:layout==="classic"?4:layout==="modern"?12:8,
      fontFamily:"Segoe UI,system-ui,sans-serif",fontSize:14},
    components:{Segmented:{itemSelectedBg:primary,itemSelectedColor:"#fff"},
      Table:{headerBg:palette==="dark"?"#273447":"#f3f6fa"}}
  }),[palette,layout,primary]);
  const nav=<nav aria-label="Nawigacja KartyLeczenia" className="main-nav">
    {sections.map(x=><button key={x.id} type="button"
      className={"nav-item "+(section===x.id?"is-active":"")}
      onClick={()=>navigate(x.id)}>{x.name}</button>)}
  </nav>;
  const controls=<div className="appearance-controls">
    <Select aria-label="Układ" value={layout} onChange={setLayout} options={[
      {label:"Modern — menu boczne",value:"modern"}, {label:"Classic — zakładki",value:"classic"},
      {label:"Fluent — Windows 11",value:"fluent"}]}/>
    <Select aria-label="Motyw" value={palette} onChange={setPalette} options={[
      {label:"Jasny",value:"light"}, {label:"Ciemny",value:"dark"}, {label:"Morski",value:"sea"}]}/>
  </div>;
  const content=section==="documents"?<Documents onOpen={next=>{setDraft(next);setSection("treatment");}}/>:
    section==="treatment"?<Treatment draft={draft} onDocuments={()=>setSection("documents")}/>:
    section==="prices"?<Prices/>:section==="catalogue"?<CataloguePage onDirtyChange={setCatalogueDirty}/>:
    section==="expiry"?<Expiry/>:
    section==="saved"?<SavedCards/>:<SettingsPage/>;
  return <ConfigProvider theme={appearance}>
    <AutomaticUpdateCheck/>
    <div className={"app-shell layout-"+layout+" palette-"+palette}>
      {layout!=="classic"&&<aside className="sidebar">
        <div className="sidebar-brand"><div className="brand-mark">KL</div>
          <div><strong>KartyLeczenia</strong><small>ReactKL · SQL</small></div></div>
        {nav}
        <div className="sidebar-footer">Interfejs React + WebView2<br/>Dane pozostają w SQL Server</div>
      </aside>}
      <div className="workspace">
        <header className="topbar">
          <div className="topbar-title"><strong>{layout==="classic"?"KartyLeczenia":sections.find(x=>x.id===section)?.name}</strong></div>
          {controls}
        </header>
        {layout==="classic"&&<div className="top-navigation">{nav}</div>}
        <main className="main-content">
          {!isDesktop()&&<Alert type="info" className="notice" showIcon
            message="Podgląd przeglądarkowy. Połączenie SQL działa w aplikacji Windows ReactKL."/>}
          {content}
        </main>
        <footer className="statusline"><span>ReactKL — WebView2 / C#</span>
          <span>Układ {layout} · motyw {palette}</span></footer>
      </div>
    </div>
  </ConfigProvider>;
}
