import {useEffect,useState} from "react";
import {Button,Checkbox,InputNumber,Modal,Typography} from "antd";
import type {ColumnsType} from "antd/es/table";
import type {ExpiryViewRow} from "./expiryGrouping";

const specs=[
  {key:"name",label:"Lek",width:260},
  {key:"batch",label:"Seria",width:150},
  {key:"received",label:"Data przyjęcia",width:160},
  {key:"expires",label:"Data ważności",width:160},
  {key:"remaining",label:"Pozostało",width:125},
  {key:"deliveryCount",label:"Dostawy",width:105},
  {key:"warehouseName",label:"Magazyn",width:160},
  {key:"status",label:"Status",width:145}
] as const;
const keys=specs.map(item=>item.key);
const fixed=["name","expires"];
const prefix="kl.expiryTable.";

function restoreKeys(key:string):string[]{
  try{
    const data:unknown=JSON.parse(localStorage.getItem(prefix+key)??"null");
    if(Array.isArray(data)){
      const valid=data.filter((item:unknown):item is string=>
        typeof item==="string"&&keys.includes(item as (typeof keys)[number]));
      return [...new Set([...fixed,...valid])];
    }
  }catch{/* Older local preferences may not be JSON. */}
  return [...keys];
}
function restoreOrder():string[]{
  const saved=restoreKeys("order");
  // Every new column must appear even if the stored layout predates it.
  return ["name",...saved.filter(item=>item!=="name"),
    ...keys.filter(item=>item!=="name"&&!saved.includes(item))];
}
function restoreWidths():Record<string,number>{
  try{
    const data:unknown=JSON.parse(localStorage.getItem(prefix+"widths")??"null");
    if(data&&typeof data==="object"&&!Array.isArray(data))
      return Object.fromEntries(Object.entries(data).filter(([key,value])=>
        keys.includes(key as (typeof keys)[number])&&
        typeof value==="number"&&Number.isFinite(value)&&value>=80&&value<=650));
  }catch{/* Use default table widths. */}
  return {};
}
export function useExpiryTableLayout(columns:ColumnsType<ExpiryViewRow>){
  const [visible,setVisible]=useState<string[]>(()=>restoreKeys("visible"));
  const [order,setOrder]=useState<string[]>(restoreOrder);
  const [widths,setWidths]=useState<Record<string,number>>(restoreWidths);
  const [open,setOpen]=useState(false);

  useEffect(()=>{
    try{
      localStorage.setItem(prefix+"visible",JSON.stringify(visible));
      localStorage.setItem(prefix+"order",JSON.stringify(order));
      localStorage.setItem(prefix+"widths",JSON.stringify(widths));
    }catch{/* Keep active layout for this session. */}
  },[visible,order,widths]);

  useEffect(()=>{
    const key=(event:KeyboardEvent)=>{
      if(event.ctrlKey&&event.shiftKey&&event.code==="KeyL"){
        event.preventDefault();setOpen(value=>!value);
      }
    };
    window.addEventListener("keydown",key);
    return()=>window.removeEventListener("keydown",key);
  },[]);

  function move(key:string,delta:-1|1){
    if(key==="name")return;
    setOrder(old=>{
      const from=old.indexOf(key),to=from+delta;
      if(from<1||to<1||to>=old.length)return old;
      const next=[...old];
      [next[from],next[to]]=[next[to],next[from]];
      return next;
    });
  }
  const arranged=columns.filter(column=>visible.includes(String(column.key??"")))
    .sort((left,right)=>order.indexOf(String(left.key))-
      order.indexOf(String(right.key)))
    .map(column=>({
      ...column,width:widths[String(column.key)]??column.width
    }));

  const dialog=<Modal className="kl-dialog-expiry-columns" open={open}
    title="Parametry listy — terminy ważności"
    onCancel={()=>setOpen(false)} onOk={()=>setOpen(false)}
    okText="Gotowe" cancelButtonProps={{style:{display:"none"}}}>
    <Typography.Paragraph type="secondary">
      Widoczność, kolejność i szerokość kolumn są zapamiętywane wyłącznie na tym stanowisku.
      Lek i data ważności pozostają widoczne.
    </Typography.Paragraph>
    <Checkbox.Group value={visible} onChange={values=>
      setVisible([...new Set([...fixed,...values.map(String)])])}
      style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:8}}
      options={specs.map(item=>({value:item.key,label:item.label,
        disabled:fixed.includes(item.key)}))}/>
    <Typography.Paragraph style={{marginTop:15}} type="secondary">
      Szerokości w pikselach i kolejność widocznych kolumn:
    </Typography.Paragraph>
    <div style={{maxHeight:300,overflowY:"auto"}}>
      {order.filter(key=>visible.includes(key)).map(key=>{
        const item=specs.find(spec=>spec.key===key)!;
        return <div key={key} style={{display:"flex",alignItems:"center",gap:8,marginBottom:7}}>
          <span style={{flex:1}}>{item.label}</span>
          <InputNumber aria-label={"Szerokość: "+item.label} style={{width:100}}
            min={80} max={650} value={widths[key]??item.width}
            onChange={value=>{if(value!==null)setWidths(previous=>({
              ...previous,[key]:Math.max(80,Math.min(650,value))
            }));}}/>
          <Button size="small" disabled={key==="name"||order.indexOf(key)<=1}
            aria-label={"Przesuń "+item.label+" w lewo"}
            onClick={()=>move(key,-1)}>←</Button>
          <Button size="small" disabled={key==="name"||order.indexOf(key)===order.length-1}
            aria-label={"Przesuń "+item.label+" w prawo"}
            onClick={()=>move(key,1)}>→</Button>
        </div>;
      })}
    </div>
    <Button size="small" style={{marginTop:14}}
      onClick={()=>{
        setVisible([...keys]);setOrder([...keys]);setWidths({});
      }}>Przywróć domyślne kolumny</Button>
  </Modal>;
  return {columns:arranged,dialog,openColumns:()=>setOpen(true)};
}
