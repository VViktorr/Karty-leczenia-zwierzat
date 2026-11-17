param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Server', 'WebView2')]
    [string]$Mode,

    [string]$WebView2Package
)

$ErrorActionPreference = 'Stop'
$instance = 'KLARCHIVE'
$serviceName = 'MSSQL$' + $instance
$port = 51433
$marker = 'HKLM:\SOFTWARE\KartyLeczenia\SharedArchive'
$instanceNames = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL'
$login = 'KartyLeczeniaOperator'

function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'Pakiet nie ma prawidłowego podpisu Microsoft.'
    }
}

function Invoke-LocalSql([string]$Database, [string]$Sql) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder.DataSource = "lpc:localhost\$instance"
    $builder.InitialCatalog = $Database
    $builder.IntegratedSecurity = $true
    $builder.ConnectTimeout = 3
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandTimeout = 60
            $command.CommandText = $Sql
            $command.ExecuteScalar()
        }
        finally { $command.Dispose() }
    }
    finally { $connection.Dispose() }
}

function Install-ArchiveEngine {
    $media = Join-Path $PSScriptRoot 'SQLEXPR_x64_ENU.exe'
    $destination = Join-Path $PSScriptRoot 'SQLExpress'
    $serverAdmin = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    Write-Host 'Pobieranie SQL Server 2022 Express z serwera Microsoft…'
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://download.microsoft.com/download/3/8/d/38de7036-2433-4207-8eae-06e247e17b25/SQLEXPR_x64_ENU.exe' -OutFile $media
    Assert-MicrosoftSignature $media
    $extract = Start-Process -FilePath $media -ArgumentList "/q /x:`"$destination`"" -Wait -PassThru
    $setup = Join-Path $destination 'setup.exe'
    if ($extract.ExitCode -ne 0 -or !(Test-Path -LiteralPath $setup)) {
        throw "Nie udało się rozpakować SQL Server Express. Kod $($extract.ExitCode)."
    }
    Assert-MicrosoftSignature $setup
    $arguments = "/QS /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=$instance " +
        "/IACCEPTSQLSERVERLICENSETERMS /SQLSYSADMINACCOUNTS=`"$serverAdmin`" /SQLSVCSTARTUPTYPE=Automatic " +
        '/TCPENABLED=0 /NPENABLED=0 /UpdateEnabled=True'
    # Record that this installer attempted to create the instance so it can resume after a restart.
    New-Item -Path $marker -Force | Out-Null
    New-ItemProperty -Path $marker -Name Instance -Value $instance -PropertyType String -Force | Out-Null
    $result = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
    if ($result.ExitCode -in 1641, 3010) { exit 3010 }
    if ($result.ExitCode -ne 0) {
        throw "Instalacja SQL Server Express zakończyła się błędem $($result.ExitCode). Zobacz SQL Server Setup Bootstrap\Log\Summary.txt."
    }
}

function Set-RegistryDword([string]$Path, [string]$Name, [int]$Value) {
    if ((Get-ItemProperty -LiteralPath $Path).$Name -eq $Value) { return $false }
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType DWord -Force | Out-Null
    return $true
}

function Set-RegistryString([string]$Path, [string]$Name, [string]$Value) {
    if ((Get-ItemProperty -LiteralPath $Path).$Name -ceq $Value) { return $false }
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType String -Force | Out-Null
    return $true
}

function Initialize-Archive {
    if ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw 'Instalacja serwerowa wymaga 64-bitowego Windows na procesorze Intel lub AMD.'
    }
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and (Get-ItemProperty -LiteralPath $marker -ErrorAction SilentlyContinue).Instance -ne $instance) {
        throw "Instancja $instance już istnieje i nie została utworzona przez instalator Kart leczenia."
    }
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($listener) {
        $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
        if (!$serviceInfo -or @($listener | Where-Object OwningProcess -ne $serviceInfo.ProcessId).Count -gt 0) {
            throw "Port TCP $port jest używany przez inną usługę."
        }
    }
    if (!$service) { Install-ArchiveEngine }

    $instanceId = (Get-ItemProperty -LiteralPath $instanceNames).$instance
    if (!$instanceId) { throw "Brakuje konfiguracji instancji SQL Server $instance." }
    $engine = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer"
    $tcp = Join-Path $engine 'SuperSocketNetLib\Tcp'
    $ipAll = Join-Path $tcp 'IPAll'
    $restart = Set-RegistryDword $engine 'LoginMode' 2
    if (Set-RegistryDword $tcp 'ListenOnAllIPs' 1) { $restart = $true }
    if (Set-RegistryString $ipAll 'TcpDynamicPorts' '') { $restart = $true }
    if (Set-RegistryString $ipAll 'TcpPort' "$port") { $restart = $true }
    if (Set-RegistryDword $tcp 'Enabled' 1) { $restart = $true }
    if ($restart) { Restart-Service -Name $serviceName }
    else { Start-Service -Name $serviceName }

    Write-Host 'Przygotowywanie bazy i uprawnień…'
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        try {
            Invoke-LocalSql 'master' 'SELECT 1;' | Out-Null
            break
        }
        catch {
            if ($attempt -eq 12) { throw }
            Start-Sleep -Seconds 2
        }
    }
    $server = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    $archiveListener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Where-Object OwningProcess -eq $server.ProcessId
    if (!$archiveListener) {
        throw "Instancja $instance nie nasłuchuje na porcie TCP $port."
    }
    Invoke-LocalSql 'master' "IF DB_ID(N'KartyLeczenia') IS NULL EXEC(N'CREATE DATABASE [KartyLeczenia]');" | Out-Null
    $schema = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Setup-SharedArchive.sql') -Raw -Encoding UTF8
    Invoke-LocalSql 'KartyLeczenia' $schema | Out-Null

    if ((Invoke-LocalSql 'master' "SELECT COUNT(*) FROM sys.sql_logins WHERE name = N'$login';") -eq 0) {
        $secret = Read-Host "Ustaw hasło konta SQL $login (14–128 znaków, wielkie i małe litery, cyfra i znak specjalny)" -AsSecureString
        $buffer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        try {
            $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($buffer)
            if ($password.Length -lt 14 -or $password.Length -gt 128) {
                throw 'Hasło musi mieć od 14 do 128 znaków. Uruchom instalator ponownie.'
            }
            $escaped = $password.Replace("'", "''")
            $sql = "CREATE LOGIN [$login] WITH PASSWORD=N'$escaped', CHECK_POLICY=ON, DEFAULT_DATABASE=[KartyLeczenia];"
            try { Invoke-LocalSql 'master' $sql | Out-Null }
            catch { throw 'SQL Server odrzucił utworzenie konta. Sprawdź wymagania hasła i uruchom instalator ponownie.' }
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($buffer)
            $secret.Dispose()
            Remove-Variable password, escaped, sql -ErrorAction SilentlyContinue
        }
    }
    Invoke-LocalSql 'KartyLeczenia' @"
IF USER_ID(N'$login') IS NULL CREATE USER [$login] FOR LOGIN [$login];
IF IS_ROLEMEMBER(N'KartyLeczeniaOperator', N'$login') <> 1
    ALTER ROLE [KartyLeczeniaOperator] ADD MEMBER [$login];
"@ | Out-Null

    $rule = 'KartyLeczenia-SQL-Archive'
    if (!(Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name $rule -DisplayName 'Karty leczenia zwierząt — SQL archiwum' `
            -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -RemoteAddress LocalSubnet `
            -Profile Domain,Private -Service $serviceName | Out-Null
    }
    Write-Host "Adres dla stanowisk zdalnych: $env:COMPUTERNAME,$port"
}

try {
    if ($Mode -eq 'WebView2') {
        Assert-MicrosoftSignature $WebView2Package
        $result = Start-Process -FilePath $WebView2Package -ArgumentList '/silent /install' -Wait -PassThru
        exit $result.ExitCode
    }
    Initialize-Archive
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($Mode -eq 'Server') { Read-Host 'Naciśnij Enter, aby wrócić do instalatora' | Out-Null }
    exit 1
}
