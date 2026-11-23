type SkinColors = {
  background: string;
  panel: string;
  toolbar: string;
  line: string;
  text: string;
  muted: string;
  accent: string;
  selection: string;
};

type SkinPreset = {
  label: string;
  dense: boolean;
  radius: number;
  buttonRadius: number;
  controlHeight: number;
  fontFamily: string;
  light: SkinColors;
  dark: SkinColors;
};

export const skinPresets = {
  orchard: {
    label: "Orchard — inspiracja macOS", dense: false, radius: 11,
    buttonRadius: 7, controlHeight: 30, fontFamily: 'system-ui,"Segoe UI",sans-serif',
    light: {background:"#f7f7f9",panel:"#fff",toolbar:"#eeeff4",line:"#d6d8e0",text:"#20222b",muted:"#565b69",accent:"#005dc5",selection:"#dceaff"},
    dark: {background:"#202128",panel:"#282a32",toolbar:"#30323b",line:"#494c59",text:"#f4f5fa",muted:"#bac1d0",accent:"#0966c4",selection:"#253d5d"},
  },
  material: {
    label: "Material — inspiracja Google", dense: false, radius: 16,
    buttonRadius: 20, controlHeight: 36, fontFamily: 'Roboto,"Segoe UI",sans-serif',
    light: {background:"#f5f2fb",panel:"#fff",toolbar:"#eae2f7",line:"#d7cbdf",text:"#292335",muted:"#5c5269",accent:"#67439c",selection:"#eaddff"},
    dark: {background:"#201b27",panel:"#2a2433",toolbar:"#362d44",line:"#574b63",text:"#f5eafa",muted:"#ccc0d5",accent:"#7549b0",selection:"#493562"},
  },
  aero: {
    label: "Aero Retro — inspiracja Windows 7", dense: true, radius: 4,
    buttonRadius: 3, controlHeight: 28, fontFamily: '"Segoe UI",system-ui,sans-serif',
    light: {background:"#dbe9f6",panel:"#fbfdff",toolbar:"#d5e9f8",line:"#9ebbd4",text:"#20344c",muted:"#3e576f",accent:"#005c92",selection:"#cee9fd"},
    dark: {background:"#172937",panel:"#213744",toolbar:"#294656",line:"#537188",text:"#edf7ff",muted:"#baceda",accent:"#006497",selection:"#2b526a"},
  },
  spectrum: {
    label: "Spectrum — inspiracja Adobe", dense: true, radius: 5,
    buttonRadius: 12, controlHeight: 28, fontFamily: '"Segoe UI",system-ui,sans-serif',
    light: {background:"#e9e9eb",panel:"#f8f8f9",toolbar:"#e2e2e5",line:"#bdbdc4",text:"#242429",muted:"#53535d",accent:"#1557be",selection:"#dae7ff"},
    dark: {background:"#18181a",panel:"#242426",toolbar:"#302f33",line:"#4d4c51",text:"#f5f5f6",muted:"#c2c2c7",accent:"#1e61c8",selection:"#263e62"},
  },
  carbon: {
    label: "Carbon — inspiracja IBM", dense: true, radius: 0,
    buttonRadius: 0, controlHeight: 30, fontFamily: '"IBM Plex Sans","Segoe UI",sans-serif',
    light: {background:"#f4f4f4",panel:"#fff",toolbar:"#e0e0e0",line:"#aaa",text:"#161616",muted:"#525252",accent:"#0043ce",selection:"#d9e6ff"},
    dark: {background:"#161616",panel:"#262626",toolbar:"#353535",line:"#606060",text:"#f4f4f4",muted:"#c6c6c6",accent:"#0755d4",selection:"#253d65"},
  },
  nordic: {
    label:"Nordic — jasny i spokojny",dense:false,radius:12,
    buttonRadius:8,controlHeight:32,fontFamily:'"Segoe UI",system-ui,sans-serif',
    light:{background:"#edf4f7",panel:"#ffffff",toolbar:"#dfeaf0",line:"#bfd0d9",text:"#1a3441",muted:"#5b7381",accent:"#147d97",selection:"#d1edf3"},
    dark:{background:"#12232d",panel:"#203742",toolbar:"#2c4754",line:"#486774",text:"#eef8fa",muted:"#afc6ce",accent:"#26a5c2",selection:"#265562"},
  },
  forest: {
    label:"Forest — naturalny",dense:false,radius:10,
    buttonRadius:7,controlHeight:32,fontFamily:'"Segoe UI",system-ui,sans-serif',
    light:{background:"#f0f4ed",panel:"#fffefb",toolbar:"#e5ede1",line:"#cad8c7",text:"#25382c",muted:"#627266",accent:"#317248",selection:"#d9efdf"},
    dark:{background:"#1a271e",panel:"#25372b",toolbar:"#314837",line:"#506a55",text:"#f0f8ed",muted:"#bed1bd",accent:"#4eaa72",selection:"#315940"},
  },
  sandstone: {
    label:"Sandstone — ciepły",dense:false,radius:8,
    buttonRadius:6,controlHeight:32,fontFamily:'"Segoe UI",system-ui,sans-serif',
    light:{background:"#f8f4ed",panel:"#fffefd",toolbar:"#eee7dc",line:"#dccfbc",text:"#3c3027",muted:"#78695b",accent:"#ad6432",selection:"#f8e6d5"},
    dark:{background:"#28201b",panel:"#362a24",toolbar:"#47362e",line:"#6c5444",text:"#fff6ed",muted:"#dcc8b4",accent:"#d68a50",selection:"#5c402b"},
  },
  midnight: {
    label:"Midnight — kontrastowy",dense:true,radius:6,
    buttonRadius:5,controlHeight:30,fontFamily:'"Segoe UI",system-ui,sans-serif',
    light:{background:"#e8edf5",panel:"#fff",toolbar:"#e1e8f3",line:"#b9c6da",text:"#172842",muted:"#526681",accent:"#3455b0",selection:"#dce5ff"},
    dark:{background:"#0c1426",panel:"#16213b",toolbar:"#213251",line:"#3a5076",text:"#f0f4ff",muted:"#b6c6e4",accent:"#83a7fa",selection:"#283c6c"},
  },
} satisfies Record<string, SkinPreset>;

export type PresetName = keyof typeof skinPresets;
export const presetNames = Object.keys(skinPresets) as PresetName[];
