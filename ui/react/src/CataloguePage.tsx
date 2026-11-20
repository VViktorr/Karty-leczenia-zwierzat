import {useListPagination} from "./ListPagination";
import {useListColumnLayout} from "./ListColumnLayout";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  Alert, Button, Card, Empty, Input, Modal, Space, Table,
  Tag, Typography, Tooltip, message
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke } from "./bridge";

interface CatalogueListing { symbol: string; name: string; status: string; variants: number; }
interface SubiektProduct { symbol:string; name:string; reason?:string; }
interface CatalogueVariant {
  id: number | null;
  species: string; indication: string; route: string; dose: string;
  tissues: string; milk: string; remarks: string; source: string;
  short: string; price: string; pdf: string;
}
interface CatalogueProduct {
  symbol: string; name: string; rplId: string; description: string;
  revision: string; variants: CatalogueVariant[];
}
interface ChplProposal {
  variants: CatalogueVariant[];
  evidence: string;
  warnings: string[];
}
interface PendingChplProposal extends ChplProposal {
  path: string;
  filename: string;
}
interface UiVariant extends CatalogueVariant { key: string; }

const textFields = [
  { key: "species", title: "Gatunek", width: 150 },
  { key: "indication", title: "Wskazania", width: 240 },
  { key: "route", title: "Droga podania", width: 145 },
  { key: "dose", title: "Dawkowanie", width: 230 },
  { key: "tissues", title: "Tkanki jadalne", width: 160 },
  { key: "milk", title: "Mleko", width: 145 },
  { key: "remarks", title: "Uwagi", width: 190 },
  { key: "source", title: "Źródło danych", width: 160 },
  { key: "short", title: "Skrót karencji", width: 160 },
  { key: "price", title: "Karencja w cenniku", width: 200 }
] as const;

function keyForVariant(variant: CatalogueVariant) {
  return variant.id == null ? "new-" + crypto.randomUUID() : String(variant.id);
}

function emptyVariant(): UiVariant {
  return {
    id: null, key: "new-" + crypto.randomUUID(), species: "", indication: "",
    route: "", dose: "", tissues: "", milk: "", remarks: "", source: "",
    short: "", price: "", pdf: ""
  };
}

export default function CataloguePage({
  onDirtyChange, initialEntry, onPickerOpened
}: {
  onDirtyChange?: (value: boolean) => void;
  initialEntry: {symbol:string;direct:boolean} | null;
  onPickerOpened: () => void;
}) {
  const [input, setInput] = useState("");
  const [query, setQuery] = useState("");
  const [list, setList] = useState<CatalogueListing[]>([]);
  const [listBusy, setListBusy] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  const [savedNotice, setSavedNotice] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [original, setOriginal] = useState<CatalogueProduct | null>(null);
  const [editing, setEditing] = useState<CatalogueProduct | null>(null);
  const [variants, setVariants] = useState<UiVariant[]>([]);
  const [dirty, setDirty] = useState(false);
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);
  const [pdfUrl, setPdfUrl] = useState<string | null>(null);
  const [pdfName, setPdfName] = useState("");
  const [proposal, setProposal] = useState<PendingChplProposal | null>(null);
  const [version, setVersion] = useState(0);
  const [missingRows,setMissingRows]=useState<SubiektProduct[]|null>(null);
  const [missingOpen,setMissingOpen]=useState(false);
  const [missingBusy,setMissingBusy]=useState(false);
  const [missingTotal,setMissingTotal]=useState(0);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [pickerQuery, setPickerQuery] = useState("");
  const [pickerRows, setPickerRows] = useState<SubiektProduct[]>([]);
  const [pickerSelection, setPickerSelection] = useState<string | null>(null);
  const [pickerError, setPickerError] = useState("");
  const [pickerBusy, setPickerBusy] = useState(false);
  const pickerRequest = useRef(0);
  const [msgApi, contextHolder] = message.useMessage();

  useEffect(() => {
    const timeout = setTimeout(() => setQuery(input.trim()), 280);
    return () => clearTimeout(timeout);
  }, [input]);

  useEffect(() => {
    let cancelled = false;
    setListBusy(true);
    invoke<CatalogueListing[]>("searchCatalogue", { query })
      .then(data => { if (!cancelled) setList(data); })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (!cancelled) setListBusy(false); });
    return () => { cancelled = true; };
  }, [query, version]);

  function loadProduct(data: CatalogueProduct) {
    setOriginal(data);
    setEditing(data);
    setVariants(data.variants.map(item => ({ ...item, key: keyForVariant(item) })));
    setSelected([]);
    setSavedNotice("");
    setDirty(false);
  }

  async function openProduct(row: CatalogueListing, force = false) {
    if (dirty && !force) {
      Modal.confirm({
        className:"kl-dialog-catalogue-product-discard", title: "Niezapisane zmiany",
        content: "Zmiany w bieżącej kartotece nie zostały zapisane. Otworzyć inną kartotekę?",
        okText: "Odrzuć i otwórz",
        cancelText: "Pozostań",
        onOk: () => { void openProduct(row, true); }
      });
      return;
    }
    setWorking(true);
    setError("");
    try {
      const product = await invoke<CatalogueProduct>("readCatalogueProduct", { symbol: row.symbol });
      loadProduct(product);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }

  const searchSubiekt = useCallback(async (query: string) => {
    const request = ++pickerRequest.current;
    setPickerBusy(true);
    setPickerError("");
    setPickerRows([]);
    setPickerSelection(null);
    try {
      const rows = await invoke<SubiektProduct[]>("searchSubiektCatalogue", { query: query.trim() });
      if (request !== pickerRequest.current) return;
      setPickerRows(rows);
      if (rows.length === 1) setPickerSelection(rows[0].symbol);
    } catch (error) {
      if (request === pickerRequest.current)
        setPickerError(error instanceof Error ? error.message : String(error));
    } finally {
      if (request === pickerRequest.current) setPickerBusy(false);
    }
  }, []);

  const handledEntry = useRef<typeof initialEntry>(null);
  useEffect(() => {
    if (!initialEntry || handledEntry.current === initialEntry) return;
    handledEntry.current = initialEntry;
    onPickerOpened();
    if (initialEntry.direct) {
      void openSubiektProduct(initialEntry.symbol);
    } else {
      setPickerQuery(initialEntry.symbol);
      setPickerOpen(true);
      void searchSubiekt(initialEntry.symbol);
    }
  }, [initialEntry, onPickerOpened, searchSubiekt]);

  function openPicker() {
    const symbol = editing?.symbol ?? "";
    setPickerQuery(symbol);
    setPickerOpen(true);
    void searchSubiekt(symbol);
  }

  function closePicker() {
    ++pickerRequest.current;
    setPickerOpen(false);
  }

  async function checkMissing() {
    if(missingBusy)return;
    setMissingBusy(true);setError("");setPickerError("");
    try{
      const data=await invoke<{items:SubiektProduct[];count:number;total:number}>(
        "missingCatalogueProducts",{refresh:true});
      setMissingRows(data.items);setMissingTotal(data.total);setMissingOpen(true);
    }catch(error){setError(error instanceof Error?error.message:String(error));}
    finally{setMissingBusy(false);}
  }

  async function openSubiektProduct(symbol: string, force = false) {
    if (working) return;
    if (dirty && !force) {
      Modal.confirm({
        className:"kl-dialog-catalogue-subiekt-discard", title: "Niezapisane zmiany katalogu",
        content: "Odrzucić bieżące zmiany i przejść do wskazanego produktu Subiekta?",
        okText: "Odrzuć i przejdź", cancelText: "Pozostań",
        onOk: () => openSubiektProduct(symbol, true)
      });
      return;
    }
    setWorking(true);
    setError("");
    setPickerError("");
    try {
      const product = await invoke<CatalogueProduct>("openSubiektCatalogueProduct", { symbol });
      loadProduct(product);
      if (!product.revision) {
        setOriginal(null);
        setDirty(true);
      }
      closePicker();
      setMissingOpen(false);
    } catch (error) {
      const reason = error instanceof Error ? error.message : String(error);
      if (pickerOpen || missingOpen) setPickerError(reason);
      else setError(reason);
    } finally { setWorking(false); }
  }

  function changeField(field: "rplId" | "description", value: string) {
    setEditing(current => current ? { ...current, [field]: value } : current);
    setDirty(true);
  }

  function changeVariant(key: string, field: keyof CatalogueVariant, value: string) {
    setVariants(current => current.map(row => row.key === key ? { ...row, [field]: value } : row));
    setDirty(true);
  }

  function addVariant() {
    setVariants(current => [...current, emptyVariant()]);
    setDirty(true);
  }

  function discardNewVariant(key: string) {
    setVariants(current => current.filter(row => row.key !== key));
    setSelected(current => current.filter(value => value !== key));
    setDirty(true);
  }

  async function choosePdf() {
    if (!editing) return;
    if (selected.length === 0) {
      setError("Zaznacz co najmniej jeden wariant, do którego należy przypisać ChPL.");
      return;
    }
    setWorking(true);
    setError("");
    try {
      const result = await invoke<{cancelled: boolean; path?: string; name?: string}>(
        "chooseCataloguePdf");
      if (!result.cancelled && result.path) {
        setVariants(rows => rows.map(item =>
          selected.includes(item.key) ? { ...item, pdf: result.path! } : item
        ));
        setDirty(true);
        void msgApi.success("Wybrano " + (result.name ?? "PDF") +
          ". Powiązanie zostanie zapisane dopiero po zatwierdzeniu kartoteki.");
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally { setWorking(false); }
  }

  async function suggestFromPdf() {
    if (!editing || working) return;
    setWorking(true);
    setError("");
    try {
      const picked = await invoke<{cancelled:boolean;path?:string;name?:string}>(
        "chooseCataloguePdf");
      if (picked.cancelled || !picked.path) return;
      const result = await invoke<ChplProposal>("analyseCataloguePdf", {path:picked.path});
      setProposal({...result,path:picked.path,filename:picked.name ?? "ChPL.pdf"});
    } catch(e) {
      setError(e instanceof Error?e.message:String(e));
    } finally {setWorking(false);}
  }

  function acceptProposal() {
    if (!proposal) return;
    let added=0, filled=0, skipped=0;
    const next = [...variants];
    for (const suggestion of proposal.variants) {
      const species = suggestion.species.trim();
      const route = suggestion.route.trim();
      const match = next.find(row =>
        row.species.trim().toLocaleLowerCase("pl")===species.toLocaleLowerCase("pl") &&
        row.route.trim().toLocaleLowerCase("pl")===route.toLocaleLowerCase("pl"));
      if (match) {
        const update = {...match};
        for (const field of ["indication","dose","tissues","milk","source"] as const) {
          if (!update[field].trim() && suggestion[field]?.trim()) {
            update[field] = suggestion[field];filled++;
          }
        }
        if (!update.pdf && proposal.path) {update.pdf=proposal.path;filled++;}
        next[next.indexOf(match)] = update;
      } else if (species && route && suggestion.dose.trim()) {
        next.push({...emptyVariant(),...suggestion,id:null,key:"new-"+crypto.randomUUID(),
          pdf:proposal.path});
        added++;
      } else skipped++;
    }
    setVariants(next);
    if (added || filled) setDirty(true);
    void msgApi.info(
      "Propozycje w edytorze: dodano "+added+
      " wariantów, uzupełniono "+filled+" pustych pól, pominięto "+skipped+
      ". Sprawdź każdą pozycję przed zapisem SQL."
    );
    setProposal(null);
  }

  const closePdf = useCallback(() => { setPdfUrl(null); }, []);

  useEffect(() => () => {
    if (pdfUrl) URL.revokeObjectURL(pdfUrl);
  }, [pdfUrl]);

  async function viewPdf(path: string) {
    if (!path) return;
    setWorking(true);
    setError("");
    try {
      const result = await invoke<{base64: string; name: string}>(
        "readCataloguePdf", { path });
      // The PDF remains in the SQL catalogue; only a temporary local Blob URL
      // is created for the built-in WebView2 PDF viewer.
      const binary = atob(result.base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
      const url = URL.createObjectURL(new Blob([bytes], { type: "application/pdf" }));
      setPdfName(result.name);
      setPdfUrl(url);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally { setWorking(false); }
  }

  async function saveProduct() {
    if (!editing) return;
    if(!editing.symbol.trim()||!editing.name.trim()){
      setError("Wpisz symbol i nazwę kartoteki leku.");
      return;
    }
    if (variants.some(v => v.id === null && (!v.species.trim() || !v.route.trim() || !v.dose.trim()))) {
      setError("Nowy wariant musi zawierać gatunek, drogę podania i dawkowanie.");
      return;
    }
    setWorking(true);
    setError("");
    setSavedNotice("");
    try {
      const product: CatalogueProduct = {
        ...editing,
        variants: variants.map(({ key: _key, ...rest }) => rest)
      };
      await invoke("saveCatalogueProduct", { product });
      const fresh = await invoke<CatalogueProduct>("readCatalogueProduct", {
        symbol: editing.symbol
      });
      loadProduct(fresh);
      setSavedNotice("Zapisano kartotekę i warianty w SQL.");
      setVersion(value => value + 1);
    } catch (e) {
      setError((e instanceof Error ? e.message : String(e)) +
        " Jeżeli ktoś zmienił tę kartotekę na innym stanowisku, odśwież ją przed ponownym zapisem.");
    } finally { setWorking(false); }
  }

  const columns: ColumnsType<UiVariant> = [
    ...textFields.map(({ key, title, width }) => ({
      title, key, width,
      render: (_: unknown, row: UiVariant) =>
        <Input.TextArea
          aria-label={title + " " + row.key}
          value={row[key]}
          autoSize={{ minRows: 1, maxRows: 4 }}
          onChange={event => changeVariant(row.key, key, event.target.value)}
        />
    })),
    {
      title: "ChPL (PDF)", key: "pdf", width: 235,
      render: (_: unknown, row) => <Space direction="vertical" size={3}>
        <Tooltip title={row.pdf || "Brak powiązanego dokumentu PDF"}>
          <Typography.Text ellipsis style={{ width: 205, maxWidth: 205 }}>
            {row.pdf ? row.pdf.split("/").at(-1) : "Nie przypisano"}
          </Typography.Text>
        </Tooltip>
        <Space>
          <Button size="small" disabled={!row.pdf}
            onClick={() => { void viewPdf(row.pdf); }}>Otwórz PDF</Button>
          {row.id === null && <Button size="small" danger
            onClick={() => discardNewVariant(row.key)}>Usuń nowy</Button>}
        </Space>
      </Space>
    }
  ];

  const productsLayout = useListColumnLayout<CatalogueListing>("catalogue-products", [
    { title: "Lek", dataIndex: "name", ellipsis: true },
    { title: "Warianty", dataIndex: "variants", width: 85, align: "right" }
  ], false);
  const variantsLayout = useListColumnLayout("catalogue-variants", columns, false);

  const productsPagination=useListPagination("catalogue",list.length,20);
  const missingPagination=useListPagination("catalogue-missing",missingRows?.length??0,20);
  const pickerPagination=useListPagination("catalogue-picker",pickerRows.length);

  return <>
    {contextHolder}
    <div className="page-heading">
      <div>
        <Typography.Title level={3}>Katalog leków i ChPL</Typography.Title>
        <Typography.Text type="secondary">
          Dane wariantów i dokumenty urzędowe z bazy KartyLeczenia SQL.
        </Typography.Text>
      </div>
      <Space wrap>
        <Button disabled={missingBusy||working} loading={missingBusy}
          onClick={()=>{void checkMissing();}}>Sprawdź braki w katalogu</Button>
        <Button type="primary" disabled={working}
          onClick={openPicker}>Dodaj / edytuj w katalogu</Button>
        <Button onClick={() => setVersion(value => value + 1)}
          loading={listBusy}>Odśwież katalog</Button>
      </Space>
    </div>
    <Alert className="notice" showIcon type="info"
      message="Zmiana danych klinicznych wymaga ręcznej weryfikacji z aktualną ChPL."
      description="Dołączenie PDF nie zmienia automatycznie gatunku, dawkowania ani karencji. Zapis jest zatwierdzany osobno." />
    {error && <Alert className="notice" type="error" showIcon message={error} />}
    {savedNotice && <Alert className="notice" type="success" showIcon message={savedNotice} />}
    <div className="catalogue-layout">
      <Card className="catalogue-list">
        <Input.Search allowClear value={input}
          placeholder="Nazwa lub symbol leku..."
          onSearch={value=>{
            const trimmed=value.trim();
            if(trimmed===query)setVersion(value=>value+1);
            else setQuery(trimmed);
          }}
          onChange={event => setInput(event.target.value)} />
        <Table<CatalogueListing> {...productsLayout.tableProps} rowKey="symbol" size="small"
          loading={listBusy} dataSource={list}
          pagination={productsPagination}
          scroll={{ y: "calc(100vh - 370px)" }}
          onRow={row => ({
            onClick: () => { void openProduct(row); }
          })}
          rowClassName={row =>
            editing?.symbol === row.symbol ? "catalogue-selected" : ""}
          locale={{ emptyText: <Empty description="Brak produktów" /> }}
          columns={productsLayout.columns}
        />
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Pierwszych 200 kartotek spełniających filtr.
        </Typography.Text>
      </Card>
      <Card className="catalogue-editor">
        {!editing ? <Empty description="Wybierz lek z katalogu" /> : <>
          <div className="catalogue-editor-heading">
            <div>
              <Typography.Title level={4} style={{ marginBottom: 4 }}>
                {editing.name || (original?"Kartoteka leku":"Nowa kartoteka leku")}
              </Typography.Title>
              <Space>
                <Tag>{editing.symbol}</Tag>
                <Tag color="blue">{variants.length} wariantów</Tag>
                {dirty && <Tag color="orange">Niezapisane zmiany</Tag>}
              </Space>
            </div>
            <Space>
              <Button disabled={working} onClick={() => {
                if (original) loadProduct(original);
                else {setEditing(null);setVariants([]);setDirty(false);}
              }}>Cofnij zmiany</Button>
              <Button type="primary" disabled={!dirty} loading={working}
                onClick={() => { void saveProduct(); }}>Zapisz w SQL</Button>
            </Space>
          </div>
          <div className="catalogue-product-fields">
            <label>Symbol leku (identyfikator w katalogu SQL)
              <Input value={editing.symbol} readOnly />
            </label>
            <label>Nazwa leku
              <Input value={editing.name} readOnly />
            </label>
            <label>Numer RPL
              <Input value={editing.rplId}
                onChange={event => changeField("rplId", event.target.value)} />
            </label>
            <label>Opis
              <Input.TextArea autoSize={{ minRows: 1, maxRows: 3 }}
                value={editing.description}
                onChange={event => changeField("description", event.target.value)} />
            </label>
          </div>
          <div className="catalogue-variant-toolbar">
            <Space wrap>
              <Button onClick={addVariant}>Dodaj wariant</Button>
              <Button disabled={!selected.length || working}
                onClick={() => { void choosePdf(); }}>
                Dołącz PDF do zaznaczonych ({selected.length})
              </Button>
              <Button disabled={working} onClick={() => { void suggestFromPdf(); }}>
                Wczytaj propozycje z ChPL
              </Button>
            </Space>
            <Typography.Text type="secondary">
              Istniejące warianty nie są kasowane przez pominięcie w edytorze.
            </Typography.Text>
          </div>
          <Table<UiVariant> {...variantsLayout.tableProps}
            className={"catalogue-variants "+variantsLayout.tableProps.className}
            rowKey="key" dataSource={variants} size="small" pagination={false}
            loading={working}
            scroll={{ x: 1990, y: "calc(100vh - 530px)" }}
            rowSelection={{
              selectedRowKeys: selected,
              onChange: keys => setSelected(keys.map(String))
            }}
            columns={variantsLayout.columns}
          />
        </>}
      </Card>
    </div>
    <Modal className="kl-dialog-catalogue-picker" open={pickerOpen} title="Wybierz kartotekę Subiekta" width={680}
      onCancel={working ? undefined : closePicker} closable={!working} maskClosable={false}
      keyboard={!working} cancelButtonProps={{ disabled: working }} cancelText="Anuluj"
      okText="Wybierz" okButtonProps={{ disabled: !pickerSelection || pickerBusy }}
      confirmLoading={working}
      onOk={() => { if (pickerSelection) void openSubiektProduct(pickerSelection); }}>
      <Space.Compact style={{ width: "100%", marginBottom: 12 }}>
        <Input autoFocus aria-label="Symbol lub nazwa z Subiekta"
          placeholder="Symbol lub nazwa z Subiekta" maxLength={128} value={pickerQuery}
          onChange={event => setPickerQuery(event.target.value)}
          onPressEnter={() => { if (!pickerBusy && !working) void searchSubiekt(pickerQuery); }} />
        <Button disabled={working} loading={pickerBusy} onClick={() => void searchSubiekt(pickerQuery)}>
          Szukaj
        </Button>
      </Space.Compact>
      {pickerError && <Alert type="error" showIcon message={pickerError} />}
      {pickerRows.length === 100 && <Typography.Text type="secondary">
        Pokazano pierwsze 100 kartotek. Zawęź wyszukiwanie.
      </Typography.Text>}
      <Table<SubiektProduct> rowKey="symbol" size="small" dataSource={pickerRows}
        loading={pickerBusy} pagination={pickerPagination} scroll={{ y: 300 }}
        locale={{ emptyText: pickerBusy ? "Wczytywanie kartotek…" : "Brak pasujących kartotek." }}
        rowSelection={{ type: "radio", selectedRowKeys: pickerSelection ? [pickerSelection] : [],
          onChange: keys => setPickerSelection(keys[0] == null ? null : String(keys[0])) }}
        onRow={row => ({
          onClick: () => setPickerSelection(row.symbol),
          onDoubleClick: () => { void openSubiektProduct(row.symbol); }
        })}
        columns={[
          { title: "Symbol", dataIndex: "symbol", width: 180 },
          { title: "Nazwa", dataIndex: "name" }
        ]} />
    </Modal>
    <Modal className="kl-dialog-catalogue-missing" open={missingOpen}
      title={"Leki do uzupełnienia ("+(missingRows?.length??0)+" / "+missingTotal+")"}
      width={900} onCancel={()=>setMissingOpen(false)}
      footer={<Button onClick={()=>setMissingOpen(false)}>Zamknij</Button>}>
      {pickerError&&<Alert className="notice" type="error" showIcon message={pickerError}/>}
      <Typography.Paragraph type="secondary">
        Lista obejmuje grupę leków z Subiekta, również produkty bez stanu w magazynie.
        Otwórz produkt, aby uzupełnić istniejącą kartotekę lub przygotować nową.
        Bez Twojego zatwierdzenia nic nie zapisuje się w SQL.
      </Typography.Paragraph>
      <Table<SubiektProduct> rowKey="symbol" size="small"
        dataSource={missingRows??[]} pagination={missingPagination}
        scroll={{y:420}} columns={[
          {title:"Symbol Subiekta",dataIndex:"symbol",width:210},
          {title:"Nazwa leku",dataIndex:"name"},
          {title:"Powód",dataIndex:"reason",width:260},
          {title:"Otwórz",key:"open",width:105,
            render:(_value,row)=><Button size="small" loading={working}
              onClick={()=>{void openSubiektProduct(row.symbol);}}>Uzupełnij</Button>}
        ]}/>
    </Modal>
    <Modal className="kl-dialog-catalogue-proposal" open={proposal !== null}
      title={"Sprawdź odczyt ChPL — " + (proposal?.filename ?? "")}
      width="90vw"
      onCancel={() => setProposal(null)}
      footer={<Space>
        <Button onClick={() => setProposal(null)}>Anuluj</Button>
        <Button type="primary" disabled={!proposal?.variants.length}
          onClick={acceptProposal}>
          Przenieś propozycje do edytora (bez zapisu SQL)
        </Button>
      </Space>}>
      {proposal && <>
        <Alert className="notice" type="warning" showIcon
          message="To tylko automatyczna propozycja. Sprawdź zgodność każdej wartości z aktualną urzędową polską ChPL."
          description="Dane z dokumentów wielogatunkowych lub niejednoznacznych mogą pozostać puste."/>
        {proposal.warnings.map((warning,index) =>
          <Alert key={index} type="info" className="notice" showIcon message={warning}/>)}
        <Table<CatalogueVariant> rowKey={(_,index)=>String(index)}
          dataSource={proposal.variants} pagination={false} size="small"
          scroll={{x:1100,y:230}}
          columns={[
            {title:"Gatunek",dataIndex:"species",width:120},
            {title:"Droga podania",dataIndex:"route",width:135},
            {title:"Wskazanie",dataIndex:"indication",width:245,ellipsis:true},
            {title:"Dawkowanie",dataIndex:"dose",width:255,ellipsis:true},
            {title:"Tkanki",dataIndex:"tissues",width:180,ellipsis:true},
            {title:"Mleko",dataIndex:"milk",width:160,ellipsis:true}
          ]}/>
        <Typography.Title level={5} style={{marginTop:18}}>
          Rozpoznane fragmenty źródłowe (do weryfikacji)
        </Typography.Title>
        <pre className="chpl-evidence">{proposal.evidence}</pre>
      </>}
    </Modal>
    <Modal className="kl-dialog-catalogue-pdf" open={pdfUrl !== null} title={"ChPL — " + pdfName}
      width="94vw" destroyOnClose
      onCancel={closePdf}
      footer={<Button onClick={closePdf}>Zamknij podgląd</Button>}>
      {pdfUrl && <iframe src={pdfUrl} title="Dokument ChPL" referrerPolicy="no-referrer"
        style={{ width: "100%", height: "76vh", border: 0, background: "#fff" }} />}
    </Modal>
  </>;
}
