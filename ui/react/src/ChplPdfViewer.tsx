import {useEffect, useRef, useState} from "react";
import {Alert, Button, Space, Spin, Typography} from "antd";
import {Document, Page, pdfjs} from "react-pdf";
import worker from "pdfjs-dist/build/pdf.worker.min.mjs?url";
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";

pdfjs.GlobalWorkerOptions.workerSrc = worker;

export default function ChplPdfViewer({url, name}: {url: string; name: string}) {
  const viewport = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(720);
  const [pages, setPages] = useState(0);
  const [page, setPage] = useState(1);
  const [native, setNative] = useState(false);
  const [zoom, setZoom] = useState(100);
  useEffect(() => {
    const element = viewport.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) =>
      setWidth(Math.max(240, Math.min(1000, Math.floor(entry.contentRect.width - 24)))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return <>
    <Space wrap>
      {!native && <>
      <Button disabled={page <= 1} onClick={() => setPage(page - 1)}>Poprzednia</Button>
      <Typography.Text>{pages ? `Strona ${page} z ${pages}` : "Dokument ChPL"}</Typography.Text>
      <Button disabled={page >= pages} onClick={() => setPage(page + 1)}>Następna</Button>
      <Button aria-label="Pomniejsz" disabled={zoom <= 50} onClick={() => setZoom(zoom - 25)}>−</Button>
      <Button aria-label="Przywróć powiększenie" onClick={() => setZoom(100)}>{zoom}%</Button>
      <Button aria-label="Powiększ" disabled={zoom >= 200} onClick={() => setZoom(zoom + 25)}>+</Button>
      </>}
      <Button onClick={() => setNative(!native)}>{native ? "Podgląd w aplikacji" : "Podgląd natywny / drukuj"}</Button>
      <Button href={url} download={name || "ChPL.pdf"}>Pobierz PDF</Button>
    </Space>
    <div ref={viewport} className="kl-chpl-pdf">
      {native ? <iframe src={url} title="Dokument ChPL" referrerPolicy="no-referrer"/> :
      <Document file={url} onLoadSuccess={({numPages}) => setPages(numPages)}
        loading={<Spin aria-label="Ładowanie ChPL"/>}
        error={<Alert type="error" title="Nie udało się wyświetlić PDF. Użyj przycisku Pobierz PDF."/>}>
        {pages > 0 && <Page pageNumber={page} width={Math.round(width * zoom / 100)}
          loading={<Spin aria-label="Ładowanie strony"/>}/>}
      </Document>}
    </div>
  </>;
}
