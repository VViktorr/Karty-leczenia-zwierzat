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
    private readonly DocumentListCache documentLists = new();
    // Short snapshots avoid the expensive delivery/stock query being repeated
    // when switching between price levels, netto/brutto, and expiry screens.
    private readonly TimedSqlCache<List<MedicineExpiry>> expiryLists = new(TimeSpan.FromSeconds(30));
    private readonly TimedSqlCache<List<MedicinePrice>> priceLists = new(TimeSpan.FromSeconds(30));
    private readonly TimedSqlCache<MedicinePriceLevel[]> priceLevelLists = new(TimeSpan.FromMinutes(10));
    // A single catalogue SELECT per minute is shared by all price levels.
    private readonly TimedSqlCache<PriceCatalogueSnapshot> priceCatalogueLists =
        new(TimeSpan.FromMinutes(1));
    private readonly PriceCatalogueService priceCatalogueService;
    private readonly CardArchiveService cards;
    private readonly SavedCardRevisionService savedCardEditor;
    private readonly SharedClinicService clinicService;
    private readonly TreatmentChoiceService treatmentChoices;
    private readonly AnimalDescriptionService animalDescriptions;
    private byte[]? stagedClinicStamp;
    private byte[]? stagedDoctorStamp;
    private readonly SqlBackupService backups;
    private readonly SqlRestoreService restores;
    private readonly ReactUpdateService updates;
    private ReactUpdateInfo? checkedUpdate;
    private string? startupPublicationError;
    private string? selectedRestoreFile;
    private bool restoreRunning;
    private readonly CatalogueService catalogue;
    private readonly Dictionary<string, byte[]> pendingCataloguePdfs = new(StringComparer.Ordinal);
    private bool backupRunning;

    private readonly Dictionary<string, CancellationTokenSource> active = [];
    private readonly Dictionary<string, DocumentData> drafts = [];

    internal ReactWindow()
    {
        cards = new CardArchiveService(archive);
        savedCardEditor = new SavedCardRevisionService(archive);
        clinicService = new SharedClinicService(archive);
        treatmentChoices = new TreatmentChoiceService(archive);
        animalDescriptions = new AnimalDescriptionService(archive);
        backups = new SqlBackupService(archive);
        restores = new SqlRestoreService(archive);
        updates = new ReactUpdateService(archive);
        catalogue = new CatalogueService(archive);
        priceCatalogueService = new PriceCatalogueService(archive);
        base.Text = "KartyLeczenia — ReactKL";
        MinimumSize = new Size(900, 600);
        Size = new Size(1400, 850);
        AutoScaleMode = AutoScaleMode.Dpi;
        ReactWindowPlacement.Restore(this);
        Controls.Add(web);
        FormClosed += (_, _) => ReactWindowPlacement.Save(this);
        Shown += async (_, _) =>
        {
            await StartAsync();
            await PublishServerUpdateOnStartup();
        };
        FormClosing += (_, e) =>
        {
            if (backupRunning || restoreRunning)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Trwa operacja konserwacyjna SQL. Zamknięcie programu jest chwilowo zablokowane.",
                    "KartyLeczenia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var token in active.Values) token.Cancel();
        };
    }

    private void LoadInstallationConnection()
    {
        // A fresh client installation contains only archive-installation.ini.
        // Keep existing DPAPI settings from the previous installation untouched.
        if (archive.Load().Options is not null) return;
        string? sqlServer = ReactInstallUpdates.Server;
        if (string.IsNullOrWhiteSpace(sqlServer)) return;
        try
        {
            archive.Save(new ConnectionOptions(sqlServer, "KartyLeczenia",
                WindowsAuthentication: true, UserName: "", Password: "",
                TrustCertificate: true), rememberPassword: false);
        }
        catch (Exception ex) { CrashLog.Write("ReactKL.InitialArchiveConnection", ex); }
    }

    private async Task PublishServerUpdateOnStartup()
    {
        if (!ReactInstallUpdates.HasPendingPublication) return;
        try
        {
            await ReactInstallUpdates.PublishPending();
            startupPublicationError = null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.PublishStartup", ex);
            startupPublicationError = ex.Message;
            if (!IsDisposed)
                MessageBox.Show(this,
                    "Instalator ReactKL nie został opublikowany w SQL:\n" + ex.Message +
                    "\nMożesz ponowić publikację w Ustawieniach → Aktualizacje.",
                    "Aktualizacja KartyLeczenia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task StartAsync()
    {
        try
        {
            LoadInstallationConnection();
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
            if (action == "cancelRequest")
            {
                string cancelledId = Text(root, "requestId", 80);
                if (active.TryGetValue(cancelledId, out var runningRequest))
                    runningRequest.Cancel();
                return;
            }
            cancel = new CancellationTokenSource(action is "backupDatabase" or "restoreDatabase"
                ? TimeSpan.FromMinutes(45) : action == "installUpdate"
                    ? TimeSpan.FromMinutes(15) : action == "purgeTrashedCard"
                        ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(45));
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
        if (action == "getPricePrintSettings")
            return PricePrintOptionsStore.Load();
        if (action == "savePricePrintSettings")
        {
            if (!root.TryGetProperty("settings", out var value) ||
                value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Brak ustawień wydruku.");
            var settings = JsonSerializer.Deserialize<PricePrintOptions>(
                value.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new ArgumentException("Nieprawidłowe ustawienia wydruku.");
            await Task.Run(() => PricePrintOptionsStore.Save(settings), token);
            return new { saved = true };
        }
        if (action == "searchCatalogue")
        {
            string search = Text(root, "query", 120);
            return await Task.Run(() => catalogue.Search(search), token);
        }
        if (action == "readCatalogueProduct")
        {
            string symbol = Text(root, "symbol", 128);
            return await Task.Run(() => catalogue.Read(symbol), token);
        }
        if (action == "saveCatalogueProduct")
        {
            if (!root.TryGetProperty("product", out var data) ||
                data.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Brak kartoteki do zapisania.");
            var product = JsonSerializer.Deserialize<CatalogueProductModel>(
                data.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new ArgumentException("Niepoprawna kartoteka.");
            // Only staged PDF documents actually referenced in the edited product
            // may be saved. Other staged documents stay in memory, not in SQL.
            var selected = product.Variants.Select(x => x.Pdf).Distinct(StringComparer.Ordinal);
            var pdfs = selected.Where(pendingCataloguePdfs.ContainsKey)
                .ToDictionary(x => x, x => pendingCataloguePdfs[x], StringComparer.Ordinal);
            await Task.Run(() => catalogue.Save(product, pdfs), token);
            priceCatalogueLists.Clear();
            priceLists.Clear();
            foreach (var key in pdfs.Keys) pendingCataloguePdfs.Remove(key);
            return new { saved = true };
        }
        if (action == "chooseCataloguePdf")
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Wybierz urzędową ChPL (PDF)",
                Filter = "Dokument PDF (*.pdf)|*.pdf",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return new { cancelled = true };
            var info = new FileInfo(dialog.FileName);
            if (info.Length is < 5 or > 26214400)
                throw new InvalidOperationException("PDF musi mieć od 5 bajtów do 25 MB.");
            if (pendingCataloguePdfs.Count >= 10)
                throw new InvalidOperationException(
                    "Dołączono zbyt wiele PDF w jednej sesji. Zapisz kartotekę przed kolejnym wyborem.");
            byte[] bytes = await File.ReadAllBytesAsync(dialog.FileName, token);
            if (!bytes.AsSpan().StartsWith("%PDF-"u8))
                throw new InvalidDataException("Wybrany plik nie jest poprawnym PDF.");
            string path = "ChPL/" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(bytes)) + ".pdf";
            pendingCataloguePdfs[path] = bytes;
            return new { cancelled = false, path, name = info.Name, size = info.Length };
        }
        if (action == "analyseCataloguePdf")
        {
            string pdfPath = Text(root, "path", 450);
            byte[] bytes = pendingCataloguePdfs.TryGetValue(pdfPath, out var staged)
                ? staged : await Task.Run(() => catalogue.ReadPdf(pdfPath), token);
            return await Task.Run(() =>
            {
                string temp = Path.Combine(Path.GetTempPath(),
                    "ReactKL-ChPL-" + Guid.NewGuid().ToString("N") + ".pdf");
                try
                {
                    File.WriteAllBytes(temp, bytes);
                    var proposal = ChplPdfAssistant.Read(temp);
                    return new
                    {
                        variants = proposal.Variants.Select(v => v with
                        {
                            Route = AdministrationRouteLegend.Abbreviate(v.Route)
                        }).ToArray(),
                        evidence = proposal.Evidence,
                        warnings = proposal.Warnings
                    };
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { CrashLog.Write("ChPL.TempCleanup", ex); }
                }
            }, token);
        }
        if (action == "readCataloguePdf")
        {
            string path = Text(root, "path", 450);
            byte[] data = pendingCataloguePdfs.TryGetValue(path, out var pending)
                ? pending : await Task.Run(() => catalogue.ReadPdf(path), token);
            return new { base64 = Convert.ToBase64String(data), name = Path.GetFileName(path) };
        }
        if (action == "getUpdateStatus")
            return new
            {
                installed = ReactInstallUpdates.IsInstalled,
                isServer = ReactInstallUpdates.IsServer,
                pending = ReactInstallUpdates.HasPendingPublication,
                installedVersion = ReactInstallUpdates.InstalledVersion,
                server = ReactInstallUpdates.Server ?? "",
                publicationError = startupPublicationError
            };
        if (action == "publishPendingUpdate")
        {
            if (!ReactInstallUpdates.IsServer)
                throw new InvalidOperationException("Publikacja instalatora jest dostępna tylko na serwerze SQL.");
            await ReactInstallUpdates.PublishPending(token);
            startupPublicationError = null;
            return new { published = true, version = ReactInstallUpdates.InstalledVersion };
        }
        if (action == "checkUpdate")
        {
            if (!ReactInstallUpdates.IsInstalled)
                throw new InvalidOperationException(
                    "Sprawdzanie aktualizacji SQL jest dostępne po zainstalowaniu ReactKL. Paczka testowa nie aktualizuje programu.");
            checkedUpdate = await updates.Check(token);
            return new { release = checkedUpdate,
                currentVersion = ReactUpdateService.CurrentVersion };
        }
        if (action == "installUpdate")
        {
            if (!ReactInstallUpdates.IsInstalled)
                throw new InvalidOperationException("Najpierw zainstaluj ReactKL w standardowym katalogu.");
            if (backupRunning || restoreRunning)
                throw new InvalidOperationException(
                    "Aktualizacja może rozpocząć się dopiero po zakończeniu archiwizacji lub odtwarzania.");
            var release = checkedUpdate;
            if (release is null || !release.Newer)
                throw new InvalidOperationException(
                    "Najpierw sprawdź dostępność nowszej wersji ReactKL w SQL.");
            // Re-read SQL after the user's confirmation: a newer release may
            // have replaced the one they saw in the meantime.
            var current = await updates.Check(token);
            if (current is null || current.ReleaseId != release.ReleaseId ||
                !current.Newer || !string.Equals(current.Sha256,release.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Opublikowana aktualizacja zmieniła się. Sprawdź wersję ponownie.");
            string file = await updates.Download(current,token);
            ReactUpdateService.Start(file);
            // The installer is already running. Exit the old app so Inno can
            // replace its files without launching the old user interface.
            BeginInvoke(new Action(Close));
            return new { started = true, version = current.Version };
        }
        if (action == "chooseRestoreFile")
        {
            if (restoreRunning || backupRunning)
                throw new InvalidOperationException("Na stanowisku trwa operacja konserwacyjna SQL.");
            using var dialog = new OpenFileDialog
            {
                Title = "Wybierz pełną kopię bazy KartyLeczenia",
                Filter = "Kopia zapasowa SQL (*.bak)|*.bak",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return new { cancelled = true };
            selectedRestoreFile = dialog.FileName;
            var info = new FileInfo(dialog.FileName);
            return new { cancelled = false, fileName = info.Name,
                path = info.FullName, bytes = info.Length,
                modified = info.LastWriteTime };
        }
        if (action == "restoreDatabase")
        {
            if (restoreRunning || backupRunning)
                throw new InvalidOperationException("Na stanowisku trwa już archiwizacja lub odtwarzanie SQL.");
            if (selectedRestoreFile is null ||
                Text(root, "path", 1000) != selectedRestoreFile)
                throw new InvalidOperationException("Ponownie wybierz plik .bak w systemowym oknie Windows.");
            if (Text(root, "confirmation", 40) != "KartyLeczenia")
                throw new InvalidOperationException("Potwierdź nazwę bazy KartyLeczenia.");
            restoreRunning = true;
            try
            {
                if (!await WindowsHelloConsent.VerifyAsync(this,
                    "odtworzenie bazy KartyLeczenia (zastąpienie danych)"))
                    return new { cancelled = true };
                string request = Text(root,"id",80);
                var progress = new Progress<SqlBackupProgress>(value => SendProgress(request,value));
                var result = await Task.Run(() => restores.Run(selectedRestoreFile,progress));
                selectedRestoreFile = null;
                return new { cancelled = false, result.Database,result.SourcePath,
                    result.SafetyBackupPath,result.Message };
            }
            finally { restoreRunning = false; }
        }
        if (action == "getBackupSettings")
            return SqlBackupService.Load();
        if (action == "chooseArchiveFolder")
        {
            using var dialog = new FolderBrowserDialog
            { Description = "Wybierz folder archiwum SQL na pendrivie" };
            var existing = SqlBackupService.Load().DestinationFolder;
            if (Directory.Exists(existing)) dialog.InitialDirectory = existing;
            return new { folder = dialog.ShowDialog(this) == DialogResult.OK
                ? dialog.SelectedPath : "" };
        }
        if (action == "saveBackupSettings")
        {
            var settings = BackupSettings(root);
            SqlBackupService.Save(settings);
            return new {saved = true};
        }
        if (action == "backupDatabase")
        {
            if (backupRunning || restoreRunning)
                throw new InvalidOperationException("Na stanowisku trwa operacja konserwacyjna SQL.");
            var config = SqlBackupService.Load();
            if (!await WindowsHelloConsent.VerifyAsync(this,"wykonanie pełnej kopii SQL"))
                return new {cancelled = true};
            backupRunning = true;
            string messageId = Text(root,"id",80);
            try
            {
                var progress = new Progress<SqlBackupProgress>(value => SendProgress(messageId,value));
                var result = await Task.Run(() => backups.Run(config,progress),token);
                return new {cancelled = false, result.Path, result.SqlVerified, result.Message};
            }
            finally {backupRunning = false;}
        }
        if (action == "getClinic")
        {
            var current = await Task.Run(() => clinicService.Load(), token);
            stagedClinicStamp = null;
            stagedDoctorStamp = null;
            var clinic = current.Settings;
            return new
            {
                clinic.Name, clinic.Address, clinic.Doctor, clinic.Phone,
                clinic.UseClinicStamp, clinic.UseDoctorStamp,
                hasClinicStamp = clinic.ClinicStampPng is { Length: > 0 },
                hasDoctorStamp = clinic.DoctorStampPng is { Length: > 0 },
                current.Revision
            };
        }
        if (action == "chooseClinicStamp")
        {
            string kind = Text(root, "kind", 12);
            if (kind is not ("clinic" or "doctor"))
                throw new ArgumentException("Nieznany rodzaj pieczątki.");
            using var dialog = new OpenFileDialog
            {
                Title = kind == "clinic" ? "Wybierz pieczątkę lecznicy PNG" : "Wybierz pieczątkę lekarza PNG",
                Filter = "Obraz PNG (*.png)|*.png",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return new { cancelled = true };
            var info = new FileInfo(dialog.FileName);
            if (info.Length is < 8 or > 2097152)
                throw new InvalidDataException("Pieczątka musi mieć format PNG i maksymalnie 2 MB.");
            byte[] bytes = await File.ReadAllBytesAsync(dialog.FileName, token);
            SharedClinicService.ValidatePng(bytes, kind == "clinic" ? "lecznicy" : "lekarza");
            if (kind == "clinic") stagedClinicStamp = bytes;
            else stagedDoctorStamp = bytes;
            return new { cancelled = false, name = info.Name, size = bytes.Length };
        }
        if (action == "saveClinic")
        {
            string expectedRevision = Text(root, "revision", 100);
            if (expectedRevision.Length != 64)
                throw new InvalidOperationException("Najpierw odśwież dane lecznicy z SQL.");
            var previous = await Task.Run(() => clinicService.Load(), token);
            var old = previous.Settings;
            var updated = new ClinicSettings(Text(root, "name", 200),
                Text(root, "address", 500), Text(root, "doctor", 200))
            {
                Phone = Text(root, "phone", 100),
                UseClinicStamp = Flag(root, "useClinicStamp"),
                UseDoctorStamp = Flag(root, "useDoctorStamp"),
                ClinicStampPng = Flag(root, "removeClinicStamp") ? null : stagedClinicStamp ?? old.ClinicStampPng,
                DoctorStampPng = Flag(root, "removeDoctorStamp") ? null : stagedDoctorStamp ?? old.DoctorStampPng
            };
            var saved = await Task.Run(() => clinicService.Save(updated, expectedRevision), token);
            stagedClinicStamp = null;
            stagedDoctorStamp = null;
            return new
            {
                saved = true, saved.Revision,
                hasClinicStamp = updated.ClinicStampPng is { Length: > 0 },
                hasDoctorStamp = updated.DoctorStampPng is { Length: > 0 }
            };
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
            documentLists.Clear();
            expiryLists.Clear();
            priceLists.Clear();
            priceLevelLists.Clear();
            priceCatalogueLists.Clear();
            return new { saved = true };
        }
        // Archive browsing and trash management use the program database only.
        // They must remain available when Subiekt GT is temporarily offline.
        if (action == "renderSavedCard")
            return new { html = cards.RenderSaved(Text(root, "id", 40), Flag(root, "trash")) };
        if (action == "savedCards")
            return await ReadSavedCards(token, Flag(root, "trash"), Text(root, "query", 120));
        if (action == "savedCard")
            return await ReadSavedCard(token, Text(root, "id", 40), Flag(root, "trash"));
        if (action == "updateSavedCard")
        {
            string id = Text(root, "id", 40);
            string revision = Text(root, "revision", 64);
            if (!root.TryGetProperty("fields", out var editFields) ||
                editFields.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("rows", out var editRows) ||
                editRows.ValueKind != JsonValueKind.Array ||
                editRows.GetArrayLength() > 500)
                throw new ArgumentException("Wymagana pełna zawartość edytowanej karty.");
            var fields = JsonSerializer.Deserialize<Dictionary<string,string>>(
                editFields.GetRawText()) ??
                throw new ArgumentException("Nieprawidłowe dane nagłówka karty.");
            var rows = JsonSerializer.Deserialize<List<Dictionary<string,string>>>(
                editRows.GetRawText()) ??
                throw new ArgumentException("Nieprawidłowe pozycje karty.");
            // Archive access stays available if the separate Subiekt SQL source
            // is temporarily unavailable. Run the small guarded write off UI.
            return await Task.Run(() =>
                savedCardEditor.Update(id, revision, fields, rows), token);
        }
        if (action == "trashCard")
            return await TrashCard(token, Text(root, "id", 40), Flag(root, "trash"));
        if (action == "purgeTrashedCard")
        {
            string id = Text(root, "id", 40);
            string confirmation = Text(root, "confirmation", 160);
            if (!Guid.TryParseExact(id, "N", out var cardId) ||
                string.IsNullOrWhiteSpace(confirmation))
                throw new ArgumentException("Wybierz kartę z kosza i potwierdź jej numer.");
            if (!await WindowsHelloConsent.VerifyAsync(this,
                    "trwałe usunięcie jednej karty leczenia z kosza"))
                return new { deleted = false, cancelled = true };
            return await PurgeTrashedCard(token, cardId, confirmation);
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
            DateTime today = DateTime.Today;
            DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            DateTime month = new(today.Year, today.Month, 1);
            DateTime quarter = new(today.Year, 1 + 3 * ((today.Month - 1) / 3), 1);
            DateTime year = new(today.Year, 1, 1);
            static DateTime ParseDate(JsonElement element, string name)
            {
                string value = Text(element, name, 12);
                if (!DateTime.TryParseExact(value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException("Wybierz prawidłową datę: " + name);
                return date;
            }
            string period = Text(root, "period", 20);
            // Explicit target type makes nullable dates a valid tuple switch result.
            // A deconstruction target alone does not provide a switch-expression type.
            (DateTime? From, DateTime? To) periodBounds = period switch
            {
                "all" => (null, null),
                "today" => (today, today.AddDays(1)),
                "currentWeek" => (monday, monday.AddDays(7)),
                "currentMonth" => (month, month.AddMonths(1)),
                "currentQuarter" => (quarter, quarter.AddMonths(3)),
                "currentYear" => (year, year.AddYears(1)),
                "week" => (today.AddDays(-6), today.AddDays(1)),
                "month" => (today.AddDays(-29), today.AddDays(1)),
                "customMonth" => (new DateTime(ParseDate(root, "startDate").Year,
                    ParseDate(root, "startDate").Month, 1), null),
                "custom" => (ParseDate(root, "startDate"), ParseDate(root, "endDate").AddDays(1)),
                _ => throw new ArgumentException("Nieznany okres dokumentów.")
            };
            DateTime? from = periodBounds.From;
            DateTime? to = periodBounds.To;
            if (period == "customMonth" && from is DateTime customStart)
                to = customStart.AddMonths(1);
            if (from >= to)
                throw new ArgumentException("Data końcowa musi przypadać w dniu rozpoczęcia okresu lub później.");

            string query = Text(root, "query", 120);
            bool refresh = Flag(root, "refresh");
            string cacheKey = DocumentListCache.Key(options, kind, query, from, to);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await documentLists.GetAsync(cacheKey, refresh,
                async queryToken => await documents.SearchAsync(options, query, queryToken,
                    kind, from, to, limit: 501, allowEmpty: true), token);
            CrashLog.Write($"ReactKL.Documents.List kind={kind} count={result.Items.Count} cached={result.Cached} elapsedMs={watch.ElapsedMilliseconds}");
            return new
            {
                items = result.Items.Select(x => new
                {
                    x.Id, x.Number, x.Date, x.ShortPayer, x.Payer, x.Gross,
                    x.Net, x.Currency, x.RelatedText
                }).ToArray(),
                result.Truncated, result.Cached, result.LoadedAt
            };
        }
        if (action == "readDocument")
        {
            int id = Int(root, "documentId") ?? throw new ArgumentException("Brak numeru dokumentu.");
            string number = Text(root, "number", 120);
            var document = await documents.ReadAsync(options, number, 0, token, id);
            string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            drafts[key] = document;
            return new { draftId = key, document.Number, document.Payer, document.Recipient,
                hasPayerId = document.PayerId.HasValue,
                rows = document.Rows.Select((r, index) => new {index, product = r.Get("Produkt"),
                    replacement = r.Get("Zamiennik produktu"),
                    symbol = r.Get("Symbol Subiekta"), indication = r.Get("Wskazanie ChPL"),
                    quantity = r.Get("Ilość wydana"), batch = r.Get("Seria"),
                    species = r.Get("Gatunek"), route = r.Get("Droga podania"),
                    dosing = r.Get("Dawkowanie i okres stosowania"), withdrawal = r.Get("Karencja"),
                    description = r.Get("Opis zwierząt / numer siedziby stada"),
                    animals = r.Get("Liczba zwierząt"), diagnosis = r.Get("Rozpoznanie"),
                    section = r.Get("Sekcja (II lub III)"),
                    selected = r.Printed }).ToArray() };
        }
        if (action == "treatmentSuggestions" || action == "applyTreatmentSuggestions")
        {
            string draftId = Text(root, "draftId", 30);
            if (!drafts.TryGetValue(draftId, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument przed doborem wariantów.");
            if (draft.Rows.Count > 500)
                throw new InvalidOperationException(
                    "Dokument ma zbyt wiele pozycji do zbiorczych propozycji.");
            var variants = await Task.Run(() =>
                catalogue.VariantsForSymbols(draft.Rows.Select(row =>
                    row.Get("Symbol Subiekta"))), token);
            var proposals = draft.Rows.Select((row, index) =>
            {
                variants.TryGetValue(row.Get("Symbol Subiekta").Trim(), out var entries);
                return TreatmentAutofillPlanner.Propose(index, row,
                    entries ?? []);
            }).ToArray();
            if (action == "treatmentSuggestions")
                return new
                {
                    items = proposals,
                    compatibility = draft.Rows.Select((row, index) =>
                    {
                        variants.TryGetValue(row.Get("Symbol Subiekta").Trim(), out var entries);
                        return new
                        {
                            index,
                            groups = TreatmentSpeciesGroup.CompatibleGroups(
                                (entries ?? []).Select(variant => variant.Species))
                        };
                    }).ToArray()
                };

            if (!root.TryGetProperty("candidates", out var requested) ||
                requested.ValueKind != JsonValueKind.Array ||
                requested.GetArrayLength() > 300)
                throw new ArgumentException("Brak zatwierdzonych propozycji do zastosowania.");
            if (!root.TryGetProperty("rows", out var snapshot) ||
                snapshot.ValueKind != JsonValueKind.Array ||
                snapshot.GetArrayLength() != draft.Rows.Count)
                throw new InvalidOperationException(
                    "Stan edytora się zmienił. Odśwież propozycje katalogu.");

            var snapshots = snapshot.EnumerateArray().ToArray();
            var accepted = requested.EnumerateArray().ToArray();
            var seen = new HashSet<int>();
            var applied = new List<object>();
            var skipped = new List<string>();
            foreach (var request in accepted)
            {
                int index = Int(request, "index") ?? -1;
                int variantId = Int(request, "variantId") ?? -1;
                if (index < 0 || index >= draft.Rows.Count || !seen.Add(index))
                    throw new ArgumentException("Nieprawidłowe lub powtórzone indeksy propozycji.");
                var row = draft.Rows[index];
                var proposal = proposals[index];
                var current = snapshots[index];
                bool unchanged = Int(current, "index") == index &&
                    string.Equals(Text(current, "symbol", 128),
                        row.Get("Symbol Subiekta").Trim(), StringComparison.Ordinal) &&
                    string.Equals(Text(current, "species", 3000),
                        row.Get("Gatunek").Trim(), StringComparison.Ordinal) &&
                    string.Equals(Text(current, "route", 3000),
                        row.Get("Droga podania").Trim(), StringComparison.Ordinal) &&
                    string.Equals(Text(current, "dosing", 3000),
                        row.Get("Dawkowanie i okres stosowania").Trim(), StringComparison.Ordinal) &&
                    string.Equals(Text(current, "withdrawal", 3000),
                        row.Get("Karencja").Trim(), StringComparison.Ordinal) &&
                    Flag(current, "selected") == row.Printed;
                bool catalogueUnchanged = TreatmentAutofillPlanner.MatchesReviewed(
                    proposal,
                    Text(request,"species",3000), Text(request,"route",3000),
                    Text(request,"dosing",3000), Text(request,"withdrawal",3000),
                    Text(request,"source",3000), Text(request,"pdfPath",450),
                    Flag(request,"hasPdf"));
                if (!unchanged || !catalogueUnchanged || proposal.Status != "ready" ||
                    proposal.VariantId != variantId ||
                    !variants.TryGetValue(proposal.Symbol, out var matches))
                {
                    skipped.Add($"Pozycja {index + 1}: zmieniły się dane lub wariant wymaga ręcznej kontroli.");
                    continue;
                }
                var chosen = matches.SingleOrDefault(entry => entry.Id == variantId);
                if (chosen is null)
                {
                    skipped.Add($"Pozycja {index + 1}: wariant nie jest już dostępny.");
                    continue;
                }
                var value = TreatmentVariantApplier.Apply(row, chosen);
                applied.Add(new
                {
                    index, value.Species, value.Route, value.Dosing,
                    value.Withdrawal, value.Indication, value.Description
                });
            }
            return new { applied, skipped };
        }
        if (action == "readAnimalDescriptions" || action == "saveAnimalDescription")
        {
            string id = Text(root, "draftId", 30);
            if (!drafts.TryGetValue(id, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            if (action == "readAnimalDescriptions")
                return new { items = await animalDescriptions.ReadAsync(
                    draft.Scope, draft.PayerId, token) };
            await animalDescriptions.SaveAsync(draft.Scope, draft.PayerId,
                Text(root, "description", 2000), token);
            return new { saved = true };
        }
        if (action == "readTreatmentChoices")
        {
            string id = Text(root, "draftId", 30);
            if (!drafts.TryGetValue(id, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            var saved = await treatmentChoices.ReadAsync(draft.Scope,
                draft.Rows.Select(row => row.Get("Symbol Subiekta")), token);
            return new { items = saved };
        }
        if (action == "setDraftSelections")
        {
            string id = Text(root, "draftId", 30);
            if (!drafts.TryGetValue(id, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            if (!root.TryGetProperty("selections", out var chosen) ||
                chosen.ValueKind != JsonValueKind.Array ||
                chosen.GetArrayLength() != draft.Rows.Count ||
                draft.Rows.Count > 500)
                throw new ArgumentException("Nieprawidłowa lista wybranych leków.");
            var inputs = chosen.EnumerateArray().ToArray();
            // Validate the entire snapshot before mutating any row.
            for (int index = 0; index < inputs.Length; index++)
                if (Int(inputs[index], "index") != index ||
                    !string.Equals(Text(inputs[index], "symbol", 128),
                        draft.Rows[index].Get("Symbol Subiekta").Trim(),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Stan dokumentu się zmienił. Ponownie otwórz dokument.");
            for (int index = 0; index < inputs.Length; index++)
                draft.Rows[index].Set("Drukuj", Flag(inputs[index], "selected").ToString());
            return new { updated = inputs.Length };
        }
        if (action == "treatmentVariants" || action == "applyTreatmentVariant")
        {
            string draftId = Text(root,"draftId",30);
            int index = Int(root,"index") ?? -1;
            if (!drafts.TryGetValue(draftId,out var draft) ||
                index<0 || index>=draft.Rows.Count)
                throw new InvalidOperationException("Ponownie otwórz dokument przed wyborem wariantu ChPL.");
            var row = draft.Rows[index];
            string symbol = row.Get("Symbol Subiekta").Trim();
            if (string.IsNullOrWhiteSpace(symbol))
                throw new InvalidOperationException(
                    "Pozycja dokumentu nie zawiera symbolu Subiekta. Wariant wybierz ręcznie w katalogu.");
            var product = await Task.Run(() => catalogue.Read(symbol),token);
            if (action == "treatmentVariants")
                return new {
                    product = product.Name,symbol = product.Symbol,
                    variants = product.Variants.Where(v=>v.Id.HasValue)
                        .Select(v => new {v.Id,v.Species,v.Indication,v.Route,v.Dose,
                            v.Tissues,v.Milk,v.Remarks,v.Source,v.Pdf}).ToArray()
                };
            int variantId = Int(root,"variantId") ?? -1;
            var choice = product.Variants.SingleOrDefault(v=>v.Id == variantId);
            if (choice is null || string.IsNullOrWhiteSpace(choice.Species) ||
                string.IsNullOrWhiteSpace(choice.Route) || string.IsNullOrWhiteSpace(choice.Dose))
                throw new InvalidOperationException(
                    "Wybrany wariant jest niekompletny lub nie istnieje. Nie zmieniono karty.");
            // Save only after an explicit variant choice and opt-in; the
            // remembered species/route never changes clinical values itself.
            if (Flag(root, "remember"))
                await treatmentChoices.SaveAsync(draft.Scope, symbol,
                    choice.Species, choice.Route, token);
            return TreatmentVariantApplier.Apply(row, choice);
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
                "Rozpoznanie", "Seria", "Zamiennik produktu", "Sekcja (II lub III)", "Drukuj"];
            if (!allowed.Contains(field, StringComparer.Ordinal)) throw new ArgumentException("Pole nie jest edytowalne.");
            string value = field == "Drukuj" ? Flag(root, "checked").ToString() : Text(root, "value", 3000);
            draft.Rows[index].Set(field, value);
            return new { updated = true };
        }
        if (action is "priceLevels" or "prices" or "expiries")
        {
            string stockKey = DocumentListCache.Key(options, DocumentSearchKinds.None,
                "stock-expiry", null, null);
            bool refresh = Flag(root, "refresh");
            if (action == "priceLevels")
            {
                string levelsKey = DocumentListCache.Key(options, DocumentSearchKinds.None,
                    "price-levels", null, null);
                var levels = await priceLevelLists.ReadAsync(levelsKey, refresh,
                    ct => MedicineStockService.PriceLevels(options, ct), token);
                return levels.Value;
            }
            if (action == "expiries")
            {
                var expiries = await expiryLists.ReadAsync(stockKey, refresh,
                    ct => MedicineStockService.Expiries(options, ct), token);
                return expiries.Value;
            }
            int level = Int(root, "level") ?? throw new ArgumentException("Wybierz poziom cen.");
            bool net = Flag(root, "net");
            string priceKey = DocumentListCache.Key(options, DocumentSearchKinds.None,
                $"prices-{level}-{net}", null, null);
            var priced = await priceLists.ReadAsync(priceKey, refresh, async ct =>
            {
                // Prices would otherwise re-run the heavy Expiries SQL query
                // each time the user changes between levels or net/gross.
                var stock = await expiryLists.ReadAsync(stockKey, refresh,
                    stockToken => MedicineStockService.Expiries(options, stockToken), ct);
                return await MedicineStockService.Prices(options, level, ct,
                    net: net, knownExpiries: stock.Value);
            }, token);
            var catalogueRows = await priceCatalogueLists.ReadAsync("price-catalogue", refresh,
                ct => priceCatalogueService.ReadAsync(ct), token);
            return catalogueRows.Value.FormatPrices(priced.Value);
        }
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
            // The complete edited snapshot travels with save/preview to avoid a
            // WebView2 blur-vs-click race leaving the last cell unsaved.
            if (root.TryGetProperty("rows", out var submitted) &&
                submitted.ValueKind == JsonValueKind.Array)
            {
                if (submitted.GetArrayLength() != draft.Rows.Count || draft.Rows.Count > 500)
                    throw new InvalidOperationException("Liczba pozycji zmieniła się. Ponownie otwórz dokument.");
                foreach (var item in submitted.EnumerateArray())
                {
                    int index = Int(item, "index") ?? -1;
                    if (index < 0 || index >= draft.Rows.Count)
                        throw new InvalidOperationException("Niewłaściwy indeks pozycji.");
                    var treatment = draft.Rows[index];
                    treatment.Printed = Flag(item, "selected");
                    foreach (var (key, field) in new[] {
                        ("batch", "Seria"), ("replacement", "Zamiennik produktu"),
                        ("species", "Gatunek"),
                        ("route", "Droga podania"), ("dosing", "Dawkowanie i okres stosowania"),
                        ("withdrawal", "Karencja"),
                        ("description", "Opis zwierząt / numer siedziby stada"),
                        ("animals", "Liczba zwierząt"), ("diagnosis", "Rozpoznanie"),
                        ("section", "Sekcja (II lub III)")
                    })
                        treatment.Set(field, Text(item, key, 3000));
                }
            }
            static DateTime? OptionalDate(JsonElement element, string name)
            {
                string text = Text(element, name, 35);
                if (string.IsNullOrWhiteSpace(text)) return null;
                if (!DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException("Niepoprawna data i godzina: " + name);
                return date;
            }
            var reported = OptionalDate(root, "reported");
            var performed = RequiredDate(root, "performed");
            string findings = Text(root, "findings", 2000);
            if (action == "previewDraft")
                return new { html = cards.RenderDraft(draft, reported, performed, findings) };
            return cards.Save(draft, reported, performed, findings, Flag(root, "confirmDuplicate"));
        }
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
        var card = JsonSerializer.Deserialize<Card>(json) ??
            throw new InvalidDataException("Zapisana karta ma nieprawidłową zawartość.");
        return new { card.Fields, card.Rows,
            revision = SavedCardRevisionService.Revision(json) };
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

    private async Task<object> PurgeTrashedCard(
        CancellationToken token, Guid cardId, string confirmation)
    {
        await using var db = ArchiveDb();
        await db.OpenAsync(token);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, token);
        using (var access = new SqlCommand("""
            IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
                THROW 50041, N'Trwałe usunięcie wymaga uprawnienia sysadmin SQL Server.', 1;
            """, db, tx))
            await access.ExecuteNonQueryAsync(token);
        using (var check = new SqlCommand("""
            SELECT card_year,card_sequence,document
            FROM dbo.treatment_cards WITH (UPDLOCK,HOLDLOCK)
            WHERE id=@id AND deleted_at_utc IS NOT NULL;
            """, db, tx))
        {
            check.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = cardId;
            await using var reader = await check.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                throw new InvalidOperationException(
                    "Karta nie znajduje się już w koszu. Odśwież listę.");
            string number = reader.IsDBNull(0) || reader.IsDBNull(1) ? "" :
                $"K/{reader.GetInt64(1)}/{reader.GetInt32(0):D4}";
            string document = reader.GetString(2);
            if (!string.Equals(confirmation, number.Length > 0 ? number : document,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Potwierdzony numer karty nie jest zgodny z danymi SQL. Nic nie usunięto.");
        }
        using (var remove = new SqlCommand("""
            DELETE FROM dbo.treatment_cards
            WHERE id=@id AND deleted_at_utc IS NOT NULL;
            """, db, tx))
        {
            remove.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = cardId;
            if (await remove.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException(
                    "Karta zmieniła stan w trakcie operacji. Nie została usunięta.");
        }
        await tx.CommitAsync(token);
        return new { deleted = true, cancelled = false };
    }

    private SqlConnection ArchiveDb()
    {
        var saved = archive.Load().Options ??
            throw new InvalidOperationException("Archiwum SQL nie jest skonfigurowane na tym stanowisku.");
        var connectionString = new SqlConnectionStringBuilder(
            saved.BuildConnectionString("KartyLeczenia")) {ApplicationIntent = ApplicationIntent.ReadWrite};
        return new SqlConnection(connectionString.ConnectionString);
    }

    private static SqlBackupSettings BackupSettings(JsonElement root) => new(
        DestinationFolder: Text(root,"destinationFolder",1000),
        WorkingFolder: Text(root,"workingFolder",1000),
        Naming: Text(root,"naming",20),
        CustomName: Text(root,"customName",200),
        Overwrite: Flag(root,"overwrite"),
        Compress: Flag(root,"compress"));
    private void SendProgress(string id, SqlBackupProgress state)
    {
        if (IsDisposed || web.CoreWebView2 is null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            id, @event="progress", stage=state.Stage, percent=state.Percent,
            detail=state.Detail
        },new JsonSerializerOptions(JsonSerializerDefaults.Web)));
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
