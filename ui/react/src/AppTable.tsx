import type {TableProps} from "antd";
import {Spin, Table, Tooltip} from "antd";
import {lazy, Suspense, useMemo} from "react";
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
export default function AppTable<T extends object>({grid, className, style, wrapRows, wrapHeaders = true, ...props}:
  TableProps<T> & {grid?: GridOptions; wrapRows?: boolean; wrapHeaders?: boolean}) {
  const {table, tableTextPreview} = useComponents();
  const columns = useMemo(()=>{
    if(!tableTextPreview.enabled || !props.columns)return props.columns;
    const addTextPreview=(items:NonNullable<TableProps<T>["columns"]>):NonNullable<TableProps<T>["columns"]> =>
      items.map(column=>{
        if("children" in column)return {...column,children:addTextPreview(column.children)};
        if(!column.render && column.dataIndex===undefined)return column;
        return {...column,render:(value,row,index)=>{
          const output=column.render ? column.render(value,row,index) : value;
          if(typeof output!=="string" && typeof output!=="number")return output;
          if(typeof output==="string" && !output.trim())return output;
          return <Tooltip mouseEnterDelay={tableTextPreview.delaySeconds}
            styles={{root:{maxWidth:"min(560px, 80vw)"}}}
            title={<div style={{fontSize:tableTextPreview.fontSize,whiteSpace:"pre-wrap",overflowWrap:"anywhere"}}>
              {output}
            </div>}>
            <span>{output}</span>
          </Tooltip>;
        }};
      });
    return addTextPreview(props.columns);
  },[props.columns,tableTextPreview]);
  const complexColumns = props.columns?.some(column => "children" in column && !!column.children);
  const canUseGrid = !complexColumns && !props.rowSelection &&
    !props.expandable && !props.summary &&
    props.columns?.every(column => !!("dataIndex" in column && typeof column.dataIndex === "string") ||
      !!("key" in column && typeof column.key === "string")) !== false;
  return table.mode === "ag" && canUseGrid
    ? <Suspense fallback={<Spin aria-label="Ładowanie tabeli"/>}>
        <DataGrid<T> {...props} columns={columns} wrapRows={wrapRows} wrapHeaders={wrapHeaders} style={style} resize={grid?.resize} move={grid?.move}/>
      </Suspense>
    : <Table<T> {...props} columns={columns}
        scroll={props.scroll?.y === "100%" ? {...props.scroll, y:undefined} : props.scroll} className={[className, !wrapHeaders && "kl-table-nowrap-headers"].filter(Boolean).join(" ")} style={style}/>;
}
