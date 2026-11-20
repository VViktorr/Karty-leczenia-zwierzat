#ifndef ReleaseVersion
  #error ReleaseVersion must be supplied manually
#endif
#ifndef ReleaseFileVersion
  #error ReleaseFileVersion must be supplied manually
#endif
#ifndef PayloadDir
  #error PayloadDir must point to the published ReactKL application
#endif
#ifndef CatalogSeedFile
  #error CatalogSeedFile must point to the complete catalogue seed ZIP
#endif

[Setup]
; SAME AppId as the existing SQL KartyLeczenia installer: an in-place upgrade,
; not a second installation. SQL/clinic settings remain in their original stores.
AppId=pl.kartyleczenia.winforms.sql
AppName=Karty Leczenia
AppVersion={#ReleaseVersion}
VersionInfoVersion={#ReleaseFileVersion}
VersionInfoProductVersion={#ReleaseFileVersion}
VersionInfoTextVersion={#ReleaseVersion}
VersionInfoProductTextVersion={#ReleaseVersion}
DefaultDirName={autopf}\KartyLeczenia
DefaultGroupName=Karty Leczenia
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
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"

[Files]
; Server-only first-time SQL initialization package. A client NEVER extracts
; or executes these files; upgrades keep the existing SQL configuration.
Source: "Configure-SharedArchive.ps1"; Flags: dontcopy
Source: "..\scripts\Setup-SharedArchive.sql"; Flags: dontcopy
Source: "{#CatalogSeedFile}"; Flags: dontcopy
; The exact installer used on a server is copied locally for first-start SQL
; publication. Never publish while files are still being installed.
Source: "{srcexe}"; DestDir: "{app}"; DestName: "KartyLeczenia-Update-Pending-v{#ReleaseVersion}.exe"; Flags: external ignoreversion; Check: IsServerInstall
Source: "..\scripts\Publish-ReactKLUpdate.ps1"; DestDir: "{app}"; Flags: ignoreversion; Check: IsServerInstall
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
; Preserve existing server role and address without prompting again on upgrade.
Filename: "{app}\archive-installation.ini"; Section: "Archive"; Key: "Server"; String: "{code:InstallerArchiveServer}"; Check: ShouldWriteArchiveConfiguration
Filename: "{app}\archive-installation.ini"; Section: "Archive"; Key: "Role"; String: "{code:InstallerRole}"; Check: ShouldWriteArchiveConfiguration
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
Type: files; Name: "{commondesktop}\KartyLeczenia.lnk"
Type: files; Name: "{commonprograms}\KartyLeczenia\KartyLeczenia.lnk"
Type: files; Name: "{commonprograms}\Karty leczenia zwierząt (SQL).lnk"
Type: files; Name: "{commondesktop}\Karty leczenia zwierząt (SQL).lnk"

[Dirs]
Name: "{commonprograms}\Karty Leczenia"

[Icons]
Name: "{commonprograms}\Karty Leczenia\Karty Leczenia"; Filename: "{app}\KartyLeczenia.ReactKL.exe"; WorkingDir: "{app}"
Name: "{commonprograms}\Karty Leczenia\Odinstaluj Karty Leczenia"; Filename: "{uninstallexe}"; WorkingDir: "{app}"
Name: "{commondesktop}\Karty Leczenia"; Filename: "{app}\KartyLeczenia.ReactKL.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KartyLeczenia.ReactKL.exe"; Description: "Uruchom Karty Leczenia"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  ArchivePort = '51433';
  SqlLicenseUrl = 'https://www.microsoft.com/useterms';
  SqlPackage = 'SQLEXPR_x64_ENU.exe';
  SqlDownloadUrl = 'https://download.microsoft.com/download/dea8c210-c44a-4a9d-9d80-0c81578860c5/ENU/SQLEXPR_x64_ENU.exe';
  WebView2Key = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  WebView2Package = 'MicrosoftEdgeWebview2Setup.exe';

var
  RolePage: TInputOptionWizardPage;
  ServerModePage: TInputOptionWizardPage;
  ServerPage: TInputQueryWizardPage;
  ExistingServerAddress: String;
  ComponentProgress: TOutputMarqueeProgressWizardPage;
  DownloadPage: TDownloadWizardPage;
  ExistingInstallAtStart: Boolean;

function ExistingArchiveConfiguration: Boolean;
begin
  Result := FileExists(AddBackslash(WizardDirValue) + 'archive-installation.ini');
end;

function PreviousInstallationRole: String;
var
  Config: String;
begin
  Config := AddBackslash(WizardDirValue) + 'archive-installation.ini';
  if FileExists(Config) then
    Result := Lowercase(Trim(GetIniString('Archive','Role','',Config)))
  else
    Result := '';
end;

function RoleChanged: Boolean;
var
  PreviousRole: String;
begin
  PreviousRole := PreviousInstallationRole;
  Result := ((PreviousRole = 'server') and (RolePage.SelectedValueIndex <> 0)) or
            ((PreviousRole = 'client') and (RolePage.SelectedValueIndex <> 1));
end;

function NeedsServerConfiguration: Boolean;
begin
  Result := (RolePage.SelectedValueIndex = 0) and
     ((not ExistingArchiveConfiguration) or RoleChanged or
      (PreviousInstallationRole = '') or
      (ServerModePage.SelectedValueIndex <> 0));
end;

// Preserve unchanged installations. An explicit instance change, role change
// or remote server address change intentionally updates only archive-installation.ini.
function ShouldWriteArchiveConfiguration: Boolean;
var
  StoredServer: String;
begin
  StoredServer := GetIniString('Archive','Server','',
    AddBackslash(WizardDirValue) + 'archive-installation.ini');
  Result := (not ExistingArchiveConfiguration) or RoleChanged or
            (PreviousInstallationRole = '') or
            NeedsServerConfiguration or
            ((RolePage.SelectedValueIndex = 1) and
             (CompareText(Trim(ServerPage.Values[0]),StoredServer) <> 0));
end;

function InstallerRole(Param: String): String;
begin
  if RolePage.SelectedValueIndex = 0 then Result := 'server'
  else Result := 'client';
end;

function InstallerArchiveServer(Param: String): String;
begin
  if RolePage.SelectedValueIndex = 0 then
  begin
    if ServerModePage.SelectedValueIndex = 2 then
      Result := GetComputerNameString + ',' + ArchivePort
    else if ServerModePage.SelectedValueIndex = 1 then
      Result := ExistingServerAddress
    else
      Result := GetIniString('Archive','Server','',
        AddBackslash(WizardDirValue) + 'archive-installation.ini');
  end
  else
  begin
    Result := Trim(ServerPage.Values[0]);
    if (Pos(',', Result) = 0) and (Pos('\', Result) = 0) then
      Result := Result + ',' + ArchivePort;
  end;
end;

function IsServerInstall: Boolean;
begin
  // Selected role, rather than a stale INI, decides whether this setup
  // publishes its executable to the server update channel.
  Result := RolePage.SelectedValueIndex = 0;
end;

procedure InitializeWizard;
begin
  RolePage := CreateInputOptionPage(wpWelcome,
    'Rodzaj stanowiska', 'Wybierz rolę komputera',
    'Dotychczasowa rola jest zaznaczona. Możesz ją zmienić świadomie; istniejące dane SQL pozostaną zachowane.',
    True, False);
  RolePage.Add('Serwer SQL — publikuj instalator w bazie KartyLeczenia');
  RolePage.Add('Stanowisko zdalne — pobieraj aktualizacje z serwera');
  RolePage.SelectedValueIndex := 1;
  if PreviousInstallationRole = 'server' then
    RolePage.SelectedValueIndex := 0;
  ServerModePage := CreateInputOptionPage(RolePage.ID,
    'Instancja SQL Server', 'Gdzie przygotować bazę KartyLeczenia?',
    'Wybór tylko podczas nowej instalacji serwera. Dane istniejącej bazy będą zachowane.',
    True, False);
  ServerModePage.Add('Zachowaj dotychczasowe polaczenie SQL bez zmian');
  ServerModePage.Add('Wybierz istniejaca instancje SQL Server na tym komputerze');
  ServerModePage.Add('Zainstaluj lub uzyj osobnej instancji KLARCHIVE');
  if (ExistingArchiveConfiguration and (PreviousInstallationRole = 'server')) then
    ServerModePage.SelectedValueIndex := 0
  else
    ServerModePage.SelectedValueIndex := 1;
  ServerPage := CreateInputQueryPage(ServerModePage.ID,
    'Adres bazy KartyLeczenia', 'Podaj serwer SQL',
    'Przykład: SERWER,51433 lub SERWER\INSTANCJA');
  ServerPage.Add('Serwer:',False);
  ServerPage.Values[0] := GetIniString('Archive','Server','',
    AddBackslash(WizardDirValue) + 'archive-installation.ini');
  DownloadPage := CreateDownloadPage('Pobieranie składników Microsoft',
    'Pobieranie brakujących składników SQL Server lub WebView2.', nil);
  ComponentProgress := CreateOutputMarqueeProgressPage(
    'Przygotowywanie serwera', 'Konfiguracja SQL i importu katalogu leków.');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // Always show the role on a repair/re-install. Its default is the current
  // installation's role; keeping it never reinitializes SQL.
  Result := ((PageID = ServerModePage.ID) and
              (RolePage.SelectedValueIndex <> 0))
         or ((PageID = ServerPage.ID) and
              (RolePage.SelectedValueIndex = 0));
end;

function ValidAddress(const Address: String): Boolean;
var
  I, CommaAt, SlashAt, StopAt, Port: Integer;
  NumericPort: String;
begin
  Result := False;
  if (Length(Address) = 0) or (Length(Address) > 270) then Exit;
  CommaAt := Pos(',', Address);
  SlashAt := Pos('\', Address);
  StopAt := Length(Address) + 1;
  if CommaAt > 0 then
  begin
    NumericPort := Copy(Address, CommaAt + 1, Length(Address));
    if (Length(NumericPort) = 0) or (Length(NumericPort) > 5) then Exit;
    for I := 1 to Length(NumericPort) do
      if (NumericPort[I] < '0') or (NumericPort[I] > '9') then Exit;
    Port := StrToInt(NumericPort);
    if (Port < 1) or (Port > 65535) then Exit;
    StopAt := CommaAt;
  end;
  if SlashAt > 0 then
  begin
    if (SlashAt >= StopAt - 1) or (StopAt - SlashAt > 17) then Exit;
    if Pos('\', Copy(Address, SlashAt + 1, Length(Address))) > 0 then Exit;
  end;
  if (StopAt = 1) or (SlashAt = 1) then Exit;
  for I := 1 to StopAt - 1 do
    if not ((Address[I] >= 'a') and (Address[I] <= 'z')) and
       not ((Address[I] >= 'A') and (Address[I] <= 'Z')) and
       not ((Address[I] >= '0') and (Address[I] <= '9')) and
       (Address[I] <> '.') and (Address[I] <> '-') and (Address[I] <> '_') and
       (I <> SlashAt) then Exit;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = RolePage.ID) and RoleChanged then
  begin
    Result := MsgBox('Zmieniasz rodzaj stanowiska zapisany w poprzedniej instalacji.' + #13#10 +
      'Dotychczasowa baza SQL i hasla zostana zachowane.' + #13#10 +
      'Jesli wybierzesz Serwer, kreator przygotuje polaczenie i ewentualny import SQL.' + #13#10 +
      'Czy chcesz kontynuowac?',mbConfirmation,MB_YESNO) = IDYES;
    if not Result then Exit;
  end;
  if (CurPageID = ServerModePage.ID) and
     (ServerModePage.SelectedValueIndex = 0) and
     ((not ExistingArchiveConfiguration) or RoleChanged) then
  begin
    MsgBox('Na tym stanowisku nie ma jeszcze zatwierdzonej konfiguracji serwera.' + #13#10 +
      'Wybierz istniejaca instancje SQL lub KLARCHIVE.',mbError,MB_OK);
    Result := False;
    Exit;
  end;
  if (CurPageID = ServerPage.ID) and
     (RolePage.SelectedValueIndex <> 0) and
     (not ValidAddress(Trim(ServerPage.Values[0]))) then
  begin
    MsgBox('Wpisz poprawny adres serwera SQL, np. SERWER,51433 lub SERWER\INSTANCJA.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    ExistingInstallAtStart := ExistingArchiveConfiguration;
end;

function HasWebView2Version(RootKey: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, WebView2Key, 'pv', Version)
    and (Trim(Version) <> '') and (Trim(Version) <> '0.0.0.0');
end;

function NeedsWebView2: Boolean;
begin
  Result := not (HasWebView2Version(HKLM32) or
    HasWebView2Version(HKLM64) or HasWebView2Version(HKCU));
end;

function PowerShellPath: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function HelperParams: String;
begin
  Result := '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Configure-SharedArchive.ps1') + '"';
end;

procedure DownloadComponent(const Url, FileName: String);
begin
  DownloadPage.Clear;
  DownloadPage.Add(Url, FileName, '');
  if not WizardSilent then DownloadPage.Show;
  try
    DownloadPage.Download;
  finally
    if not WizardSilent then DownloadPage.Hide;
  end;
end;

function RunComponent(const Params, Stage: String; var ExitCode: Integer): Boolean;
begin
  if not WizardSilent then
  begin
    ComponentProgress.SetText(Stage, '');
    ComponentProgress.Show;
  end;
  try
    if not WizardSilent then ComponentProgress.Animate;
    Result := Exec(PowerShellPath, HelperParams + Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  finally
    if not WizardSilent then ComponentProgress.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode, OpenResult: Integer;
  LicenseChoice: Integer;
  Mode, Stage: String;
  Report: AnsiString;
  ReportPath: String;
begin
  Result := '';

  try
    ExtractTemporaryFile('Configure-SharedArchive.ps1');
    if NeedsServerConfiguration then
    begin
      if WizardSilent then
      begin
        Result := 'Instalacja serwerowa wymaga kreatora interaktywnego.';
        Exit;
      end;

      Mode := 'Server';
      ReportPath := ExpandConstant('{tmp}\archive-server-result.txt');
      if ServerModePage.SelectedValueIndex = 1 then
      begin
        Mode := 'Existing';
        ExistingServerAddress := '';
        DeleteFile(ReportPath);
      end;

      if (Mode = 'Server') and not IsX64OS then
      begin
        Result := 'Instancja KLARCHIVE wymaga Windows x64 na procesorze Intel lub AMD.';
        Exit;
      end;

      if (Mode = 'Server') and not RegValueExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL', 'KLARCHIVE') then
      begin
        repeat
          LicenseChoice := MsgBox('Instalator pobierze i zainstaluje Microsoft SQL Server 2025 Express.' + #13#10 +
            'Tak — akceptuję warunki licencji SQL Server 2025 Express.' + #13#10 +
            'Nie — otwórz stronę warunków licencji Microsoft.' + #13#10 +
            'Anuluj — przerwij instalację.', mbConfirmation, MB_YESNOCANCEL);
          if LicenseChoice = IDNO then
            ShellExec('open', SqlLicenseUrl, '', '', SW_SHOWNORMAL, ewNoWait, OpenResult);
        until LicenseChoice <> IDNO;
        if LicenseChoice <> IDYES then
        begin
          Result := 'Instalacja SQL Server wymaga akceptacji warunków Microsoft.';
          Exit;
        end;
        DownloadComponent(SqlDownloadUrl, SqlPackage);
      end;

      ExtractTemporaryFile('Setup-SharedArchive.sql');
      ExtractTemporaryFile('catalog-seed-win-x64.zip');
      if Mode = 'Server' then
        Stage := 'Instalowanie SQL Server i import katalogu leków…'
      else
        Stage := 'Konfigurowanie archiwum SQL i import katalogu leków…';
      if not RunComponent(' -Mode ' + Mode, Stage, ExitCode) then
      begin
        Result := 'Nie udało się uruchomić konfiguracji serwera: ' + SysErrorMessage(ExitCode);
        Exit;
      end;
      if ExitCode = 3010 then
      begin
        NeedsRestart := True;
        Result := 'SQL Server wymaga ponownego uruchomienia komputera. Potem ponownie uruchom instalator.';
        Exit;
      end;
      if ExitCode = 2 then
      begin
        Result := 'Anulowano wybór istniejącej instancji SQL.';
        Exit;
      end;
      if ExitCode <> 0 then
      begin
        Result := 'Konfiguracja serwera archiwum zakończyła się błędem. Szczegóły wyświetlono w oknie konfiguracji.';
        Exit;
      end;
      if Mode = 'Existing' then
      begin
        if LoadStringFromFile(ReportPath, Report) then
          ExistingServerAddress := Trim(String(Report));
        if not ValidAddress(ExistingServerAddress) then
        begin
          Result := 'Nie udało się odczytać adresu skonfigurowanej instancji.';
          Exit;
        end;
      end;
    end
    else if ShouldWriteArchiveConfiguration and
            (RolePage.SelectedValueIndex <> 0) and
            not ValidAddress(Trim(ServerPage.Values[0])) then
    begin
      Result := 'Podaj adres serwera archiwum. Przy instalacji cichej użyj /ARCHIVESERVER=nazwa-serwera.';
      Exit;
    end;

    if NeedsWebView2 then
    begin
      DownloadComponent(WebView2Url, WebView2Package);
      if not RunComponent(' -Mode WebView2 -WebView2Package "' +
        ExpandConstant('{tmp}\') + WebView2Package + '"', 'Instalowanie WebView2 Runtime…', ExitCode) then
      begin
        Result := 'Nie udało się uruchomić instalatora WebView2 Runtime: ' + SysErrorMessage(ExitCode);
        Exit;
      end;
      if ExitCode = 3010 then
      begin
        NeedsRestart := True;
        Result := 'WebView2 wymaga ponownego uruchomienia komputera. Potem ponownie uruchom instalator.';
        Exit;
      end;
      if (ExitCode <> 0) or NeedsWebView2 then
        Result := 'Nie udało się zainstalować WebView2 Runtime. Kod: ' + IntToStr(ExitCode) + '.';
    end;
  except
    Result := 'Nie udało się przygotować instalacji: ' + GetExceptionMessage;
  end;
end;


procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (not ExistingInstallAtStart) and IsServerInstall then
    WizardForm.FinishedLabel.Caption := 'Baza Karty Leczenia została przygotowana.' + #13#10 +
      'Adres dla innych stanowisk: ' + InstallerArchiveServer('') + #13#10 +
      'Przy pierwszym uruchomieniu program opublikuje instalator w SQL.';
end;
