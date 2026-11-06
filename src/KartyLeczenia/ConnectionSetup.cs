using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

public sealed record ConnectionOptions(string Server, string Database, bool WindowsAuthentication,
    string UserName, string Password, bool TrustCertificate, string DrugGroup = "Leki")
{
    public string BuildConnectionString(string? database = null)
    {
        if (string.IsNullOrWhiteSpace(Server)) throw new InvalidOperationException("Wpisz adres serwera SQL.");
        if (!WindowsAuthentication && string.IsNullOrWhiteSpace(UserName)) throw new InvalidOperationException("Wpisz login SQL lub wybierz logowanie Windows.");
        string catalog = database ?? Database;
        if (string.IsNullOrWhiteSpace(catalog)) throw new InvalidOperationException("Wybierz podmiot lub wpisz nazwę jego bazy.");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Server.Trim(), InitialCatalog = catalog.Trim(), IntegratedSecurity = WindowsAuthentication,
            TrustServerCertificate = TrustCertificate, Encrypt = true, ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 8, ApplicationName = "KartyLeczenia", PersistSecurityInfo = false
        };
        if (!WindowsAuthentication) { builder.UserID = UserName.Trim(); builder.Password = Password; }
        return builder.ConnectionString;
    }
    // Never use record-generated ToString(): it would expose the password in diagnostics.
    public override string ToString() => "Konfiguracja połączenia z Subiektem";
}

public sealed record CompanyDatabase(string Database, string Company)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Company) ? Database : $"{Company} — [{Database}]";
}
public sealed record DiscoveryResult(IReadOnlyList<CompanyDatabase> Companies, int Skipped);
public sealed record ConnectionTestResult(string Company, IReadOnlyList<string> DrugGroups);

public sealed class SubiektConnectionService
{
    static readonly string[] RequiredTables = ["dok__Dokument", "dok_Pozycja", "tw__Towar", "dok_MagRuch", "adr_Historia", "sl_GrupaTw"];

    public async Task<DiscoveryResult> DiscoverAsync(ConnectionOptions options, CancellationToken token)
    {
        var names = new List<string>();
        await using (var master = new SqlConnection(options.BuildConnectionString("master")))
        {
            await master.OpenAsync(token);
            using var command = new SqlCommand("SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name", master) { CommandTimeout = 8 };
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) names.Add(reader.GetString(0));
        }
        var companies = new List<CompanyDatabase>(); int skipped = 0;
        foreach (string name in names)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await using var db = new SqlConnection(options.BuildConnectionString(name));
                await db.OpenAsync(token);
                using var identify = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name IN ('pd__Podmiot','dok__Dokument','tw__Towar')", db) { CommandTimeout = 8 };
                if (Convert.ToInt32(await identify.ExecuteScalarAsync(token)) != 3) { skipped++; continue; }
                string company = "";
                try
                {
                    using var title = new SqlCommand("SELECT TOP (1) pd_LicNazwaFirmy FROM dbo.pd__Podmiot ORDER BY pd_Id", db) { CommandTimeout = 8 };
                    company = (await title.ExecuteScalarAsync(token))?.ToString() ?? "";
                }
                catch (SqlException ex) when (ex.Number is 229 or 207 or 208) { /* A readable company title is optional. */ }
                companies.Add(new(name, company));
            }
            catch (SqlException) { skipped++; }
        }
        return new(companies, skipped);
    }

    public async Task<ConnectionTestResult> TestAsync(ConnectionOptions options, CancellationToken token)
    {
        await using var db = new SqlConnection(options.BuildConnectionString());
        await db.OpenAsync(token);
        // SELECT TOP(0) tests actual SELECT rights, not just table visibility.
        foreach (string table in RequiredTables)
        {
            using var read = new SqlCommand($"SELECT TOP (0) * FROM dbo.[{table}]", db) { CommandTimeout = 8 };
            await using var reader = await read.ExecuteReaderAsync(token);
        }
        using (var writes = new SqlCommand("""
            SELECT CASE WHEN EXISTS (
              SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
              WHERE t.is_ms_shipped=0 AND (
                HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT','INSERT')=1 OR
                HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT','UPDATE')=1 OR
                HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT','DELETE')=1 OR
                HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT','ALTER')=1 OR
                HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT','CONTROL')=1))
              OR HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL')=1
              OR HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE')=1
              OR HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER ANY SCHEMA')=1
            THEN 1 ELSE 0 END
            """, db) { CommandTimeout = 8 })
        {
            if (Convert.ToInt32(await writes.ExecuteScalarAsync(token)) != 0)
                throw new InvalidOperationException("Połączenie działa, ale konto ma uprawnienia do zapisu lub zmiany struktury tej bazy. Użyj osobnego konta tylko do odczytu — nie konta sa ani administratora.");
        }
        var groups = new List<string>();
        using (var command = new SqlCommand("SELECT grt_Nazwa FROM dbo.sl_GrupaTw ORDER BY grt_Nazwa", db) { CommandTimeout = 8 })
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) if (!reader.IsDBNull(0)) groups.Add(reader.GetString(0).Trim());
        if (!groups.Contains(options.DrugGroup.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new DrugGroupNotFoundException(groups);
        string company = options.Database;
        try
        {
            using var title = new SqlCommand("SELECT TOP (1) pd_LicNazwaFirmy FROM dbo.pd__Podmiot ORDER BY pd_Id", db) { CommandTimeout = 8 };
            company = (await title.ExecuteScalarAsync(token))?.ToString() ?? options.Database;
        }
        catch (SqlException ex) when (ex.Number is 229 or 207 or 208) { }
        return new(company, groups);
    }

    public static string FriendlyError(Exception ex) => ex switch
    {
        SqlException sql when sql.Number == 18456 => "Serwer odrzucił logowanie. Sprawdź tryb Windows/SQL, login i hasło. Login SQL jest kontem serwera, nie operatorem Subiekta.",
        SqlException sql when sql.Number is 4060 or 916 => "Konto nie ma dostępu do wskazanej bazy. Wybierz inny podmiot lub sprawdź uprawnienia konta.",
        SqlException sql when sql.Number == 229 => "Brakuje uprawnień SELECT do wymaganych danych. Poproś administratora o dostęp tylko do odczytu.",
        SqlException sql when sql.Number is 207 or 208 => "Wybrana baza nie ma wymaganej struktury Subiekta GT. Wybierz właściwy podmiot.",
        SqlException sql when sql.Number == -2 => "Serwer nie odpowiedział w wyznaczonym czasie. Sprawdź adres, sieć i działanie SQL Server.",
        SqlException sql when sql.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || sql.Message.Contains("certyfikat", StringComparison.OrdinalIgnoreCase) => "Nie można zweryfikować certyfikatu SQL. Sprawdź certyfikat serwera; dla znanego serwera w swojej sieci możesz włączyć opcję zaufania certyfikatowi.",
        SqlException => "Nie udało się połączyć z SQL Server. Sprawdź adres i nazwę instancji lub port, dostęp sieciowy i ustawienia serwera.",
        OperationCanceledException => "Operacja anulowana.",
        _ => ex.Message
    };
}

public sealed class DrugGroupNotFoundException(IReadOnlyList<string> groups)
    : InvalidOperationException("Połączenie działa. Nie znaleziono wybranej grupy leków — wybierz ją z listy i ponów test.")
{
    public IReadOnlyList<string> Groups { get; } = groups;
}

public sealed record SavedConnection(string Server, string Database, bool WindowsAuthentication, string UserName,
    bool TrustCertificate, string DrugGroup, string? ProtectedPassword);

public sealed class ConnectionSettingsStore(string? filePath = null)
{
    readonly string path = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "connection.json");
    public (ConnectionOptions? Options, bool Remembered, string? Warning) Load()
    {
        if (!File.Exists(path)) return (null, false, null);
        try
        {
            var saved = JsonSerializer.Deserialize<SavedConnection>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            string password = ""; string? warning = null;
            if (!string.IsNullOrWhiteSpace(saved.ProtectedPassword))
            {
                try { password = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(saved.ProtectedPassword), null, DataProtectionScope.CurrentUser)); }
                catch (Exception ex) when (ex is CryptographicException or FormatException) { warning = "Nie można odczytać zapamiętanego hasła. Wpisz je ponownie i zapisz ustawienia."; }
            }
            return (new(saved.Server, saved.Database, saved.WindowsAuthentication, saved.UserName, password, saved.TrustCertificate, saved.DrugGroup ?? "Leki"), !string.IsNullOrWhiteSpace(saved.ProtectedPassword), warning);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        { return (null, false, "Nie udało się odczytać ustawień. Skonfiguruj połączenie ponownie i zapisz je."); }
    }
    public void Save(ConnectionOptions options, bool rememberPassword)
    {
        string? secret = rememberPassword && !options.WindowsAuthentication && options.Password.Length > 0
            ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(options.Password), null, DataProtectionScope.CurrentUser)) : null;
        var saved = new SavedConnection(options.Server.Trim(), options.Database.Trim(), options.WindowsAuthentication, options.UserName.Trim(), options.TrustCertificate, options.DrugGroup.Trim(), secret);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
