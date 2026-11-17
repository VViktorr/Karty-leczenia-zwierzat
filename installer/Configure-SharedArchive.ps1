param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Server', 'Existing', 'WebView2')]
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
$databaseUser = 'KartyLeczeniaOperatorUser'

function Show-ArchiveDialog {
    param(
        [string]$Title,
        [string]$Message,
        [string[]]$Choices,
        [switch]$TextInput,
        [switch]$Password,
        [switch]$Notice
    )

    $dialog = New-Object System.Windows.Forms.Form
    try {
        $dialog.Text = $Title
        $dialog.ClientSize = [System.Drawing.Size]::new(590, 166)
        $dialog.StartPosition = 'CenterScreen'
        $dialog.FormBorderStyle = 'FixedDialog'
        $dialog.MinimizeBox = $false
        $dialog.MaximizeBox = $false
        $dialog.TopMost = $true
        $dialog.AutoScaleMode = 'Dpi'
        $dialog.Font = [System.Drawing.SystemFonts]::MessageBoxFont

        $description = New-Object System.Windows.Forms.Label
        $description.SetBounds(16, 16, 558, 66)
        $description.Text = $Message
        $dialog.Controls.Add($description)

        $field = $null
        if ($Choices) {
            $field = New-Object System.Windows.Forms.ComboBox
            $field.DropDownStyle = 'DropDownList'
            $field.Items.AddRange([object[]]$Choices)
            $field.SelectedIndex = 0
        } elseif ($TextInput -or $Password) {
            $field = New-Object System.Windows.Forms.TextBox
            $field.UseSystemPasswordChar = $Password.IsPresent
            if ($Password) { $field.MaxLength = 128 }
        }
        if ($field) {
            $field.SetBounds(16, 87, 558, 28)
            $field.AccessibleName = $Title
            $dialog.Controls.Add($field)
            $dialog.ActiveControl = $field
        }

        $ok = New-Object System.Windows.Forms.Button
        $ok.SetBounds(386, 125, 90, 28)
        $ok.Text = 'OK'
        $ok.DialogResult = 'OK'
        $dialog.Controls.Add($ok)
        $dialog.AcceptButton = $ok
        if (!$Notice) {
            $cancel = New-Object System.Windows.Forms.Button
            $cancel.SetBounds(484, 125, 90, 28)
            $cancel.Text = 'Anuluj'
            $cancel.DialogResult = 'Cancel'
            $dialog.Controls.Add($cancel)
            $dialog.CancelButton = $cancel
        }

        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            if (!$Notice) { exit 2 }
            return
        }
        if ($Choices) { return $field.SelectedIndex }
        if ($Password) {
            $secret = New-Object Security.SecureString
            foreach ($character in $field.Text.ToCharArray()) { $secret.AppendChar($character) }
            $field.Clear()
            $secret.MakeReadOnly()
            return $secret
        }
        if ($TextInput) { return $field.Text }
    }
    finally { $dialog.Dispose() }
}

function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'Pakiet nie ma prawidłowego podpisu Microsoft.'
    }
}

function Invoke-LocalSql([string]$Database, [string]$Sql) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = if ($instance -eq 'MSSQLSERVER') { 'lpc:localhost' } else { "lpc:localhost\$instance" }
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 3
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.psbase.ConnectionString
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

function Show-OdbcSourceError([string]$SummaryPath, [datetime]$Started) {
    $summary = Get-Item -LiteralPath $SummaryPath -ErrorAction SilentlyContinue
    if (!$summary -or $summary.LastWriteTime -lt $Started.AddSeconds(-2)) { return $false }
    try {
        $details = Get-Content -LiteralPath $SummaryPath -Raw
    }
    catch { return $false }
    $odbcError = [regex]::Match($details,
        '(?im)^\s*Error description:[^\r\n]*ODBC Driver (17|18) for SQL Server[^\r\n]*?(msodbcsql(?:_?(?:17|18))?\.msi)')
    if ($details -notmatch '(?im)^\s*Component error code:\s*1706\s*$' -or !$odbcError.Success) {
        return $false
    }

    $driverVersion = $odbcError.Groups[1].Value
    $packageName = $odbcError.Groups[2].Value
    $options = @('Otwórz instrukcję Microsoft', 'Otwórz dziennik instalacji SQL', 'Zamknij')
    $choice = Show-ArchiveDialog -Title "Brakuje pakietu sterownika ODBC $driverVersion" -Notice -Choices $options -Message (
        "Windows Installer nie znalazł pliku $packageName (błąd 1706). Napraw istniejący sterownik z pakietu tej samej wersji albo wybierz działającą instancję SQL w kreatorze.")
    try {
        if ($choice -eq 0) {
            Start-Process 'https://learn.microsoft.com/troubleshoot/sql/database-engine/install/windows/restore-missing-windows-installer-cache-files'
        } elseif ($choice -eq 1) {
            Start-Process -FilePath $SummaryPath
        }
    }
    catch {
        Show-ArchiveDialog -Title 'Dziennik instalacji SQL' -Notice -Message $SummaryPath
    }
    return $true
}

function Install-ArchiveEngine {
    $media = Join-Path $PSScriptRoot 'SQLEXPR_x64_ENU.exe'
    $destination = Join-Path $PSScriptRoot 'SQLExpress'
    $serverAdmin = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if (!(Test-Path -LiteralPath $media)) { throw 'Brakuje pobranego pakietu SQL Server Express.' }
    Assert-MicrosoftSignature $media
    $extract = Start-Process -FilePath $media -ArgumentList "/q /x:`"$destination`"" -Wait -PassThru -WindowStyle Hidden
    $setup = Join-Path $destination 'setup.exe'
    if ($extract.ExitCode -ne 0 -or !(Test-Path -LiteralPath $setup)) {
        throw "Nie udało się rozpakować SQL Server Express. Kod $($extract.ExitCode)."
    }
    Assert-MicrosoftSignature $setup
    $arguments = "/Q /ACTION=Install /FEATURES=SQLENGINE /INSTANCENAME=$instance " +
        "/IACCEPTSQLSERVERLICENSETERMS /SQLSYSADMINACCOUNTS=`"$serverAdmin`" /SQLSVCSTARTUPTYPE=Automatic " +
        '/TCPENABLED=0 /NPENABLED=0 /UpdateEnabled=True'
    # Record that this installer attempted to create the instance so it can resume after a restart.
    New-Item -Path $marker -Force | Out-Null
    New-ItemProperty -Path $marker -Name Instance -Value $instance -PropertyType String -Force | Out-Null
    $started = Get-Date
    $result = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($result.ExitCode -in 1641, 3010) { exit 3010 }
    if ($result.ExitCode -ne 0) {
        $summaryPath = Join-Path $env:ProgramFiles 'Microsoft SQL Server\170\Setup Bootstrap\Log\Summary.txt'
        if (Show-OdbcSourceError $summaryPath $started) { exit 1 }
        throw "Instalacja SQL Server Express zakończyła się błędem $($result.ExitCode). Dziennik: $summaryPath"
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

function Initialize-ArchiveSqlLogin {
    $secret = Show-ArchiveDialog -Title 'Hasło operatora archiwum' -Password -Message (
        "Ustaw hasło konta SQL $login. Wpisz dokładnie sześć cyfr (0–9).")
    $buffer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($buffer)
        if ($password -notmatch '\A[0-9]{6}\z') {
            throw 'Hasło musi składać się z dokładnie sześciu cyfr (0–9). Uruchom instalator ponownie.'
        }
        $escaped = $password.Replace("'", "''")
        $sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
CREATE LOGIN [$login] WITH PASSWORD='$escaped', CHECK_POLICY=OFF, CHECK_EXPIRATION=OFF, DEFAULT_DATABASE=[KartyLeczenia];
USE [KartyLeczenia];
EXEC(N'CREATE USER [$databaseUser] FOR LOGIN [$login];');
EXEC(N'ALTER ROLE [KartyLeczeniaOperator] ADD MEMBER [$databaseUser];');
COMMIT;
"@
        try { Invoke-LocalSql 'master' $sql | Out-Null }
        catch {
            $failure = $_.Exception
            while ($failure -isnot [System.Data.SqlClient.SqlException] -and $failure.InnerException) {
                $failure = $failure.InnerException
            }
            if ($failure -is [System.Data.SqlClient.SqlException]) {
                $detail = $failure.Message.Replace($password, '[ukryte]')
                throw "Konfiguracja konta operatora archiwum. SQL $($failure.Number), wiersz $($failure.LineNumber). $detail"
            }
            throw "Konfiguracja konta operatora archiwum. Błąd $($failure.GetType().Name)."
        }
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($buffer)
        $secret.Dispose()
        Remove-Variable password, escaped, sql -ErrorAction SilentlyContinue
    }
}

function Initialize-ExistingArchive {
    # Only Database Engine instance registrations are considered.
    $registered = Get-Item -LiteralPath $instanceNames -ErrorAction SilentlyContinue
    if (!$registered) { throw 'Nie znaleziono zainstalowanych instancji SQL Server.' }
    $instances = @(
        foreach ($name in ($registered.GetValueNames() | Sort-Object)) {
            $id = $registered.GetValue($name)
            $service = if ($name -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { 'MSSQL$' + $name }
            $status = Get-Service -Name $service -ErrorAction SilentlyContinue
            $setup = Get-ItemProperty -LiteralPath "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$id\Setup" -ErrorAction SilentlyContinue
            $version = if ($setup.PatchLevel) { $setup.PatchLevel } elseif ($setup.Version) { $setup.Version } else { 'nieustalona' }
            [pscustomobject]@{ Name = $name; State = $status.Status; Version = $version }
        }
    )
    if (!$instances.Count) { throw 'Nie znaleziono zainstalowanych instancji SQL Server.' }

    $choices = foreach ($item in $instances) {
        $state = switch ("$($item.State)") {
            'Running' { 'uruchomiona' }
            'Stopped' { 'zatrzymana' }
            '' { 'brak usługi' }
            default { $item.State }
        }
        "$($item.Name) | wersja $($item.Version) | $state"
    }
    $selection = Show-ArchiveDialog -Title 'Instancje SQL Server' -Choices $choices -Message (
        'Wybierz uruchomioną instancję SQL. Baza archiwum zostanie przygotowana po potwierdzeniu.')
    $selected = $instances[$selection]
    if ($selected.State -ne 'Running') {
        throw 'Wybrana instancja nie działa. Uruchom ją i ponów instalację.'
    }
    $script:instance = $selected.Name
    $productVersion = Invoke-LocalSql 'master' "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'));"
    $admin = Invoke-LocalSql 'master' "SELECT IS_SRVROLEMEMBER(N'sysadmin');"
    if ($admin -ne 1) {
        throw 'Uruchom instalator z konta Windows mającego uprawnienia sysadmin na wybranej instancji.'
    }

    $databaseState = Invoke-LocalSql 'master' @'
SELECT CASE WHEN DB_ID(N'KartyLeczenia') IS NULL THEN 0
            WHEN CONVERT(nvarchar(32), DATABASEPROPERTYEX(N'KartyLeczenia', 'Status')) = N'ONLINE'
             AND CONVERT(nvarchar(32), DATABASEPROPERTYEX(N'KartyLeczenia', 'Updateability')) = N'READ_WRITE' THEN 1
            ELSE 2 END;
'@
    if ($databaseState -eq 2) { throw 'Istniejąca baza KartyLeczenia musi być dostępna do odczytu i zapisu.' }
    if ($databaseState -eq 1) {
        $schemaState = Invoke-LocalSql 'KartyLeczenia' @'
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE is_ms_shipped = 0)
    SELECT 0;
ELSE IF OBJECT_ID(N'dbo.treatment_cards', N'U') IS NOT NULL
    AND OBJECT_ID(N'dbo.animal_descriptions', N'U') IS NOT NULL
    AND NOT EXISTS (
        SELECT 1 FROM (VALUES
            (N'dbo.treatment_cards', N'id', N'uniqueidentifier'),
            (N'dbo.treatment_cards', N'archive_order', N'bigint'),
            (N'dbo.treatment_cards', N'source_scope', N'nvarchar'),
            (N'dbo.treatment_cards', N'payer_id', N'int'),
            (N'dbo.treatment_cards', N'client', N'nvarchar'),
            (N'dbo.treatment_cards', N'document', N'nvarchar'),
            (N'dbo.treatment_cards', N'treatment_date', N'date'),
            (N'dbo.treatment_cards', N'search_text', N'nvarchar'),
            (N'dbo.treatment_cards', N'card_json', N'nvarchar'),
            (N'dbo.treatment_cards', N'print_json', N'nvarchar'),
            (N'dbo.animal_descriptions', N'description_key', N'binary'),
            (N'dbo.animal_descriptions', N'source_scope', N'nvarchar'),
            (N'dbo.animal_descriptions', N'payer_id', N'int'),
            (N'dbo.animal_descriptions', N'description', N'nvarchar'),
            (N'dbo.animal_descriptions', N'last_used', N'bigint')
        ) expected(table_name, column_name, type_name)
        WHERE NOT EXISTS (
            SELECT 1 FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(expected.table_name, N'U')
                AND c.name = expected.column_name AND TYPE_NAME(c.system_type_id) = expected.type_name
                AND (expected.type_name <> N'nvarchar' OR c.max_length = -1)
                AND (expected.column_name <> N'description_key' OR c.max_length = 32)
                AND (expected.column_name <> N'archive_order' OR c.is_identity = 1)
        )
    )
    AND EXISTS (
        SELECT 1 FROM sys.key_constraints k
        JOIN sys.index_columns ic ON ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE k.parent_object_id = OBJECT_ID(N'dbo.treatment_cards') AND k.type = N'PK' AND c.name = N'id'
          AND ic.key_ordinal = 1
    )
    AND EXISTS (
        SELECT 1 FROM sys.key_constraints k
        JOIN sys.index_columns ic ON ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE k.parent_object_id = OBJECT_ID(N'dbo.animal_descriptions') AND k.type = N'PK' AND c.name = N'description_key'
          AND ic.key_ordinal = 1
    )
    SELECT 1;
ELSE SELECT 2;
'@
        if ($schemaState -eq 2) { throw 'Istniejąca baza KartyLeczenia ma inną strukturę. Nie została zmieniona.' }
    }

    $windowsOnly = (Invoke-LocalSql 'master' "SELECT CONVERT(int, SERVERPROPERTY('IsIntegratedSecurityOnly'));") -eq 1
    $useWindows = $windowsOnly
    if (!$windowsOnly) {
        $method = Show-ArchiveDialog -Title 'Logowanie do archiwum' -Message 'Wybierz sposób logowania na stanowiskach.' -Choices @(
            'Login i hasło SQL', 'Konto lub grupa Windows')
        $useWindows = $method -eq 1
    }

    $tcpPort = Invoke-LocalSql 'master' @'
SELECT MIN(port)
FROM sys.dm_tcp_listener_states
WHERE type_desc = N'TSQL' AND state_desc = N'ONLINE' AND port > 0
    AND ip_address NOT IN (N'127.0.0.1', N'::1');
'@
    $address = if ($tcpPort -and $tcpPort -isnot [DBNull]) {
        "$env:COMPUTERNAME,$tcpPort"
    } elseif ($instance -eq 'MSSQLSERVER') {
        "$env:COMPUTERNAME,1433"
    } else {
        "$env:COMPUTERNAME\$instance"
    }
    if (!$tcpPort -or $tcpPort -is [DBNull]) {
        Show-ArchiveDialog -Title 'Dostęp sieciowy do SQL' -Notice -Message (
            'Instancja nie nasłuchuje na porcie TCP w sieci. Administrator musi skonfigurować dostęp dla stanowisk.')
    }

    $account = $login
    if ($useWindows) {
        $account = (Show-ArchiveDialog -Title 'Konto lub grupa Windows' -TextInput -Message (
            'Wpisz konto lub grupę Windows w formacie DOMENA\nazwa. Użyj konta dostępnego na stanowiskach.')).Trim()
        if (!$account -or $account.Length -gt 128 -or $account -notmatch '^[^\\]+\\[^\\]+$') {
            throw 'Podaj nazwę konta lub grupy w formacie DOMENA\nazwa.'
        }
        try {
            $ntAccount = New-Object Security.Principal.NTAccount $account
            $null = $ntAccount.Translate([Security.Principal.SecurityIdentifier])
        }
        catch { throw 'Windows nie rozpoznaje wybranego konta lub grupy.' }
    }

    Show-ArchiveDialog -Title 'Potwierdź konfigurację bazy' -Message (
        "Utworzyć lub skonfigurować bazę KartyLeczenia na instancji $instance ($productVersion) dla konta $account?")
    Invoke-LocalSql 'master' "IF DB_ID(N'KartyLeczenia') IS NULL EXEC(N'CREATE DATABASE [KartyLeczenia]');" | Out-Null
    $schema = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Setup-SharedArchive.sql') -Raw -Encoding UTF8
    Invoke-LocalSql 'KartyLeczenia' $schema | Out-Null

    $literal = $account.Replace("'", "''")
    $identifier = '[' + $account.Replace(']', ']]') + ']'
    if ($useWindows) {
        Invoke-LocalSql 'KartyLeczenia' @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF SUSER_ID(N'$literal') IS NULL CREATE LOGIN $identifier FROM WINDOWS;
IF USER_ID(N'$literal') IS NULL CREATE USER $identifier FOR LOGIN $identifier;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$literal' AND sid = SUSER_SID(N'$literal'))
    THROW 50002, N'Konto bazy jest już przypisane do innego loginu.', 1;
IF ISNULL(IS_ROLEMEMBER(N'KartyLeczeniaOperator', N'$literal'), 0) <> 1
    ALTER ROLE [KartyLeczeniaOperator] ADD MEMBER $identifier;
COMMIT;
"@ | Out-Null
    } else {
        $existingLogin = Invoke-LocalSql 'master' "SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$login';"
        if ($existingLogin -eq 1) {
            $mapped = Invoke-LocalSql 'KartyLeczenia' @"
SELECT COUNT(*) FROM sys.database_role_members m
JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.sql_logins s ON s.sid = u.sid
WHERE u.type = N'S' AND r.name = N'KartyLeczeniaOperator'
    AND s.name = N'$login' AND s.is_disabled = 0;
"@
            if ($mapped -ne 1) { throw 'Istniejący login SQL nie jest aktywnym kontem tego archiwum. Wybierz konto Windows lub skonfiguruj login z administratorem.' }
        } else {
            Initialize-ArchiveSqlLogin
        }
    }
    Set-Content -LiteralPath (Join-Path $PSScriptRoot 'archive-server-result.txt') -Value $address -Encoding ASCII
    $methodName = if ($useWindows) { 'Windows' } else { 'SQL' }
    Show-ArchiveDialog -Title 'Archiwum skonfigurowane' -Notice -Message (
        "Adres dla stanowisk: $address. Tryb logowania: $methodName.")
}

function Initialize-Archive {
    $processor = Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1
    if (!$processor) { throw 'Nie udało się odczytać architektury procesora z Windows.' }
    if ($processor.Architecture -ne 9 -or ![Environment]::Is64BitOperatingSystem) {
        throw "Instancja KLARCHIVE wymaga Windows x64 na procesorze Intel lub AMD. Kod architektury procesora: $($processor.Architecture)."
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
        Initialize-ArchiveSqlLogin
    }
    Invoke-LocalSql 'KartyLeczenia' @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @member sysname;
SELECT @member = name FROM sys.database_principals
WHERE sid = SUSER_SID(N'$login') AND type = N'S' AND principal_id > 4;
IF @member IS NULL
BEGIN
    EXEC(N'CREATE USER [$databaseUser] FOR LOGIN [$login];');
    SET @member = N'$databaseUser';
END;
IF ISNULL(IS_ROLEMEMBER(N'KartyLeczeniaOperator', @member), 0) <> 1
BEGIN
    DECLARE @grant nvarchar(512) = N'ALTER ROLE [KartyLeczeniaOperator] ADD MEMBER ' + QUOTENAME(@member);
    EXEC(@grant);
END;
COMMIT;
"@ | Out-Null

    $rule = 'KartyLeczenia-SQL-Archive'
    if (!(Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name $rule -DisplayName 'Karty leczenia zwierząt — SQL archiwum' `
            -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -RemoteAddress LocalSubnet `
            -Profile Domain,Private -Service $serviceName | Out-Null
    }
}

try {
    if ($Mode -ne 'WebView2' -and [Environment]::Is64BitOperatingSystem -and ![Environment]::Is64BitProcess) {
        # Use the native registry view and SQL client on WOW64.
        $nativePowerShell = Join-Path $env:SystemRoot 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
        if (!(Test-Path -LiteralPath $nativePowerShell)) {
            throw 'Nie udało się znaleźć 64-bitowego programu Windows PowerShell.'
        }
        & $nativePowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Mode $Mode
        exit $LASTEXITCODE
    }
    if ($Mode -eq 'WebView2') {
        Assert-MicrosoftSignature $WebView2Package
        $result = Start-Process -FilePath $WebView2Package -ArgumentList '/silent /install' -Wait -PassThru
        exit $result.ExitCode
    }
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
    if ($Mode -eq 'Existing') { Initialize-ExistingArchive }
    else { Initialize-Archive }
}
catch {
    if ($Mode -ne 'WebView2') {
        $detail = $_.Exception.Message
        try {
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            Show-ArchiveDialog -Title 'Błąd konfiguracji SQL' -Message $detail -Notice
        } catch { }
    }
    exit 1
}
