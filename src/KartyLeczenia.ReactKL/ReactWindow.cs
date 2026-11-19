using System.Text.Json;
using KartyLeczenia;
using Microsoft.Data.SqlClient;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// React is the only user-facing application. WinForms supplies only the native
/// top-level window required to host WebView2. No legacy forms or AntdUI types.
/// </summary>
internal sealed class ReactWindow : Form
{
    private const string Origin = "https://kartyleczenia.local/";
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly ConnectionSettingsStore subiekt = new();
    private readonly ConnectionSettingsStore archive = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "archive-connection.json"));
    private readonly DocumentService documents = new();
    private readonly CardArchiveService cards;

    private readonly Dictionary<string, CancellationTokenSource> active = [];
    private readonly Dictionary<string, DocumentData> drafts = [];

    internal ReactWindow()
    {
        cards = new CardArchiveService(archive);
        this.Text = "KartyLeczenia — ReactKL";
        MinimumSize = new Size(900, 600);
        Size = new Size(1400, 850);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(web);
        Shown += async (_, _) => await StartAsync();
        FormClosing += (_, _) =>
        {
            foreach (var token in active.Values) token.Cancel();
        };
    }

    private async Task StartAsync()
    {
        try
        {
            string dist = Path.Combine(AppContext.BaseDirectory, "react-ui");
            if (!File.Exists(Path.Combine(dist, "index.html")))
                throw new DirectoryNotFoundException("Brak interfejsu React w folderze react-ui. Zbuduj frontend przed publikacją.");
            string profile = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "ReactWebView2");
            Directory.CreateDirectory(profile);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (IsDisposed) return;
            await web.EnsureCoreWebView2Async(env);
            var browser = web.CoreWebView2;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.IsWebMessageEnabled = true;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.Settings.IsPasswordAutosaveEnabled = false;
            browser.Settings.IsGeneralAutofillEnabled = false;
            browser.SetVirtualHostNameToFolderMapping("kartyleczenia.local", dist,
                CoreWebView2HostResourceAccessKind.DenyCors);
            browser.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            browser.NewWindowRequested += (_, e) => e.Handled = true;
            browser.WebMessageReceived += Message;
            browser.Navigate(Origin + "index.html");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.Start", ex);
            MessageBox.Show(this, ex.Message, "ReactKL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void Message(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!string.Equals(new Uri(e.Source).Host, "kartyleczenia.local", StringComparison.OrdinalIgnoreCase)
            || !e.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
        string requestId = "";
        CancellationTokenSource? cancel = null;
        try
        {
            using var msg = JsonDocument.Parse(e.WebMessageAsJson);
            var root = msg.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            requestId = Text(root, "id", 80);
            string action = Text(root, "action", 40);
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(action)) return;
            cancel = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            active[requestId] = cancel;
            object result = await Handle(action, root, cancel.Token);
            if (!cancel.IsCancellationRequested) Send(requestId, result);
        }
        catch (OperationCanceledException) { Send(requestId, null, "Operacja została anulowana."); }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.Message", ex);
            Send(requestId, null, SubiektConnectionService.FriendlyError(ex));
        }
        finally
        {
            if (active.Remove(requestId, out var running)) running.Dispose();
        }
    }

    private async Task<object> Handle(string action, JsonElement root, CancellationToken token)
    {
        if (action == "getClinic")
        {
            var clinic = ClinicSettingsStore.Load();
            return new {clinic.Name, clinic.Address, clinic.Doctor, clinic.Phone};
        }
        if (action == "saveClinic")
        {
            var clinic = new ClinicSettings(Text(root, "name", 200),
                Text(root, "address", 500), Text(root, "doctor", 200))
            { Phone = Text(root, "phone", 100) };
            ClinicSettingsStore.Save(clinic);
            return new { saved = true };
        }
        if (action == "getArchiveSettings")
        {
            var setting = archive.Load().Options;
            return new
            {
                configured = setting is not null,
                server = setting?.Server ?? "",
                windowsAuthentication = setting?.WindowsAuthentication ?? true,
                userName = setting?.UserName ?? "",
                trustCertificate = setting?.TrustCertificate ?? false
            };
        }
        if (action == "saveArchiveSettings")
        {
            string server = Text(root, "server", 200);
            string userName = Text(root, "userName", 200);
            bool windows = Flag(root, "windowsAuthentication");
            string password = Text(root, "password", 200);
            var current = archive.Load().Options;
            if (!windows && password.Length == 0 && current?.Server == server &&
                current.UserName == userName) password = current.Password;
            var setting = new ConnectionOptions(server, "KartyLeczenia", windows,
                userName, password, Flag(root, "trustCertificate"));
            await using var db = new SqlConnection(new SqlConnectionStringBuilder(
                setting.BuildConnectionString("KartyLeczenia"))
                { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
            await db.OpenAsync(token);
            using var check = new SqlCommand("SELECT TOP (1) id FROM dbo.treatment_cards", db);
            _ = await check.ExecuteScalarAsync(token);
            archive.Save(setting, Flag(root, "rememberPassword"));
            return new { saved = true };
        }
        if (action == "getSettings")
        {
            var (saved, _, warning) = subiekt.Load();
            return new
            {
                configured = saved is not null,
                warning, server = saved?.Server ?? "", database = saved?.Database ?? "",
                windowsAuthentication = saved?.WindowsAuthentication ?? true,
                userName = saved?.UserName ?? "", trustCertificate = saved?.TrustCertificate ?? false,
                drugGroup = saved?.DrugGroup ?? "Leki", warehouseId = saved?.WarehouseId
            };
        }
        if (action == "saveSettings")
        {
            string server = Text(root, "server", 200);
            string database = Text(root, "database", 200);
            string username = Text(root, "userName", 200);
            string password = Text(root, "password", 200);
            bool windows = Flag(root, "windowsAuthentication");
            bool trust = Flag(root, "trustCertificate");
            int? warehouse = Int(root, "warehouseId");
            var previous = subiekt.Load().Options;
            var config = new ConnectionOptions(server, database, windows, username,
                password.Length == 0 && !windows && previous?.UserName == username &&
                previous?.Server == server ? previous.Password : password,
                trust, Text(root, "drugGroup", 80), warehouse);
            await new SubiektConnectionService().TestAsync(config, token);
            subiekt.Save(config, Flag(root, "rememberPassword"));
            return new { saved = true };
        }
        var options = subiekt.Load().Options ??
            throw new InvalidOperationException("Brak konfiguracji Subiekta. Uzupełnij ustawienia połączenia.");
        if (action == "searchDocuments")
        {
            string category = Text(root, "kind", 20);
            DocumentSearchKinds kind = category switch
            {
                "invoices" => DocumentSearchKinds.Invoices,
                "receipts" => DocumentSearchKinds.Receipts,
                "warehouse" => DocumentSearchKinds.WarehouseReleases,
                "all" => DocumentSearchKinds.All,
                _ => throw new ArgumentException("Nieznany rodzaj dokumentu.")
            };
            var today = DateTime.Today;
            (DateTime? from, DateTime? to) = Text(root, "period", 20) switch
            {
                "today" => (today, today.AddDays(1)),
                "week" => (today.AddDays(-6), today.AddDays(1)),
                "month" => (today.AddDays(-29), today.AddDays(1)),
                _ => ((DateTime?)null, (DateTime?)null)
            };
            var rows = await documents.SearchAsync(options, Text(root, "query", 120),
                token, kind, from, to, limit: 201, allowEmpty: true);
            return new { items = rows.Take(200).Select(x => new
                {x.Id, x.Number, x.Date, x.ShortPayer, x.Payer, x.Gross, x.Net,
                 x.Currency, x.RelatedText}).ToArray(), truncated = rows.Count > 200 };
        }
        if (action == "readDocument")
        {
            int id = Int(root, "documentId") ?? throw new ArgumentException("Brak numeru dokumentu.");
            string number = Text(root, "number", 120);
            var document = await documents.ReadAsync(options, number, 0, token, id);
            string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            drafts[key] = document;
            return new { draftId = key, document.Number, document.Payer, document.Recipient,
                rows = document.Rows.Select((r, index) => new {index, product = r.Get("Produkt"),
                    quantity = r.Get("Ilość wydana"), batch = r.Get("Seria"),
                    species = r.Get("Gatunek"), route = r.Get("Droga podania"),
                    dosing = r.Get("Dawkowanie i okres stosowania"), withdrawal = r.Get("Karencja"),
                    description = r.Get("Opis zwierząt / numer siedziby stada"),
                    animals = r.Get("Liczba zwierząt"), diagnosis = r.Get("Rozpoznanie"),
                    selected = r.Printed }).ToArray() };
        }
        if (action == "updateDraft")
        {
            string id = Text(root, "draftId", 30);
            int index = Int(root, "index") ?? -1;
            string field = Text(root, "field", 80);
            if (!drafts.TryGetValue(id, out var draft) || index < 0 || index >= draft.Rows.Count)
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            string[] allowed = ["Gatunek", "Droga podania", "Dawkowanie i okres stosowania",
                "Karencja", "Opis zwierząt / numer siedziby stada", "Liczba zwierząt",
                "Rozpoznanie", "Seria", "Drukuj"];
            if (!allowed.Contains(field, StringComparer.Ordinal)) throw new ArgumentException("Pole nie jest edytowalne.");
            string value = field == "Drukuj" ? Flag(root, "checked").ToString() : Text(root, "value", 3000);
            draft.Rows[index].Set(field, value);
            return new { updated = true };
        }
        if (action == "priceLevels")
            return await MedicineStockService.PriceLevels(options, token);
        if (action == "prices")
        {
            int level = Int(root, "level") ?? 1;
            return await MedicineStockService.Prices(options, level, token, net: Flag(root, "net"));
        }
        if (action == "expiries")
            return await MedicineStockService.Expiries(options, token);
        if (action == "saveDraft" || action == "previewDraft")
        {
            string draftId = Text(root, "draftId", 30);
            if (!drafts.TryGetValue(draftId, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument przed zapisaniem.");
            static DateTime RequiredDate(JsonElement element, string name)
            {
                string text = Text(element, name, 35);
                if (!DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException("Podaj datę i godzinę: " + name);
                return date;
            }
            var reported = RequiredDate(root, "reported");
            var performed = RequiredDate(root, "performed");
            string findings = Text(root, "findings", 2000);
            if (action == "previewDraft")
                return new { html = cards.RenderDraft(draft, reported, performed, findings) };
            return cards.Save(draft, reported, performed, findings, Flag(root, "confirmDuplicate"));
        }
        if (action == "renderSavedCard")
            return new { html = cards.RenderSaved(Text(root, "id", 40), Flag(root, "trash")) };
        if (action == "savedCards")
            return await ReadSavedCards(token, Flag(root, "trash"), Text(root, "query", 120));
        if (action == "savedCard")
            return await ReadSavedCard(token, Text(root, "id", 40), Flag(root, "trash"));
        if (action == "trashCard")
            return await TrashCard(token, Text(root, "id", 40), Flag(root, "trash"));
        throw new InvalidOperationException("Nieznana operacja interfejsu: " + action);
    }

    private async Task<object> ReadSavedCards(CancellationToken token, bool trash, string query)
    {
        await using var db = ArchiveDb();
        await db.OpenAsync(token);
        using var cmd = new SqlCommand("""
            SELECT TOP (500) id, treatment_date, client, document, card_year, card_sequence
            FROM dbo.treatment_cards
            WHERE ((@trash=0 AND deleted_at_utc IS NULL) OR (@trash=1 AND deleted_at_utc IS NOT NULL))
              AND (@query=N'' OR CHARINDEX(@query, search_text)>0)
            ORDER BY treatment_date DESC, archive_order DESC;
            """, db);
        cmd.Parameters.AddWithValue("@trash", trash);
        cmd.Parameters.AddWithValue("@query", query.ToUpperInvariant());
        var result = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string number = reader.IsDBNull(4) || reader.IsDBNull(5) ? "" :
                $"K/{reader.GetInt64(5)}/{reader.GetInt32(4):D4}";
            result.Add(new { id = reader.GetGuid(0).ToString("N"),
                date = reader.GetDateTime(1), client = reader.GetString(2),
                document = reader.GetString(3), number });
        }
        return result;
    }

    private async Task<object> ReadSavedCard(CancellationToken token, string id, bool trash)
    {
        await using var db = ArchiveDb();
        await db.OpenAsync(token);
        using var cmd = new SqlCommand("""
            SELECT card_json FROM dbo.treatment_cards WHERE id=@id
            AND ((@trash=0 AND deleted_at_utc IS NULL) OR (@trash=1 AND deleted_at_utc IS NOT NULL));
            """, db);
        cmd.Parameters.AddWithValue("@id", Guid.ParseExact(id, "N"));
        cmd.Parameters.AddWithValue("@trash", trash);
        string? json = (string?)await cmd.ExecuteScalarAsync(token);
        if (json is null) throw new InvalidOperationException("Nie znaleziono zapisanej karty.");
        return JsonSerializer.Deserialize<Card>(json) ??
            throw new InvalidDataException("Zapisana karta ma nieprawidłową zawartość.");
    }

    private async Task<object> TrashCard(CancellationToken token, string id, bool trash)
    {
        await using var db = ArchiveDb();
        await db.OpenAsync(token);
        using var cmd = new SqlCommand(trash
            ? "UPDATE dbo.treatment_cards SET deleted_at_utc=NULL WHERE id=@id AND deleted_at_utc IS NOT NULL"
            : "UPDATE dbo.treatment_cards SET deleted_at_utc=SYSUTCDATETIME() WHERE id=@id AND deleted_at_utc IS NULL", db);
        cmd.Parameters.AddWithValue("@id", Guid.ParseExact(id, "N"));
        return new { updated = await cmd.ExecuteNonQueryAsync(token) > 0 };
    }

    private SqlConnection ArchiveDb()
    {
        var saved = archive.Load().Options ??
            throw new InvalidOperationException("Archiwum SQL nie jest skonfigurowane na tym stanowisku.");
        var connectionString = new SqlConnectionStringBuilder(
            saved.BuildConnectionString("KartyLeczenia")) {ApplicationIntent = ApplicationIntent.ReadWrite};
        return new SqlConnection(connectionString.ConnectionString);
    }

    private static int? Int(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : null;
    }
    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        string text = value.GetString()?.Trim() ?? "";
        if (text.Length > maxLength) throw new ArgumentException("Przekroczono długość pola " + name);
        return text;
    }
    private void Send(string id, object? data, string? error = null)
    {
        if (IsDisposed || web.CoreWebView2 is null || id.Length == 0) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            id, ok = error is null, data, error
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
