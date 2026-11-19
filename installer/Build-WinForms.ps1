param(
    [string]$Version = '',
    [string]$Iscc = "C:\Program Files\Inno Setup 7\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
# Each new build must have a newer version for the SQL updater.
# A caller can still specify -Version X.Y.Z or X.Y.Z.W.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Date).ToString('yyyy.MM.dd.HHmm')
}
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$repository = Split-Path -Parent $PSScriptRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) ("KartyLeczenia-build-" + [Guid]::NewGuid().ToString('N'))
Push-Location -LiteralPath $repository
try {
    dotnet publish src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release -r win-x64 --self-contained true -o publish-winforms
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja WinForms nie powiodla sie.' }

    dotnet publish src/KartyLeczenia.CatalogImport/KartyLeczenia.CatalogImport.csproj -c Release -r win-x64 --self-contained true -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja importera katalogu nie powiodla sie.' }

    Copy-Item -LiteralPath 'src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite' -Destination $stage
    Copy-Item -LiteralPath 'data/ChPL' -Destination $stage -Recurse
    # Versioned additive entries ship with the installer and run only against
    # the server archive. They are idempotent and preserve operator changes.
    Copy-Item -LiteralPath 'scripts/catalogue-additions' -Destination $stage -Recurse
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $repository 'catalog-seed-win-x64.zip'
    $temporaryZip = Join-Path $repository ('catalog-seed-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
        Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
    }

    & $Iscc "/DReleaseVersion=$Version" "/DReleaseFileVersion=$fileVersion" "/O$repository" 'installer\KartyLeczenia.WinForms.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Kompilacja instalatora nie powiodla sie.' }
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
) {
    throw 'Wersja musi mieć format X.Y.Z lub X.Y.Z.W.'
}
$parts = $Version.Split('.')
foreach ($part in $parts) {
    if ([int]$part -gt 65535) { throw 'Składowa wersji przekracza limit 65535.' }
}
$fileVersion = if ($parts.Count -eq 3) { "$Version.0" } else { $Version }
Write-Host "Wersja instalatora SQL KartyLeczenia: $Version"
$repository = Split-Path -Parent $PSScriptRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) ("KartyLeczenia-build-" + [Guid]::NewGuid().ToString('N'))
Push-Location -LiteralPath $repository
try {
    dotnet publish src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release -r win-x64 --self-contained true -o publish-winforms
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja WinForms nie powiodla sie.' }

    dotnet publish src/KartyLeczenia.CatalogImport/KartyLeczenia.CatalogImport.csproj -c Release -r win-x64 --self-contained true -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publikacja importera katalogu nie powiodla sie.' }

    Copy-Item -LiteralPath 'src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite' -Destination $stage
    Copy-Item -LiteralPath 'data/ChPL' -Destination $stage -Recurse
    # Versioned additive entries ship with the installer and run only against
    # the server archive. They are idempotent and preserve operator changes.
    Copy-Item -LiteralPath 'scripts/catalogue-additions' -Destination $stage -Recurse
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $repository 'catalog-seed-win-x64.zip'
    $temporaryZip = Join-Path $repository ('catalog-seed-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip)
        Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
    }

    & $Iscc "/DReleaseVersion=$Version" "/O$repository" 'installer\KartyLeczenia.WinForms.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Kompilacja instalatora nie powiodla sie.' }
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
