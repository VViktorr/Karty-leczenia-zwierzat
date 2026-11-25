import {useState} from "react";
import {Button, Input, Select, Space, Typography} from "antd";
import Modal from "./AppModal";

type Entry = {id:string; label:string; source:string};
type Props = {
  entries:Entry[];
  onClose:()=>void;
  onApply?:(id:string,value:string)=>void;
  onCatalogue?:(id:string)=>void;
};

function shorten(source:string) {
  return source.trim()
    .replace(/\bTkanki jadalne\s*:/giu, "Tkanki:")
    .replace(/\bOkres karencji\s*:\s*/giu, "")
    .replace(/[^\S\r\n]{2,}/gu, " ");
}

export default function WithdrawalAssistant({entries,onClose,onApply,onCatalogue}:Props) {
  const [selected,setSelected]=useState(entries[0]?.id);
  const entry=entries.find(item=>item.id===selected) ?? entries[0];
  if(!entry)return null;
  const proposal=shorten(entry.source);
  const changed=proposal!==entry.source.trim();

  return <Modal className="kl-dialog-withdrawal-assistant" title="Asystent karencji"
    open width="min(960px, 94vw)" onCancel={onClose} footer={<Space wrap>
      <Button onClick={onClose}>Zamknij</Button>
      {onCatalogue&&<Button onClick={()=>onCatalogue(entry.id)}>Otwórz warianty w katalogu</Button>}
      {onApply&&<Button type="primary" disabled={!changed}
        onClick={()=>onApply(entry.id,proposal)}>Zastosuj do karty</Button>}
    </Space>}>
    <Space orientation="vertical" style={{width:"100%"}}>
      <Select aria-label="Lek" value={entry.id} onChange={setSelected} style={{width:"100%"}}
        options={entries.map(item=>({label:item.label,value:item.id}))}/>
      <Typography.Text strong>Tekst źródłowy</Typography.Text>
      <Input.TextArea aria-label="Tekst źródłowy karencji" value={entry.source} readOnly rows={4}/>
      <Typography.Text strong>Proponowany zapis</Typography.Text>
      <Input.TextArea aria-label="Proponowany zapis karencji" value={proposal} readOnly rows={4}/>
      {!changed&&<Typography.Text type="secondary">Brak prostego skrótu. Pełny zapis pozostaje dostępny.</Typography.Text>}
      {onCatalogue&&<Typography.Text type="secondary">Krótkie wpisy dla tkanek, mleka i jaj zapisujesz przy właściwym wariancie w katalogu.</Typography.Text>}
    </Space>
  </Modal>;
}
