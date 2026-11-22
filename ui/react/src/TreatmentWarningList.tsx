import {useRef, type ReactNode} from "react";
import {useVirtualizer} from "@tanstack/react-virtual";

type Item = {row: {index: number}};
type WarningListProps<T extends Item> = {
  items: readonly T[];
  render: (item: T) => ReactNode;
  virtual: boolean;
};

export default function TreatmentWarningList<T extends Item>({items, render, virtual}: WarningListProps<T>) {
  const viewport = useRef<HTMLDivElement>(null);
  const enabled = virtual && items.length > 25;
  const list = useVirtualizer({
    count: items.length,
    getScrollElement: () => viewport.current,
    getItemKey: index => items[index].row.index,
    estimateSize: () => 58,
    overscan: 5,
    enabled,
  });
  if (!enabled) return <>{items.map(render)}</>;
  return <div className="treatment-warning-scroll" ref={viewport} role="region"
    tabIndex={0} aria-label="Pozycje karty do sprawdzenia">
    <div style={{height:list.getTotalSize(), position:"relative"}}>
      {list.getVirtualItems().map(item => <div key={item.key} data-index={item.index}
        ref={list.measureElement}
        style={{position:"absolute", insetInline:0, top:0, transform:`translateY(${item.start}px)`}}>
        {render(items[item.index])}
      </div>)}
    </div>
  </div>;
}
