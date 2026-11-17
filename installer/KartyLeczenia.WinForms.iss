#ifndef ReleaseVersion
  #define ReleaseVersion "1.0.0"
#endif

[Setup]
AppId=pl.kartyleczenia.winforms
AppName=Karty leczenia zwierząt (WinForms)
AppVersion={#ReleaseVersion}
DefaultDirName={localappdata}\Programs\KartyLeczenia.WinForms
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KartyLeczenia.WinForms.exe
SetupIconFile=..\src\KartyLeczenia\Resources\AppIcon\app-icon.ico
OutputDir=..
OutputBaseFilename=KartyLeczenia-WinForms-Setup-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Files]
Source: "..\publish-winforms\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Karty leczenia zwierząt (WinForms)"; Filename: "{app}\KartyLeczenia.WinForms.exe"; WorkingDir: "{app}"
