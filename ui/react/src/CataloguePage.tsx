import Modal from "./AppModal";
import Table from "./AppTable";
import {Suspense, lazy} from "react";
import {compactWithdrawal, isPoultry,rplEntryFromPdfName} from "./cataloguePdfMetadata";
import {useListPagination} from "./ListPagination";
import {useListColumnLayout} from "./ListColumnLayout";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  Alert, Button, Card, Checkbox, Empty, Input, Space, Spin,
  Tag, Typography, Tooltip, message
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke } from "./bridge";

const ChplPdfViewer = lazy(() => import("./ChplPdfViewer"));

interface CatalogueListing { symbol: string; name: string; status: string; variants: number; }
interface SubiektProduct { symbol:string; name:string; reason?:string; }
interface CatalogueVariant {
  id: number | null;
  species: string; indication: string; route: string; dose: string;
  tissues: string; milk: string; eggs: string; remarks: string; source: string;
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
  { key: "eggs", title: "Jaja", width: 145 },
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
    route: "", dose: "", tissues: "", milk: "", eggs: "", remarks: "", source: "",
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
  const [proposalVerified,setProposalVerified]=useState(false);
  const [pdfNames,setPdfNames]=useState<Record<string,string>>({});
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

  function suggestRplEntry(filename: string) {
    const rplId = rplEntryFromPdfName(filename);
    if (rplId) setEditing(current => current && !current.rplId.trim()
      ? {...current,rplId} : current);
  }

  function fillWithdrawal(row: UiVariant): UiVariant {
    const compact = compactWithdrawal(row.tissues,isPoultry(row.species) ? "" : row.milk,row.eggs);
    if (!compact) return row;
    const short = row.short.trim() ? row.short : compact;
    return {...row,short,price:row.price.trim() ? row.price : short};
  }

  function fillSelectedWithdrawal() {
    const chosen = new Set(selected);
    setVariants(rows=>rows.map(row=>chosen.size===0||chosen.has(row.key) ? fillWithdrawal(row) : row));
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
        const {path,name} = result;
        if (name) {
          setPdfNames(current=>({...current,[path]:name}));
          suggestRplEntry(name);
        }
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
      setProposalVerified(false);
      setProposal({...result,path:picked.path,filename:picked.name ?? "ChPL.pdf"});
    } catch(e) {
      setError(e instanceof Error?e.message:String(e));
    } finally {setWorking(false);}
  }

  function editProposal(index: number, field: "species" | "route" | "dose", value: string) {
    setProposal(current => current && ({
      ...current, variants: current.variants.map((row, i) =>
        i === index ? {...row, [field]: value} : row)
    }));
  }

  function acceptProposal() {
    if (!proposal) return;
    let added=0, filled=0, skipped=0;
    const next = [...variants];
    for (const suggestion of proposal.variants) {
      const species = suggestion.species.trim();
      const route = suggestion.route.trim();
      if (!species || !route || !suggestion.dose.trim()) { skipped++; continue; }
      const match = next.find(row =>
        row.species.trim().toLocaleLowerCase("pl")===species.toLocaleLowerCase("pl") &&
        row.route.trim().toLocaleLowerCase("pl")===route.toLocaleLowerCase("pl"));
      const source = proposalVerified ? "Zweryfikowane przez użytkownika — " + proposal.filename : suggestion.source;
      if (match) {
        const update = {...match};
        for (const field of ["indication","dose","tissues","milk"] as const) {
          if (!update[field].trim() && suggestion[field]?.trim()) {
            update[field] = suggestion[field];filled++;
          }
        }
        if (!update.source.trim() && source) {update.source=source;filled++;}
        if (!update.pdf) {update.pdf=proposal.path;filled++;}
        const formatted = fillWithdrawal(update);
        if (formatted.short!==update.short) filled++;
        if (formatted.price!==update.price) filled++;
        next[next.indexOf(match)] = formatted;
      } else {
        next.push(fillWithdrawal({...emptyVariant(),...suggestion,id:null,key:"new-"+crypto.randomUUID(),
          pdf:proposal.path,source}));
        added++;
      }
    }
    if (!added && !filled) {
      void msgApi.info("Brak nowych danych do przeniesienia. Sprawdź, czy pola są już uzupełnione.");
      return;
    }
    suggestRplEntry(proposal.filename);
    setPdfNames(current=>({...current,[proposal.path]:proposal.filename}));
    setVariants(next);
    setDirty(true);
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
        key === "milk" && isPoultry(row.species) ? "—" : <Input.TextArea
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
            {row.pdf ? (pdfNames[row.pdf] ?? (/^ChPL\/[a-f0-9]{64}\.pdf$/i.test(row.pdf)
              ? "Powiązany plik PDF" : row.pdf.split("/").at(-1))) : "Nie przypisano"}
          </Typography.Text>
        </Tooltip>
        {row.pdf && <Typography.Text type="secondary">
          {original?.variants.some(variant=>variant.id===row.id&&variant.pdf===row.pdf)
            ? "Zapisano w SQL" : "Zostanie zapisany z kartoteką"}
        </Typography.Text>}
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
        <Button size="small" onClick={productsLayout.openColumns}>Kolumny kartotek</Button>
        <Input.Search allowClear value={input}
          placeholder="Nazwa lub symbol leku..."
          onSearch={value=>{
            const trimmed=value.trim();
            if(trimmed===query)setVersion(value=>value+1);
            else setQuery(trimmed);
          }}
          onChange={event => setInput(event.target.value)} />
        <Table<CatalogueListing> grid={productsLayout.grid} {...productsLayout.tableProps} rowKey="symbol" size="small"
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
            <label>Numer RPL / identyfikator wpisu
              <Input value={editing.rplId}
                onChange={event => changeField("rplId", event.target.value)} />
            </label>
            <label>Opis (opcjonalny)
              <Input.TextArea placeholder="Np. postać leku, wielkość opakowania lub uwaga do kartoteki" autoSize={{ minRows: 1, maxRows: 3 }}
                value={editing.description}
                onChange={event => changeField("description", event.target.value)} />
            </label>
          </div>
          <div className="catalogue-variant-toolbar">
            <Space wrap>
              <Button onClick={variantsLayout.openColumns}>Kolumny wariantów</Button>
              <Button onClick={addVariant}>Dodaj wariant</Button>
              <Button disabled={!selected.length || working}
                onClick={() => { void choosePdf(); }}>
                Dołącz PDF do zaznaczonych ({selected.length})
              </Button>
              <Button disabled={working||!variants.length} onClick={fillSelectedWithdrawal}>
                Uzupełnij skróty karencji
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
    {productsLayout.dialog}
    {variantsLayout.dialog}
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
        <Button type="primary" disabled={!proposal?.variants.some(row=>row.species.trim()&&row.route.trim()&&row.dose.trim())}
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
        {proposal.variants.some(row=>!row.species.trim()||!row.route.trim()||!row.dose.trim()) &&
          <Alert className="notice" type="info" showIcon
            message="Uzupełnij w tabeli gatunek, drogę podania i dawkowanie według ChPL, aby przenieść daną pozycję."/>}
        <Table<CatalogueVariant> rowKey={(_,index)=>String(index)}
          dataSource={proposal.variants} pagination={false} size="small"
          scroll={{x:1100,y:230}}
          columns={[
            {title:"Gatunek",dataIndex:"species",width:120,
              render:(value:string,_row,index)=><Input.TextArea aria-label={"Gatunek — pozycja "+(index+1)}
                size="small" autoSize={{minRows:1,maxRows:3}} value={value}
                onChange={event=>editProposal(index,"species",event.target.value)}/>} ,
            {title:"Droga podania",dataIndex:"route",width:135,
              render:(value:string,_row,index)=><Input.TextArea aria-label={"Droga podania — pozycja "+(index+1)}
                size="small" autoSize={{minRows:1,maxRows:3}} value={value}
                onChange={event=>editProposal(index,"route",event.target.value)}/>} ,
            {title:"Wskazanie",dataIndex:"indication",width:245,ellipsis:true},
            {title:"Dawkowanie",dataIndex:"dose",width:255,
              render:(value:string,_row,index)=><Input.TextArea aria-label={"Dawkowanie — pozycja "+(index+1)}
                size="small" autoSize={{minRows:1,maxRows:3}} value={value}
                onChange={event=>editProposal(index,"dose",event.target.value)}/>} ,
            {title:"Tkanki",dataIndex:"tissues",width:180,ellipsis:true},
            {title:"Mleko",dataIndex:"milk",width:160,ellipsis:true}
          ]}/>
        <Checkbox checked={proposalVerified} onChange={event=>setProposalVerified(event.target.checked)}>
          Sprawdziłem przenoszone dane z aktualną ChPL
        </Checkbox>
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
      {pdfUrl && <Suspense fallback={<Spin aria-label="Ładowanie podglądu ChPL"/>}>
        <ChplPdfViewer key={pdfUrl} url={pdfUrl} name={pdfName}/>
      </Suspense>}
    </Modal>
  </>;
}
