import {useState} from "react";
import {Button, Input, Select, Space, Typography} from "antd";
import Modal from "./AppModal";

type Entry = {id:string; label:string; source:string};
type Props = {
  entries:Entry[];
  onClose:()=>void;
  onApply?:(id:string,value:string)=>void;
  onCatalogue?:(id:string)=>void;
  onApplyAll?:(changes:{id:string;source:string;value:string}[])=>void|Promise<void>;
  applyAllLabel?:string;
};

function shorten(source:string) {
  return source.trim()
    .replace(/\bTkanki jadalne\s*:/giu, "Tkanki:")
    .replace(/\bOkres karencji\s*:\s*/giu, "")
    .replace(/(^|[.;]\s+|\n)codziennie przygotować świeży roztwór[.;](?=\s|$)/giu, "$1")
    .replace(/[^\S\r\n]{2,}/gu, " ");
}

export default function WithdrawalAssistant({entries,onClose,onApply,onCatalogue,onApplyAll,applyAllLabel}:Props) {
  const changes=entries.map(item=>({...item,value:shorten(item.source)}))
    .filter(item=>item.value.trim() && item.value!==item.source.trim());
  const changedIds=new Set(changes.map(item=>item.id));
  const [selected,setSelected]=useState(changes[0]?.id ?? entries[0]?.id);
  const [applying,setApplying]=useState(false);
  const entry=entries.find(item=>item.id===selected) ?? entries[0];
  if(!entry)return null;
  const proposal=shorten(entry.source);
  const changed=proposal!==entry.source.trim();

  return <Modal className="kl-dialog-withdrawal-assistant" title="Asystent AI — karencje"
    open width="min(960px, 94vw)" onCancel={applying?undefined:onClose} closable={!applying}
    keyboard={!applying} maskClosable={false} footer={<Space wrap>
      <Button disabled={applying} onClick={onClose}>Zamknij</Button>
      {onApplyAll&&<Button type="primary" loading={applying} disabled={!changes.length} onClick={async()=>{
        setApplying(true);
        try{await onApplyAll(changes);}
        finally{setApplying(false);}
      }}>{applyAllLabel} ({changes.length})</Button>}
      {onCatalogue&&<Button onClick={()=>onCatalogue(entry.id)}>Otwórz warianty w katalogu</Button>}
      {onApply&&<Button type="primary" disabled={applying||!changed}
        onClick={()=>onApply(entry.id,proposal)}>Zastosuj do karty</Button>}
    </Space>}>
    <Space orientation="vertical" style={{width:"100%"}}>
      <Typography.Text type="secondary">
        Lokalne reguły formatowania · Propozycje: {changes.length} z {entries.length}
      </Typography.Text>
      <Select disabled={applying} aria-label="Lek" value={entry.id} onChange={setSelected} style={{width:"100%"}}
        options={entries.map(item=>({label:changedIds.has(item.id)?item.label+" (propozycja)":item.label,value:item.id}))}/>
      <Typography.Text strong>Tekst źródłowy</Typography.Text>
      <Input.TextArea aria-label="Tekst źródłowy karencji" value={entry.source} readOnly rows={4}/>
      <Typography.Text strong>Proponowany zapis</Typography.Text>
      <Input.TextArea aria-label="Proponowany zapis karencji" value={proposal} readOnly rows={4}/>
      {!changed&&<Typography.Text type="secondary">Brak prostego skrótu. Pełny zapis pozostaje dostępny.</Typography.Text>}
      {onCatalogue&&<Typography.Text type="secondary">Krótkie wpisy dla tkanek, mleka i jaj zapisujesz przy właściwym wariancie w katalogu.</Typography.Text>}
    </Space>
  </Modal>;
}
