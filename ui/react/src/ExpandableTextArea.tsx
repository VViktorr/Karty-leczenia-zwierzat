import {useState, type ComponentProps} from "react";
import {Dropdown, Input} from "antd";
import Modal from "./AppModal";

type Props = Omit<ComponentProps<typeof Input.TextArea>, "onChange"> & {
  onValueChange?: (value: string) => void;
  onApply?: (value: string) => void;
  editValue?: string;
};

export default function ExpandableTextArea({onValueChange, onApply, editValue, ...props}: Props) {
  const [draft, setDraft] = useState<string | null>(null);
  const current = editValue ?? String(props.value ?? props.defaultValue ?? "");
  const open = () => setDraft(current);
  const editable = !props.disabled && !props.readOnly && !!onValueChange;

  return <>
    <Dropdown trigger={["contextMenu"]} disabled={!editable} menu={{items:[
      {key:"expand", label:"Otwórz w większym oknie edycji", onClick:open}
    ]}}>
      <span style={{display:"block"}} onContextMenu={event=>{
        if (editable) event.stopPropagation();
      }}>
        <Input.TextArea {...props} onChange={event=>onValueChange?.(event.target.value)}/>
      </span>
    </Dropdown>
    {draft !== null && <Modal className="kl-dialog-expanded-text" open width="min(1000px, 92vw)"
      title={props["aria-label"] || "Edycja tekstu"} okText="Zastosuj" cancelText="Anuluj"
      maskClosable={false} onCancel={()=>setDraft(null)}
      onOk={()=>{
        if (draft !== current) (onApply ?? onValueChange)?.(draft);
        setDraft(null);
      }}>
      <Input.TextArea autoFocus aria-label={props["aria-label"] || "Edytowany tekst"}
        rows={12} maxLength={props.maxLength} value={draft}
        onChange={event=>setDraft(event.target.value)}/>
    </Modal>}
  </>;
}
