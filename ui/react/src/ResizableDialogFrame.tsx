import {useRef, useState, type ReactNode} from "react";
import {Rnd} from "react-rnd";

type Geometry = {x:number; y:number; width:number|string; height:number|string};

export default function ResizableDialogFrame({storageKey, children}:{
  storageKey:string; children:ReactNode;
}) {
  const key = "kl.rndDialog." + storageKey;
  const zoom = Number(getComputedStyle(document.documentElement).getPropertyValue("zoom")) || 1;
  const [initial] = useState<Geometry>(() => {
    const defaults = {x:0, y:0, width:"100%", height:"auto"};
    try {
      const saved = JSON.parse(localStorage.getItem(key) ?? "null") as Partial<Geometry> | null;
      if (!saved) return defaults;
      const limit = Math.max(1, document.documentElement.clientWidth - 24);
      const heightLimit = Math.max(1, document.documentElement.clientHeight - 124);
      return {
        x: Number.isFinite(saved.x) && Math.abs(saved.x!) < limit / 2 ? saved.x! : 0,
        y: Number.isFinite(saved.y) && saved.y! >= -88 && saved.y! < heightLimit / 2 ? saved.y! : 0,
        width: typeof saved.width === "number" && saved.width >= 320 ? Math.min(saved.width, limit) : "100%",
        height: typeof saved.height === "number" && saved.height >= 190 ? Math.min(saved.height, heightLimit) : "auto"
      };
    } catch { return defaults; }
  });
  const geometry = useRef(initial);
  const save = (change:Partial<Geometry>) => {
    geometry.current = {...geometry.current, ...change};
    try { localStorage.setItem(key, JSON.stringify(geometry.current)); }
    catch { /* Window placement is optional. */ }
  };

  return <Rnd className="kl-rnd-dialog-frame" style={{position:"relative"}}
    default={initial} bounds="window" scale={zoom} minWidth={320} minHeight={190}
    dragHandleClassName="ant-modal-header" cancel="button, a, input, textarea, select"
    onDragStop={(_, {x,y})=>save({x,y})}
    onResizeStop={(_, __, element, delta, position)=>{
      save({...position,
        ...(delta.width !== 0 ? {width:element.offsetWidth} : {}),
        ...(delta.height !== 0 ? {height:element.offsetHeight} : {})
      });
    }}>
    {children}
  </Rnd>;
}
