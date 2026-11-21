import Modal from "./AppModal";
import {ListFormattingPanel,useListFormatting} from "./ListFormatting";
import {moveColumn,useTableMouseLayout} from "./TableMouseLayout";
import {useEffect,useState} from "react";
import {Button,Checkbox,InputNumber,Tabs,Typography} from "antd";
import type {ColumnsType} from "antd/es/table";

const specs=[
  {key:"name",label:"Lek",width:260},
  {key:"batch",label:"Seria",width:150},
  {key:"received",label:"Data przyjęcia",width:160},
  {key:"expires",label:"Data ważności",width:160},
  {key:"remaining",label:"Pozostało",width:125},
  {key:"warehouseName",label:"Magazyn",width:160},
  {key:"status",label:"Status",width:145}
] as const;
const keys=specs.map(item=>item.key);
const prefix="kl.expiryTable.";

function restoreKeys(key:string):string[]{
  try{
    const data:unknown=JSON.parse(localStorage.getItem(prefix+key)??"null");
    if(Array.isArray(data)){
      const valid=data.filter((item:unknown):item is string=>
        typeof item==="string"&&keys.includes(item as (typeof keys)[number]));
      if(valid.length)return [...new Set(valid)];
    }
  }catch{/* Older local preferences may not be JSON. */}
  return [...keys];
}
function restoreOrder():string[]{
  const saved=restoreKeys("order");
  // Every new column must appear even if the stored layout predates it.
  return [...saved,...keys.filter(item=>!saved.includes(item))];
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
export function useExpiryTableLayout<T extends object>(columns:ColumnsType<T>){
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
    setOrder(old=>{
      const from=old.indexOf(key),to=from+delta;
      if(from<0||to<0||to>=old.length)return old;
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

  const formatting=useListFormatting("expiry");
  const mouse = useTableMouseLayout("expiry");
  const mouseColumns = mouse.decorate(arranged, column=>String(column.key??""),
    (key,width)=>setWidths(previous=>({...previous,[key]:width})),
    (source,target)=>setOrder(previous=>moveColumn(previous,source,target)), 80, 650);

  const dialog=<Modal className="kl-dialog-expiry-columns" open={open}
    title="Parametry listy — terminy ważności"
    onCancel={()=>setOpen(false)} onOk={()=>setOpen(false)}
    okText="Gotowe" cancelButtonProps={{style:{display:"none"}}}>
    <Tabs items={[
      {key:"columns",label:"Kolumny",children:<>
    <Typography.Paragraph type="secondary">
      Widoczność, kolejność i szerokość kolumn są zapamiętywane wyłącznie na tym stanowisku.
      Zgodnie z WinForms musi pozostać widoczna przynajmniej jedna kolumna.
    </Typography.Paragraph>
    <Checkbox.Group value={visible} onChange={values=>{
      const chosen=values.map(String);
      if(chosen.length)setVisible([...new Set(chosen)]);
    }}
      style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:8}}
      options={specs.map(item=>({value:item.key,label:item.label,
        disabled:visible.length===1&&visible.includes(item.key)}))}/>
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
          <Button size="small" disabled={order.indexOf(key)<=0}
            aria-label={"Przesuń "+item.label+" w lewo"}
            onClick={()=>move(key,-1)}>←</Button>
          <Button size="small" disabled={order.indexOf(key)===order.length-1}
            aria-label={"Przesuń "+item.label+" w prawo"}
            onClick={()=>move(key,1)}>→</Button>
        </div>;
      })}
    </div>
    <Button size="small" style={{marginTop:14}}
      onClick={()=>{
        setVisible([...keys]);setOrder([...keys]);setWidths({});mouse.resetHeight();
      }}>Przywróć domyślne kolumny</Button>
      </>},
      {key:"format",label:"Formatowanie",children:<ListFormattingPanel formatting={formatting} columns={specs.map(item=>({...item}))}/>}
    ]}/>
  </Modal>;
  return {columns:formatting.apply(mouseColumns,column=>String(column.key??"")),tableProps:mouse.tableProps,dialog,openColumns:()=>setOpen(true)};
}
