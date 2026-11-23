import Modal from "./AppModal";
import {ListFormattingPanel,useListFormatting} from "./ListFormatting";
import {moveColumn,useTableMouseLayout} from "./TableMouseLayout";
import {useEffect,useState} from "react";
import {useComponents} from "./componentSettings";
import {Button,Checkbox,Dropdown,InputNumber,Tabs,Typography} from "antd";
import type {ColumnsType} from "antd/es/table";

/** Workstation-local, WinForms-style column visibility, order and widths. */
export function useListColumnLayout<T extends object>(
  listId:string,columns:ColumnsType<T>,shortcutEnabled=true,requiredColumn?:string
) {
  const rememberColumns=useComponents().table.rememberColumns;
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
    if(!rememberColumns)return {visible:keys,order:keys,widths:{}};
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
  const [visible,setVisible]=useState<string[]>(()=>[...new Set([...initial.visible,...(requiredColumn ? [requiredColumn] : [])])]);
  const [order,setOrder]=useState<string[]>(initial.order);
  const [widths,setWidths]=useState<Record<string,number>>(initial.widths);
  const [open,setOpen]=useState(false);

  useEffect(()=>{
    if(!rememberColumns)return;
    try{localStorage.setItem(storageKey,JSON.stringify({visible,order,widths}));}
    catch{/* No SQL dependency; current session remains usable. */}
  },[storageKey,visible,order,widths,rememberColumns]);
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

  const formatting=useListFormatting(listId);
  const mouse = useTableMouseLayout(listId);
  const mouseColumns = mouse.decorate(arranged, identity,
    (key,width)=>setWidths(previous=>({...previous,[key]:width})),
    (source,target)=>setOrder(previous=>moveColumn(previous,source,target)));

  function resetColumns(){
    setVisible([...keys]);setOrder([...keys]);setWidths({});mouse.resetHeight();
  }

  function fitColumns(table:HTMLElement){
    const grid=table.querySelector(".kl-ag-grid");
    if(grid){grid.dispatchEvent(new Event("kl-fit-columns"));return;}
    const context=document.createElement("canvas").getContext("2d");
    if(!context)return;
    const rows=Array.from(table.querySelectorAll<HTMLTableRowElement>("tr[data-row-key]"));
    const sizes=Object.fromEntries(arranged.map((column,index)=>{
      const header=table.querySelectorAll<HTMLElement>(".ant-table-thead th")[index];
      const label=definitions.find(item=>item.key===identity(column))?.label??"";
      function measure(element:HTMLElement|undefined,text:string){
        if(!element)return 0;
        const style=getComputedStyle(element.firstElementChild??element);
        context!.font=[style.fontWeight,style.fontSize,style.fontFamily].join(" ");
        return context!.measureText(text.trim()).width;
      }
      const content=rows.map(row=>measure(row.cells[index],row.cells[index]?.textContent??""));
      return [identity(column),Math.max(60,Math.min(1000,Math.ceil(Math.max(
        measure(header,label)+40,...content.map(width=>width+26)))))];
    }));
    setWidths(previous=>({...previous,...sizes}));
  }

  const dialog=<Modal className={"kl-dialog-columns-"+listId} open={open}
      title="Parametry listy — kolumny" onCancel={()=>setOpen(false)}
      onOk={()=>setOpen(false)} okText="Gotowe"
      cancelButtonProps={{style:{display:"none"}}}>
    <Tabs items={[
      {key:"columns",label:"Kolumny",children:<>
    <Typography.Paragraph type="secondary">
      Widoczność, kolejność i szerokość kolumn obowiązują wyłącznie na tym stanowisku.
      Przynajmniej jedna kolumna musi pozostać widoczna.
    </Typography.Paragraph>
    <Checkbox.Group value={visible} onChange={values=>{
        const next=values.map(String);
        if(next.length)setVisible([...new Set([...next,...(requiredColumn ? [requiredColumn] : [])])]);
      }}
      options={definitions.map(item=>({label:item.label,value:item.key,
        disabled:item.key===requiredColumn||(visible.length===1&&visible.includes(item.key))}))}
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
    <Button size="small" onClick={resetColumns}>Przywróć domyślne kolumny</Button>
      </>},
      {key:"format",label:"Formatowanie",children:<ListFormattingPanel formatting={formatting} columns={definitions}/>}
    ]}/>
  </Modal>;
  const grid = {
    id:listId,
    resize:(key:string,width:number)=>setWidths(previous=>({...previous,[key]:width})),
    move:(visibleOrder:string[])=>setOrder(previous=>{
      const moved=new Set(visibleOrder);
      let next=0;
      return previous.map(key=>moved.has(key)?visibleOrder[next++]:key);
    })
  };
  return {grid,columns:formatting.apply(mouseColumns,identity),tableProps:mouse.tableProps,openColumns:()=>setOpen(true),resetColumns,fitColumns,dialog};
}

// Forward the event at the table boundary before a grid intercepts bubbling.
export function TableContextTrigger({onContextMenu,onContextMenuCapture,...props}:
  import("react").ComponentPropsWithRef<"div">) {
  return <div {...props} onContextMenuCapture={event=>{
    onContextMenuCapture?.(event);
    if(onContextMenu){
      onContextMenu(event);
      event.stopPropagation();
    }
  }}/>;
}

export function ListParametersMenu({children,onOpen,fillHeight}: {
  children: import("react").ReactNode;
  onOpen: () => void;
  fillHeight?: boolean;
}) {
  return <Dropdown trigger={["contextMenu"]}
    menu={{items:[{key:"parameters",label:"Parametry listy",onClick:onOpen}]}}>
    <TableContextTrigger className={fillHeight?"fill-list-table":undefined}>{children}</TableContextTrigger>
  </Dropdown>;
}
