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
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Uruchom PowerShell jako administrator serwera.'
}
if ([IO.Path]::GetExtension($Installer) -ine '.exe') { throw 'Wskaż instalator EXE.' }
$installerFile = Get-Item -LiteralPath $Installer
if ($installerFile.Name -notmatch ('-v' + [regex]::Escape($Version) + '\.exe$')) {
    throw 'Numer wersji nie zgadza się z nazwą instalatora.'
}
New-Item -Path $Folder -ItemType Directory -Force | Out-Null
# Use localized account names obtained from well-known SIDs.
$administrators = ([Security.Principal.SecurityIdentifier]'S-1-5-32-544').Translate(
    [Security.Principal.NTAccount]).Value
$readers = ([Security.Principal.SecurityIdentifier]'S-1-5-11').Translate(
    [Security.Principal.NTAccount]).Value
$share = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
if ($share) {
    if ([IO.Path]::GetFullPath($share.Path) -ine [IO.Path]::GetFullPath($Folder)) {
        throw "Udział $ShareName wskazuje na inny folder: $($share.Path)."
    }
} else {
    New-SmbShare -Name $ShareName -Path $Folder -FullAccess $administrators -ReadAccess $readers | Out-Null
}
# Only administrators may write publication files. Readers have read/execute rights.
& icacls.exe $Folder /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-11:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Nie udało się ustawić uprawnień NTFS.' }
$destination = Join-Path $Folder $installerFile.Name
if ([IO.Path]::GetFullPath($installerFile.FullName) -ine [IO.Path]::GetFullPath($destination)) {
    Copy-Item -LiteralPath $installerFile.FullName -Destination $destination -Force
}
$sha = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
$manifest = @{ version = $Version; installer = $installerFile.Name; sha256 = $sha } |
    ConvertTo-Json -Compress
$temp = Join-Path $Folder ('release-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($temp, $manifest, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination (Join-Path $Folder 'release.json') -Force
} finally {
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}
Write-Host "Opublikowano aktualizację $Version w \\$env:COMPUTERNAME\$ShareName"
Write-Host "SHA-256: $sha"
