using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Avalon;

internal sealed record SqlProfile(string Server = "", string Database = "", bool Windows = true,
    string Login = "", bool TrustCertificate = false)
{
    internal SqlConnection Connect(string password) => new(new SqlConnectionStringBuilder
    {
        DataSource = Server,
        InitialCatalog = Database,
        IntegratedSecurity = Windows,
        UserID = Windows ? "" : Login,
        Password = Windows ? "" : password,
        TrustServerCertificate = TrustCertificate,
        ConnectTimeout = 10,
        ApplicationName = "Karty leczenia Avalon"
    }.ConnectionString);
}

internal sealed class SqlConnections
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "Avalon", "connections.json");
    private Dictionary<string, SqlProfile> profiles;
    private readonly Dictionary<string, string> passwords = new();

    public SqlConnections()
    {
        try { profiles = JsonSerializer.Deserialize<Dictionary<string, SqlProfile>>(File.ReadAllText(SettingsFile)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { profiles = new(); }
    }

    public SqlConnection OpenArchiveConnection() => OpenConfigured("archive", "KartyLeczenia");
    public SqlConnection OpenSubiektConnection() => OpenConfigured("subiekt", "Subiekt GT");

    private SqlConnection OpenConfigured(string key, string name)
    {
        if (!profiles.TryGetValue(key, out var profile))
            throw new InvalidOperationException($"Skonfiguruj i przetestuj połączenie {name} w zakładce Ustawienia.");
        if (!profile.Windows && !passwords.ContainsKey(key))
            throw new InvalidOperationException("Wpisz hasło SQL w Ustawieniach i kliknij „Połącz i zapisz”.");
        return profile.Connect(passwords.GetValueOrDefault(key, ""));
    }

    public Control CreateEditor(string key, string title, string? fixedDatabase = null)
    {
        var saved = profiles.GetValueOrDefault(key) ?? new SqlProfile();
        var server = new TextBox { Text = saved.Server, Watermark = @"np. SERWER\INSTANCJA" };
        var database = new TextBox { Text = fixedDatabase ?? saved.Database, IsReadOnly = fixedDatabase is not null };
        var authentication = new ComboBox
        {
            ItemsSource = new[] { "Konto Windows", "Login i hasło SQL Server" },
            SelectedIndex = saved.Windows ? 0 : 1
        };
        var login = new TextBox { Text = saved.Login };
        var password = new TextBox { PasswordChar = '●', Text = passwords.GetValueOrDefault(key, "") };
        var credentials = new StackPanel { Spacing = 6, IsVisible = !saved.Windows,
            Children = { Field("Login SQL", login), Field("Hasło SQL (do zamknięcia programu)", password) } };
        authentication.SelectionChanged += (_, _) => credentials.IsVisible = authentication.SelectedIndex == 1;
        var trust = new CheckBox { Content = "Ufaj certyfikatowi serwera", IsChecked = saved.TrustCertificate };
        var status = new TextBlock { Text = saved.Server.Length > 0 ? "Zapisana konfiguracja" : "",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var connect = new Button { Content = "Połącz i zapisz" };
        connect.Click += async (_, _) =>
        {
            var profile = new SqlProfile(server.Text?.Trim() ?? "", database.Text?.Trim() ?? "",
                authentication.SelectedIndex == 0, login.Text?.Trim() ?? "", trust.IsChecked == true);
            string secret = password.Text ?? "";
            if (profile.Server.Length == 0 || profile.Database.Length == 0 ||
                (!profile.Windows && (profile.Login.Length == 0 || secret.Length == 0)))
            {
                status.Text = "Uzupełnij serwer, bazę i wymagane dane logowania.";
                return;
            }

            connect.IsEnabled = false;
            status.Text = "Sprawdzanie połączenia…";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var connection = profile.Connect(secret);
                await connection.OpenAsync(timeout.Token);

                var updated = new Dictionary<string, SqlProfile>(profiles) { [key] = profile };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                string temporary = SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(updated));
                    File.Move(temporary, SettingsFile, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                profiles = updated;
                passwords[key] = profile.Windows ? "" : secret;
                status.Text = $"Połączono z bazą {connection.Database}. Zapisano konfigurację.";
            }
            catch (SqlException ex) { status.Text = $"Połączenie nie powiodło się (SQL {ex.Number}). Sprawdź adres, uprawnienia i certyfikat."; }
            catch (OperationCanceledException) { status.Text = "Przekroczono czas połączenia."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { status.Text = "Połączenie działa, ale nie udało się zapisać konfiguracji."; }
            finally { connect.IsEnabled = true; }
        };

        return new StackPanel { Spacing = 8,
            Children = { new TextBlock { Text = title, FontSize = 19 },
                Field("Serwer SQL", server), Field("Baza danych", database),
                Field("Uwierzytelnianie", authentication), credentials, trust, connect, status } };
    }

    private static Control Field(string name, Control control) =>
        new StackPanel { Spacing = 4, Children = { new Label { Content = name, Target = control }, control } };
}
