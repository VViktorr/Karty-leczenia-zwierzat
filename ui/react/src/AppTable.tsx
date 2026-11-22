import {ProTable, type ProColumns} from "@ant-design/pro-table";
import type {TableProps} from "antd";
import {Button, Spin} from "antd";
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
  // ProTable's renderer receives formatted content. Existing cell editors use raw values.
  const columns = props.columns?.map(column => {
    if ("children" in column || typeof column.title === "function")
      throw new Error("AppTable requires flat columns with static headers.");
    const {render, dataIndex, title, ...definition} = column;
    const mapped: ProColumns<T> = {...definition, dataIndex, title};
    if (render) mapped.render = (_display, row, index) => {
      const path = Array.isArray(dataIndex) ? dataIndex : dataIndex === undefined ? [] : [dataIndex];
      const value = path.reduce<unknown>((current, key) =>
        (current as Record<PropertyKey, unknown> | null | undefined)?.[String(key)], row);
      return render(path.length ? value : undefined, row, index) as ReturnType<NonNullable<ProColumns<T>["render"]>>;
    };
    return mapped;
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
      : <ProTable<T> {...props} columns={columns} search={false} options={false} toolBarRender={false}
          tableAlertRender={false} cardProps={false} columnEmptyText={false}
          tableClassName={className} tableStyle={style}/>}
  </>;
}
