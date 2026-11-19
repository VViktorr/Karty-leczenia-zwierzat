[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$Pending
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:ProgramData 'KartyLeczenia\update-publish.log'
$stream = $null
$db = $null

try {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
    Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] Publishing $Version to SQL."
    $file = Get-Item -LiteralPath $Installer
    if ($Pending -and $file.FullName -ine (Join-Path $PSScriptRoot "KartyLeczenia-Update-Pending-v$Version.exe")) {
        throw 'Invalid pending installer path.'
    }
    $metadata = $file.VersionInfo
    $productVersion = '{0}.{1}.{2}.{3}' -f $metadata.ProductMajorPart, $metadata.ProductMinorPart,
        $metadata.ProductBuildPart, $metadata.ProductPrivatePart
    if ($file.Extension -ine '.exe' -or [version]$productVersion -ne [version]("$Version.0")) {
        throw 'The installer product version does not match the expected release.'
    }

    $stream = [IO.File]::Open($file.FullName, 'Open', 'Read', 'Read')
    if ($stream.Length -le 0 -or $stream.Length -gt [int]::MaxValue) { throw 'Invalid installer size.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($stream) }
    finally { $sha.Dispose() }
    $stream.Position = 0

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = 'KartyLeczenia'
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 8
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $true
    $db = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $db.Open()
    $transaction = $db.BeginTransaction()
    try {
        $command = $db.CreateCommand()
        try {
            $command.Transaction = $transaction
            $command.CommandTimeout = 120
            $command.CommandText = 'SELECT version FROM dbo.program_update WITH (UPDLOCK, HOLDLOCK) WHERE id = 1;'
            $previous = $command.ExecuteScalar()
            if ($null -ne $previous -and [version]$previous -gt [version]$Version) {
                throw "SQL already contains newer version $previous."
            }
            $command.CommandText = @'
UPDATE dbo.program_update SET release_id = NEWID(), version = @version,
    sha256 = @hash, installer = @installer, published_at_utc = SYSUTCDATETIME()
WHERE id = 1;
IF @@ROWCOUNT = 0
    INSERT dbo.program_update (id, release_id, version, sha256, installer)
    VALUES (1, NEWID(), @version, @hash, @installer);
'@
            $command.Parameters.Add('@version', [Data.SqlDbType]::NVarChar, 32).Value = $Version
            $command.Parameters.Add('@hash', [Data.SqlDbType]::Binary, 32).Value = $hash
            $command.Parameters.Add('@installer', [Data.SqlDbType]::VarBinary, -1).Value = $stream
            $null = $command.ExecuteNonQuery()
        }
        finally { $command.Dispose() }
        $transaction.Commit()
    }
    finally { $transaction.Dispose() }
    $stream.Dispose()
    $stream = $null
    if ($Pending) { Remove-Item -LiteralPath $file.FullName }
    Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] Published $Version."
}
catch {
    try { Add-Content -LiteralPath $log -Encoding UTF8 -Value "[$(Get-Date -Format s)] ERROR: $_" }
    catch {}
    Write-Error -ErrorAction Continue $_
    exit 1
}
finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $db) { $db.Dispose() }
}
