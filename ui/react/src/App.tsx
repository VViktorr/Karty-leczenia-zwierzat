import { useEffect, useMemo, useState, useRef } from "react";
import {
  Alert, Button, Card, Checkbox, ConfigProvider, DatePicker, Descriptions, Empty,
  Form, Input, InputNumber, Modal, Segmented, Select, Space, Spin, Switch, Table,
  Tag, Typography, theme, message
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke, isDesktop } from "./bridge";
import type { DocumentKind, DocumentRow, DocumentsResult, DatePeriod } from "./bridge";

type LayoutName = "classic" | "modern" | "fluent";
type PaletteName = "light" | "dark" | "sea";
type Section = "documents" | "prices" | "treatment" | "saved" | "expiry" | "settings";
interface DraftRow {
  index: number; product: string; quantity: string; batch: string;
  species: string; route: string; dosing: string; withdrawal: string;
  description: string; animals: string; diagnosis: string; selected: boolean;
}
interface Draft {
  draftId: string; number: string; payer: string; recipient: string; rows: DraftRow[];
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
  {key:"diagnosis",field:"Rozpoznanie",title:"Rozpoznanie"}
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
  useEffect(() => { setRows(draft?.rows ?? []); setSavedNumber(""); }, [draft]);
  async function saveCard(confirmDuplicate = false) {
    if (!draft || busy) return;
    setBusy(true); setError("");
    try {
      const result = await invoke<SaveResult>("saveDraft", {
        draftId:draft.draftId, reported, performed, findings, confirmDuplicate
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
        draftId:draft.draftId, reported, performed, findings
      });
      setPreview(data.html); setError("");
    } catch(e) {setError(e instanceof Error ? e.message : String(e));}
    finally {setBusy(false);}
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
    {title:"Ilość",dataIndex:"quantity",width:100},
    ...editable.map(({key,title}) => ({
      title, dataIndex:key, width:key==="dosing" || key==="withdrawal" ? 245 : 170,
      render:(value:string,row:DraftRow) => <Input.TextArea autoSize={{minRows:1,maxRows:3}}
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
  const [error,setError]=useState("");
  const remote=useRemote<SavedCard[]>("savedCards",{trash,query},[trash,query]);
  const open=async (row: SavedCard) => {
    try {setSelected(row);setCard(await invoke<StoredCard>("savedCard",{id:row.id,trash}));setError("");}
    catch(e){setSelected(null);setError(e instanceof Error?e.message:String(e));}
  };
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
      <Table rowKey="id" loading={remote.busy} dataSource={remote.data ?? []}
        scroll={{x:800,y:"calc(100vh - 385px)"}} pagination={{pageSize:35}}
        onRow={row=>({onDoubleClick:()=>{void open(row);}})}
        columns={[
          {title:"Numer karty",dataIndex:"number",width:150},
          {title:"Data",dataIndex:"date",render:date,width:145},
          {title:"Kontrahent",dataIndex:"client",ellipsis:true},
          {title:"Dokument",dataIndex:"document",width:175},
          {title:"Czynności",width:155,render:(_,row)=><Space>
            <Button size="small" onClick={()=>{void open(row);}}>Otwórz</Button>
            <Button size="small" danger={!trash} onClick={()=>changeTrash(row)}>{trash?"Przywróć":"Kosz"}</Button>
          </Space>}
        ]}/>
    </Card>
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
  </>;
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
        message="ReactKL korzysta z tej samej konfiguracji SQL co wcześniejsza aplikacja, ale ma własny interfejs ustawień."/>
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
  </>;
}

export default function App() {
  const [layout,setLayout]=useState<LayoutName>(()=>saved("kl.layout",layouts,"modern"));
  const [palette,setPalette]=useState<PaletteName>(()=>saved("kl.palette",palettes,"light"));
  const [section,setSection]=useState<Section>("documents");
  const [draft,setDraft]=useState<Draft|null>(null);
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
      onClick={()=>setSection(x.id)}>{x.name}</button>)}
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
    section==="prices"?<Prices/>:section==="expiry"?<Expiry/>:
    section==="saved"?<SavedCards/>:<SettingsPage/>;
  return <ConfigProvider theme={appearance}>
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
