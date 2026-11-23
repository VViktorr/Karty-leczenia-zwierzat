import type {TableProps} from "antd";
import {Button, Spin, Table} from "antd";
import {lazy, Suspense, useEffect, useState} from "react";
import {useComponents} from "./componentSettings";

const DataGrid = lazy(() => import("./DataGrid")) as typeof import("./DataGrid").default;

type GridOptions = {
  id: string;
  resize: (key: string, width: number) => void;
  move: (order: string[]) => void;
};

export default function AppTable<T extends object>({grid, className, style, ...props}:
  TableProps<T> & {grid?: GridOptions}) {
  const {table} = useComponents();
  const [gridEnabled, setGridEnabled] = useState(() => {
    if(!grid)return false;
    try {
      const saved=localStorage.getItem("kl.ag-grid."+grid.id);
      if(saved!==null)return saved==="true";
    }catch{/* workstation fallback */}
    return table.mode==="ag";
  });
  useEffect(()=>{
    if(!grid){setGridEnabled(false);return;}
    try{
      const saved=table.rememberColumns?localStorage.getItem("kl.ag-grid."+grid.id):null;
      setGridEnabled(saved===null?table.mode==="ag":saved==="true");
    }catch{setGridEnabled(table.mode==="ag");}
  },[table.mode,table.rememberColumns,grid?.id]);
  return <>
    {grid && <Button size="small" className="kl-grid-toggle" aria-pressed={gridEnabled}
      onClick={() => {
        const next = !gridEnabled;
        setGridEnabled(next);
        if(table.rememberColumns) {
          try { localStorage.setItem("kl.ag-grid." + grid.id, String(next)); }
          catch { /* Session only. */ }
        }
      }}>{gridEnabled ? "Widok standardowy" : "Widok AG Grid"}</Button>}
    {grid && gridEnabled
      ? <Suspense fallback={<Spin aria-label="Ładowanie tabeli"/>}>
          <DataGrid<T> {...props} resize={grid.resize} move={grid.move}/>
        </Suspense>
      : <Table<T> {...props} className={className} style={style}/>}
  </>;
}
