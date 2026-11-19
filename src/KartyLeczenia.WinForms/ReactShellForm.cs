using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KartyLeczenia.Windows;

/// <summary>
/// First incremental React desktop host. Existing SQL/C# services own all
/// privileged operations; the browser only requests a small explicit API.
/// The WinForms application remains available for features not migrated yet.
/// </summary>
internal sealed class ReactShellForm : Form
{
    const string WebOrigin = "https://kartyleczenia.local/";
    readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    readonly DocumentService documents = new();
    readonly ConnectionSettingsStore connections = new();
    CancellationTokenSource? work;

    internal ReactShellForm()
    {
        Text = "KartyLeczenia — React (wersja testowa)";
        Size = new Size(1380, 850);
        MinimumSize = new Size(890, 580);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(browser);
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => work?.Cancel();
    }

    async Task InitializeAsync()
    {
        try
        {
            var dist = Path.Combine(AppContext.BaseDirectory, "react-ui");
            if (!File.Exists(Path.Combine(dist, "index.html")))
                throw new DirectoryNotFoundException(
                    "Brak zbudowanego interfejsu React. Uruchom npm install i npm run build " +
                    "w ui/react, a następnie ponownie skompiluj/publikuj WinForms.");
            string profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KartyLeczenia", "ReactWebView2");
            Directory.CreateDirectory(profile);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (IsDisposed) return;
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.IsWebMessageEnabled = true;
            browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            browser.CoreWebView2.SetVirtualHostNameToFolderMapping("kartyleczenia.local",
                dist, CoreWebView2HostResourceAccessKind.DenyCors);
            browser.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith(WebOrigin, StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };
            browser.CoreWebView2.WebMessageReceived += OnBrowserMessage;
            browser.CoreWebView2.Navigate(WebOrigin + "index.html");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.Initialize", ex);
            MessageBox.Show(this, ex.Message, "ReactKL — uruchomienie",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    async void OnBrowserMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(WebOrigin, StringComparison.OrdinalIgnoreCase)) return;
        string id = "";
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("id", out var idProperty) ||
                idProperty.ValueKind != JsonValueKind.String) return;
            id = idProperty.GetString() ?? "";
            if (id.Length is < 1 or > 80) return;
            string action = root.TryGetProperty("action", out var actionValue) &&
                actionValue.ValueKind == JsonValueKind.String ? actionValue.GetString() ?? "" : "";

            if (action == "openClassic")
            {
                using var legacy = new MainForm(false, InterfaceScale.Load());
                Hide();
                try { legacy.ShowDialog(this); }
                finally { if (!IsDisposed) Show(); }
                Reply(id, new { opened = true });
                return;
            }

            var options = connections.Load().Options
                ?? throw new InvalidOperationException(
                    "Brak zapisanego połączenia z Subiektem. Skonfiguruj je w dotychczasowym programie WinForms.");
            work?.Cancel();
            work?.Dispose();
            work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = work.Token;

            if (action == "searchDocuments")
            {
                string query = String(root, "query", 120);
                string type = String(root, "kind", 20);
                var kind = type switch
                {
                    "invoices" => DocumentSearchKinds.Invoices,
                    "receipts" => DocumentSearchKinds.Receipts,
                    "warehouse" => DocumentSearchKinds.WarehouseReleases,
                    "all" => DocumentSearchKinds.All,
                    _ => throw new ArgumentException("Nieznany rodzaj dokumentów.")
                };
                string period = String(root, "period", 12);
                var today = DateTime.Today;
                (DateTime? from, DateTime? to) = period switch
                {
                    "today" => (today, today.AddDays(1)),
                    "week" => (today.AddDays(-6), today.AddDays(1)),
                    "month" => (today.AddDays(-29), today.AddDays(1)),
                    "all" => (null, null),
                    _ => throw new ArgumentException("Nieznany zakres dat.")
                };
                var found = await documents.SearchAsync(options, query, token,
                    kind, from, to, limit: 201, allowEmpty: true);
                if (token.IsCancellationRequested || IsDisposed) return;
                Reply(id, new
                {
                    items = found.Take(200).Select(row => new
                    {
                        row.Id, row.Number, row.Date, row.ShortPayer, row.Payer,
                        row.Gross, row.Net, row.Currency, row.RelatedText
                    }).ToArray(),
                    truncated = found.Count > 200
                });
            }
            else if (action == "readDocument")
            {
                string number = String(root, "number", 120);
                int documentId = root.TryGetProperty("documentId", out var rawId) &&
                    rawId.TryGetInt32(out int parsed) ? parsed : 0;
                if (documentId <= 0 || string.IsNullOrWhiteSpace(number))
                    throw new ArgumentException("Wybierz dokument z listy.");
                var card = await documents.ReadAsync(options, number, 0, token, documentId);
                if (token.IsCancellationRequested || IsDisposed) return;
                Reply(id, new
                {
                    card.Number, card.Payer, card.Recipient,
                    rows = card.Rows.Select(row => new
                    {
                        product = row.Get("Produkt"),
                        quantity = row.Get("Ilość wydana"),
                        batch = row.Get("Seria"),
                        species = row.Get("Gatunek"),
                        route = row.Get("Droga podania")
                    }).ToArray()
                });
            }
            else throw new ArgumentException("Nieobsługiwane polecenie interfejsu.");
        }
        catch (OperationCanceledException) { /* Superseded by a newer query. */ }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.Message", ex);
            Reply(id, null, SubiektConnectionService.FriendlyError(ex));
        }
    }

    static string String(JsonElement root, string key, int maxLength)
    {
        if (!root.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        if (text.Length > maxLength) throw new ArgumentException("Zbyt długa wartość filtra.");
        return text.Trim();
    }

    void Reply(string id, object? data, string? error = null)
    {
        if (IsDisposed || browser.CoreWebView2 is null || string.IsNullOrEmpty(id)) return;
        browser.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(new { id, ok = error is null, data, error }));
    }
}
