import {moveColumn,useTableMouseLayout} from "./TableMouseLayout";
import {useEffect,useState} from "react";
import {Button,Checkbox,InputNumber,Modal,Typography} from "antd";
import type {ColumnsType} from "antd/es/table";

/** Workstation-local, WinForms-style column visibility, order and widths. */
export function useListColumnLayout<T extends object>(
  listId:string,columns:ColumnsType<T>,shortcutEnabled=true
) {
  const identity=(column:ColumnsType<T>[number]):string =>
    String(column.key ?? ("dataIndex" in column ? column.dataIndex : ""));
  const definitions=columns.map(column=>({
    key:identity(column),
    label:typeof column.title==="string"?column.title:identity(column),
    width:typeof column.width==="number"?column.width:160
  })).filter(item=>item.key.length>0);
  const keys=definitions.map(item=>item.key);
  const storageKey="kl.columns."+listId+".v1";
  function read():{visible:string[];order:string[];widths:Record<string,number>} {
    try{
      const parsed:unknown=JSON.parse(localStorage.getItem(storageKey)??"null");
      if(parsed && typeof parsed==="object" && !Array.isArray(parsed)){
        const state=parsed as Record<string,unknown>;
        const valid=(source:unknown)=>Array.isArray(source)
          ? [...new Set(source.filter((item:unknown):item is string=>
              typeof item==="string"&&keys.includes(item)))]
          : [];
        const savedOrder=valid(state.order);
        const savedVisible=valid(state.visible);
        const widths=state.widths && typeof state.widths==="object" &&
          !Array.isArray(state.widths)
          ? Object.fromEntries(Object.entries(state.widths).filter(([key,value])=>
              keys.includes(key)&&typeof value==="number"&&
              Number.isFinite(value)&&value>=60&&value<=1000))
          : {};
        return {
          visible:savedVisible.length?savedVisible:keys,
          order:[...savedOrder,...keys.filter(key=>!savedOrder.includes(key))],
          widths
        };
      }
    }catch{/* Invalid historical settings are reset locally. */}
    return {visible:keys,order:keys,widths:{}};
  }
  const [initial]=useState(read);
  const [visible,setVisible]=useState<string[]>(initial.visible);
  const [order,setOrder]=useState<string[]>(initial.order);
  const [widths,setWidths]=useState<Record<string,number>>(initial.widths);
  const [open,setOpen]=useState(false);

  useEffect(()=>{
    try{localStorage.setItem(storageKey,JSON.stringify({visible,order,widths}));}
    catch{/* No SQL dependency; current session remains usable. */}
  },[storageKey,visible,order,widths]);
  useEffect(()=>{
    if(!shortcutEnabled)return;
    const shortcut=(event:KeyboardEvent)=>{
      if(event.ctrlKey&&event.shiftKey&&event.code==="KeyL"){
        event.preventDefault();setOpen(value=>!value);
      }
    };
    window.addEventListener("keydown",shortcut);
    return()=>window.removeEventListener("keydown",shortcut);
  },[shortcutEnabled]);

  const arranged=columns.filter(column=>visible.includes(identity(column)))
    .sort((a,b)=>order.indexOf(identity(a))-order.indexOf(identity(b)))
    .map(column=>({...column,width:widths[identity(column)]??column.width}));

  function move(key:string,delta:-1|1){
    setOrder(previous=>{
      const from=previous.indexOf(key),to=from+delta;
      if(from<0||to<0||to>=previous.length)return previous;
      const next=[...previous];
      [next[from],next[to]]=[next[to],next[from]];
      return next;
    });
  }

  const mouse = useTableMouseLayout(listId);
  const mouseColumns = mouse.decorate(arranged, identity,
    (key,width)=>setWidths(previous=>({...previous,[key]:width})),
    (source,target)=>setOrder(previous=>moveColumn(previous,source,target)));

  const dialog=<Modal className={"kl-dialog-columns-"+listId} open={open}
      title="Parametry listy — kolumny" onCancel={()=>setOpen(false)}
      onOk={()=>setOpen(false)} okText="Gotowe"
      cancelButtonProps={{style:{display:"none"}}}>
    <Typography.Paragraph type="secondary">
      Widoczność, kolejność i szerokość kolumn obowiązują wyłącznie na tym stanowisku.
      Przynajmniej jedna kolumna musi pozostać widoczna.
    </Typography.Paragraph>
    <Checkbox.Group value={visible} onChange={values=>{
        const next=values.map(String);
        if(next.length)setVisible([...new Set(next)]);
      }}
      options={definitions.map(item=>({label:item.label,value:item.key,
        disabled:visible.length===1&&visible.includes(item.key)}))}
      style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:8}}/>
    <div style={{marginTop:16,maxHeight:300,overflowY:"auto"}}>
      {order.filter(key=>visible.includes(key)).map(key=>{
        const item=definitions.find(value=>value.key===key);
        if(!item)return null;
        return <div key={key} style={{display:"flex",gap:7,alignItems:"center",marginBottom:7}}>
          <span style={{flex:1}}>{item.label}</span>
          <InputNumber min={60} max={1000} style={{width:90}}
            aria-label={"Szerokość kolumny "+item.label}
            value={widths[key]??item.width}
            onChange={value=>{if(value!==null)setWidths(previous=>({
              ...previous,[key]:Math.max(60,Math.min(1000,value))
            }));}}/>
          <Button size="small" disabled={order.indexOf(key)===0}
            aria-label={"Przesuń "+item.label+" w lewo"}
            onClick={()=>move(key,-1)}>←</Button>
          <Button size="small" disabled={order.indexOf(key)===order.length-1}
            aria-label={"Przesuń "+item.label+" w prawo"}
            onClick={()=>move(key,1)}>→</Button>
        </div>;
      })}
    </div>
    <Button size="small" onClick={()=>{
      setVisible([...keys]);setOrder([...keys]);setWidths({});mouse.resetHeight();
    }}>Przywróć domyślne kolumny</Button>
  </Modal>;
  return {columns:mouseColumns,tableProps:mouse.tableProps,openColumns:()=>setOpen(true),dialog};
}
