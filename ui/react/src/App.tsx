import { useEffect, useMemo, useState } from "react";
import { Alert, Button, Card, ConfigProvider, Descriptions, Divider, Empty, Input, Modal, Segmented, Select, Space, Table, Tag, Typography, theme } from "antd";
import type { ColumnsType } from "antd/es/table";
import { invoke, isDesktop } from "./bridge";
import type { DocumentDetail, DocumentKind, DocumentRow, DocumentsResult, DatePeriod } from "./bridge";

type LayoutName = "classic" | "modern" | "fluent";
type PaletteName = "light" | "dark" | "sea";
type Section = "documents" | "prices" | "treatment" | "saved" | "expiry" | "settings";
const sections: Array<{id: Section; name: string}> = [
  {id: "documents", name: "Dokumenty"},
  {id: "prices", name: "Cennik leków"},
  {id: "treatment", name: "Karta leczenia"},
  {id: "saved", name: "Zapisane karty"},
  {id: "expiry", name: "Terminy ważności"},
  {id: "settings", name: "Ustawienia"}
];
function saved<T extends string>(key: string, values: readonly T[], fallback: T): T {
  try {
    const value = localStorage.getItem(key);
    return values.find(item => item === value) ?? fallback;
  } catch { return fallback; }
}
const layoutNames = ["classic", "modern", "fluent"] as const;
const paletteNames = ["light", "dark", "sea"] as const;
const money = (value: number | null, currency: string) => value === null ? "—" :
  new Intl.NumberFormat("pl-PL", {minimumFractionDigits: 2, maximumFractionDigits: 2}).format(value) +
  " " + (currency || "zł");
const dateString = (value: string | null) => value ? new Intl.DateTimeFormat("pl-PL").format(new Date(value)) : "—";

function Documents({openClassic}: {openClassic: () => void}) {
  const [kind, setKind] = useState<DocumentKind>("invoices");
  const [period, setPeriod] = useState<DatePeriod>("month");
  const [query, setQuery] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [rows, setRows] = useState<DocumentRow[]>([]);
  const [truncated, setTruncated] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [detail, setDetail] = useState<DocumentDetail | null>(null);
  const [reading, setReading] = useState(false);

  useEffect(() => {
    let expired = false;
    const timeout = setTimeout(() => {
      setLoading(true);
      setError("");
      invoke<DocumentsResult>("searchDocuments", {kind, query, period})
        .then(result => { if (!expired) { setRows(result.items); setTruncated(result.truncated); } })
        .catch(reason => {
          if (!expired) { setRows([]); setError(reason instanceof Error ? reason.message : String(reason)); }
        }).finally(() => { if (!expired) setLoading(false); });
    }, query.length ? 280 : 0);
    return () => { expired = true; clearTimeout(timeout); };
  }, [kind, period, query, refresh]);

  useEffect(() => {
    const keyDown = (event: KeyboardEvent) => {
      if (event.key === "F5") { event.preventDefault(); setRefresh(n => n + 1); }
    };
    window.addEventListener("keydown", keyDown);
    return () => window.removeEventListener("keydown", keyDown);
  }, []);

  async function open(row: DocumentRow) {
    setReading(true);
    setError("");
    try {
      setDetail(await invoke<DocumentDetail>("readDocument", {documentId: row.id, number: row.number}));
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setReading(false); }
  }
  const columns: ColumnsType<DocumentRow> = useMemo(() => [
    {title: "Data", key: "date", dataIndex: "date", width: 115, render: dateString,
      sorter: (a, b) => (a.date || "").localeCompare(b.date || "")},
    {title: "Numer", key: "number", dataIndex: "number", width: 190, ellipsis: true,
      sorter: (a, b) => a.number.localeCompare(b.number)},
    {title: "Kontrahent", key: "payer", dataIndex: "shortPayer", ellipsis: true,
      render: (name: string, row) => <span title={row.payer}>{name || row.payer || "—"}</span>},
    {title: "Wartość brutto", key: "gross", dataIndex: "gross", align: "right", width: 165,
      render: (amount: number | null, row) => money(amount, row.currency),
      sorter: (a, b) => (a.gross || 0) - (b.gross || 0)},
    {title: "Powiązane dokumenty", key: "relatedText", dataIndex: "relatedText", width: 245, ellipsis: true}
  ], []);

  return <>
    <div className="page-heading">
      <div><Typography.Title level={3}>Dokumenty Subiekta GT</Typography.Title>
        <Typography.Text type="secondary">Dane odczytywane przez istniejący serwis C# i SQL Server.</Typography.Text></div>
      <Space>
        <Button onClick={() => setRefresh(n => n + 1)} loading={loading}>Odśwież · F5</Button>
        <Button onClick={openClassic}>Pełny program WinForms</Button>
      </Space>
    </div>
    <Card className="document-card">
      <div className="document-filters">
        <Segmented size="large" block value={kind} onChange={value => setKind(value as DocumentKind)}
          options={[
            {label: "Faktury", value: "invoices"},
            {label: "Paragony", value: "receipts"},
            {label: "WZ", value: "warehouse"},
            {label: "Wszystkie", value: "all"}]} />
        <Input.Search size="large" allowClear aria-label="Wyszukaj dokument"
          placeholder="Numer dokumentu lub kontrahent" value={query}
          onChange={event => setQuery(event.target.value)} onSearch={() => setRefresh(n => n + 1)} />
        <Select size="large" value={period} onChange={setPeriod} aria-label="Okres dokumentów"
          options={[
            {label: "Ostatnie 30 dni", value: "month"},
            {label: "Ostatnie 7 dni", value: "week"},
            {label: "Dzisiaj", value: "today"},
            {label: "Wszystkie daty", value: "all"}]} />
      </div>
      {error && <Alert className="notice" type="error" showIcon message={error}/>}
      {truncated && <Alert className="notice" type="info" message="Wyświetlono 200 dokumentów. Zawęź wyszukiwanie."/>}
      <Table<DocumentRow> size="middle" bordered={false} rowKey="id" columns={columns}
        dataSource={rows} loading={loading} scroll={{x: 1020, y: "calc(100vh - 410px)"}}
        pagination={{pageSize: 30, showSizeChanger: true, pageSizeOptions: [15, 30, 60, 100],
          showTotal: total => "Dokumentów: " + total}}
        onRow={record => ({onDoubleClick: () => { void open(record); }})}
        locale={{emptyText: <Empty description="Brak dokumentów dla wybranych filtrów"/>}}
      />
      <Typography.Text className="table-hint" type="secondary">
        Dwuklik pokazuje szczegóły dokumentu; edycja karty pozostaje na razie w WinForms.
      </Typography.Text>
    </Card>
    <Modal title={detail ? "Dokument " + detail.number : "Dokument"} open={detail !== null}
      onCancel={() => setDetail(null)} footer={<Button onClick={() => setDetail(null)}>Zamknij</Button>}
      width={900} destroyOnClose>
      {detail && <>
        <Descriptions bordered size="small" column={1}
          items={[
            {key: "payer", label: "Płatnik", children: detail.payer},
            {key: "recipient", label: "Odbiorca", children: detail.recipient}]} />
        <Divider/>
        <Typography.Title level={5}>Pozycje leków</Typography.Title>
        <Table rowKey={(_, index) => String(index)} size="small" pagination={false}
          dataSource={detail.rows} scroll={{x: 700}}
          columns={[
            {title: "Produkt", dataIndex: "product"},
            {title: "Ilość", dataIndex: "quantity", width: 95},
            {title: "Seria", dataIndex: "batch", width: 110},
            {title: "Gatunek", dataIndex: "species", width: 110},
            {title: "Droga", dataIndex: "route", width: 115}]} />
      </>}
    </Modal>
    {reading && <div className="loading-mask">Odczytywanie dokumentu…</div>}
  </>;
}

function App() {
  const [layout, setLayout] = useState<LayoutName>(() => saved("kl.layout", layoutNames, "modern"));
  const [palette, setPalette] = useState<PaletteName>(() => saved("kl.palette", paletteNames, "light"));
  const [section, setSection] = useState<Section>("documents");
  useEffect(() => { try { localStorage.setItem("kl.layout", layout); } catch { /* unavailable */ } }, [layout]);
  useEffect(() => { try { localStorage.setItem("kl.palette", palette); } catch { /* unavailable */ } }, [palette]);
  const isDark = palette === "dark";
  const primary = palette === "sea" ? "#15836f" : isDark ? "#78a5fa" : "#1768d8";
  const skin = useMemo(() => ({
    algorithm: isDark ? theme.darkAlgorithm : theme.defaultAlgorithm,
    token: {colorPrimary: primary, borderRadius: layout === "classic" ? 4 : layout === "modern" ? 12 : 8,
      fontFamily: "Segoe UI, Inter, system-ui, sans-serif", fontSize: 14},
    components: {
      Table: {headerBg: isDark ? "#273447" : "#f3f6fa", rowHoverBg: isDark ? "#304764" : "#edf4ff"},
      Segmented: {itemSelectedBg: primary, itemSelectedColor: "#ffffff"}
    }
  }), [isDark, primary, layout]);

  const openClassic = () => { void invoke("openClassic").catch(() => {}); };
  const navigation = <nav aria-label="Nawigacja" className="main-nav">
    {sections.map(item => <button key={item.id} type="button"
      className={"nav-item " + (section === item.id ? "is-active" : "")}
      onClick={() => setSection(item.id)}>{item.name}</button>)}
  </nav>;
  const options = <div className="appearance-controls">
    <Select size="middle" aria-label="Układ interfejsu" value={layout} onChange={setLayout}
      options={[
        {label: "Nowoczesny — menu boczne", value: "modern"},
        {label: "Klasyczny — zakładki", value: "classic"},
        {label: "Fluent — Windows 11", value: "fluent"}]} />
    <Select size="middle" aria-label="Kolorystyka" value={palette} onChange={setPalette}
      options={[{label: "Jasny", value: "light"}, {label: "Ciemny", value: "dark"},
        {label: "Morski", value: "sea"}]} />
  </div>;
  return <ConfigProvider theme={skin}>
    <div className={"app-shell layout-" + layout + " palette-" + palette}>
      {layout !== "classic" && <aside className="sidebar">
        <div className="sidebar-brand"><div className="brand-mark">KL</div>
          <div><strong>KartyLeczenia</strong><small>SQL · ReactKL</small></div></div>
        {navigation}
        <div className="sidebar-footer">Interfejs testowy<br/>Dane pozostają w SQL Server</div>
      </aside>}
      <div className="workspace">
        <header className="topbar">
          <div className="topbar-title"><strong>{layout === "classic" ? "KartyLeczenia" :
            sections.find(item => item.id === section)?.name}</strong>
            {layout === "classic" && <Tag color="blue">ReactKL</Tag>}</div>
          {options}
        </header>
        {layout === "classic" && <div className="top-navigation">{navigation}</div>}
        <main className="main-content">
          {!isDesktop() && <Alert type="info" showIcon className="notice"
            message="Podgląd w przeglądarce — dostęp do Subiekta wymaga aplikacji Windows WebView2."/>}
          {section === "documents" ? <Documents openClassic={openClassic}/> :
            <div className="placeholder"><Card>
              <Typography.Title level={4}>{sections.find(item => item.id === section)?.name}</Typography.Title>
              <Typography.Paragraph>Ta funkcja pozostaje w istniejącym WinForms. Przeniesiemy ją po sprawdzeniu ekranu dokumentów.</Typography.Paragraph>
              <Button type="primary" onClick={openClassic}>Otwórz w WinForms</Button>
            </Card></div>}
        </main>
        <footer className="statusline">
          <span>React + WebView2 · lokalna aplikacja Windows</span>
          <span>Układ: {layout} · motyw: {palette}</span>
        </footer>
      </div>
    </div>
  </ConfigProvider>;
}
export default App;
