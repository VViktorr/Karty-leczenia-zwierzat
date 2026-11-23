import {Alert, Button, Card, Checkbox, Select, Slider, Space, Switch, Typography} from "antd";
import {defaultComponents, resetComponents, updateComponents, useComponents,
  type ShortcutAction} from "./componentSettings";

const zoomOptions = [50,75,100,125,150,175,200].map(value=>({value,label:value+"%"}));
const shortcutOptions = [
  "alt+left","alt+right","alt+enter","alt+s","alt+shift+left",
  "alt+shift+right","mod+enter","mod+s","mod+shift+s","ctrl+enter",
].map(value=>({value,label:value.replaceAll("mod","Ctrl")}));
const shortcutLabels: Record<ShortcutAction,string> = {
  back:"Poprzedni etap kreatora",next:"Następny etap kreatora",
  summary:"Podsumowanie karty",save:"Zapisz kartę",
};
const shortcutActions: ShortcutAction[] = ["back","next","summary","save"];

export default function ComponentsSettings() {
  const settings = useComponents();
  const duplicateShortcuts = shortcutActions.filter((action,index) =>
    shortcutActions.some((other,otherIndex) =>
      otherIndex<index && settings.shortcuts[other]===settings.shortcuts[action]));
  function changeShortcut(action:ShortcutAction, value:string) {
    // Never configure the same shortcut for two simultaneous actions.
    if (shortcutActions.some(other=>other!==action && settings.shortcuts[other]===value)) return;
    updateComponents("shortcuts",{[action]:value});
  }
  return <Space direction="vertical" size={16} style={{width:"100%"}}>
    <Typography.Paragraph type="secondary">
      Ustawienia stanowiska dotyczą wszystkich okien korzystających z tych komponentów.
      Nie zmieniają połączeń SQL ani danych archiwum.
    </Typography.Paragraph>
    <Card size="small" title="PDF — karty leczenia i dokumenty ChPL">
      <Space direction="vertical" size={12} style={{width:"100%"}}>
        <label>Tryb przeglądania PDF
          <Select style={{display:"block",maxWidth:420}} aria-label="Tryb przeglądania PDF"
            value={settings.pdf.mode} onChange={mode=>updateComponents("pdf",{mode})}
            options={[{value:"scroll",label:"Przewijanie ciągłe wszystkich stron"},
              {value:"pages",label:"Jedna strona — poprzednia / następna"}]}/>
        </label>
        <label>Silnik podglądu
          <Select style={{display:"block",maxWidth:420}} aria-label="Silnik podglądu PDF"
            value={settings.pdf.viewer} onChange={viewer=>updateComponents("pdf",{viewer})}
            options={[{value:"react",label:"React-PDF / PDF.js"},
              {value:"native",label:"Natywny podgląd WebView2"}]}/>
        </label>
        <label>Domyślne powiększenie
          <Select aria-label="Domyślne powiększenie PDF" style={{display:"block",width:180}}
            value={settings.pdf.zoom} options={zoomOptions}
            onChange={zoom=>updateComponents("pdf",{zoom})}/>
        </label>
        <Checkbox checked={settings.pdf.rememberZoom}
          onChange={event=>updateComponents("pdf",{rememberZoom:event.target.checked})}>
          Zapamiętuj ostatnie powiększenie
        </Checkbox>
        <Alert type="info" showIcon title="PDF generuje WebView2; PDF.js odpowiada za jego wyświetlanie."
          description="Podgląd HTML pozostaje dostępny awaryjnie, gdy wygenerowanie PDF się nie powiedzie."/>
      </Space>
    </Card>
    <Card size="small" title="Tabele i układ kolumn">
      <Space direction="vertical" size={12}>
        <label>Domyślny widok tabel
          <Select aria-label="Domyślny silnik tabel" style={{display:"block",width:280}}
            value={settings.table.mode} onChange={mode=>updateComponents("table",{mode})}
            options={[{value:"antd",label:"Ant Design — standardowy"},
              {value:"ag",label:"AG Grid — zaawansowany"}]}/>
        </label>
        <Checkbox checked={settings.table.rememberColumns}
          onChange={event=>updateComponents("table",{rememberColumns:event.target.checked})}>
          Zapamiętuj widok i kolejność kolumn na tym stanowisku
        </Checkbox>
      </Space>
    </Card>
    <Card size="small" title="Skróty klawiaturowe — React Hotkeys Hook">
      <Space direction="vertical" size={12}>
        <Switch checked={settings.shortcuts.enabled} aria-label="Włącz skróty klawiaturowe"
          onChange={enabled=>updateComponents("shortcuts",{enabled})}/>
        {shortcutActions.map(action=><label key={action} style={{display:"block"}}>
          {shortcutLabels[action]}
          <Select aria-label={shortcutLabels[action]} style={{display:"block",width:260}}
            disabled={!settings.shortcuts.enabled}
            value={settings.shortcuts[action]} options={shortcutOptions}
            onChange={value=>changeShortcut(action,value)}/>
        </label>)}
        {duplicateShortcuts.length>0&&<Alert type="warning"
          title="Dwa działania nie mogą mieć tego samego skrótu."/>}
        <Typography.Text type="secondary">
          Skróty działają podczas pracy w kreatorze karty. F5 i Ctrl+Shift+L zachowują
          swoje dotychczasowe funkcje w listach.
        </Typography.Text>
      </Space>
    </Card>
    <Card size="small" title="Buforowanie danych React (TanStack Query)">
      <Space direction="vertical" size={12}>
        <label>Czas świeżości danych klienta
          <Select aria-label="Czas świeżości danych" style={{display:"block",width:200}}
            value={settings.cache.staleSeconds}
            options={[5,15,30,60].map(value=>({value,label:value+" sekund"}))}
            onChange={staleSeconds=>updateComponents("cache",{staleSeconds})}/>
        </label>
        <Checkbox checked={settings.cache.retry}
          onChange={event=>updateComponents("cache",{retry:event.target.checked})}>
          Ponów raz nieudane pobieranie danych
        </Checkbox>
        <Typography.Text type="secondary">
          Ustawienia dotyczą bufora UI; limitów zapytań i bezpieczeństwa SQL nie zmieniamy.
        </Typography.Text>
      </Space>
    </Card>
    <Button onClick={resetComponents}>Przywróć domyślne ustawienia komponentów</Button>
  </Space>;
}
