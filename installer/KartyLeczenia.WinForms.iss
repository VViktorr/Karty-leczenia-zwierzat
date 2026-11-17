#ifndef ReleaseVersion
  #define ReleaseVersion "1.0.0"
#endif

[Setup]
AppId=pl.kartyleczenia.winforms.sql
AppName=KartyLeczenia
AppVersion={#ReleaseVersion}
DefaultDirName={autopf}\KartyLeczenia.WinForms
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KartyLeczenia.WinForms.exe
SetupIconFile=..\src\KartyLeczenia\Resources\AppIcon\app-icon.ico
OutputDir=..
OutputBaseFilename=KartyLeczenia-Setup-win-x64-v{#ReleaseVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Files]
Source: "Configure-SharedArchive.ps1"; Flags: dontcopy
Source: "..\scripts\Setup-SharedArchive.sql"; Flags: dontcopy
Source: "..\publish-winforms\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
Filename: "{app}\archive-installation.ini"; Section: "Archive"; Key: "Server"; String: "{code:ArchiveServer}"

[UninstallDelete]
Type: files; Name: "{app}\archive-installation.ini"

[InstallDelete]
Type: files; Name: "{commonprograms}\Karty leczenia zwierząt (SQL).lnk"
Type: files; Name: "{commondesktop}\Karty leczenia zwierząt (SQL).lnk"

[Icons]
Name: "{commonprograms}\KartyLeczenia"; Filename: "{app}\KartyLeczenia.WinForms.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\KartyLeczenia"; Filename: "{app}\KartyLeczenia.WinForms.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\KartyLeczenia.WinForms.exe"; Description: "Uruchom KartyLeczenia"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  ArchivePort = '51433';
  WebView2Key = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Bootstrapper = 'MicrosoftEdgeWebview2Setup.exe';
  WebView2DownloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  SqlLicenseUrl = 'https://www.microsoft.com/useterms';
  SqlPackage = 'SQLEXPR_x64_ENU.exe';
  SqlDownloadUrl = 'https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SQLEXPR_x64_ENU.exe';

var
  RolePage: TInputOptionWizardPage;
  ServerPage: TInputOptionWizardPage;
  AddressPage: TInputQueryWizardPage;
  ExistingServerAddress: String;
  DownloadPage: TDownloadWizardPage;
  ComponentProgress: TOutputMarqueeProgressWizardPage;

function NeedsWebView2: Boolean;
var
  Version: String;
begin
  Result := not (RegQueryStringValue(HKLM32, WebView2Key, 'pv', Version)
    and (Trim(Version) <> '') and (Trim(Version) <> '0.0.0.0'));
end;

function ArchiveServer(Param: String): String;
begin
  if RolePage.SelectedValueIndex = 0 then
  begin
    if ServerPage.SelectedValueIndex = 0 then
      Result := ExistingServerAddress
    else
      Result := GetComputerNameString + ',' + ArchivePort;
  end
  else
  begin
    Result := Trim(AddressPage.Values[0]);
    if (Pos(',', Result) = 0) and (Pos('\', Result) = 0) then
      Result := Result + ',' + ArchivePort;
  end;
end;

function HasRunningSqlInstance: Boolean;
var
  Locator, Wmi, Services: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\cimv2');
    Services := Wmi.ExecQuery('SELECT Name FROM Win32_Service WHERE State = ''Running'' AND ' +
      '(Name = ''MSSQLSERVER'' OR Name LIKE ''MSSQL$%'')');
    Result := Services.Count > 0;
  except
    Log('Nie udało się odczytać stanu lokalnych usług SQL Server.');
  end;
end;

procedure InitializeWizard;
var
  PreviousEngine: String;
begin
  RolePage := CreateInputOptionPage(wpWelcome, 'Rodzaj instalacji', 'Wybierz rolę tego komputera',
    'Program WinForms zostanie zainstalowany w obu trybach.', True, False);
  RolePage.Add('Serwer — przygotuj wspólną bazę archiwum i zainstaluj program');
  RolePage.Add('Stanowisko zdalne — zainstaluj program i wskaż serwer archiwum');
  if GetPreviousData('ArchiveRole', '') = 'server' then
    RolePage.SelectedValueIndex := 0
  else
    RolePage.SelectedValueIndex := 1;

  ServerPage := CreateInputOptionPage(RolePage.ID, 'Instancja SQL Server', 'Wybierz, gdzie zapisać archiwum',
    'W trybie istniejącej instancji jej nazwa, wersja i stan pojawią się przed utworzeniem bazy.', True, False);
  ServerPage.Add('Wybierz istniejącą instancję na tym komputerze');
  ServerPage.Add('Zainstaluj lub użyj osobnej instancji KLARCHIVE');
  PreviousEngine := GetPreviousData('ArchiveEngine', '');
  if (PreviousEngine = 'existing') or ((PreviousEngine = '') and HasRunningSqlInstance) then
    ServerPage.SelectedValueIndex := 0
  else
    ServerPage.SelectedValueIndex := 1;

  AddressPage := CreateInputQueryPage(ServerPage.ID, 'Serwer archiwum',
    'Podaj adres wyświetlony po instalacji serwerowej.',
    'Przykład: SERWER,51433 lub SERWER\INSTANCJA. Sama nazwa lub adres IPv4 użyje portu 51433.');
  AddressPage.Add('Nazwa lub adres serwera:', False);
  AddressPage.Values[0] := ExpandConstant('{param:ARCHIVESERVER|}');
  if AddressPage.Values[0] = '' then
    AddressPage.Values[0] := GetPreviousData('ArchiveHost', '');

  DownloadPage := CreateDownloadPage('Pobieranie składników',
    'Instalator pobiera brakujące składniki Microsoft.', nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
  ComponentProgress := CreateOutputMarqueeProgressPage('Przygotowywanie składników',
    'Zaczekaj, aż instalator zakończy konfigurację.');
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if RolePage.SelectedValueIndex = 0 then
  begin
    SetPreviousData(PreviousDataKey, 'ArchiveRole', 'server');
    if ServerPage.SelectedValueIndex = 0 then
      SetPreviousData(PreviousDataKey, 'ArchiveEngine', 'existing')
    else
      SetPreviousData(PreviousDataKey, 'ArchiveEngine', 'dedicated');
  end
  else
  begin
    SetPreviousData(PreviousDataKey, 'ArchiveRole', 'client');
    SetPreviousData(PreviousDataKey, 'ArchiveHost', Trim(AddressPage.Values[0]));
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = AddressPage.ID) and (RolePage.SelectedValueIndex = 0)) or
    ((PageID = ServerPage.ID) and (RolePage.SelectedValueIndex <> 0));
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
  if (CurPageID = AddressPage.ID) and not ValidAddress(Trim(AddressPage.Values[0])) then
  begin
    MsgBox('Wpisz poprawny adres archiwum, np. SERWER,51433 lub SERWER\INSTANCJA.', mbError, MB_OK);
    Result := False;
  end;
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
    if RolePage.SelectedValueIndex = 0 then
    begin
      if WizardSilent then
      begin
        Result := 'Instalacja serwerowa wymaga kreatora interaktywnego.';
        Exit;
      end;

      Mode := 'Server';
      ReportPath := ExpandConstant('{tmp}\archive-server-result.txt');
      if ServerPage.SelectedValueIndex = 0 then
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
          LicenseChoice := MsgBox('Instalator pobierze i zainstaluje Microsoft SQL Server 2022 Express.' + #13#10 +
            'Tak — akceptuję warunki licencji SQL Server 2022 Express.' + #13#10 +
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
      if Mode = 'Server' then
        Stage := 'Instalowanie SQL Server Express i przygotowywanie archiwum…'
      else
        Stage := 'Sprawdzanie instancji SQL Server i przygotowywanie archiwum…';
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
    else if not ValidAddress(Trim(AddressPage.Values[0])) then
    begin
      Result := 'Podaj adres serwera archiwum. Przy instalacji cichej użyj /ARCHIVESERVER=nazwa-serwera.';
      Exit;
    end;

    if NeedsWebView2 then
    begin
      DownloadComponent(WebView2DownloadUrl, WebView2Bootstrapper);
      if not RunComponent(' -Mode WebView2 -WebView2Package "' +
        ExpandConstant('{tmp}\') + WebView2Bootstrapper + '"', 'Instalowanie WebView2 Runtime…', ExitCode) then
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
  if (CurPageID = wpFinished) and (RolePage.SelectedValueIndex = 0) then
    WizardForm.FinishedLabel.Caption := 'Serwer archiwum jest gotowy. Adres dla stanowisk: ' +
      ArchiveServer('') + #13#10 + 'Użyj trybu logowania wybranego podczas konfiguracji SQL.';
end;
