#ifndef ReleaseVersion
  #error ReleaseVersion must be supplied manually
#endif
#ifndef ReleaseFileVersion
  #error ReleaseFileVersion must be supplied manually
#endif
#ifndef PayloadDir
  #error PayloadDir must point to the published ReactKL application
#endif

[Setup]
; SAME AppId as the existing SQL KartyLeczenia installer: an in-place upgrade,
; not a second installation. SQL/clinic settings remain in their original stores.
AppId=pl.kartyleczenia.winforms.sql
AppName=SQL KartyLeczenia
AppVersion={#ReleaseVersion}
VersionInfoVersion={#ReleaseFileVersion}
VersionInfoProductVersion={#ReleaseFileVersion}
VersionInfoTextVersion={#ReleaseVersion}
VersionInfoProductTextVersion={#ReleaseVersion}
DefaultDirName={autopf}\KartyLeczenia
DefaultGroupName=KartyLeczenia
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KartyLeczenia.ReactKL.exe
SetupIconFile=..\src\KartyLeczenia.ReactKL\Assets\app-icon.ico
OutputBaseFilename=KartyLeczenia-Setup-win-x64-v{#ReleaseVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
WizardStyle=modern

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; Flags: unchecked

[Files]
; The exact installer used on a server is copied locally for first-start SQL
; publication. Never publish while files are still being installed.
Source: "{srcexe}"; DestDir: "{app}"; DestName: "KartyLeczenia-Update-Pending-v{#ReleaseVersion}.exe"; Flags: external ignoreversion; Check: IsServerInstall
Source: "..\scripts\Publish-ReactKLUpdate.ps1"; DestDir: "{app}"; Flags: ignoreversion; Check: IsServerInstall
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; Preserve existing server role and address without prompting again on upgrade.
Filename: "{app}\archive-installation.ini"; Section: "Archive"; Key: "Server"; String: "{code:InstallerArchiveServer}"; Check: IsNewInstallation
Filename: "{app}\archive-installation.ini"; Section: "Archive"; Key: "Role"; String: "{code:InstallerRole}"; Check: IsNewInstallation
Filename: "{app}\archive-installation.ini"; Section: "Archive"; Key: "AppVersion"; String: "{#ReleaseVersion}"

[InstallDelete]
; Remove ONLY obsolete WinForms/AntdUI executable components and old shortcuts,
; never *.sqlite, SQL configuration, PDF catalogues, profile settings or .bak.
Type: files; Name: "{app}\KartyLeczenia.WinForms.exe"
Type: files; Name: "{app}\KartyLeczenia.WinForms.dll"
Type: files; Name: "{app}\KartyLeczenia.WinForms.deps.json"
Type: files; Name: "{app}\KartyLeczenia.WinForms.runtimeconfig.json"
Type: files; Name: "{app}\AntdUI.dll"
Type: files; Name: "{commonprograms}\KartyLeczenia\SQL KartyLeczenia.lnk"
Type: files; Name: "{commonprograms}\KartyLeczenia\Ustawienia SQL KartyLeczenia.lnk"
Type: files; Name: "{commondesktop}\SQL KartyLeczenia.lnk"
Type: files; Name: "{commonprograms}\Karty leczenia zwierząt (SQL).lnk"
Type: files; Name: "{commondesktop}\Karty leczenia zwierząt (SQL).lnk"

[Dirs]
Name: "{commonprograms}\KartyLeczenia"

[Icons]
Name: "{commonprograms}\KartyLeczenia\SQL KartyLeczenia"; Filename: "{app}\KartyLeczenia.ReactKL.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\SQL KartyLeczenia"; Filename: "{app}\KartyLeczenia.ReactKL.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KartyLeczenia.ReactKL.exe"; Description: "Uruchom SQL KartyLeczenia (React)"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  RolePage: TInputOptionWizardPage;
  ServerPage: TInputQueryWizardPage;

function ExistingArchiveConfiguration: Boolean;
begin
  Result := FileExists(AddBackslash(WizardDirValue) + 'archive-installation.ini');
end;

function IsNewInstallation: Boolean;
begin
  Result := not ExistingArchiveConfiguration;
end;

function InstallerRole(Param: String): String;
begin
  if RolePage.SelectedValueIndex = 0 then Result := 'server'
  else Result := 'client';
end;

function InstallerArchiveServer(Param: String): String;
begin
  Result := Trim(ServerPage.Values[0]);
end;

function IsServerInstall: Boolean;
var
  Config: String;
begin
  Config := AddBackslash(WizardDirValue) + 'archive-installation.ini';
  if FileExists(Config) then
    Result := CompareText(GetIniString('Archive','Role','',Config),'server') = 0
  else
    Result := RolePage.SelectedValueIndex = 0;
end;

procedure InitializeWizard;
begin
  RolePage := CreateInputOptionPage(wpWelcome,
    'Rodzaj stanowiska', 'Wybierz rolę komputera',
    'Podczas aktualizacji zachowamy dotychczasową rolę i połączenie SQL.',
    True, False);
  RolePage.Add('Serwer SQL — publikuj instalator w bazie KartyLeczenia');
  RolePage.Add('Stanowisko zdalne — pobieraj aktualizacje z serwera');
  RolePage.SelectedValueIndex := 1;
  ServerPage := CreateInputQueryPage(RolePage.ID,
    'Adres bazy KartyLeczenia', 'Podaj serwer SQL',
    'Przykład: SERWER,51433 lub SERWER\INSTANCJA');
  ServerPage.Add('Serwer:',False);
  ServerPage.Values[0] := '';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ExistingArchiveConfiguration and
    ((PageID = RolePage.ID) or (PageID = ServerPage.ID));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = ServerPage.ID) and IsNewInstallation and
     (Trim(ServerPage.Values[0]) = '') then
  begin
    MsgBox('Wpisz adres serwera bazy KartyLeczenia.', mbError, MB_OK);
    Result := False;
  end;
end;
