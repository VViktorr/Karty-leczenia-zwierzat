import { useEffect } from "react";

type Bounds = {left:number;top:number;width:number;height:number};
const storagePrefix="kl.dialogPlacement.v1.";

function dialogKey(modal:HTMLElement):string {
  // One set of window preferences for each dialog type, not each patient/product.
  // Stable IDs keep positions separate even when a patient, medicine or PDF
  // filename changes in the dialog title between openings.
  const explicit=Array.from(modal.classList).find(name=>name.startsWith("kl-dialog-"));
  if(explicit)return storagePrefix+explicit;
  const title=modal.querySelector<HTMLElement>(
    ".ant-modal-title, .ant-modal-confirm-title")?.textContent?.trim()??"Okno";
  const kind=title.split(/\s+[—–]\s+/)[0].replace(/\s+/g," ").slice(0,100);
  let hash=2166136261;
  for(const character of kind){
    hash^=character.charCodeAt(0);
    hash=Math.imul(hash,16777619);
  }
  return storagePrefix+(hash>>>0).toString(16);
}
function read(key:string):Bounds|null {
  try {
    const parsed:unknown=JSON.parse(localStorage.getItem(key)??"null");
    if(!parsed||typeof parsed!=="object")return null;
    const obj=parsed as Partial<Bounds>;
    if(![obj.left,obj.top,obj.width,obj.height].every(x=>typeof x==="number"&&Number.isFinite(x)))return null;
    if((obj.width??0)<320||(obj.height??0)<190)return null;
    return obj as Bounds;
  }catch{return null;}
}
function fit(bounds:Bounds):Bounds {
  const width=Math.max(320,Math.min(bounds.width,Math.max(320,window.innerWidth-16)));
  const height=Math.max(190,Math.min(bounds.height,Math.max(190,window.innerHeight-16)));
  return {width,height,
    left:Math.max(0,Math.min(bounds.left,window.innerWidth-width)),
    top:Math.max(0,Math.min(bounds.top,window.innerHeight-height))};
}
function place(modal:HTMLElement,b:Bounds){
  const value=fit(b);
  modal.style.left=value.left+"px";
  modal.style.top=value.top+"px";
  modal.style.width=value.width+"px";
  modal.style.height=value.height+"px";
}
function current(modal:HTMLElement):Bounds {
  const rect=modal.getBoundingClientRect();
  return {left:rect.left,top:rect.top,width:rect.width,height:rect.height};
}

/**
 * Applies to Ant Design Modal and Modal.confirm, including catalogue, ChPL,
 * card previews, backups and settings. No SQL calls; workstation-local.
 */
export default function DialogPlacement(){
  useEffect(()=>{
    const initialized=new WeakSet<HTMLElement>();
    const cleanups:Array<()=>void>=[];
    const scan=()=>{
      document.querySelectorAll<HTMLElement>(".ant-modal-wrap .ant-modal").forEach(modal=>{
        if(initialized.has(modal))return;
        const wrapper=modal.closest<HTMLElement>(".ant-modal-wrap");
        if(!wrapper||wrapper.getClientRects().length===0)return;
        // Ant Design's programmatic Modal.confirm has no .ant-modal-header.
        // Use its title as the drag handle too (never the confirmation buttons).
        const header=modal.querySelector<HTMLElement>(
          ".ant-modal-header, .ant-modal-confirm-title");
        if(!header)return;
        initialized.add(modal);
        const key=dialogKey(modal);
        const rect=current(modal);
        // Preserve declared modal width on first open. The header is draggable
        // and native bottom-right CSS resize controls its dimensions.
        modal.classList.add("kl-movable-modal");
        place(modal,read(key)??{
          left:Math.max(0,(window.innerWidth-rect.width)/2),
          top:Math.max(0,rect.top),
          width:Math.max(320,rect.width),height:Math.max(190,rect.height)
        });
        let active=false;
        let moving=false;
        let startX=0,startY=0,originX=0,originY=0;
        const persist=()=>{
          if(active||moving||!modal.isConnected)return;
          try{localStorage.setItem(key,JSON.stringify(fit(current(modal))));}
          catch{/* Geometry is still usable until this window closes. */}
        };
        const onDown=(e:PointerEvent)=>{
          if(e.button!==0||(e.target instanceof Element&&
             e.target.closest("button,input,select,textarea,a,[role=button]")))return;
          moving=true;startX=e.clientX;startY=e.clientY;
          const b=current(modal);originX=b.left;originY=b.top;
          header.setPointerCapture(e.pointerId);
          e.preventDefault();
        };
        const onMove=(e:PointerEvent)=>{
          if(!moving)return;
          const b=current(modal);
          place(modal,{left:originX+e.clientX-startX,top:originY+e.clientY-startY,
            width:b.width,height:b.height});
        };
        const onUp=()=>{
          if(!moving)return;
          moving=false;persist();
        };
        header.addEventListener("pointerdown",onDown);
        header.addEventListener("pointermove",onMove);
        header.addEventListener("pointerup",onUp);
        header.addEventListener("pointercancel",onUp);
        // Native CSS resize may finish outside the element. Persist on the
        // next pointer release even if ResizeObserver was not called yet.
        const onRelease=()=>{if(!moving)persist();};
        window.addEventListener("pointerup",onRelease);
        const resize=new ResizeObserver(()=>{if(!active)persist();});
        resize.observe(modal);
        active=true;
        const ready=requestAnimationFrame(()=>{active=false;});
        cleanups.push(()=>{
          cancelAnimationFrame(ready);
          resize.disconnect();
          header.removeEventListener("pointerdown",onDown);
          header.removeEventListener("pointermove",onMove);
          header.removeEventListener("pointerup",onUp);
          header.removeEventListener("pointercancel",onUp);
          window.removeEventListener("pointerup",onRelease);
        });
      });
    };
    const observer=new MutationObserver(scan);
    observer.observe(document.body,{childList:true,subtree:true,
      attributes:true,attributeFilter:["class"]});
    scan();
    const onResize=()=>{
      document.querySelectorAll<HTMLElement>(".kl-movable-modal").forEach(modal=>{
        if(modal.getClientRects().length)place(modal,current(modal));
      });
    };
    window.addEventListener("resize",onResize);
    return ()=>{
      observer.disconnect();window.removeEventListener("resize",onResize);
      cleanups.forEach(clean=>clean());
    };
  },[]);
  return null;
}
