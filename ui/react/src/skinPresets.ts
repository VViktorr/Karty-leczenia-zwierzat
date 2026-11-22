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
  navigation: "tabs" | "side";
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
    label: "Orchard — inspiracja macOS", navigation: "side", dense: false, radius: 11,
    buttonRadius: 7, controlHeight: 30, fontFamily: 'system-ui,"Segoe UI",sans-serif',
    light: {background:"#f7f7f9",panel:"#fff",toolbar:"#eeeff4",line:"#d6d8e0",text:"#20222b",muted:"#565b69",accent:"#005dc5",selection:"#dceaff"},
    dark: {background:"#202128",panel:"#282a32",toolbar:"#30323b",line:"#494c59",text:"#f4f5fa",muted:"#bac1d0",accent:"#0966c4",selection:"#253d5d"},
  },
  material: {
    label: "Material — inspiracja Google", navigation: "tabs", dense: false, radius: 16,
    buttonRadius: 20, controlHeight: 36, fontFamily: 'Roboto,"Segoe UI",sans-serif',
    light: {background:"#f5f2fb",panel:"#fff",toolbar:"#eae2f7",line:"#d7cbdf",text:"#292335",muted:"#5c5269",accent:"#67439c",selection:"#eaddff"},
    dark: {background:"#201b27",panel:"#2a2433",toolbar:"#362d44",line:"#574b63",text:"#f5eafa",muted:"#ccc0d5",accent:"#7549b0",selection:"#493562"},
  },
  aero: {
    label: "Aero Retro — inspiracja Windows 7", navigation: "tabs", dense: true, radius: 4,
    buttonRadius: 3, controlHeight: 28, fontFamily: '"Segoe UI",system-ui,sans-serif',
    light: {background:"#dbe9f6",panel:"#fbfdff",toolbar:"#d5e9f8",line:"#9ebbd4",text:"#20344c",muted:"#3e576f",accent:"#005c92",selection:"#cee9fd"},
    dark: {background:"#172937",panel:"#213744",toolbar:"#294656",line:"#537188",text:"#edf7ff",muted:"#baceda",accent:"#006497",selection:"#2b526a"},
  },
  spectrum: {
    label: "Spectrum — inspiracja Adobe", navigation: "side", dense: true, radius: 5,
    buttonRadius: 12, controlHeight: 28, fontFamily: '"Segoe UI",system-ui,sans-serif',
    light: {background:"#e9e9eb",panel:"#f8f8f9",toolbar:"#e2e2e5",line:"#bdbdc4",text:"#242429",muted:"#53535d",accent:"#1557be",selection:"#dae7ff"},
    dark: {background:"#18181a",panel:"#242426",toolbar:"#302f33",line:"#4d4c51",text:"#f5f5f6",muted:"#c2c2c7",accent:"#1e61c8",selection:"#263e62"},
  },
  carbon: {
    label: "Carbon — inspiracja IBM", navigation: "side", dense: true, radius: 0,
    buttonRadius: 0, controlHeight: 30, fontFamily: '"IBM Plex Sans","Segoe UI",sans-serif',
    light: {background:"#f4f4f4",panel:"#fff",toolbar:"#e0e0e0",line:"#aaa",text:"#161616",muted:"#525252",accent:"#0043ce",selection:"#d9e6ff"},
    dark: {background:"#161616",panel:"#262626",toolbar:"#353535",line:"#606060",text:"#f4f4f4",muted:"#c6c6c6",accent:"#0755d4",selection:"#253d65"},
  },
} satisfies Record<string, SkinPreset>;

export type PresetName = keyof typeof skinPresets;
export const presetNames = Object.keys(skinPresets) as PresetName[];
