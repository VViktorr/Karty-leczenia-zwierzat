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
OutputBaseFilename=KartyLeczenia-WinForms-Setup-win-x64-v{#ReleaseVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
UsePreviousTasks=no

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "webview2"; Description: "Zainstaluj Microsoft Edge WebView2 Runtime (wymaga internetu)"; GroupDescription: "Dodatkowe składniki"; Check: NeedsWebView2

[Files]
Source: "..\publish-winforms\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Karty leczenia zwierząt (WinForms)"; Filename: "{app}\KartyLeczenia.WinForms.exe"; WorkingDir: "{app}"

[Code]
const
  WebView2Key = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Bootstrapper = 'MicrosoftEdgeWebview2Setup.exe';
  WebView2DownloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

function HasWebView2Version(RootKey: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, WebView2Key, 'pv', Version)
    and (Trim(Version) <> '') and (Trim(Version) <> '0.0.0.0');
end;

function NeedsWebView2: Boolean;
begin
  Result := not (HasWebView2Version(HKLM32) or HasWebView2Version(HKCU));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if not WizardIsTaskSelected('webview2') or not NeedsWebView2 then
    Exit;

  try
    DownloadTemporaryFile(WebView2DownloadUrl, WebView2Bootstrapper, '', nil);
  except
    Result := 'Nie udało się pobrać WebView2 Runtime. Sprawdź połączenie z internetem.' +
      #13#10 + GetExceptionMessage;
    Exit;
  end;

  if not Exec(ExpandConstant('{tmp}\') + WebView2Bootstrapper, '/silent /install',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := 'Nie udało się uruchomić instalatora WebView2 Runtime: ' + SysErrorMessage(ExitCode);
    Exit;
  end;

  if NeedsWebView2 then
    Result := 'Nie udało się zainstalować WebView2 Runtime. Kod instalatora Microsoft: ' +
      IntToStr(ExitCode) + '.';
end;
