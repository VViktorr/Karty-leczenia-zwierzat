import {useEffect, useState} from "react";
import {Alert, Spin, Typography} from "antd";
import {invoke} from "./bridge";
import ChplPdfViewer from "./ChplPdfViewer";

// Use the same WebView2 PrintToPdfAsync result as PDF download and e-mail.
// HTML is shown only when PDF creation fails (for example in browser-only mode).
export default function CardPdfPreview({request, fallbackHtml, name}:{
  request: Record<string,unknown>; fallbackHtml:string; name:string;
}) {
  const requestKey=JSON.stringify(request);
  const [state,setState]=useState<{url:string|null;error:string;busy:boolean}>({
    url:null,error:"",busy:true,
  });
  useEffect(()=>{
    let active=true;
    let objectUrl:string|null=null;
    setState({url:null,error:"",busy:true});
    void invoke<{base64:string}>("previewCardPdf",JSON.parse(requestKey) as Record<string,unknown>)
      .then(result=>{
        if(!active)return;
        const decoded=atob(result.base64);
        const bytes=new Uint8Array(decoded.length);
        for(let i=0;i<decoded.length;i++)bytes[i]=decoded.charCodeAt(i);
        objectUrl=URL.createObjectURL(new Blob([bytes],{type:"application/pdf"}));
        setState({url:objectUrl,error:"",busy:false});
      })
      .catch(reason=>{
        if(!active)return;
        setState({url:null,busy:false,
          error:reason instanceof Error?reason.message:String(reason)});
      });
    return()=>{active=false;if(objectUrl)URL.revokeObjectURL(objectUrl);};
  },[requestKey]);
  return <>
    {state.busy&&<Spin aria-label="Generowanie podglądu PDF karty leczenia"/>}
    {state.url ? <ChplPdfViewer url={state.url} name={name}/> :
      !state.busy&&<>
        <Alert type="warning" showIcon title="Nie udało się utworzyć podglądu PDF."
          description={state.error+" Wyświetlam awaryjny podgląd HTML."}/>
        <iframe title="Awaryjny podgląd HTML karty leczenia" srcDoc={fallbackHtml}
          style={{width:"100%",height:"65vh",border:0,background:"#fff",marginTop:8}}/>
      </>}
    {state.url&&<Typography.Text type="secondary" style={{display:"block",fontSize:12}}>
      To jest rzeczywisty PDF wygenerowany przez WebView2. Aby drukować, wybierz „Podgląd natywny / drukuj”.
    </Typography.Text>}
  </>;
}
