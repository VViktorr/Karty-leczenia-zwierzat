param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'Wersje instalatora trzeba podac recznie w formacie X.Y.Z lub X.Y.Z.W.'
}
foreach ($part in $Version.Split('.')) {
    if ([int]$part -gt 65535) { throw 'Skladowe wersji nie moga przekraczac 65535.' }
}
$fileVersion = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) ("ReactKL-installer-" + [Guid]::NewGuid().ToString('N'))
$installerScript = Join-Path $PSScriptRoot 'KartyLeczenia.ReactKL.iss'
if (!$Iscc) {
    foreach ($candidate in @(
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )) {
        if (Test-Path -LiteralPath $candidate) { $Iscc = $candidate; break }
    }
}
if (!$Iscc -or !(Test-Path -LiteralPath $Iscc)) {
    throw 'Nie znaleziono ISCC.exe. Zainstaluj Inno Setup lub podaj -Iscc.'
}
Push-Location -LiteralPath $root
try {
    Write-Host "ReactKL: budowanie recznie wybranej wersji $Version"
    Push-Location 'ui/react'
    try {
        & npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Instalacja zaleznosci React nie powiodla sie.' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Kompilacja React nie powiodla sie.' }
    }
    finally { Pop-Location }

    dotnet publish src/KartyLeczenia.ReactKL/KartyLeczenia.ReactKL.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" "-p:FileVersion=$fileVersion" "-p:AssemblyVersion=$fileVersion" -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja aplikacji ReactKL nie powiodla sie.' }
    if (!(Test-Path -LiteralPath (Join-Path $stage 'react-ui/index.html'))) {
        throw 'Brakuje zbudowanego interfejsu React w plikach instalatora.'
    }
    if (Test-Path -LiteralPath (Join-Path $stage 'AntdUI.dll')) {
        throw 'Instalator ReactKL nie moze zawierac AntdUI.dll.'
    }
    $output = Join-Path $root 'Instalatory'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $Iscc "/DReleaseVersion=$Version" "/DReleaseFileVersion=$fileVersion" "/DPayloadDir=$stage" "/O$output" $installerScript
    if ($LASTEXITCODE -ne 0) { throw 'Budowanie instalatora Inno Setup nie powiodlo sie.' }
    Write-Host "Gotowy instalator: $output\KartyLeczenia-Setup-win-x64-v$Version.exe"
    Write-Host 'Na serwerze instalator zostanie automatycznie opublikowany w SQL po pierwszym uruchomieniu ReactKL.'
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
