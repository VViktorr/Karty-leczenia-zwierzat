import {useLayoutEffect, useState, type ReactNode} from "react";
import {Rnd} from "react-rnd";

import {restoreGeometry, type Geometry} from "./dialogGeometry";

function readViewport() {
  const zoom = Number(getComputedStyle(document.documentElement).zoom) || 1;
  return {zoom, width:window.innerWidth / zoom, height:window.innerHeight / zoom};
}

export default function ResizableDialogFrame({storageKey, children}:{
  storageKey:string; children:ReactNode;
}) {
  const key = "kl.rndDialog.v2." + storageKey;
  const [{zoom,width,height},setViewport] = useState(readViewport);
  useLayoutEffect(()=>{
    const refresh = () => setViewport(readViewport());
    const observer = new ResizeObserver(refresh);
    observer.observe(document.documentElement);
    window.addEventListener("resize",refresh);
    return ()=>{
      observer.disconnect();
      window.removeEventListener("resize",refresh);
    };
  },[]);
  const maxWidth = Math.max(1,width-24);
  const maxHeight = Math.max(1,height-24);
  const [geometry,setGeometry] = useState<Geometry>(() => {
    let stored:string|null = null;
    try { stored = localStorage.getItem(key); }
    catch { /* Window placement is optional. */ }
    return restoreGeometry(stored, width, height+100);
  });
  const [maximized, setMaximized] = useState(false);
  const save = (change:Partial<Geometry>) => {
    const updated = {...geometry, ...change};
    setGeometry(updated);
    try { localStorage.setItem(key, JSON.stringify(updated)); }
    catch { /* Window placement is optional. */ }
  };

  return <Rnd className={"kl-rnd-dialog-frame"+(maximized?" kl-rnd-dialog-maximized":"")} style={{position:"relative",maxWidth,maxHeight}}
    disableDragging={maximized} enableResizing={!maximized}
    position={{x:geometry.x,y:geometry.y}} size={{width:geometry.width,height:geometry.height}}
    bounds="window" scale={zoom} minWidth={Math.min(320,maxWidth)} minHeight={Math.min(190,maxHeight)}
    maxWidth={maxWidth} maxHeight={maxHeight}
    resizeHandleWrapperClass="kl-dialog-resize-handles"
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
