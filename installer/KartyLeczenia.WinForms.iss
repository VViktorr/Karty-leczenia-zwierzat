#ifndef ReleaseVersion
  #define ReleaseVersion "1.0.0"
#endif

[Setup]
AppId=pl.kartyleczenia.winforms.sql
AppName=Karty leczenia zwierząt
AppVersion={#ReleaseVersion}
DefaultDirName={autopf}\KartyLeczenia.WinForms
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KartyLeczenia.WinForms.exe
SetupIconFile=..\src\KartyLeczenia\Resources\AppIcon\app-icon.ico
OutputDir=..
OutputBaseFilename=KartyLeczenia-WinForms-Setup-win-x64-v{#ReleaseVersion}
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

[Icons]
Name: "{commonprograms}\Karty leczenia zwierząt (SQL)"; Filename: "{app}\KartyLeczenia.WinForms.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\Karty leczenia zwierząt (SQL)"; Filename: "{app}\KartyLeczenia.WinForms.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\KartyLeczenia.WinForms.exe"; Description: "Uruchom Karty leczenia zwierząt"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  ArchivePort = '51433';
  WebView2Key = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Bootstrapper = 'MicrosoftEdgeWebview2Setup.exe';
  WebView2DownloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  SqlLicenseUrl = 'https://www.microsoft.com/useterms';

var
  RolePage: TInputOptionWizardPage;
  AddressPage: TInputQueryWizardPage;
  DownloadPage: TDownloadWizardPage;

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
    Result := GetComputerNameString + ',' + ArchivePort
  else
    Result := Trim(AddressPage.Values[0]) + ',' + ArchivePort;
end;

procedure InitializeWizard;
begin
  RolePage := CreateInputOptionPage(wpWelcome, 'Rodzaj instalacji', 'Wybierz rolę tego komputera',
    'Program WinForms zostanie zainstalowany w obu trybach.', True, False);
  RolePage.Add('Serwer — przygotuj wspólną bazę archiwum i zainstaluj program');
  RolePage.Add('Stanowisko zdalne — zainstaluj program i wskaż serwer archiwum');
  if GetPreviousData('ArchiveRole', '') = 'server' then
    RolePage.SelectedValueIndex := 0
  else
    RolePage.SelectedValueIndex := 1;

  AddressPage := CreateInputQueryPage(RolePage.ID, 'Serwer archiwum',
    'Podaj nazwę komputera lub adres IPv4.',
    'Wpisz adres komputera, na którym wykonano instalację serwerową.');
  AddressPage.Add('Nazwa lub adres serwera:', False);
  AddressPage.Values[0] := ExpandConstant('{param:ARCHIVESERVER|}');
  if AddressPage.Values[0] = '' then
    AddressPage.Values[0] := GetPreviousData('ArchiveHost', '');

  DownloadPage := CreateDownloadPage('Pobieranie składników',
    'Instalator pobiera brakujące składniki Microsoft.', nil);
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if RolePage.SelectedValueIndex = 0 then
    SetPreviousData(PreviousDataKey, 'ArchiveRole', 'server')
  else
  begin
    SetPreviousData(PreviousDataKey, 'ArchiveRole', 'client');
    SetPreviousData(PreviousDataKey, 'ArchiveHost', Trim(AddressPage.Values[0]));
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AddressPage.ID) and (RolePage.SelectedValueIndex = 0);
end;

function ValidHost(const Host: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  if (Length(Host) = 0) or (Length(Host) > 253) then Exit;
  for I := 1 to Length(Host) do
    if not ((Host[I] >= 'a') and (Host[I] <= 'z')) and
       not ((Host[I] >= 'A') and (Host[I] <= 'Z')) and
       not ((Host[I] >= '0') and (Host[I] <= '9')) and
       (Host[I] <> '.') and (Host[I] <> '-') then Exit;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = AddressPage.ID) and not ValidHost(Trim(AddressPage.Values[0])) then
  begin
    MsgBox('Wpisz nazwę komputera lub adres IPv4 serwera archiwum.', mbError, MB_OK);
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

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode, OpenResult: Integer;
  LicenseChoice: Integer;
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

      if not RegValueExists(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL', 'KLARCHIVE') then
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
      end;

      ExtractTemporaryFile('Setup-SharedArchive.sql');
      if not Exec(PowerShellPath, HelperParams + ' -Mode Server', '',
        SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then
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
      if ExitCode <> 0 then
      begin
        Result := 'Konfiguracja serwera archiwum zakończyła się błędem. Szczegóły wyświetlono w oknie konfiguracji.';
        Exit;
      end;
    end
    else if not ValidHost(Trim(AddressPage.Values[0])) then
    begin
      Result := 'Podaj adres serwera archiwum. Przy instalacji cichej użyj /ARCHIVESERVER=nazwa-serwera.';
      Exit;
    end;

    if NeedsWebView2 then
    begin
      DownloadPage.Clear;
      DownloadPage.Add(WebView2DownloadUrl, WebView2Bootstrapper, '');
      if not WizardSilent then DownloadPage.Show;
      try
        DownloadPage.Download;
      finally
        if not WizardSilent then DownloadPage.Hide;
      end;
      if not Exec(PowerShellPath, HelperParams + ' -Mode WebView2 -WebView2Package "' +
        ExpandConstant('{tmp}\') + WebView2Bootstrapper + '"', '',
        SW_HIDE, ewWaitUntilTerminated, ExitCode) then
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
      ArchiveServer('') + #13#10 + 'Konto SQL: KartyLeczeniaOperator.';
end;
