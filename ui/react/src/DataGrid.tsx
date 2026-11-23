import {AgGridReact} from "ag-grid-react";
import type {CustomCellRendererProps, CustomInnerHeaderProps} from "ag-grid-react";
import {AllCommunityModule, ModuleRegistry, themeQuartz} from "ag-grid-community";
import type {ColDef, GridApi} from "ag-grid-community";
import {Empty, Pagination, Spin, theme} from "antd";
import type {TableProps} from "antd";
import {useEffect, useMemo, useRef, useState} from "react";
import type {CSSProperties, ReactNode} from "react";
import {useComponents} from "./componentSettings";

ModuleRegistry.registerModules([AllCommunityModule]);

type Column<T extends object> = NonNullable<TableProps<T>["columns"]>[number];

function Header({displayName, style}: CustomInnerHeaderProps & {style?: CSSProperties}) {
  return <span style={{flex:"1 1 auto",minWidth:0,...style}}>{displayName}</span>;
}

function Cell<T extends object>({data, value, node, columnDefinition}: CustomCellRendererProps<T> &
  {columnDefinition: Column<T>}) {
  if (!data || !("render" in columnDefinition) || !columnDefinition.render)
    return value as ReactNode;
  return columnDefinition.render(value, data, node.rowIndex ?? 0) as ReactNode;
}

export default function DataGrid<T extends object>({columns, dataSource, pagination, loading,
  rowKey, scroll, size, rowClassName, style, onRow, locale, resize, move}:
  TableProps<T> & {resize?: (key: string, width: number) => void; move?: (order: string[]) => void}) {
  const preferences=useComponents().agGrid;
  const viewport = useRef<HTMLDivElement>(null);
  const [api, setApi] = useState<GridApi<T>>();
  const {token} = theme.useToken();
  const gridTheme = useMemo(() => themeQuartz.withParams({
    backgroundColor: token.colorBgContainer,
    foregroundColor: token.colorText,
    headerBackgroundColor: token.colorFillAlter,
    borderColor: token.colorBorderSecondary,
    accentColor: token.colorPrimary,
    fontFamily: token.fontFamily,
    fontSize: token.fontSize,
  }), [token]);
  const rows = useMemo(() => [...(dataSource ?? [])], [dataSource]);
  const pages = pagination === false ? undefined : pagination;
  const [localPage, setLocalPage] = useState(1);
  const pageSize = pages?.pageSize ?? 25;
  const page = Math.min(pages?.current ?? localPage, Math.max(1, Math.ceil(rows.length / pageSize)));
  useEffect(() => { api?.paginationGoToPage(page - 1); }, [api, page, pageSize, rows]);
  useEffect(() => {
    const element = viewport.current;
    const fit = () => api?.autoSizeAllColumns();
    element?.addEventListener("kl-fit-columns", fit);
    return () => element?.removeEventListener("kl-fit-columns", fit);
  }, [api]);

  const columnDefs = useMemo(() => (columns ?? []).flatMap<ColDef<T>>(column => {
    if ("children" in column) return [];
    const index = "dataIndex" in column ? column.dataIndex : undefined;
    const id = String(column.key ?? index);
    const sort = "sorter" in column ? column.sorter : undefined;
    const columnSort = typeof sort === "function" ? sort : undefined;
    const headerStyle = column.onHeaderCell?.(column)?.style;
    return [{
      colId: id,
      headerName: typeof column.title === "string" ? column.title : id,
      width: typeof column.width === "number" ? column.width : 160,
      flex: column.width === undefined ? 1 : null,
      sortable:preferences.sortable && typeof index === "string",
      filter: preferences.filters && typeof index === "string",
      floatingFilter: preferences.filters && typeof index === "string",
      comparator: columnSort ? (_a, _b, a, b) => a.data && b.data ? columnSort(a.data, b.data) : 0 : undefined,
      valueGetter: params => params.data && typeof index === "string"
        ? (params.data as Record<string, unknown>)[index] : undefined,
      cellRenderer: Cell,
      cellRendererParams: {columnDefinition: column},
      cellClass: params => params.data
        ? column.onCell?.(params.data, params.node.rowIndex ?? 0)?.className ?? "" : "",
      cellStyle: params => {
        const style = params.data ? column.onCell?.(params.data, params.node.rowIndex ?? 0)?.style : undefined;
        return {
          fontFamily: style?.fontFamily ?? "",
          fontSize: typeof style?.fontSize === "number" ? style.fontSize + "px" : style?.fontSize ?? "",
          fontWeight: style?.fontWeight ?? "",
          color: style?.color ?? "",
          backgroundColor: style?.backgroundColor ?? "",
          textAlign: style?.textAlign ?? column.align ?? "left",
        };
      },
      headerStyle: {backgroundColor: headerStyle?.backgroundColor ?? ""},
      headerComponentParams: {innerHeaderComponent: Header, innerHeaderComponentParams: {
        style: headerStyle
      }}
    }];
  }), [columns,preferences.sortable,preferences.filters]);
  function rowAttributes(row?: T, index?: number) {
    return row ? onRow?.(row, index) : undefined;
  }

  return <>
    <div ref={viewport} className="kl-ag-grid" style={{height: scroll?.y ?? 360}}>
      <AgGridReact<T> theme={gridTheme} columnDefs={columnDefs} rowData={rows}
        defaultColDef={{minWidth:60, maxWidth:1000,resizable:preferences.resizableColumns}}
        rowBuffer={preferences.rowBuffer}
        headerHeight={preferences.headerHeight}
        animateRows={preferences.animateRows}
        rowHeight={preferences.rowHeight}
        loading={typeof loading === "boolean" ? loading : loading?.spinning}
        loadingOverlayComponent={() => <Spin aria-label="Ładowanie danych"/>}
        noRowsOverlayComponent={() => locale?.emptyText ?? <Empty description="Brak danych"/>}
        pagination={!!pages} paginationPageSize={pageSize} paginationPageSizeSelector={false} suppressPaginationPanel
        getRowId={({data}) => String(typeof rowKey === "function" ? rowKey(data, rows.indexOf(data)) :
          (data as Record<string, unknown>)[String(rowKey ?? "key")])}
        onGridReady={event => setApi(event.api)}
        getRowClass={({data, rowIndex}) => [
          typeof rowClassName === "function" && data ? rowClassName(data, rowIndex, 0) : rowClassName,
        ].filter((part): part is string => typeof part === "string").join(" ")}
        onRowClicked={event => {
          if (event.event) rowAttributes(event.data, event.rowIndex ?? undefined)?.onClick?.(event.event as never);
        }}
        onRowDoubleClicked={event => {
          if (event.event) rowAttributes(event.data, event.rowIndex ?? undefined)?.onDoubleClick?.(event.event as never);
        }}
        onCellKeyDown={event => {
          if (event.event instanceof KeyboardEvent && event.event.key === "Enter" &&
            event.event.target instanceof HTMLElement && !event.event.target.closest("button,input,a,[role=button]")) {
            rowAttributes(event.data, event.rowIndex ?? undefined)?.onDoubleClick?.(event.event as never);
          }
        }}
        onColumnMoved={event => {
          if (event.finished && event.source === "uiColumnMoved")
            move?.(event.api.getAllDisplayedColumns().map(column => column.getColId()));
        }}
        onColumnResized={event => {
          if (event.finished && (event.source === "uiColumnResized" || event.source === "autosizeColumns") && event.column)
            resize?.(event.column.getColId(), event.column.getActualWidth());
        }}/>
    </div>
    {pages && <Pagination {...pages} current={page} pageSize={pageSize} total={rows.length}
      onChange={(next, size) => {setLocalPage(next);pages.onChange?.(next, size);}}
      className="kl-ag-pagination"/>}
  </>;
}
