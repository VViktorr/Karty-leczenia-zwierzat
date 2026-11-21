import {useRef, useState, type ReactNode} from "react";
import {Rnd} from "react-rnd";

import {restoreGeometry, type Geometry} from "./dialogGeometry";

export default function ResizableDialogFrame({storageKey, children}:{
  storageKey:string; children:ReactNode;
}) {
  const key = "kl.rndDialog." + storageKey;
  const zoom = Number(getComputedStyle(document.documentElement).getPropertyValue("zoom")) || 1;
  const [initial] = useState<Geometry>(() => {
    let stored:string|null = null;
    try { stored = localStorage.getItem(key); }
    catch { /* Window placement is optional. */ }
    return restoreGeometry(stored, document.documentElement.clientWidth, document.documentElement.clientHeight);
  });
  const geometry = useRef(initial);
  const [maximized, setMaximized] = useState(false);
  const save = (change:Partial<Geometry>) => {
    geometry.current = {...geometry.current, ...change};
    try { localStorage.setItem(key, JSON.stringify(geometry.current)); }
    catch { /* Window placement is optional. */ }
  };

  return <Rnd className={"kl-rnd-dialog-frame"+(maximized?" kl-rnd-dialog-maximized":"")} style={{position:"relative"}}
    disableDragging={maximized} enableResizing={!maximized}
    default={initial} bounds="window" scale={zoom} minWidth={320} minHeight={190}
    dragHandleClassName="ant-modal-header, .ant-modal-confirm-title" cancel="button, a, input, textarea, select"
    onDragStop={(_, {x,y})=>save({x,y})}
    onResizeStop={(_, __, element, delta, position)=>{
      save({...position,
        ...(delta.width !== 0 ? {width:element.offsetWidth} : {}),
        ...(delta.height !== 0 ? {height:element.offsetHeight} : {})
      });
    }}>
    {children}
    <button type="button" className="kl-rnd-dialog-maximize"
      title={maximized?"Przywróć rozmiar":"Maksymalizuj"}
      aria-label={maximized?"Przywróć rozmiar":"Maksymalizuj"}
      onClick={()=>setMaximized(value=>!value)}>
      <span aria-hidden="true">{maximized?"❐":"□"}</span>
    </button>
  </Rnd>;
}
