import type {TableProps} from "antd";
import {Spin, Table} from "antd";
import {lazy, Suspense} from "react";
import {useComponents} from "./componentSettings";

const DataGrid = lazy(() => import("./DataGrid")) as typeof import("./DataGrid").default;

type GridOptions = {
  id: string;
  resize: (key: string, width: number) => void;
  move: (order: string[]) => void;
};

/** All compatible grids use one workstation-wide engine setting.
 * Keep Ant Design for special row-selection/expandable/grouped tables
 * until the corresponding interactions have AG Grid equivalents.
 */
export default function AppTable<T extends object>({grid, className, style, wrapRows, ...props}:
  TableProps<T> & {grid?: GridOptions; wrapRows?: boolean}) {
  const {table} = useComponents();
  const complexColumns = props.columns?.some(column => "children" in column && !!column.children);
  const canUseGrid = !complexColumns && !props.rowSelection &&
    !props.expandable && !props.summary &&
    props.columns?.every(column => !!("dataIndex" in column && typeof column.dataIndex === "string") ||
      !!("key" in column && typeof column.key === "string")) !== false;
  return table.mode === "ag" && canUseGrid
    ? <Suspense fallback={<Spin aria-label="Ładowanie tabeli"/>}>
        <DataGrid<T> {...props} wrapRows={wrapRows} style={style} resize={grid?.resize} move={grid?.move}/>
      </Suspense>
    : <Table<T> {...props} className={className} style={style}/>;
}
