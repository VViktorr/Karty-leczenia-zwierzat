import {Modal as AntModal, type ModalFuncProps, type ModalProps} from "antd";
import ResizableDialogFrame from "./ResizableDialogFrame";

function renderFrame({className, title, modalRender}: ModalProps | ModalFuncProps): ModalProps["modalRender"] {
  const stableClass = className?.split(/\s+/).find(name=>name.startsWith("kl-dialog-"));
  const key = stableClass?.slice("kl-dialog-".length) ?? (typeof title === "string" ? title : "dialog");
  return content => <ResizableDialogFrame key={key} storageKey={key}>
    {modalRender ? modalRender(content) : content}
  </ResizableDialogFrame>;
}

function AppModal(props:ModalProps) {
  return <AntModal {...props} style={{top:12,...props.style}} modalRender={renderFrame(props)}/>;
}

export default Object.assign(AppModal, {
  confirm: (props:ModalFuncProps) => AntModal.confirm({...props, style:{top:12,...props.style}, modalRender:renderFrame(props)})
});
