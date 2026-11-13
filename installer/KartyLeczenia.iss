[Setup]
AppId=pl.kartyleczenia.desktop
AppName=Karty leczenia zwierząt
AppVersion={#ReleaseVersion}
DefaultDirName={localappdata}\Programs\KartyLeczenia
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KartyLeczenia.exe
SetupIconFile=..\src\KartyLeczenia\Resources\AppIcon\app-icon.ico
OutputDir=..
OutputBaseFilename=KartyLeczenia-Setup-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Karty leczenia zwierząt"; Filename: "{app}\KartyLeczenia.exe"; WorkingDir: "{app}"
