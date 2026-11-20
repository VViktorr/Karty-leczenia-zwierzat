import {useEffect, useState} from "react";
import type {CSSProperties, HTMLAttributes, PointerEvent} from "react";
import type {ColumnsType} from "antd/es/table";

const columnDragType = "application/x-kl-column";

function HeaderCell({children, ...props}: HTMLAttributes<HTMLTableCellElement>) {
  return <th {...props}>{children}<span className="kl-column-grip" aria-hidden="true"
    onClick={event=>event.stopPropagation()}/></th>;
}

function BodyCell({children, ...props}: HTMLAttributes<HTMLTableCellElement>) {
  return <td {...props}><div className="kl-row-content">{children}</div>
    <span className="kl-row-grip" aria-hidden="true"
      onClick={event=>event.stopPropagation()} onDoubleClick={event=>event.stopPropagation()}/>
  </td>;
}

const components = {header: {cell: HeaderCell}, body: {cell: BodyCell}};

function resize(event: PointerEvent<HTMLTableCellElement>, axis: "x" | "y",
  min: number, max: number, apply: (size: number) => void) {
  if (event.button !== 0 || !(event.target instanceof HTMLElement)) return;
  const grip = event.target;
  if (!grip.classList.contains(axis === "x" ? "kl-column-grip" : "kl-row-grip")) return;
  const cell = event.currentTarget;
  const element = axis === "x" ? cell : cell.parentElement;
  if (!element) return;
  const rect = element.getBoundingClientRect();
  const initial = axis === "x" ? element.offsetWidth : element.offsetHeight;
  const scale = (axis === "x" ? rect.width : rect.height) / initial || 1;
  const coordinate = axis === "x" ? "clientX" : "clientY";
  const start = event[coordinate];
  const pointerId = event.pointerId;
  event.preventDefault();
  event.stopPropagation();
  grip.setPointerCapture(pointerId);
  const preview = (move: globalThis.PointerEvent) => {
    if (move.pointerId !== pointerId) return;
    const next = Math.max(min, Math.min(max, initial + (move[coordinate] - start) / scale));
    grip.style.transform = axis === "x"
      ? `translateX(${next - initial}px)` : `translateY(${next - initial}px)`;
  };
  const finish = (end: globalThis.PointerEvent) => {
    if (end.pointerId !== pointerId) return;
    grip.style.transform = "";
    grip.removeEventListener("pointermove", preview);
    grip.removeEventListener("pointerup", finish);
    grip.removeEventListener("lostpointercapture", finish);
    if (end.type !== "pointerup") return;
    const delta = (end[coordinate] - start) / scale;
    if (Math.abs(delta) >= 1)
      apply(Math.max(min, Math.min(max, Math.round(initial + delta))));
  };
  grip.addEventListener("pointermove", preview);
  grip.addEventListener("pointerup", finish);
  grip.addEventListener("lostpointercapture", finish);
}

export function useTableMouseLayout(listId: string) {
  const storageKey = "kl.rowHeight." + listId;
  const [height, setHeight] = useState<number | undefined>(() => {
    try {
      const saved = Number(localStorage.getItem(storageKey));
      if (saved >= 24 && saved <= 240) return saved;
    } catch { /* Use the table default. */ }
  });
  useEffect(() => {
    try {
      if (height === undefined) localStorage.removeItem(storageKey);
      else localStorage.setItem(storageKey, String(height));
    } catch { /* Keep the active height. */ }
  }, [height, storageKey]);

  function decorate<T extends object>(columns: ColumnsType<T>,
    keyFor: (column: ColumnsType<T>[number]) => string,
    setWidth: (key: string, width: number) => void,
    move: (source: string, target: string) => void,
    minWidth = 60, maxWidth = 1000, pinned: readonly string[] = []) {
    return columns.map<ColumnsType<T>[number]>(column => {
      if ("children" in column) return column;
      const key = keyFor(column);
      const movable = !!key && !pinned.includes(key);
      const dragType = columnDragType + "-" + listId;
      return {
        ...column,
        onHeaderCell: header => {
          const props = column.onHeaderCell?.(header) ?? {};
          return {
            ...props,
            draggable: movable,
            className: [props.className, key && "kl-resizable-column",
              movable && "kl-movable-column"].filter(Boolean).join(" "),
            onDragStart: (event: React.DragEvent<HTMLTableCellElement>) => {
              if (!movable || (event.target instanceof Element &&
                event.target.closest(".kl-column-grip"))) {
                event.preventDefault();
                return;
              }
              event.dataTransfer.effectAllowed = "move";
              event.dataTransfer.setData(dragType, key);
            },
            onDragOver: (event: React.DragEvent<HTMLTableCellElement>) => {
              if (!movable || !event.dataTransfer.types.includes(dragType)) return;
              event.preventDefault();
              event.dataTransfer.dropEffect = "move";
            },
            onDrop: (event: React.DragEvent<HTMLTableCellElement>) => {
              const source = event.dataTransfer.getData(dragType);
              if (!movable || !source || source === key || pinned.includes(source)) return;
              event.preventDefault();
              move(source, key);
            },
            onPointerDown: (event: PointerEvent<HTMLTableCellElement>) => {
              props.onPointerDown?.(event);
              if (key) resize(event, "x", minWidth, maxWidth, width => setWidth(key, width));
            }
          };
        },
        onCell: (record: T, index?: number) => {
          const props = column.onCell?.(record, index) ?? {};
          return {
            ...props,
            className: [props.className, "kl-resizable-row"].filter(Boolean).join(" "),
            onPointerDown: (event: PointerEvent<HTMLTableCellElement>) => {
              props.onPointerDown?.(event);
              resize(event, "y", 24, 240, setHeight);
            }
          };
        }
      };
    });
  }
  return {
    decorate,
    resetHeight: () => setHeight(undefined),
    tableProps: {components, className: "kl-mouse-table" + (height ? " kl-fixed-row-height" : ""),
      style: {"--kl-row-height": height === undefined ? undefined : height + "px"} as CSSProperties}
  };
}

export function moveColumn(order: string[], source: string, target: string) {
  const from = order.indexOf(source), to = order.indexOf(target);
  if (from < 0 || to < 0 || from === to) return order;
  const next = [...order];
  next.splice(from, 1);
  next.splice(to, 0, source);
  return next;
}
