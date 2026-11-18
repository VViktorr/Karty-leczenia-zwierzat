# Run as administrator on the archive server after building the new Inno Setup installer.
# Example: .\Publish-KartyLeczeniaUpdate.ps1 -Installer 'C:\Instalatory\KartyLeczenia-Setup-win-x64-v1.0.1.exe' -Version '1.0.1'
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$Installer,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [string]$Folder = "$env:ProgramData\KartyLeczenia\Updates",
    [string]$ShareName = 'KartyLeczenia-Updates'
)
$ErrorActionPreference = 'Stop'
$logPath = Join-Path $env:ProgramData 'KartyLeczenia\update-publish.log'
trap {
    $failure = $_
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
        Add-Content -LiteralPath $logPath -Encoding UTF8 -Value (
            '[{0}] ERROR version {1}: {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Version, $failure.ToString())
    } catch {}
    Write-Host ('Blad publikacji aktualizacji: ' + $failure.ToString())
    exit 1
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Uruchom PowerShell jako administrator serwera.'
}
if ([IO.Path]::GetExtension($Installer) -ine '.exe') { throw 'Wskaz instalator EXE.' }
$installerFile = Get-Item -LiteralPath $Installer
if ($installerFile.Name -notmatch ('-v' + [regex]::Escape($Version) + '\.exe$')) {
    throw 'Numer wersji nie zgadza sie z nazwa instalatora.'
}
New-Item -Path $Folder -ItemType Directory -Force | Out-Null
# Never replace a newer server release with an older installer.
$existingManifest = Join-Path $Folder 'release.json'
if (Test-Path -LiteralPath $existingManifest -PathType Leaf) {
    $currentRelease = Get-Content -LiteralPath $existingManifest -Raw | ConvertFrom-Json
    $publishedVersion = [version]$currentRelease.version
    if ([version]$Version -lt $publishedVersion) {
        throw "Na serwerze jest juz nowsza wersja $publishedVersion. Nie opublikowano starszej $Version."
    }
}
# Use localized account names obtained from well-known SIDs.
$administrators = ([Security.Principal.SecurityIdentifier]'S-1-5-32-544').Translate(
    [Security.Principal.NTAccount]).Value
$readers = ([Security.Principal.SecurityIdentifier]'S-1-5-11').Translate(
    [Security.Principal.NTAccount]).Value
$share = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
if ($share) {
    if ([IO.Path]::GetFullPath($share.Path) -ine [IO.Path]::GetFullPath($Folder)) {
        throw "Udzial $ShareName wskazuje na inny folder: $($share.Path)."
    }
} else {
    New-SmbShare -Name $ShareName -Path $Folder -FullAccess $administrators -ReadAccess $readers | Out-Null
}
# Only administrators may write publication files. Readers have read/execute rights.
& icacls.exe $Folder /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-11:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Nie udalo sie ustawic uprawnien NTFS.' }
# Every release has one stable name on the server. Stage the new binary first,
# so remote workstations never see a partly copied executable.
$publishedName = 'KartyLeczenia-Setup-win-x64.exe'
$destination = Join-Path $Folder $publishedName
$stagedInstaller = Join-Path $Folder ('installer-' + [guid]::NewGuid().ToString('N') + '.tmp')
$tempManifest = Join-Path $Folder ('release-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    Copy-Item -LiteralPath $installerFile.FullName -Destination $stagedInstaller
    $sha = (Get-FileHash -LiteralPath $stagedInstaller -Algorithm SHA256).Hash
    $manifest = @{ version = $Version; installer = $publishedName; sha256 = $sha } |
        ConvertTo-Json -Compress

    # An atomic rename/replace on the same volume keeps the public file whole.
    if ([IO.File]::Exists($destination)) {
        [IO.File]::Replace($stagedInstaller, $destination, $null)
    } else {
        [IO.File]::Move($stagedInstaller, $destination)
    }

    # Publish metadata only after the installer is in place.
    [IO.File]::WriteAllText($tempManifest, $manifest, [Text.UTF8Encoding]::new($false))
    $releasePath = Join-Path $Folder 'release.json'
    if ([IO.File]::Exists($releasePath)) {
        [IO.File]::Replace($tempManifest, $releasePath, $null)
    } else {
        [IO.File]::Move($tempManifest, $releasePath)
    }

    # Remove installers from the previous versioned-file scheme after success.
    Get-ChildItem -LiteralPath $Folder -File -Filter 'KartyLeczenia-Setup-win-x64-v*.exe' |
        Remove-Item -Force
} finally {
    Remove-Item -LiteralPath $stagedInstaller -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempManifest -ErrorAction SilentlyContinue
}
Write-Host "Opublikowano aktualizacje $Version w \\$env:COMPUTERNAME\$ShareName"
Write-Host "Plik serwera: $publishedName"
Write-Host "SHA-256: $sha"

try {
    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value (
        '[{0}] OK version {1}; file {2}; SHA256 {3}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Version, $destination, $sha)
} catch {
    Write-Host ('Uwaga: nie udalo sie zapisac dziennika publikacji: ' + $_.Exception.Message)
}
