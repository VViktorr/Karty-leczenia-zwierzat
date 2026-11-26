import type {TableProps} from "antd";
import {Spin, Table, Tooltip} from "antd";
import type {ReactNode} from "react";
import {lazy, Suspense, useMemo} from "react";
import {useComponents} from "./componentSettings";

// Tooltip forwards this positioning option to its underlying trigger.
const cursorTooltipPosition = {alignPoint: true};

const DataGrid = lazy(() => import("./DataGrid")) as typeof import("./DataGrid").default;

export function TableTextPreview({text, children}: {text: string | number; children: ReactNode}) {
  const {tableTextPreview} = useComponents();
  if (!tableTextPreview.enabled || (typeof text === "string" && !text.trim())) return children;
  return <Tooltip {...cursorTooltipPosition} placement="top"
    mouseEnterDelay={tableTextPreview.delaySeconds}
    styles={{root:{maxWidth:"min(560px, 80vw)"}}}
    title={<div style={{fontSize:tableTextPreview.fontSize,whiteSpace:"pre-wrap",overflowWrap:"anywhere"}}>
      {text}
    </div>}>
    <span>{children}</span>
  </Tooltip>;
}

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
          return <TableTextPreview text={output}>{output}</TableTextPreview>;
        }};
      });
    return addTextPreview(props.columns);
  },[props.columns,tableTextPreview]);
  const complexColumns = props.columns?.some(column => "children" in column && !!column.children);
  const canUseGrid = !complexColumns && !props.rowSelection &&
    !props.expandable && !props.summary &&
    props.columns?.every(column => !!("dataIndex" in column && typeof column.dataIndex === "string") ||
      !!("key" in column && typeof column.key === "string")) !== false;
  const useAgGrid = table.mode === "ag" && canUseGrid;
  const lockWidths = !useAgGrid && !complexColumns && !!columns?.length &&
    !!className?.split(/\s+/).includes("kl-mouse-table");
  let antColumns = columns;
  let antScroll = props.scroll?.y === "100%" ? {...props.scroll, y:undefined} : props.scroll;
  if (lockWidths && columns) {
    const sized = columns.map(column=>({...column,width:typeof column.width==="number"?column.width:160}));
    const firstRight = sized.findIndex(column=>column.fixed==="right");
    const spacerAt = firstRight < 0 ? sized.length : firstRight;
    // A flexible spacer absorbs spare viewport width instead of stretching user-sized columns.
    antColumns = [...sized.slice(0,spacerAt), {key:"kl-column-spacer",className:"kl-column-spacer"},
      ...sized.slice(spacerAt)];
    const selectionWidth = props.rowSelection
      ? (typeof props.rowSelection.columnWidth==="number"?props.rowSelection.columnWidth:48) : 0;
    const expandWidth = props.expandable
      ? (typeof props.expandable.columnWidth==="number"?props.expandable.columnWidth:48) : 0;
    const width = sized.reduce((sum,column)=>sum+column.width,selectionWidth+expandWidth+1);
    antScroll = {...antScroll,x:Math.max(width,typeof antScroll?.x==="number"?antScroll.x:0)};
  }
  return useAgGrid
    ? <Suspense fallback={<Spin aria-label="Ładowanie tabeli"/>}>
        <DataGrid<T> {...props} columns={columns} wrapRows={wrapRows} wrapHeaders={wrapHeaders} style={style} resize={grid?.resize} move={grid?.move}/>
      </Suspense>
    : <Table<T> {...props} columns={antColumns} tableLayout={lockWidths?"fixed":props.tableLayout}
        scroll={antScroll} className={[className, !wrapHeaders && "kl-table-nowrap-headers"].filter(Boolean).join(" ")} style={style}/>;
}
