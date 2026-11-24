import {useState, type ComponentProps} from "react";
import {Input} from "antd";
import {ExpandedEditTarget} from "./ListColumnLayout";
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
    <ExpandedEditTarget onOpen={editable ? open : undefined}>
      <Input.TextArea {...props} onChange={event=>onValueChange?.(event.target.value)}/>
    </ExpandedEditTarget>
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
