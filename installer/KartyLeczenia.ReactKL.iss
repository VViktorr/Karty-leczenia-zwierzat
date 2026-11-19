#ifndef ReleaseVersion
  #error Wersje ReleaseVersion trzeba podac recznie
#endif
#ifndef ReleaseFileVersion
  #error Brak ReleaseFileVersion
#endif
#ifndef PayloadDir
  #error Brak katalogu opublikowanych plikow ReactKL
#endif

[Setup]
AppId=pl.kartyleczenia.reactkl
AppName=Karty leczenia zwierząt — ReactKL
AppVersion={#ReleaseVersion}
VersionInfoVersion={#ReleaseFileVersion}
DefaultDirName={localappdata}\Programs\KartyLeczenia.ReactKL
DefaultGroupName=KartyLeczenia ReactKL
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KartyLeczenia.ReactKL.exe
SetupIconFile=..\src\KartyLeczenia.ReactKL\Assets\app-icon.ico
OutputBaseFilename=ReactKL-Setup-win-x64-v{#ReleaseVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
WizardStyle=modern

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Skróty:"

[Icons]
Name: "{autoprograms}\KartyLeczenia ReactKL"; Filename: "{app}\KartyLeczenia.ReactKL.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\KartyLeczenia ReactKL"; Filename: "{app}\KartyLeczenia.ReactKL.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KartyLeczenia.ReactKL.exe"; Description: "Uruchom ReactKL"; Flags: nowait postinstall skipifsilent
