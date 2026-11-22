import type {TableProps} from "antd";
import {Button, Spin, Table} from "antd";
import {lazy, Suspense, useState} from "react";

const DataGrid = lazy(() => import("./DataGrid")) as typeof import("./DataGrid").default;

type GridOptions = {
  id: string;
  resize: (key: string, width: number) => void;
  move: (order: string[]) => void;
};

export default function AppTable<T extends object>({grid, className, style, ...props}:
  TableProps<T> & {grid?: GridOptions}) {
  const [gridEnabled, setGridEnabled] = useState(() => {
    try { return grid !== undefined && localStorage.getItem("kl.ag-grid." + grid.id) === "true"; }
    catch { return false; }
  });
  return <>
    {grid && <Button size="small" className="kl-grid-toggle" aria-pressed={gridEnabled}
      onClick={() => {
        const next = !gridEnabled;
        setGridEnabled(next);
        try { localStorage.setItem("kl.ag-grid." + grid.id, String(next)); } catch { /* Session only. */ }
      }}>{gridEnabled ? "Widok standardowy" : "Widok AG Grid"}</Button>}
    {grid && gridEnabled
      ? <Suspense fallback={<Spin aria-label="Ładowanie tabeli"/>}>
          <DataGrid<T> {...props} resize={grid.resize} move={grid.move}/>
        </Suspense>
      : <Table<T> {...props} className={className} style={style}/>}
  </>;
}
