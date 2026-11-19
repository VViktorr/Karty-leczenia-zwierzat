[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [switch]$TrustServerCertificate
)

$ErrorActionPreference = 'Stop'
$db = $null
$stream = $null
$log = Join-Path $env:ProgramData 'KartyLeczenia\reactkl-update-publish.log'
try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
    $file = Get-Item -LiteralPath $Installer
    if ($file.Extension -ine '.exe' -or $file.Name -notmatch '^ReactKL-Setup-win-x64-v') {
        throw 'Wybierz instalator ReactKL z Build-ReactKL.ps1. Nie można opublikować instalatora WinForms.'
    }
    $fileVersion = $file.VersionInfo
    $actual = '{0}.{1}.{2}.{3}' -f $fileVersion.FileMajorPart,$fileVersion.FileMinorPart,$fileVersion.FileBuildPart,$fileVersion.FilePrivatePart
    if ([version]$actual -ne [version]$Version) {
        throw "Wersja instalatora $actual jest inna niz podana $Version."
    }
    $stream = [IO.File]::Open($file.FullName,'Open','Read','Read')
    if ($stream.Length -le 0 -or $stream.Length -gt [int]::MaxValue) {
        throw 'Niepoprawny rozmiar pliku instalatora.'
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($stream) }
    finally { $sha.Dispose() }
    $stream.Position=0

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = 'KartyLeczenia'
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 12
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = [bool]$TrustServerCertificate
    $db = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $db.Open()

    $tx = $db.BeginTransaction()
    try {
        $cmd = $db.CreateCommand()
        try {
            $cmd.Transaction=$tx
            $cmd.CommandTimeout=300
            $cmd.CommandText='SELECT version FROM dbo.react_program_update WITH (UPDLOCK,HOLDLOCK) WHERE id=1;'
            $old=$cmd.ExecuteScalar()
            if ($null -ne $old -and [version]$old -ge [version]$Version) {
                throw "W SQL jest juz wersja $old. Nowa publikacja wymaga wyzszego numeru."
            }
            $cmd.CommandText=@'
UPDATE dbo.react_program_update SET release_id=NEWID(),version=@version,
    sha256=@hash,installer=@file,published_at_utc=SYSUTCDATETIME() WHERE id=1;
IF @@ROWCOUNT=0
    INSERT dbo.react_program_update(id,release_id,version,sha256,installer)
    VALUES(1,NEWID(),@version,@hash,@file);
'@
            $cmd.Parameters.Add('@version',[Data.SqlDbType]::NVarChar,32).Value=$Version
            $cmd.Parameters.Add('@hash',[Data.SqlDbType]::Binary,32).Value=$hash
            $cmd.Parameters.Add('@file',[Data.SqlDbType]::VarBinary,-1).Value=$stream
            $null=$cmd.ExecuteNonQuery()
        } finally { $cmd.Dispose() }
        $tx.Commit()
    } catch {
        $tx.Rollback()
        throw
    } finally { $tx.Dispose() }
    Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] Published ReactKL $Version: $($file.FullName)"
    Write-Host "ReactKL $Version opublikowany w SQL KartyLeczenia, SHA256 $([Convert]::ToHexString($hash))."
} catch {
    try { Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] ERROR $_" } catch {}
    Write-Error -ErrorAction Continue $_
    exit 1
} finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $db) { $db.Dispose() }
}
