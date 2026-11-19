import { useCallback, useEffect, useState } from "react";
import {
  Alert, Button, Card, Empty, Input, Modal, Space, Table,
  Tag, Typography, Tooltip, message
} from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke } from "./bridge";

interface CatalogueListing { symbol: string; name: string; status: string; variants: number; }
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

export default function CataloguePage() {
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
  const [pdfUrl, setPdfUrl] = useState<string | null>(null);
  const [pdfName, setPdfName] = useState("");
  const [version, setVersion] = useState(0);
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
        title: "Niezapisane zmiany",
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

  function changeField(field: "name" | "rplId" | "description", value: string) {
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

  const closePdf = useCallback(() => {
    setPdfUrl(previous => { if (previous) URL.revokeObjectURL(previous); return null; });
  }, []);

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
    if (!editing || !original) return;
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

  return <>
    {contextHolder}
    <div className="page-heading">
      <div>
        <Typography.Title level={3}>Katalog leków i ChPL</Typography.Title>
        <Typography.Text type="secondary">
          Dane wariantów i dokumenty urzędowe z bazy KartyLeczenia SQL.
        </Typography.Text>
      </div>
      <Button onClick={() => setVersion(value => value + 1)}
        loading={listBusy}>Odśwież katalog</Button>
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
          onChange={event => setInput(event.target.value)} />
        <Table<CatalogueListing> rowKey="symbol" size="small"
          loading={listBusy} dataSource={list}
          pagination={{ pageSize: 20, showSizeChanger: false }}
          scroll={{ y: "calc(100vh - 370px)" }}
          onRow={row => ({
            onClick: () => { void openProduct(row); },
            onDoubleClick: () => { void openProduct(row); }
          })}
          rowClassName={row =>
            editing?.symbol === row.symbol ? "catalogue-selected" : ""}
          locale={{ emptyText: <Empty description="Brak produktów" /> }}
          columns={[
            { title: "Lek", dataIndex: "name", ellipsis: true },
            { title: "Warianty", dataIndex: "variants", width: 85, align: "right" }
          ]}
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
                {editing.name}
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
              }}>Cofnij zmiany</Button>
              <Button type="primary" disabled={!dirty} loading={working}
                onClick={() => { void saveProduct(); }}>Zapisz w SQL</Button>
            </Space>
          </div>
          <div className="catalogue-product-fields">
            <label>Nazwa leku
              <Input value={editing.name}
                onChange={event => changeField("name", event.target.value)} />
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
            </Space>
            <Typography.Text type="secondary">
              Istniejące warianty nie są kasowane przez pominięcie w edytorze.
            </Typography.Text>
          </div>
          <Table<UiVariant> className="catalogue-variants"
            rowKey="key" dataSource={variants} size="small" pagination={false}
            loading={working}
            scroll={{ x: 1990, y: "calc(100vh - 530px)" }}
            rowSelection={{
              selectedRowKeys: selected,
              onChange: keys => setSelected(keys.map(String))
            }}
            columns={columns}
          />
        </>}
      </Card>
    </div>
    <Modal open={pdfUrl !== null} title={"ChPL — " + pdfName}
      width="94vw" destroyOnClose
      onCancel={closePdf}
      footer={<Button onClick={closePdf}>Zamknij podgląd</Button>}>
      {pdfUrl && <iframe src={pdfUrl} title="Dokument ChPL" referrerPolicy="no-referrer"
        style={{ width: "100%", height: "76vh", border: 0, background: "#fff" }} />}
    </Modal>
  </>;
}
