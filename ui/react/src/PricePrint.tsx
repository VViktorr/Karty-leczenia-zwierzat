import Modal from "./AppModal";
import { useEffect, useMemo, useRef, useState } from "react";
import { Alert, Button, Card, Checkbox, Divider, InputNumber, Select, Space, Switch, Typography } from "antd";
import { invoke, isDesktop } from "./bridge";
import {useComponents, updateComponents} from "./componentSettings";

export interface PricePrintRow {
  symbol: string; name: string; gross: number | null; latestBatch: string;
  fullCodes: string; fullNames: string;
  variantsCodes: string; variantsNames: string;
  speciesCodes: string; speciesNames: string; needsCompletion: boolean;
}
export interface PricePrintOptions {
  landscape: boolean; margin: number; rowHeight: number; padding: number;
  font: string; size: number; fontWeight: number;
  number: boolean; batch: boolean; price: boolean; withdrawal: boolean;
  withdrawalFormat: number; routeCodes: boolean;
  legend: "hidden"|"start"|"every"; widths: number[];
  columnFonts: string[]; columnSizes: number[];
  columnWeights: number[]; columnAlignments: string[];
  pageFontNames: string[]; pageFontSizes: number[];
}
export const defaultPricePrint: PricePrintOptions = {
  landscape: true, margin:7, rowHeight:0, padding:0.5,
  font:"Arial", size:10, fontWeight:400,
  number:true, batch:true, price:true, withdrawal:true,
  withdrawalFormat:1, routeCodes:true, legend:"start",
  widths:[4,43,14,13,40],
  columnFonts:["","","","","",""],
  columnSizes:[10,10,10,10,10,10],
  columnWeights:[400,400,400,400,400,400],
  columnAlignments:["left","left","left","left","right","left"],
  pageFontNames:["","",""],pageFontSizes:[10,10,10]
};
export const priceColumns=["Lp.","Lek","Seria","Cena","Karencja"];
export const priceRouteLegend =
  "IM – domięśniowo, IV – dożylnie, SC – podskórnie, ID – śródskórnie, IP – dootrzewnowo, IA – dostawowo, PO – doustnie, IUT – domacicznie, IMM – dowymieniowo, IVAG – dopochwowo, TOP / EXT – miejscowo / zewnętrznie, POUR-ON – przez polewanie, SPOT-ON – punktowo na skórę, OPH – do oka, OT / AUR – do ucha, PR – doodbytniczo.";

export function withdrawalText(row:PricePrintRow, settings:PricePrintOptions):string {
  return settings.withdrawalFormat === 0
    ? (settings.routeCodes?row.fullCodes:row.fullNames)
    : settings.withdrawalFormat === 2
      ? (settings.routeCodes?row.speciesCodes:row.speciesNames)
      : (settings.routeCodes?row.variantsCodes:row.variantsNames);
}
export function normalizePriceSettings(input:Partial<PricePrintOptions>|null):PricePrintOptions {
  if(!input)return {...defaultPricePrint};
  const merged={...defaultPricePrint,...input};
  const array=<T,>(value:T[]|undefined, fallback:T[]) =>
    Array.isArray(value)&&value.length===fallback.length ? value : [...fallback];
  return {
    ...merged,
    widths:array(input.widths,defaultPricePrint.widths),
    columnFonts:array(input.columnFonts,defaultPricePrint.columnFonts),
    columnSizes:array(input.columnSizes,defaultPricePrint.columnSizes),
    columnWeights:array(input.columnWeights,defaultPricePrint.columnWeights),
    columnAlignments:array(input.columnAlignments,defaultPricePrint.columnAlignments),
    pageFontNames:array(input.pageFontNames,defaultPricePrint.pageFontNames),
    pageFontSizes:array(input.pageFontSizes,defaultPricePrint.pageFontSizes)
  };
}
const html=(value:unknown)=>String(value??"").replace(/[&<>"']/g,ch=>({
  "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"
}[ch]??ch));
const safeFont=(value:string)=>JSON.stringify((value||"Arial").replace(/[;{}\r\n]/g,"").slice(0,100))+", sans-serif";
const numeric=(value:number)=>Number.isFinite(value)?String(value):"10";
const money=(value:number|null)=>value===null?"":new Intl.NumberFormat("pl-PL",{
  minimumFractionDigits:2,maximumFractionDigits:2
}).format(value);
function columnCss(settings:PricePrintOptions,index:number) {
  const font=settings.columnFonts[index]||settings.font;
  const alignment=settings.columnAlignments[index]||"left";
  const alignmentSafe=["left","center","right"].includes(alignment)?alignment:"left";
  return `font-family:${safeFont(font)};font-size:${numeric(settings.columnSizes[index]||settings.size)}pt; font-weight:${numeric(settings.columnWeights[index]||400)};text-align:${alignmentSafe};`;
}
function headingCss(settings:PricePrintOptions,index:number) {
  return `font-family:${safeFont(settings.pageFontNames[index]||settings.font)};font-size:${numeric(settings.pageFontSizes[index]||10)}pt;`;
}

/** Build one printable, paginated document from the ALREADY LOADED rows. No SQL. */
export function pricePrintHtml(
  rows:PricePrintRow[], settings:PricePrintOptions, heading:string
):string {
  const visible=[settings.number,true,settings.batch,settings.price,settings.withdrawal];
  const indexes=visible.flatMap((enabled,index)=>enabled?[index]:[]);
  const widths=indexes.map(i=>settings.widths[i]??20);
  const sum=widths.reduce((a,b)=>a+b,0)||1;
  const cols=indexes.map((i,j)=>`<col style="width:${100*widths[j]/sum}%">`).join("");
  const colHeaders=indexes.map(i=>`<th style="${columnCss(settings,0)}">${html(i===3?heading:priceColumns[i])}</th>`).join("");
  const title=`<h2 class="price-title" style="${headingCss(settings,0)}">Cennik leków</h2>
    <p class="price-date" style="${headingCss(settings,1)}">${html(new Intl.DateTimeFormat("pl-PL").format(new Date()))}</p>`;
  const legend=`<p class="price-legend" style="${headingCss(settings,2)}"><strong>Drogi podania</strong> · ${html(priceRouteLegend)}</p>`;
  const td=(i:number,value:unknown)=>`<td style="${columnCss(settings,i+1)}">${html(value).replace(/\n/g,"<br>")}</td>`;
  const tbody=rows.map((row,index)=>{
    const values=[index+1,row.name,row.latestBatch,money(row.gross),
      withdrawalText(row,settings)];
    return "<tr>"+indexes.map(i=>td(i,values[i])).join("")+"</tr>";
  }).join("");
  const width=settings.landscape?297:210;
  const height=settings.landscape?210:297;
  // Static script does not interpolate SQL strings; all row values were HTML-escaped.
  const pagination=String.raw`
(function(){
  var pageContainer=document.getElementById('pages');
  var rowTemplate=document.getElementById('print-source');
  var sources=Array.from(rowTemplate.content.querySelectorAll('tbody tr'));
  function page(first) {
    var sheet=document.createElement('section');
    sheet.className='sheet';
    var header=document.createElement('header');
    header.className='page-header';
    if(first)header.innerHTML=document.getElementById('print-title').innerHTML;
    sheet.appendChild(header);
    var legends=document.getElementById('print-legends');
    var showLegend=legends.dataset.mode==='every'||(first&&legends.dataset.mode==='start');
    if(showLegend) {
      var legend=document.createElement('div');
      legend.innerHTML=legends.innerHTML;
      sheet.appendChild(legend);
    }
    var table=document.createElement('table');
    table.innerHTML=document.getElementById('table-heading').innerHTML;
    var body=document.createElement('tbody');
    table.appendChild(body);
    sheet.appendChild(table);
    var footer=document.createElement('footer');
    footer.className='page-counter';
    sheet.appendChild(footer);
    pageContainer.appendChild(sheet);
    return {sheet:sheet,body:body,footer:footer};
  }
  function paginate(){
    pageContainer.innerHTML='';
    var current=page(true), created=[current];
    for(var i=0;i<sources.length;i++){
      var row=sources[i].cloneNode(true);
      current.body.appendChild(row);
      if(current.sheet.scrollHeight>current.sheet.clientHeight+2 &&
        current.body.children.length>1) {
        row.remove();
        current=page(false);
        created.push(current);
        current.body.appendChild(row);
      }
      // Never clip a very long single row, even if it exceeds one sheet.
      if(current.sheet.scrollHeight>current.sheet.clientHeight+2 &&
        current.body.children.length===1)current.sheet.classList.add('oversized');
    }
    created.forEach(function(item,index){
      item.footer.textContent='Strona '+(index+1)+' / '+created.length;
    });
    document.getElementById('total-pages').textContent=String(created.length);
  }
  if(document.fonts&&document.fonts.ready)
    document.fonts.ready.then(function(){requestAnimationFrame(paginate);});
  else requestAnimationFrame(paginate);
})();`;
  return `<!doctype html><html lang="pl"><head><meta charset="utf-8">
    <title>Cennik leków</title>
    <style>
      @page {size:A4 ${settings.landscape?"landscape":"portrait"};margin:0}
      * {box-sizing:border-box}
      body {margin:0;background:#e7e9ed;font-family:${safeFont(settings.font)};
        font-size:${numeric(settings.size)}pt;font-weight:${numeric(settings.fontWeight)}}
      .sheet {position:relative;width:${width}mm;height:${height}mm;
        padding:${numeric(settings.margin)}mm;padding-bottom:${numeric(settings.margin+7)}mm;
        margin:16px auto;background:#fff;
        box-shadow:0 3px 18px #aaa;overflow:hidden;break-after:page;page-break-after:always}
      .sheet.oversized {height:auto;min-height:${height}mm;overflow:visible}
      .page-header:empty{display:none}
      .price-title{margin:0 0 3mm;line-height:1.2}
      .price-date{margin:0 0 3mm}
      .price-legend{margin:0 0 3mm;line-height:1.15}
      table{width:100%;table-layout:fixed;border-collapse:collapse}
      th,td{border:1px solid #999;padding:${numeric(settings.padding)}mm 1.6mm;
        vertical-align:top;overflow-wrap:anywhere;white-space:normal}
      th {background:#eee}
      td {height:${numeric(settings.rowHeight)}mm}
      tr {break-inside:avoid;page-break-inside:avoid}
      .page-counter {position:absolute;bottom:2mm;right:${numeric(settings.margin)}mm;
        font-size:8pt;color:#555}
      #debug-pages {font-size:10pt;text-align:center;padding:8px}
      @media print {
        html,body{background:#fff}
        #debug-pages {display:none}
        .sheet {margin:0;box-shadow:none}
        .sheet:last-child{break-after:auto;page-break-after:auto}
        th{-webkit-print-color-adjust:exact;print-color-adjust:exact}
      }
    </style></head><body>
    <div id="debug-pages">Podgląd A4 · liczba stron: <span id="total-pages">…</span></div>
    <div id="pages"></div>
    <template id="print-title">${title}</template>
    <template id="print-legends" data-mode="${settings.legend}">${legend}</template>
    <template id="table-heading"><colgroup>${cols}</colgroup><thead><tr>${colHeaders}</tr></thead></template>
    <template id="print-source"><table><tbody>${tbody}</tbody></table></template>
    <script>${pagination}</script></body></html>`;
}

export function PricePrintSettingsPanel({
  initial,onSave,onClose,onPreview
}:{
  initial:PricePrintOptions;
  onSave:(settings:PricePrintOptions)=>void;
  onClose:()=>void;
  onPreview:(settings:PricePrintOptions)=>void;
}){
  const [form,setForm]=useState<PricePrintOptions>(()=>normalizePriceSettings(initial));
  const [activeCol,setActiveCol]=useState(0);
  const [activePage,setActivePage]=useState(0);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState("");
  useEffect(()=>setForm(normalizePriceSettings(initial)),[initial]);
  const set=<K extends keyof PricePrintOptions>(key:K,value:PricePrintOptions[K])=>
    setForm(old=>({...old,[key]:value}));
  function arraySet<K extends "widths"|"columnFonts"|"columnSizes"|"columnWeights"|"columnAlignments"|"pageFontNames"|"pageFontSizes">(
    key:K,index:number,value:PricePrintOptions[K][number]
  ){
    setForm(old=>({...old,[key]:old[key].map((item,i)=>i===index?value:item)}));
  }
  async function save(preview=false){
    setBusy(true);setError("");
    try{
      await invoke("savePricePrintSettings",{settings:form});
      onSave(form);
      if(preview)onPreview(form);
    }catch(e){setError(e instanceof Error?e.message:String(e));}
    finally{setBusy(false);}
  }
  const fonts=["","Arial","Calibri","Segoe UI","Tahoma","Times New Roman"];
  const fontOpts=fonts.map(name=>({label:name||"Jak w ustawieniach ogólnych",value:name}));
  const availableFonts=(current:string)=>current&&!fonts.includes(current)
    ? [...fontOpts,{label:current,value:current}] : fontOpts;
  return <Modal className="kl-dialog-price-settings" open width="min(1040px,96vw)" title="Ustawienia wydruku cennika"
    onCancel={onClose} destroyOnHidden
    footer={<Space wrap>
      <Button onClick={onClose}>Zamknij</Button>
      <Button onClick={()=>{void save();}} loading={busy}>Zapisz ustawienia</Button>
      <Button type="primary" loading={busy} onClick={()=>{void save(true);}}>
        Zapisz i otwórz podgląd
      </Button>
    </Space>}>
    {error&&<Alert type="error" showIcon title={error} style={{marginBottom:12}}/>}
    <div style={{maxHeight:"67vh",overflowY:"auto",paddingRight:8}}>
      <Card size="small" title="Strona A4">
        <Space wrap size={12}>
          <label>Orientacja <Select value={form.landscape?"landscape":"portrait"}
            onChange={v=>set("landscape",v==="landscape")}
            options={[{label:"Pozioma",value:"landscape"},{label:"Pionowa",value:"portrait"}]}
            style={{width:145}}/></label>
          <label>Margines (mm) <InputNumber min={4} max={25} value={form.margin}
            onChange={v=>set("margin",v??7)}/></label>
          <label>Czcionka <Select value={form.font} onChange={v=>set("font",v)}
            options={fontOpts.filter(v=>v.value).concat(
              fonts.includes(form.font)?[]:[{label:form.font,value:form.font}])}
            style={{width:160}}/></label>
          <label>Rozmiar (pt) <InputNumber min={6} max={48} value={form.size}
            onChange={v=>set("size",v??10)}/></label>
          <label>Grubość <Select value={form.fontWeight}
            onChange={v=>set("fontWeight",v)}
            options={[400,500,600,700,800,900].map(v=>({label:String(v),value:v}))}
            style={{width:90}}/></label>
        </Space>
        <Divider titlePlacement="start">Napisy na stronie</Divider>
        <Space wrap>
          <Select value={activePage} onChange={setActivePage} style={{width:190}}
            options={[{label:"Tytuł cennika",value:0},{label:"Data",value:1},
              {label:"Legenda dróg podania",value:2}]}/>
          <Select value={form.pageFontNames[activePage]||""}
            onChange={v=>arraySet("pageFontNames",activePage,v)}
            options={availableFonts(form.pageFontNames[activePage])} style={{width:170}}/>
          <label>Rozmiar (pt) <InputNumber min={6} max={48}
            value={form.pageFontSizes[activePage]}
            onChange={v=>arraySet("pageFontSizes",activePage,v??10)}/></label>
        </Space>
      </Card>
      <Card size="small" title="Tabela" style={{marginTop:10}}>
        <Space wrap size={12}>
          <label>Min. wysokość wiersza (mm) <InputNumber min={0} max={18} value={form.rowHeight}
            onChange={v=>set("rowHeight",v??0)}/></label>
          <label>Odstęp pionowy (mm) <InputNumber min={0} max={5} step={0.5}
            value={form.padding} onChange={v=>set("padding",v??0.5)}/></label>
        </Space>
      </Card>
      <Card size="small" title="Treść i kolumny" style={{marginTop:10}}>
        <Space wrap>
          {(["number","batch","price","withdrawal"] as const).map((key,i)=>
            <Checkbox key={key} checked={form[key]} onChange={e=>set(key,e.target.checked)}>
              {["Lp.","Seria","Cena","Karencja"][i]}
            </Checkbox>)}
        </Space>
        <Divider titlePlacement="start">Format nagłówka i każdej kolumny</Divider>
        <Space wrap size={12}>
          <Select value={activeCol} onChange={setActiveCol}
            options={[{label:"Nagłówki",value:0},...priceColumns.map((label,i)=>({
              label:"Kolumna: "+label,value:i+1}))]} style={{width:190}}/>
          <Select value={form.columnFonts[activeCol]||""}
            onChange={v=>arraySet("columnFonts",activeCol,v)}
            options={availableFonts(form.columnFonts[activeCol])} style={{width:175}}/>
          <label>Rozmiar (pt) <InputNumber min={6} max={48}
            value={form.columnSizes[activeCol]}
            onChange={v=>arraySet("columnSizes",activeCol,v??10)}/></label>
          <Select value={form.columnWeights[activeCol]}
            onChange={v=>arraySet("columnWeights",activeCol,v)}
            options={[400,500,600,700,800,900].map(v=>({label:"Grubość "+v,value:v}))}
            style={{width:140}}/>
          <Select value={form.columnAlignments[activeCol]}
            onChange={v=>arraySet("columnAlignments",activeCol,v)}
            options={[{label:"Do lewej",value:"left"},{label:"Do środka",value:"center"},
              {label:"Do prawej",value:"right"}]} style={{width:130}}/>
          {activeCol>0&&<label>Szerokość <InputNumber min={1} max={100}
            value={form.widths[activeCol-1]}
            onChange={v=>arraySet("widths",activeCol-1,v??1)}/></label>}
        </Space>
      </Card>
      <Card size="small" title="Legenda oraz zapis karencji" style={{marginTop:10}}>
        <Space wrap size={12}>
          <label>Karencja <Select value={form.withdrawalFormat}
            onChange={v=>set("withdrawalFormat",v)} style={{width:235}}
            options={[{label:"Pełna",value:0},
              {label:"Skrócona wg wariantów",value:1},
              {label:"Skrócona wg gatunków",value:2}]}/></label>
          <label>Droga podania <Select value={form.routeCodes?"codes":"names"}
            onChange={v=>set("routeCodes",v==="codes")} style={{width:165}}
            options={[{label:"Kody (IM, IV…)",value:"codes"},
              {label:"Pełne nazwy",value:"names"}]}/></label>
          <label>Legenda <Select value={form.legend}
            onChange={v=>set("legend",v)} style={{width:190}}
            options={[{label:"Ukryta",value:"hidden"},
              {label:"Na początku",value:"start"},
              {label:"Na każdej stronie",value:"every"}]}/></label>
        </Space>
      </Card>
    </div>
  </Modal>;
}

export function PricePrintPreview({
  rows,settings,heading,onClose
}:{rows:PricePrintRow[];settings:PricePrintOptions;heading:string;onClose:()=>void}){
  const frame=useRef<HTMLIFrameElement>(null);
  const mode=useComponents().pdf.mode;
  const [pages,setPages]=useState(1);
  const [page,setPage]=useState(1);
  const [frameReady,setFrameReady]=useState(0);
  const [savingPdf,setSavingPdf]=useState(false);
  const [pdfError,setPdfError]=useState("");
  const [savedPdf,setSavedPdf]=useState("");
  const documentText=useMemo(()=>pricePrintHtml(rows,settings,heading),[rows,settings,heading]);
  // The price-list renderer paginates asynchronously after fonts load.
  // Observe its actual generated page nodes rather than guessing row counts.
  useEffect(()=>{
    const iframe=frame.current;
    const document=iframe?.contentDocument;
    const container=document?.getElementById("pages");
    if(!container)return;
    const apply=()=>{
      const sheets=Array.from(container.querySelectorAll<HTMLElement>(".sheet"));
      setPages(Math.max(1,sheets.length));
      setPage(current=>Math.max(1,Math.min(current,sheets.length||1)));
      sheets.forEach((sheet,i)=>{sheet.style.display=mode==="pages"&&i!==page-1?"none":"";});
    };
    apply();
    const observer=new MutationObserver(apply);
    observer.observe(container,{childList:true});
    return()=>observer.disconnect();
  },[documentText,mode,page,frameReady]);
  async function savePdf(){
    const document=frame.current?.contentDocument;
    const pages=document?.getElementById("pages");
    const style=document?.head.querySelector("style");
    if(!pages?.querySelector(".sheet")||!style){
      setPdfError("Poczekaj, aż podgląd wczyta strony cennika.");
      return;
    }
    const copy=pages.cloneNode(true) as HTMLElement;
    copy.querySelectorAll<HTMLElement>(".sheet").forEach(sheet=>{sheet.style.removeProperty("display");});
    const html="<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\">"+
      style.outerHTML+"</head><body>"+copy.outerHTML+"</body></html>";
    setSavingPdf(true);setPdfError("");setSavedPdf("");
    try{
      const result=await invoke<{cancelled:boolean;fileName:string}>("savePricePdf",{
        html,landscape:settings.landscape
      });
      if(!result.cancelled)setSavedPdf("Zapisano "+result.fileName);
    }catch(error){setPdfError(error instanceof Error?error.message:String(error));}
    finally{setSavingPdf(false);}
  }
  return <Modal className="kl-dialog-price-preview" open width="96vw" title={"Podgląd wielostronicowy — "+rows.length+" leków"}
    onCancel={onClose} destroyOnHidden
    footer={<Space wrap>
      <Typography.Text type="secondary">Każda kartka ma własny nagłówek tabeli i numer strony.</Typography.Text>
      <Button onClick={onClose}>Zamknij</Button>
      {isDesktop()&&<Button loading={savingPdf} disabled={!frameReady} onClick={()=>{void savePdf();}}>Zapisz PDF</Button>}
      <Button type="primary" onClick={()=>{
        // All pages must be visible when the print engine paginates.
        frame.current?.contentDocument?.querySelectorAll<HTMLElement>(".sheet")
          .forEach(sheet=>{sheet.style.display="";});
        frame.current?.contentWindow?.print();
      }}>Drukuj</Button>
    </Space>}>
    {pdfError&&<Alert type="error" showIcon title={pdfError}/>}
    {savedPdf&&<Alert type="success" showIcon title={savedPdf}/>}
    <Space wrap style={{marginBottom:8}}>
      <Button onClick={()=>updateComponents("pdf",{mode:mode==="pages"?"scroll":"pages"})}>
        {mode==="pages"?"Przewijanie":"Tryb stron"}
      </Button>
      {mode==="pages"&&<>
        <Button disabled={page<=1} onClick={()=>setPage(n=>n-1)}>Poprzednia</Button>
        <Typography.Text>Strona {page} z {pages}</Typography.Text>
        <Button disabled={page>=pages} onClick={()=>setPage(n=>n+1)}>Następna</Button>
      </>}
    </Space>
    <iframe ref={frame} title="Strony cennika A4" srcDoc={documentText}
      sandbox="allow-same-origin allow-scripts allow-modals"
      onLoad={()=>setFrameReady(value=>value+1)}
      style={{width:"100%",height:"74vh",border:0,background:"#d8d9dd"}}/>
  </Modal>;
}
