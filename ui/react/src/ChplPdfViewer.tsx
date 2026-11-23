import {useEffect, useRef, useState} from "react";
import {Alert, Button, Space, Spin, Typography} from "antd";
import {Document, Page, pdfjs} from "react-pdf";
import {useVirtualizer} from "@tanstack/react-virtual";
import {updateComponents, useComponents} from "./componentSettings";
import worker from "pdfjs-dist/build/pdf.worker.min.mjs?url";
import "react-pdf/dist/Page/AnnotationLayer.css";
import "react-pdf/dist/Page/TextLayer.css";

pdfjs.GlobalWorkerOptions.workerSrc = worker;

// Shared viewer for ChPL and treatment-card PDFs. Only the PDF bytes differ;
// all toolbar, page navigation, zoom and native-viewer settings are identical.
export default function ChplPdfViewer({url, name}: {url: string; name: string}) {
  const preferences = useComponents().pdf;
  const viewport = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(720);
  const [pages, setPages] = useState(0);
  const [page, setPage] = useState(1);
  const [zoom, setZoom] = useState(preferences.zoom);
  const [mode, setMode] = useState(preferences.mode);
  const [native, setNative] = useState(preferences.viewer === "native");
  useEffect(() => {setMode(preferences.mode);}, [preferences.mode]);
  useEffect(() => {setNative(preferences.viewer === "native");}, [preferences.viewer]);
  useEffect(() => {setZoom(preferences.zoom);}, [preferences.zoom]);
  useEffect(() => {setPages(0);setPage(1);}, [url]);
  useEffect(() => {
    const element = viewport.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) =>
      setWidth(Math.max(240, Math.min(1200, Math.floor(entry.contentRect.width - 36)))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const pageWidth = Math.round(width * zoom / 100);
  const virtualizer = useVirtualizer({
    count:mode === "scroll" && !native ? pages : 0,
    getScrollElement:()=>viewport.current,
    estimateSize:()=>Math.round(pageWidth*1.42+18),
    overscan:preferences.preloadPages,
    getItemKey:index=>index,
  });
  function changeZoom(value:number) {
    const next=Math.max(50,Math.min(200,value));
    setZoom(next);
    if(preferences.rememberZoom) updateComponents("pdf",{zoom:next});
  }
  function changeMode(value:"pages"|"scroll") {
    setMode(value);updateComponents("pdf",{mode:value});setPage(1);
    viewport.current?.scrollTo({top:0});
  }
  function changeNative(value:boolean) {
    setNative(value);updateComponents("pdf",{viewer:value?"native":"react"});
  }
  return <>
    <Space wrap>
      {!native && <>
        <Button disabled={mode==="scroll"||page<=1} onClick={()=>setPage(n=>n-1)}>Poprzednia</Button>
        <Typography.Text>{pages
          ? mode==="pages"?`Strona ${page} z ${pages}`:`${pages} stron — przewijanie`
          : "Ładowanie dokumentu"}</Typography.Text>
        <Button disabled={mode==="scroll"||page>=pages} onClick={()=>setPage(n=>n+1)}>Następna</Button>
        <Button onClick={()=>changeMode(mode==="scroll"?"pages":"scroll")}>
          {mode==="scroll"?"Tryb stron":"Przewijanie"}
        </Button>
        <Button aria-label="Pomniejsz" disabled={zoom<=50} onClick={()=>changeZoom(zoom-25)}>−</Button>
        <Button aria-label="Przywróć powiększenie" onClick={()=>changeZoom(100)}>{zoom}%</Button>
        <Button aria-label="Powiększ" disabled={zoom>=200} onClick={()=>changeZoom(zoom+25)}>+</Button>
      </>}
      <Button onClick={()=>changeNative(!native)}>{native?"Podgląd w aplikacji":"Podgląd natywny / drukuj"}</Button>
      <Button href={url} download={name||"dokument.pdf"}>Pobierz PDF</Button>
    </Space>
    <div ref={viewport} className="kl-chpl-pdf" style={{overflow:"auto"}}>
      {native ? <iframe src={url} title={name||"Podgląd PDF"} referrerPolicy="no-referrer"/> :
      <Document file={url} onLoadSuccess={({numPages})=>{setPages(numPages);setPage(1);}}
        loading={<Spin aria-label="Ładowanie dokumentu PDF"/>}
        error={<Alert type="error" title="Nie udało się wyświetlić PDF. Wybierz pobranie lub podgląd natywny."/>}>
        {mode==="pages" ? (pages>0&&<Page pageNumber={Math.min(page,pages)}
          width={pageWidth} renderTextLayer={preferences.textLayer}
          renderAnnotationLayer={preferences.annotations}
          loading={<Spin aria-label="Ładowanie strony"/>}/>) :
        <div style={{position:"relative",height:virtualizer.getTotalSize()}}>
          {virtualizer.getVirtualItems().map(item=>
            <div key={item.key} ref={virtualizer.measureElement} data-index={item.index}
              style={{position:"absolute",top:0,left:0,width:"100%",
                transform:`translateY(${item.start}px)`,paddingBottom:14}}>
              <Page pageNumber={item.index+1} width={pageWidth} renderTextLayer={preferences.textLayer}
                renderAnnotationLayer={preferences.annotations}
                loading={<Spin aria-label={"Ładowanie strony "+(item.index+1)}/>}/>
            </div>)}
        </div>}
      </Document>}
    </div>
  </>;
}
