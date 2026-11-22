[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][ValidateLength(1,32)][ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?(?:-rc\.[1-9][0-9]{0,8})?\z')][string]$Version,
    [switch]$Pending,
    [switch]$TrustServerCertificate
)

# The same SQL update channel and logging mechanism as the previous SQL app.
# The server publishes its own setup file after installation/startup.
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:ProgramData 'KartyLeczenia\update-publish.log'
$stream = $null
$db = $null
function ParseRelease([string]$v) {
    if ($v.Length -gt 32 -or $v -notmatch '^(?<base>[0-9]+(?:\.[0-9]+){1,3})(?:-rc\.(?<rc>[1-9][0-9]{0,8}))?\z') {
        throw "Niepoprawny numer wersji: $v."
    }
    $number = [version]$Matches['base']
    $numeric = [version]::new($number.Major, $number.Minor, [Math]::Max($number.Build, 0), [Math]::Max($number.Revision, 0))
    $candidate = if ($Matches.ContainsKey('rc')) { [int]$Matches['rc'] } else { [int]::MaxValue }
    return @{ Numeric = $numeric; Candidate = $candidate }
}
function CompareRelease($left, $right) {
    $comparison = $left.Numeric.CompareTo($right.Numeric)
    if ($comparison -ne 0) { return $comparison }
    return $left.Candidate.CompareTo($right.Candidate)
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
    $expected = ParseRelease $Version
    $labelPrefix = 'Karty Leczenia '
    $label = if ($meta.FileDescription -and $meta.FileDescription.StartsWith($labelPrefix, [StringComparison]::Ordinal)) {
        $meta.FileDescription.Substring($labelPrefix.Length)
    } else { $meta.ProductVersion }
    $textVersion = ParseRelease $label
    if ([version]$actualFile -ne $expected.Numeric -or [version]$actualProduct -ne $expected.Numeric -or
        (CompareRelease $textVersion $expected) -ne 0) {
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
                $comparison = CompareRelease (ParseRelease $existingVersion) $expected
                if ($comparison -gt 0) {
                    throw "W SQL jest już nowsza wersja $existingVersion. Numeru instalatora nie zmieniono automatycznie."
                }
                if ($comparison -eq 0) {
                    if ([Convert]::ToBase64String($existingHash) -ne [Convert]::ToBase64String($hash)) {
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
    $hashHex = [BitConverter]::ToString($hash).Replace('-', '')
    Write-Host ('Opublikowano ReactKL {0} w SQL (SHA-256: {1}).' -f $Version, $hashHex)
} catch {
    try { Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] ERROR publishing ${Version}: $_" } catch {}
    Write-Error -ErrorAction Continue $_
    exit 1
} finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $db) { $db.Dispose() }
}
