/**
 * UI-only grouping of confirmed Subiekt deliveries. This never invents stock:
 * only distinct positive movement IDs for the same product, warehouse, exact
 * batch and expiry may be added together. Missing/uncertain deliveries remain
 * separate and show their original quantities.
 */
export interface ExpiryLot {
  name:string;
  batch:string;
  expires:string|null;
  received:string|null;
  remaining:number;
  warehouseName:string;
  warehouseId?:number|null;
  deliveriesConfirmed:boolean;
  productId:number;
  movementId:number;
}

export interface ExpiryViewRow extends ExpiryLot {
  key:string;
  deliveryCount?:number;
  multipleReceivedDates?:boolean;
  children?:ExpiryViewRow[];
}

function groupIdentity(row:ExpiryLot):string|null {
  if (!row.deliveriesConfirmed||row.productId<=0||row.movementId<=0||
      !row.batch.trim()||!row.expires||!Number.isFinite(row.remaining)||
      row.remaining<=0) return null;
  return JSON.stringify([
    row.productId,row.warehouseId??row.warehouseName,
    row.batch.trim(),row.expires
  ]);
}

export function groupExpiryLots(
  filtered:readonly ExpiryLot[],combine:boolean
):ExpiryViewRow[] {
  const entries:ExpiryViewRow[]=filtered.map((row,index)=>({
    ...row,key:"movement-"+row.productId+"-"+row.movementId+"-"+index
  }));
  if(!combine)return entries;

  // Different movement IDs are required so repeated source rows cannot
  // artificially inflate a reported stock balance.
  const candidates=new Map<string,ExpiryViewRow[]>();
  const seen=new Map<string,Set<number>>();
  for(const entry of entries){
    const key=groupIdentity(entry);
    if(key===null)continue;
    if(!seen.has(key))seen.set(key,new Set());
    if(seen.get(key)!.has(entry.movementId))continue;
    seen.get(key)!.add(entry.movementId);
    const matches=candidates.get(key)??[];
    matches.push(entry);
    candidates.set(key,matches);
  }

  const consumed=new Set<string>();
  return entries.flatMap(entry=>{
    const id=groupIdentity(entry);
    if(!id)return [entry];
    const members=candidates.get(id);
    if(!members||members.length<2||consumed.has(id))return consumed.has(id)?[]:[entry];
    consumed.add(id);
    const dates=new Set(members.map(member=>member.received));
    return [{
      ...entry,
      key:"group-"+entry.productId+"-"+entry.movementId,
      remaining:members.reduce((total,member)=>total+member.remaining,0),
      received:dates.size===1?entry.received:null,
      multipleReceivedDates:dates.size>1,
      deliveryCount:members.length,
      children:members
    }];
  });
}
