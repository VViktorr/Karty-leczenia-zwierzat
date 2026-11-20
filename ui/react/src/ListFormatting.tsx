import {useEffect, useState} from "react";
import type {CSSProperties} from "react";
import {Alert, Button, Checkbox, Form, InputNumber, Select, Space} from "antd";
import type {ColumnsType} from "antd/es/table";
import {invoke, isDesktop} from "./bridge";

type Area = "header" | "body";
type Format = {
  fontFamily?: string; fontSize?: number; textAlign?: "left" | "center" | "right";
  color?: string; backgroundColor?: string; bold?: boolean;
};
type Formats = Record<string, Partial<Record<Area, Format>>>;
const wholeList = "";

function toStyle(format: Format): CSSProperties {
  return {
    fontFamily: typeof format.fontFamily === "string" && format.fontFamily
      ? JSON.stringify(format.fontFamily) : undefined,
    fontSize: typeof format.fontSize === "number" && format.fontSize >= 8 && format.fontSize <= 40
      ? format.fontSize : undefined,
    textAlign: ["left", "center", "right"].includes(format.textAlign ?? "") ? format.textAlign : undefined,
    color: /^#[0-9a-f]{6}$/i.test(format.color ?? "") ? format.color : undefined,
    backgroundColor: /^#[0-9a-f]{6}$/i.test(format.backgroundColor ?? "") ? format.backgroundColor : undefined,
    fontWeight: typeof format.bold === "boolean" ? (format.bold ? 700 : 400) : undefined
  };
}

export function useListFormatting(listId: string) {
  const storageKey = "kl.listFormatting." + listId;
  const [formats, setFormats] = useState<Formats>(() => {
    try {
      const stored = JSON.parse(localStorage.getItem(storageKey) ?? "{}");
      if (stored && typeof stored === "object" && !Array.isArray(stored)) return stored;
    } catch { /* Default formatting remains available. */ }
    return {};
  });
  useEffect(() => {
    try { localStorage.setItem(storageKey, JSON.stringify(formats)); }
    catch { /* Keep the active formatting. */ }
  }, [formats, storageKey]);

  function apply<T extends object>(columns: ColumnsType<T>,
    identity: (column: ColumnsType<T>[number]) => string): ColumnsType<T> {
    return columns.map(column => {
      if ("children" in column) return column;
      const key = identity(column);
      const styles = (area: Area) => toStyle({...formats[wholeList]?.[area], ...formats[key]?.[area]});
      return {
        ...column,
        onHeaderCell: header => {
          const props = column.onHeaderCell?.(header) ?? {};
          return {...props, style: {...props.style, ...defined(styles("header"))}};
        },
        onCell: (row, index) => {
          const props = column.onCell?.(row, index) ?? {};
          const style = defined(styles("body"));
          return {...props, style: {...props.style, ...style},
            className: Object.keys(style).length
              ? [props.className, "kl-formatted-cell"].filter(Boolean).join(" ") : props.className};
        }
      };
    });
  }
  return {apply, formats, setFormats};
}

function defined(style: CSSProperties) {
  return Object.fromEntries(Object.entries(style).filter(([, value]) => value !== undefined));
}

let installedFonts: Promise<string[]> | undefined;

export function ListFormattingPanel({formatting, columns}: {
  formatting: ReturnType<typeof useListFormatting>;
  columns: {key: string; label: string}[];
}) {
  const {formats, setFormats} = formatting;
  const [column, setColumn] = useState(wholeList);
  const [area, setArea] = useState<Area>("body");
  const [fonts, setFonts] = useState<string[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    if (!isDesktop()) return;
    let active = true;
    installedFonts ??= invoke<string[]>("systemFontFamilies").catch(error => {
      installedFonts = undefined;
      throw error;
    });
    void installedFonts.then(values => {if (active) setFonts(values);})
      .catch(error => {if (active) setError(error instanceof Error ? error.message : String(error));});
    return () => {active = false;};
  }, []);

  const current = formats[column]?.[area] ?? {};
  const effective = {...formats[wholeList]?.[area], ...current};
  const set = <K extends keyof Format,>(field: K, value: Format[K]) =>
    setFormats(previous => {
      const next = {...previous[column]?.[area]};
      if (value === undefined) delete next[field];
      else next[field] = value;
      return {...previous, [column]: {...previous[column], [area]: next}};
    });
  const fontOptions = [...new Set([...fonts, ...(current.fontFamily ? [current.fontFamily] : [])])];
  return <Form layout="vertical">
    {error && <Alert type="warning" showIcon message={error}/>}
    <Form.Item label="Zakres">
      <Select value={column} onChange={setColumn} options={[
        {value:wholeList, label:"Cała lista"},
        ...columns.map(item=>({value:item.key,label:item.label}))
      ]}/>
    </Form.Item>
    <Form.Item label="Obszar">
      <Select value={area} onChange={setArea} options={[
        {value:"body",label:"Komórki"}, {value:"header",label:"Nagłówki"}
      ]}/>
    </Form.Item>
    <Form.Item label="Czcionka systemowa">
      <Select showSearch allowClear placeholder="Domyślna" value={current.fontFamily}
        onChange={value=>set("fontFamily",value)}
        options={fontOptions.map(value=>({value,label:value}))}/>
    </Form.Item>
    <Space wrap>
      <Form.Item label="Rozmiar (px)">
        <InputNumber min={8} max={40} value={current.fontSize} placeholder="Domyślny"
          onChange={value=>set("fontSize",value??undefined)}/>
      </Form.Item>
      <Form.Item label="Wyrównanie">
        <Select allowClear placeholder="Domyślne" value={current.textAlign} style={{width:135}}
          onChange={value=>set("textAlign",value)}
          options={[{value:"left",label:"Do lewej"}, {value:"center",label:"Na środku"},
            {value:"right",label:"Do prawej"}]}/>
      </Form.Item>
      <Form.Item label="Pogrubienie">
        <Checkbox checked={effective.bold??false} onChange={event=>set("bold",event.target.checked)}/>
      </Form.Item>
    </Space>
    <Space wrap>
      <Form.Item label="Kolor tekstu">
        <input aria-label="Kolor tekstu" type="color" value={effective.color??"#000000"}
          onChange={event=>set("color",event.target.value)}/>
      </Form.Item>
      <Form.Item label="Kolor tła">
        <input aria-label="Kolor tła" type="color" value={effective.backgroundColor??"#ffffff"}
          onChange={event=>set("backgroundColor",event.target.value)}/>
      </Form.Item>
    </Space>
    <div style={{padding:8,marginBottom:8,border:"1px solid #8885",...defined(toStyle(effective))}}>
      Podgląd formatowania
    </div>
    <Button size="small" onClick={()=>setFormats(previous=>({
      ...previous, [column]: {...previous[column], [area]: {}}
    }))}>Przywróć domyślne formatowanie</Button>
  </Form>;
}
