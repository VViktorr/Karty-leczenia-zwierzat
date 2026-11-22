param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Iscc = ''
)

$ErrorActionPreference = 'Stop'
if ($Version.Length -gt 32 -or $Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?(?:-rc\.[1-9][0-9]{0,8})?\z') {
    throw 'Wersje instalatora trzeba podac recznie w formacie X.Y.Z lub X.Y.Z.W.'
}
$numericVersion = ($Version -split '-')[0]
foreach ($part in $numericVersion.Split('.')) {
    if ([int]$part -gt 65535) { throw 'Skladowe wersji nie moga przekraczac 65535.' }
}
$fileVersion = if ($numericVersion.Split('.').Count -eq 3) { "$numericVersion.0" } else { $numericVersion }
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
        & npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Instalacja zaleznosci React nie powiodla sie.' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Kompilacja React nie powiodla sie.' }
    }
    finally { Pop-Location }

    dotnet publish src/KartyLeczenia.ReactKL/KartyLeczenia.ReactKL.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" "-p:InformationalVersion=$Version" "-p:IncludeSourceRevisionInInformationalVersion=false" "-p:FileVersion=$fileVersion" "-p:AssemblyVersion=$fileVersion" -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja aplikacji ReactKL nie powiodla sie.' }
    if (!(Test-Path -LiteralPath (Join-Path $stage 'react-ui/index.html'))) {
        throw 'Brakuje zbudowanego interfejsu React w plikach instalatora.'
    }
    if (Test-Path -LiteralPath (Join-Path $stage 'AntdUI.dll')) {
        throw 'Instalator ReactKL nie moze zawierac AntdUI.dll.'
    }

    # The manual-version installer needs a REAL bootstrap package for a new SQL server.
    # Catalogue SQLite and official ChPL PDFs are only source material for the
    # initial SQL import; the ReactKL application does not use SQLite as storage.
    $seedStage = Join-Path ([IO.Path]::GetTempPath()) ("ReactKL-seed-" + [Guid]::NewGuid().ToString('N'))
    $seedOutput = Join-Path ([IO.Path]::GetTempPath()) ("ReactKL-seedzip-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $seedStage, $seedOutput -Force | Out-Null
    $seedZip = Join-Path $seedOutput 'catalog-seed-win-x64.zip'
    dotnet publish src/KartyLeczenia.CatalogImport/KartyLeczenia.CatalogImport.csproj -c Release -r win-x64 --self-contained true -o $seedStage
    if ($LASTEXITCODE -ne 0) { throw 'Budowa pierwszego importera katalogu nie powiodla sie.' }
    foreach ($required in @(
        'src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite',
        'data/ChPL',
        'scripts/catalogue-additions'
    )) {
        if (!(Test-Path -LiteralPath $required)) { throw "Brakuje pliku paczki SQL: $required" }
    }
    $sqlite = Get-Item -LiteralPath 'src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite'
    if ($sqlite.Length -lt 4096) {
        throw 'Plik SQLite jest pusty lub jest wskaznikiem Git LFS; nie mozna budowac instalatora.'
    }
    Copy-Item -LiteralPath $sqlite.FullName -Destination $seedStage
    Copy-Item -LiteralPath 'data/ChPL' -Destination $seedStage -Recurse
    Copy-Item -LiteralPath 'scripts/catalogue-additions' -Destination $seedStage -Recurse
    if (!(Test-Path -LiteralPath (Join-Path $seedStage 'KartyLeczenia.CatalogImport.exe'))) {
        throw 'Brak programu KartyLeczenia.CatalogImport.exe w paczce startowej.'
    }
    if (!(Get-ChildItem -LiteralPath (Join-Path $seedStage 'ChPL') -Filter '*.pdf' -Recurse -File |
          Select-Object -First 1)) {
        throw 'Paczka SQL nie zawiera urzedowych ChPL PDF.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($seedStage, $seedZip)
    Write-Host "Przygotowano paczke SQL i ChPL: $seedZip"
    $output = Join-Path $root 'Instalatory'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $Iscc "/DReleaseVersion=$Version" "/DReleaseFileVersion=$fileVersion" "/DPayloadDir=$stage" "/DCatalogSeedFile=$seedZip" "/O$output" $installerScript
    if ($LASTEXITCODE -ne 0) { throw 'Budowanie instalatora Inno Setup nie powiodlo sie.' }
    Write-Host "Gotowy instalator: $output\KartyLeczenia-Setup-win-x64-v$Version.exe"
    Write-Host 'Na serwerze instalator zostanie automatycznie opublikowany w SQL po pierwszym uruchomieniu ReactKL.'
}
finally {
    Pop-Location
    if ($seedStage -and (Test-Path -LiteralPath $seedStage)) { Remove-Item -LiteralPath $seedStage -Recurse -Force }
    if ($seedOutput -and (Test-Path -LiteralPath $seedOutput)) { Remove-Item -LiteralPath $seedOutput -Recurse -Force }
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
