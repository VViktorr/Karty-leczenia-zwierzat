[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [switch]$Pending,
    [switch]$TrustServerCertificate
)

# The same SQL update channel and logging mechanism as the previous SQL app.
# The server publishes its own setup file after installation/startup.
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:ProgramData 'KartyLeczenia\update-publish.log'
$stream = $null
$db = $null
function FourPart([string]$v) {
    if ($v.Split('.').Count -eq 3) { return [version]("$v.0") }
    return [version]$v
}
try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null
    $file = Get-Item -LiteralPath $Installer
    $expectedName = if ($Pending) {
        "KartyLeczenia-Update-Pending-v$Version.exe"
    } else {
        "KartyLeczenia-Setup-win-x64-v$Version.exe"
    }
    if ($file.Name -ine $expectedName -or $file.Extension -ine '.exe') {
        throw "Plik $($file.Name) nie odpowiada instalatorowi $expectedName."
    }
    if ($Pending -and $file.DirectoryName -ine $PSScriptRoot) {
        throw 'Instalator oczekujący musi pochodzić z folderu zainstalowanej aplikacji.'
    }
    $meta = $file.VersionInfo
    $actualFile = '{0}.{1}.{2}.{3}' -f $meta.FileMajorPart,$meta.FileMinorPart,$meta.FileBuildPart,$meta.FilePrivatePart
    $actualProduct = '{0}.{1}.{2}.{3}' -f $meta.ProductMajorPart,$meta.ProductMinorPart,$meta.ProductBuildPart,$meta.ProductPrivatePart
    $expected = FourPart $Version
    if ((FourPart $actualFile) -ne $expected -or (FourPart $actualProduct) -ne $expected) {
        throw "Wersja pliku $actualFile, produktu $actualProduct i wydania $Version nie zgadzają się."
    }

    $stream = [IO.File]::Open($file.FullName,'Open','Read','Read')
    if ($stream.Length -le 0 -or $stream.Length -gt [int]::MaxValue) {
        throw 'Niepoprawny rozmiar instalatora.'
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($stream) }
    finally { $sha.Dispose() }
    $stream.Position = 0

    $connection = New-Object Data.SqlClient.SqlConnectionStringBuilder
    $connection['Data Source'] = $Server
    $connection['Initial Catalog'] = 'KartyLeczenia'
    $connection['Integrated Security'] = $true
    $connection['Connect Timeout'] = 12
    $connection['Encrypt'] = $true
    $connection['TrustServerCertificate'] = [bool]$TrustServerCertificate
    $db = New-Object Data.SqlClient.SqlConnection $connection.ConnectionString
    $db.Open()

    $tx = $db.BeginTransaction()
    try {
        $cmd = $db.CreateCommand()
        try {
            $cmd.Transaction = $tx
            $cmd.CommandTimeout = 300
            $cmd.CommandText = 'SELECT version,sha256 FROM dbo.program_update WITH (UPDLOCK,HOLDLOCK) WHERE id=1;'
            $existingVersion = $null
            $existingHash = $null
            $reader = $cmd.ExecuteReader()
            try {
                if ($reader.Read()) {
                    $existingVersion = $reader.GetString(0)
                    $existingHash = [byte[]]$reader.GetValue(1)
                }
            } finally { $reader.Dispose() }
            $shouldPublish = $true
            if ($null -ne $existingVersion) {
                $old = FourPart $existingVersion
                if ($old -gt $expected) {
                    throw "W SQL jest już nowsza wersja $existingVersion. Numeru instalatora nie zmieniono automatycznie."
                }
                if ($old -eq $expected) {
                    if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$existingHash, [byte[]]$hash)) {
                        throw "W SQL jest już wersja $existingVersion z inną sumą SHA-256. Użyj nowego numeru."
                    }
                    $shouldPublish = $false
                }
            }
            if ($shouldPublish) {
                $cmd.Parameters.Clear()
                $cmd.CommandText = @'
UPDATE dbo.program_update
SET release_id=NEWID(),version=@version,sha256=@sha,
    installer=@installer,published_at_utc=SYSUTCDATETIME() WHERE id=1;
IF @@ROWCOUNT=0
  INSERT dbo.program_update(id,release_id,version,sha256,installer)
  VALUES(1,NEWID(),@version,@sha,@installer);
'@
                $cmd.Parameters.Add('@version',[Data.SqlDbType]::NVarChar,32).Value = $Version
                $cmd.Parameters.Add('@sha',[Data.SqlDbType]::Binary,32).Value = $hash
                $cmd.Parameters.Add('@installer',[Data.SqlDbType]::VarBinary,-1).Value = $stream
                $null = $cmd.ExecuteNonQuery()
            }
        } finally { $cmd.Dispose() }
        $tx.Commit()
    } catch {
        try { $tx.Rollback() } catch {}
        throw
    } finally { $tx.Dispose() }
    $stream.Dispose()
    $stream = $null
    if ($Pending) { Remove-Item -LiteralPath $file.FullName -Force }
    Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] Published ReactKL $Version to dbo.program_update on $Server."
    Write-Host "Opublikowano ReactKL $Version w SQL (SHA-256: $([Convert]::ToHexString($hash)))."
} catch {
    try { Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] ERROR publishing $Version: $_" } catch {}
    Write-Error -ErrorAction Continue $_
    exit 1
} finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $db) { $db.Dispose() }
}
