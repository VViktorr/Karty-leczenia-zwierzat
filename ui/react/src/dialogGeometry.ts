export type Geometry = {x:number; y:number; width:number|string; height:number|string};

export function restoreGeometry(raw:string|null, viewportWidth:number, viewportHeight:number):Geometry {
  const defaults = {x:0, y:0, width:"100%", height:"auto"};
  try {
    const saved = JSON.parse(raw ?? "null") as Partial<Geometry> | null;
    if (!saved) return defaults;
    const limit = Math.max(1, viewportWidth - 24);
    const heightLimit = Math.max(1, viewportHeight - 124);
    return {
      x: Number.isFinite(saved.x) && Math.abs(saved.x!) < limit / 2 ? saved.x! : 0,
      y: Number.isFinite(saved.y) && saved.y! >= -88 && saved.y! < heightLimit / 2 ? saved.y! : 0,
      width: typeof saved.width === "number" && saved.width >= 320 ? Math.min(saved.width, limit) : "100%",
      height: typeof saved.height === "number" && saved.height >= 190 ? Math.min(saved.height, heightLimit) : "auto"
    };
  } catch { return defaults; }
}
