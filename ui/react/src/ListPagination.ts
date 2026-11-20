import {useState} from "react";
import type {TablePaginationConfig} from "antd";

export function useListPagination(list: string, count: number, defaultSize = 25): TablePaginationConfig {
  const key = "kl.listPageSize." + list;
  const sizes = [...new Set([defaultSize, 25, 50, 100])].sort((left,right)=>left-right);
  const [size,setSize] = useState<number|"all">(()=>{
    try {
      const stored = localStorage.getItem(key);
      if (stored === "all") return stored;
      if (sizes.includes(Number(stored))) return Number(stored);
    } catch { /* Use the default page size. */ }
    return defaultSize;
  });
  const allSize = Math.max(501,count);
  return {
    pageSize: size === "all" ? allSize : size,
    pageSizeOptions: [...sizes,allSize],
    showSizeChanger: {
      "aria-label":"Liczba wyświetlanych pozycji",
      options: [...sizes.map(value=>({label:value+" pozycji",value})),
        {label:"Wszystkie pobrane",value:allSize}]
    },
    onShowSizeChange: (_page,value)=>{
      const next = value === allSize ? "all" : value;
      setSize(next);
      try { localStorage.setItem(key,String(next)); } catch { /* Keep the active choice. */ }
    }
  };
}
