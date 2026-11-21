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
    private readonly ConnectionSettingsStore administrator = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "administrator-connection.json"));
    private readonly DocumentService documents = new();
    private readonly DocumentListCache documentLists = new();
    // Short snapshots avoid the expensive delivery/stock query being repeated
    // when switching between price levels, netto/brutto, and expiry screens.
    private readonly TimedSqlCache<List<MedicineExpiry>> expiryLists = new(TimeSpan.FromSeconds(30));
    private readonly TimedSqlCache<List<MedicinePrice>> priceLists = new(TimeSpan.FromSeconds(30));
    private readonly TimedSqlCache<MedicinePriceLevel[]> priceLevelLists = new(TimeSpan.FromMinutes(10));
    // Full Subiekt drug-group scan is only triggered by the explicit
    // "Sprawdź braki w katalogu" command, never by typing in the editor.
    private readonly TimedSqlCache<MedicinePrice[]> catalogueSubiektLists =
        new(TimeSpan.FromMinutes(2));
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
    private readonly ReactUpdateService updates;
    private ReactUpdateInfo? checkedUpdate;
    private string? startupPublicationError;
    private string? selectedRestoreFile;
    private bool restoreRunning;
    private readonly CatalogueService catalogue;
    private readonly Dictionary<string, byte[]> pendingCataloguePdfs = new(StringComparer.Ordinal);
    private bool backupRunning;
    private bool closeAfterVerifiedBackup;
    private bool installingUpdate;
    private bool closingForUpdate;

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
        updates = new ReactUpdateService(archive);
        catalogue = new CatalogueService(archive);
        priceCatalogueService = new PriceCatalogueService(archive);
        base.Text = "Karty Leczenia";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(480, 360);
        Size = new Size(1400, 850);
        AutoScaleMode = AutoScaleMode.Dpi;
        ReactWindowPlacement.Restore(this);
        Controls.Add(web);
        ResizeEnd += (_, _) => ReactWindowPlacement.Save(this);
        FormClosed += (_, _) => ReactWindowPlacement.Save(this);
        Shown += async (_, _) =>
        {
            await StartAsync();
            if (ReactInstallUpdates.IsServer)
                await PublishServerUpdateOnStartup();
            else
                await CheckClientUpdateOnStartup();
        };
        FormClosing += async (_, e) =>
        {
            if (backupRunning || restoreRunning)
            {
                e.Cancel = true;
                MessageBox.Show(this,
                    "Trwa operacja konserwacyjna SQL. Zamknięcie programu jest chwilowo zablokowane.",
                    "Karty Leczenia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // This is the original WinForms three-way choice. Never ask on a
            // shutdown/update initiated by Windows or when no SQL archive is configured.
            if (!closeAfterVerifiedBackup && !closingForUpdate && e.CloseReason == CloseReason.UserClosing &&
                archive.Load().Options is not null)
            {
                var answer = MessageBox.Show(this,
                    "Czy przed zamknięciem programu wykonać kopię zapasową bazy SQL KartyLeczenia?\n\n" +
                    "Tak — archiwizuj i zamknij.\n" +
                    "Nie — zamknij bez archiwizacji.\n" +
                    "Anuluj — wróć do programu.",
                    "Archiwizacja przy zamykaniu",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes)
                {
                    e.Cancel = true;
                    backupRunning = true;
                    UseWaitCursor = true;
                    try
                    {
                        if (!await WindowsHelloConsent.VerifyAsync(this,
                                "archiwizację bazy przed zamknięciem"))
                            return;
                        var settings = SqlBackupService.Load();
                        if (!Directory.Exists(settings.DestinationFolder))
                        {
                            string folder = ChooseArchiveFolder(settings.DestinationFolder);
                            if (folder.Length == 0) return;
                            settings = settings with { DestinationFolder = folder };
                            SqlBackupService.Save(settings);
                        }
                        var outcome = await Task.Run(() => backups.Run(settings,
                            new Progress<SqlBackupProgress>()), CancellationToken.None);
                        if (IsDisposed) return;
                        if (!outcome.SqlVerified)
                            MessageBox.Show(this,
                                "Kopia została utworzona, ale SQL nie potwierdził pełnej weryfikacji.\n" +
                                outcome.Message + "\n\nPlik: " + outcome.Path,
                                "Kopia — ograniczona weryfikacja", MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        closeAfterVerifiedBackup = true;
                        BeginInvoke(new Action(Close));
                    }
                    catch (Exception error)
                    {
                        CrashLog.Write("ReactKL.BackupOnClose", error);
                        if (!IsDisposed)
                            MessageBox.Show(this,
                                "Nie udało się wykonać kopii bazy KartyLeczenia.\n\n" +
                                error.Message + "\n\nProgram pozostaje otwarty.",
                                "Błąd archiwizacji", MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                    }
                    finally
                    {
                        backupRunning = false;
                        if (!IsDisposed) UseWaitCursor = false;
                    }
                    return;
                }
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
                    "Aktualizacja Karty Leczenia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CheckClientUpdateOnStartup()
    {
        if (IsDisposed || !ReactInstallUpdates.IsInstalled) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try
        {
            var release = await updates.Check(timeout.Token);
            if (!IsDisposed && release is { Newer: true })
                await OfferAndInstallUpdate(release, timeout.Token);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.StartupUpdate", ex);
            if (!IsDisposed)
                MessageBox.Show(this, ex.Message, "Aktualizacja Karty Leczenia",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task<object> OfferAndInstallUpdate(ReactUpdateInfo release, CancellationToken token)
    {
        if (backupRunning || restoreRunning)
            throw new InvalidOperationException("Zakończ archiwizację lub odtwarzanie przed aktualizacją.");
        if (installingUpdate || closingForUpdate)
            throw new InvalidOperationException("Aktualizacja jest już pobierana.");
        installingUpdate = true;
        string? file = null;
        string stage = "sprawdzanie publikacji SQL";
        try
        {
            if (MessageBox.Show(this,
                $"Dostępna wersja: {release.Version}. Obecna wersja: {release.CurrentVersion}.\n\nPobrać i uruchomić instalator?",
                "Aktualizacja Karty Leczenia", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return new { started = false };

            UseWaitCursor = true;
            var current = await updates.Check(token);
            if (current is null || current.ReleaseId != release.ReleaseId ||
                !current.Newer || !string.Equals(current.Sha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Publikacja zmieniła się. Sprawdź wersję ponownie.");
            stage = "pobieranie i weryfikacja instalatora";
            file = await updates.Download(current, token);
            token.ThrowIfCancellationRequested();
            if (backupRunning || restoreRunning)
                throw new InvalidOperationException("Zakończ archiwizację lub odtwarzanie przed aktualizacją.");
            if (IsDisposed || Disposing) return new { started = false };
            stage = "uruchamianie instalatora w Windows";
            ReactUpdateService.Start(file);
            closingForUpdate = true;
            BeginInvoke(new Action(Close));
            return new { started = true, version = current.Version };
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Aktualizacja — {stage}. {ex.Message}", ex);
        }
        finally
        {
            installingUpdate = false;
            if (!IsDisposed) UseWaitCursor = false;
            if (file is not null && !closingForUpdate)
                ReactUpdateService.DeleteDownloadFolder(Path.GetDirectoryName(file)!);
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
            // Card/archive operations legitimately carry their own "id".
            // Never confuse that SQL identifier with the RPC response key.
            requestId = ReadTextField(root, "_bridgeRequestId", 80);
            if (requestId.Length == 0)
                requestId = ReadTextField(root, "id", 80);
            string action = ReadTextField(root, "action", 40);
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(action)) return;
            if (action == "cancelRequest")
            {
                string cancelledId = ReadTextField(root, "requestId", 80);
                if (active.TryGetValue(cancelledId, out var runningRequest))
                    runningRequest.Cancel();
                return;
            }
            cancel = new CancellationTokenSource(action is "backupDatabase" or "restoreDatabase"
                ? TimeSpan.FromMinutes(45) : action is "saveCardPdf" or "emailCardPdf"
                    ? TimeSpan.FromMinutes(3) : action == "missingCatalogueProducts"
                    ? TimeSpan.FromMinutes(2) : action == "installUpdate"
                    ? TimeSpan.FromMinutes(15) : action == "publishPendingUpdate"
                    ? TimeSpan.FromMinutes(7) : action == "purgeTrashedCard"
                        ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(45));
            active[requestId] = cancel;
            object result = await HandleRequest(action, root, cancel.Token);
            if (!cancel.IsCancellationRequested) Send(requestId, result);
        }
        catch (Exception) when (cancel?.IsCancellationRequested == true)
        {
            // SqlClient can report an aborted command as SqlException.
            Send(requestId, null, "Operacja została anulowana lub przekroczyła limit czasu.");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ReactKL.Message", ex);
            Send(requestId, null, ex.Message.StartsWith("Aktualizacja — ", StringComparison.Ordinal)
                ? ex.Message : SubiektConnectionService.FriendlyError(ex));
        }
        finally
        {
            if (active.Remove(requestId, out var running)) running.Dispose();
        }
    }

    private async Task<object> HandleRequest(string action, JsonElement root, CancellationToken token)
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
        if (action is "searchSubiektCatalogue" or "openSubiektCatalogueProduct")
        {
            var settings = subiekt.Load().Options ??
                throw new InvalidOperationException("Skonfiguruj połączenie z Subiektem przed wyborem kartoteki.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            if (action == "searchSubiektCatalogue")
            {
                string query = ReadTextField(root, "query", 128);
                var matches = await MedicineStockService.FindCatalogueProducts(settings, query, timeout.Token);
                return matches.Select(item => new { item.Symbol, item.Name }).ToArray();
            }

            string symbol = ReadTextField(root, "symbol", 128).Trim();
            var products = await MedicineStockService.FindCatalogueProducts(
                settings, symbol, timeout.Token, limit: 2, exactSymbol: true);
            if (products.Length != 1)
                throw new InvalidOperationException(products.Length == 0
                    ? "Kartoteka nie jest już dostępna w grupie leków Subiekta."
                    : "Symbol odpowiada kilku kartotekom Subiekta.");
            return await Task.Run(() => catalogue.Read(products[0]), token);
        }
        if (action == "missingCatalogueProducts")
        {
            var settings = subiekt.Load().Options ??
                throw new InvalidOperationException("Skonfiguruj połączenie Subiekta przed sprawdzaniem braków.");
            bool refresh = Flag(root,"refresh");
            string key = DocumentListCache.Key(settings,DocumentSearchKinds.None,
                "catalogue-missing",null,null);
            var products = await catalogueSubiektLists.ReadAsync(
                key,refresh,
                ct => MedicineStockService.FindCatalogueProducts(
                    settings,"",ct,limit:int.MaxValue),token);
            var snapshot = await priceCatalogueLists.ReadAsync(
                "price-catalogue",refresh,
                ct => priceCatalogueService.ReadAsync(ct),token);
            var missing = products.Value
                .Select(item => new { symbol = item.Symbol, name = item.Name,
                    reason = snapshot.Value.CompletionReason(item.Symbol) })
                .Where(item => item.reason is not null).ToArray();
            return new {items=missing,count=missing.Length,total=products.Value.Length};
        }
        if (action == "searchCatalogue")
        {
            string search = ReadTextField(root, "query", 120);
            return await Task.Run(() => catalogue.Search(search), token);
        }
        if (action == "readCatalogueProduct")
        {
            string symbol = ReadTextField(root, "symbol", 128);
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
            string pdfPath = ReadTextField(root, "path", 450);
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
            string path = ReadTextField(root, "path", 450);
            byte[] data = pendingCataloguePdfs.TryGetValue(path, out var pending)
                ? pending : await Task.Run(() => catalogue.ReadPdf(path), token);
            return new { base64 = Convert.ToBase64String(data), name = Path.GetFileName(path) };
        }
        if (action == "systemFontFamilies")
        {
            using var fonts = new System.Drawing.Text.InstalledFontCollection();
            return fonts.Families.Select(font => font.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        if (action == "getProgramVersion")
            return new { version = ReactUpdateService.CurrentVersion };
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
                currentVersion = ReactInstallUpdates.InstalledVersion };
        }
        if (action == "installUpdate")
        {
            if (!ReactInstallUpdates.IsInstalled)
                throw new InvalidOperationException("Najpierw zainstaluj ReactKL w standardowym katalogu.");
            string expectedId = ReadTextField(root, "releaseId", 40);
            var release = checkedUpdate;
            if (release is null || !release.Newer || !Guid.TryParse(expectedId, out var releaseId) ||
                release.ReleaseId != releaseId)
                throw new InvalidOperationException("Sprawdź ponownie dostępność aktualizacji w SQL.");
            return await OfferAndInstallUpdate(release, token);
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
                ReadTextField(root, "path", 1000) != selectedRestoreFile)
                throw new InvalidOperationException("Ponownie wybierz plik .bak w systemowym oknie Windows.");
            if (ReadTextField(root, "confirmation", 40) != "KartyLeczenia")
                throw new InvalidOperationException("Potwierdź nazwę bazy KartyLeczenia.");
            var adminOptions = AdministratorOptions();
            string restorePath = selectedRestoreFile;
            restoreRunning = true;
            try
            {
                if (!await WindowsHelloConsent.VerifyAsync(this,
                    "odtworzenie bazy KartyLeczenia (zastąpienie danych)"))
                    return new { cancelled = true };
                string request = ReadTextField(root,"id",80);
                var progress = new Progress<SqlBackupProgress>(value => SendProgress(request,value));
                var result = await Task.Run(() => SqlRestoreService.Run(adminOptions, restorePath, progress));
                selectedRestoreFile = null;
                return new { cancelled = false, result.Database,result.SourcePath,
                    result.SafetyBackupPath,result.Message };
            }
            finally { restoreRunning = false; }
        }
        if (action == "getBackupSettings")
            return SqlBackupService.Load();
        if (action == "chooseArchiveFolder")
            return new { folder = ChooseArchiveFolder(SqlBackupService.Load().DestinationFolder) };
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
            string messageId = ReadTextField(root,"id",80);
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
            string kind = ReadTextField(root, "kind", 12);
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
            string expectedRevision = ReadTextField(root, "revision", 100);
            if (expectedRevision.Length != 64)
                throw new InvalidOperationException("Najpierw odśwież dane lecznicy z SQL.");
            var previous = await Task.Run(() => clinicService.Load(), token);
            var old = previous.Settings;
            var updated = new ClinicSettings(ReadTextField(root, "name", 200),
                ReadTextField(root, "address", 500), ReadTextField(root, "doctor", 200))
            {
                Phone = ReadTextField(root, "phone", 100),
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

        if (action == "getAdministratorSettings")
        {
            var program = archive.Load().Options;
            var current = administrator.Load();
            var setting = current.Options;
            bool sameServer = setting is not null && program is not null &&
                string.Equals(setting.Server, program.Server, StringComparison.OrdinalIgnoreCase);
            return new
            {
                configured = sameServer,
                server = program?.Server ?? "",
                windowsAuthentication = !sameServer || setting!.WindowsAuthentication,
                userName = sameServer ? setting!.UserName : "",
                remembered = sameServer && current.Remembered,
                warning = program is null ? "Najpierw zapisz połączenie w zakładce Baza Karty Leczenia."
                    : sameServer ? current.Warning : null
            };
        }
        if (action is "testAdministratorSettings" or "saveAdministratorSettings")
        {
            var program = archive.Load().Options ??
                throw new InvalidOperationException("Najpierw zapisz połączenie w zakładce Baza Karty Leczenia.");
            var setting = ArchiveConnectionFrom(root, administrator, program);
            bool save = action == "saveAdministratorSettings";
            await using var db = AdministratorDb(setting, "master");
            await db.OpenAsync(token);
            using var cmd = new SqlCommand("""
                SELECT SUSER_SNAME(), CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')),
                       ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0);
                """, db) { CommandTimeout = 8 };
            await using var reader = await cmd.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                throw new InvalidOperationException("SQL Server nie zwrócił informacji o koncie.");
            string login = reader.GetString(0);
            string instance = reader.IsDBNull(1) ? program.Server : reader.GetString(1);
            if (reader.GetInt32(2) != 1)
                throw new InvalidOperationException($"Konto „{login}” na instancji „{instance}” nie ma roli sysadmin.");
            if (save)
            {
                if (archive.Load().Options != program)
                    throw new InvalidOperationException("Zmieniono połączenie bazy programu. Sprawdź i zapisz konto ponownie.");
                administrator.Save(setting, Flag(root, "rememberPassword"));
            }
            return new { saved = save, message = save
                ? $"Zapisano konto administratora „{login}” na instancji „{instance}”."
                : $"Konto „{login}” ma rolę sysadmin na instancji „{instance}”." };
        }
        if (action == "getArchiveSettings")
        {
            var current = archive.Load();
            var setting = current.Options;
            return new
            {
                configured = setting is not null,
                current.Warning, current.Remembered,
                server = setting?.Server ?? "",
                windowsAuthentication = setting?.WindowsAuthentication ?? true,
                userName = setting?.UserName ?? "",
                trustCertificate = setting?.TrustCertificate ?? false,
                database = "KartyLeczenia"
            };
        }
        if (action is "testArchiveSettings" or "saveArchiveSettings")
        {
            var setting = ArchiveConnectionFrom(root);
            await TestArchiveConnectionAsync(setting, token);
            if (action == "saveArchiveSettings")
                archive.Save(setting, Flag(root, "rememberPassword"));
            return new
            {
                saved = action == "saveArchiveSettings",
                server = setting.Server,
                database = "KartyLeczenia",
                message = action == "saveArchiveSettings"
                    ? "Połączenie z bazą KartyLeczenia sprawdzone i zapisane na tym stanowisku."
                    : "Połączenie z bazą KartyLeczenia działa. Ustawienia nie zostały zmienione."
            };
        }
        if (action == "getSettings")
        {
            var current = subiekt.Load();
            var saved = current.Options;
            return new
            {
                configured = saved is not null,
                current.Warning, current.Remembered,
                server = saved?.Server ?? "", database = saved?.Database ?? "",
                windowsAuthentication = saved?.WindowsAuthentication ?? true,
                userName = saved?.UserName ?? "",
                trustCertificate = saved?.TrustCertificate ?? false,
                drugGroup = saved?.DrugGroup ?? "Leki",
                warehouseId = saved?.WarehouseId
            };
        }
        if (action == "discoverSubiektDatabases")
        {
            var draft = SubiektConnectionFrom(root) with { Database = "" };
            var companies = await new SubiektConnectionService().DiscoverAsync(draft, token);
            return companies;
        }
        if (action == "testSubiektSettings")
        {
            var draft = SubiektConnectionFrom(root);
            // Test credentials, SELECT-only permissions, group and warehouses.
            // Do not require an already selected group or warehouse on first
            // connection: the operator must receive lists to choose from.
            var checkedConnection = await new SubiektConnectionService().TestAsync(
                draft, token, validateSelections: false);
            return new
            {
                checkedConnection.Company, checkedConnection.DrugGroups,
                checkedConnection.Warehouses,
                groupValid = checkedConnection.DrugGroups.Contains(
                    draft.DrugGroup.Trim(), StringComparer.OrdinalIgnoreCase),
                warehouseValid = draft.WarehouseId is int warehouseId &&
                    checkedConnection.Warehouses.Any(item => item.Id == warehouseId),
                message = "Dostęp do Subiekta sprawdzony. Wybierz grupę leków i magazyn."
            };
        }
        if (action == "saveSettings")
        {
            var config = SubiektConnectionFrom(root);
            // Only tested, existing group and warehouse may be saved.
            await new SubiektConnectionService().TestAsync(config, token);
            subiekt.Save(config, Flag(root, "rememberPassword"));
            documentLists.Clear();
            expiryLists.Clear();
            priceLists.Clear();
            priceLevelLists.Clear();
            priceCatalogueLists.Clear();
            catalogueSubiektLists.Clear();
            return new { saved = true,
                message = "Sprawdzono i zapisano połączenie Subiekta na tym stanowisku." };
        }
        // Archive browsing and trash management use the program database only.
        // They must remain available when Subiekt GT is temporarily offline.
        if (action == "renderSavedCard")
        {
            string id = ReadTextField(root, "id", 40);
            bool trash = Flag(root, "trash");
            string html = await Task.Run(() => cards.RenderSaved(id, trash), token);
            return new { html };
        }
        if (action is "saveCardPdf" or "emailCardPdf")
        {
            // The archived SQL card is the single authoritative source. An
            // old preview must not produce a stale PDF after another station
            // changed the clinical data.
            string id = ReadTextField(root, "id", 40);
            bool trash = Flag(root, "trash");
            Guid cardId = Guid.Empty;
            DocumentData? draft = null;
            string html;
            string documentNumber;
            if (id.Length > 0)
            {
                if (!Guid.TryParseExact(id, "N", out cardId))
                    throw new ArgumentException("Nieprawidłowy identyfikator karty.");
                var stored = JsonSerializer.Deserialize<Card>(cards.ReadSavedJson(id, trash))
                    ?? throw new InvalidDataException("Zapisana karta nie zawiera poprawnych danych.");
                html = CardHtmlRenderer.Render(stored);
                documentNumber = stored.Fields.GetValueOrDefault("Dokument źródłowy", "");
            }
            else
            {
                string draftId = ReadTextField(root, "draftId", 30);
                if (!drafts.TryGetValue(draftId, out draft))
                    throw new InvalidOperationException("Ponownie otwórz dokument przed utworzeniem PDF.");
                // Same validation and complete edited-row snapshot as
                // previewDraft. No SQL insert or new card number is created.
                object rendered = await HandleRequest("previewDraft", root, token);
                html = JsonSerializer.SerializeToElement(rendered)
                    .GetProperty("html").GetString() ??
                    throw new InvalidDataException("Nie udało się uzyskać podglądu karty.");
                documentNumber = draft.Number;
            }
            string fileName = CardPdfService.AttachmentName(documentNumber);
            string? destination = null;
            if (action == "saveCardPdf")
            {
                using var save = new SaveFileDialog
                {
                    Title = "Zapisz kartę leczenia jako PDF",
                    Filter = "Dokument PDF (*.pdf)|*.pdf",
                    AddExtension = true, DefaultExt = "pdf",
                    FileName = fileName, OverwritePrompt = true
                };
                if (save.ShowDialog(this) != DialogResult.OK)
                    return new { cancelled = true, fileName = "", recipient = "" };
                destination = save.FileName;
            }
            // Render a separate WebView2 page on this UI/STA thread; never
            // navigate the main React editor away from the current screen.
            string temporary = await CardPdfService.RenderAsync(
                this, html, token);
            try
            {
                if (destination is not null)
                {
                    CardPdfService.MoveToDestination(temporary, destination);
                    return new { cancelled = false,
                        fileName = Path.GetFileName(destination), recipient = "" };
                }
                var (payerId, scope) = draft is not null
                    ? (draft.PayerId, draft.Scope)
                    : await ReadSavedCardPayer(cardId, trash, token);
                string? recipient = null;
                var source = subiekt.Load().Options;
                if (payerId is > 0 && source is not null &&
                    string.Equals(scope,
                        source.Server.ToUpperInvariant() + "|" +
                        source.Database.ToUpperInvariant(), StringComparison.Ordinal))
                {
                    using var mailTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    mailTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        recipient = await documents.ReadPayerEmailAsync(
                            source, payerId.Value, mailTimeout.Token);
                    }
                    catch (Exception ex) when (ex is SqlException or
                        OperationCanceledException or InvalidOperationException)
                    {
                        // No recipient is preferable to using the card's
                        // recipient rather than its actual Subiekt payer.
                        CrashLog.Write("ReactKL.Card.PayerEmail", ex);
                    }
                }
                var mail = PdfMail.ComposeInBackground(temporary, fileName, recipient);
                _ = mail.ContinueWith(task =>
                {
                    if (task.Exception is { } failure)
                        CrashLog.Write("ReactKL.Card.Mail", failure);
                }, TaskContinuationOptions.OnlyOnFaulted);
                return new { cancelled = false, fileName, recipient = recipient ?? "" };
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        if (action == "exportSavedCardJson")
        {
            // A read-only operation; the Windows user chooses the destination
            // on their own workstation. Do not send any patient JSON over UI.
            string id = ReadTextField(root, "id", 40);
            bool trash = Flag(root, "trash");
            string raw = await Task.Run(() => cards.ReadSavedJson(id, trash), token);
            var card = JsonSerializer.Deserialize<Card>(raw) ??
                throw new InvalidDataException("Nie można odczytać karty do eksportu.");
            string baseName = CardArchiveService.SafeJsonFileName(
                card.Fields.GetValueOrDefault("Nr karty", ""));
            using var dialog = new SaveFileDialog
            {
                Title = "Eksport karty leczenia do JSON",
                Filter = "Pliki JSON (*.json)|*.json",
                AddExtension = true, DefaultExt = "json",
                FileName = baseName + ".json",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return new { cancelled = true, fileName = "" };
            using var parsed = JsonDocument.Parse(raw);
            File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(parsed.RootElement,
                new JsonSerializerOptions { WriteIndented = true }));
            return new { cancelled = false, fileName = Path.GetFileName(dialog.FileName) };
        }
        if (action == "savedCards")
            return await ReadSavedCards(token, Flag(root, "trash"),
                ReadTextField(root, "query", 120), ReadTextField(root, "date", 12));
        if (action == "savedCard")
            return await ReadSavedCard(token, ReadTextField(root, "id", 40), Flag(root, "trash"));
        if (action == "updateSavedCard")
        {
            string id = ReadTextField(root, "id", 40);
            string revision = ReadTextField(root, "revision", 64);
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
            return await TrashCard(token, ReadTextField(root, "id", 40), Flag(root, "trash"));
        if (action == "purgeTrashedCard")
        {
            string id = ReadTextField(root, "id", 40);
            string confirmation = ReadTextField(root, "confirmation", 160);
            if (!Guid.TryParseExact(id, "N", out var cardId) ||
                string.IsNullOrWhiteSpace(confirmation))
                throw new ArgumentException("Wybierz kartę z kosza i potwierdź jej numer.");
            var adminOptions = AdministratorOptions();
            if (!await WindowsHelloConsent.VerifyAsync(this,
                    "trwałe usunięcie jednej karty leczenia z kosza"))
                return new { deleted = false, cancelled = true };
            return await PurgeTrashedCard(adminOptions, token, cardId, confirmation);
        }
        var options = subiekt.Load().Options ??
            throw new InvalidOperationException("Brak konfiguracji Subiekta. Uzupełnij ustawienia połączenia.");
        if (action == "searchDocuments")
        {
            string category = ReadTextField(root, "kind", 20);
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
                string value = ReadTextField(element, name, 12);
                if (!DateTime.TryParseExact(value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException("Wybierz prawidłową datę: " + name);
                return date;
            }
            string period = ReadTextField(root, "period", 20);
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

            string query = ReadTextField(root, "query", 120);
            bool refresh = Flag(root, "refresh");
            string cacheKey = DocumentListCache.Key(options, kind, query, from, to);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await documentLists.GetAsync(cacheKey, refresh,
                async queryToken => await documents.SearchAsync(options, query, queryToken,
                    kind, from, to, limit: 501, allowEmpty: true), token,
                queryToken => documents.ReadLatestDocumentIdAsync(options, queryToken));
            CrashLog.Write($"ReactKL.Documents.List kind={kind} count={result.Items.Count} cached={result.Cached} elapsedMs={watch.ElapsedMilliseconds}");
            return new
            {
                items = result.Items.Select(x => new
                {
                    x.Id, x.Number, x.Date, x.ShortPayer, x.Payer, x.Gross,
                    x.Net, x.Currency, x.RelatedText
                }).ToArray(),
                result.Truncated, result.Cached, result.LoadedAt, result.CheckedAt, result.Warning
            };
        }
        if (action == "readDocument")
        {
            int id = Int(root, "documentId") ?? throw new ArgumentException("Brak numeru dokumentu.");
            string number = ReadTextField(root, "number", 120);
            var document = await documents.ReadAsync(options, number, 0, token, id);
            string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            drafts[key] = document;
            return new { draftId = key, document.Number, document.Payer, document.Recipient,
                hasPayerId = document.PayerId.HasValue,
                rows = document.Rows.Select((r, index) => new {index, product = r.Get("Produkt"),
                    replacement = r.Get("Zamiennik produktu"),
                    referenceSymbol = r.Get("Symbol zamiennika produktu"),
                    descriptionGroup = r.Get(CardSchema.AnimalDescriptionGroup),
                    countGroup = r.Get(CardSchema.AnimalCountGroup),
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
            string draftId = ReadTextField(root, "draftId", 30);
            if (!drafts.TryGetValue(draftId, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument przed doborem wariantów.");
            if (draft.Rows.Count > 500)
                throw new InvalidOperationException(
                    "Dokument ma zbyt wiele pozycji do zbiorczych propozycji.");
            var (variants, nonMedicines) = await Task.Run(() =>
                catalogue.VariantsForSymbols(draft.Rows.Select(row =>
                    TreatmentAutofillPlanner.CatalogueSymbol(row))), token);
            var proposals = draft.Rows.Select((row, index) =>
            {
                variants.TryGetValue(TreatmentAutofillPlanner.CatalogueSymbol(row), out var entries);
                return TreatmentAutofillPlanner.Propose(index, row,
                    entries ?? []);
            }).ToArray();
            if (action == "treatmentSuggestions")
                return new
                {
                    items = proposals.Select(proposal => new
                    {
                        proposal.Index, proposal.Symbol, proposal.Status, proposal.Message,
                        proposal.VariantId, proposal.Species, proposal.Route, proposal.Dosing,
                        proposal.Withdrawal, proposal.Source, proposal.HasPdf, proposal.PdfPath,
                        groups = TreatmentSpeciesGroup.CompatibleGroups([proposal.Species]),
                        variantCount = variants.TryGetValue(proposal.Symbol, out var matched) ? matched.Count : 0,
                        nonMedicine = nonMedicines.Contains(proposal.Symbol)
                    }),
                    compatibility = draft.Rows.Select((row, index) =>
                    {
                        variants.TryGetValue(TreatmentAutofillPlanner.CatalogueSymbol(row), out var entries);
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

            string autoGroup = ReadTextField(root, "autoGroup", 10);
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
                    string.Equals(ReadTextField(current, "symbol", 128),
                        row.Get("Symbol Subiekta").Trim(), StringComparison.Ordinal) &&
                    string.Equals(ReadTextField(current, "species", 3000),
                        row.Get("Gatunek").Trim(), StringComparison.Ordinal) &&
                    string.Equals(ReadTextField(current, "route", 3000),
                        row.Get("Droga podania").Trim(), StringComparison.Ordinal) &&
                    string.Equals(ReadTextField(current, "dosing", 3000),
                        row.Get("Dawkowanie i okres stosowania").Trim(), StringComparison.Ordinal) &&
                    string.Equals(ReadTextField(current, "withdrawal", 3000),
                        row.Get("Karencja").Trim(), StringComparison.Ordinal) &&
                    Flag(current, "selected") == row.Printed;
                bool catalogueUnchanged = TreatmentAutofillPlanner.MatchesReviewed(
                    proposal,
                    ReadTextField(request,"species",3000), ReadTextField(request,"route",3000),
                    ReadTextField(request,"dosing",3000), ReadTextField(request,"withdrawal",3000),
                    ReadTextField(request,"source",3000), ReadTextField(request,"pdfPath",450),
                    Flag(request,"hasPdf"));
                if (!unchanged || !catalogueUnchanged || proposal.Status != "ready" ||
                    (autoGroup.Length > 0 && !TreatmentSpeciesGroup.Matches(autoGroup, [proposal.Species])) ||
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
            string id = ReadTextField(root, "draftId", 30);
            if (!drafts.TryGetValue(id, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            if (action == "readAnimalDescriptions")
                return new { items = await animalDescriptions.ReadAsync(
                    draft.Scope, draft.PayerId, token) };
            await animalDescriptions.SaveAsync(draft.Scope, draft.PayerId,
                ReadTextField(root, "description", 2000), token);
            return new { saved = true };
        }
        if (action == "readTreatmentChoices")
        {
            string id = ReadTextField(root, "draftId", 30);
            if (!drafts.TryGetValue(id, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            var saved = await treatmentChoices.ReadAsync(draft.Scope,
                draft.Rows.Select(row => row.Get("Symbol Subiekta")), token);
            return new { items = saved };
        }
        if (action == "setDraftSelections")
        {
            string id = ReadTextField(root, "draftId", 30);
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
                    !string.Equals(ReadTextField(inputs[index], "symbol", 128),
                        draft.Rows[index].Get("Symbol Subiekta").Trim(),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Stan dokumentu się zmienił. Ponownie otwórz dokument.");
            for (int index = 0; index < inputs.Length; index++)
                draft.Rows[index].Set("Drukuj", Flag(inputs[index], "selected").ToString());
            return new { updated = inputs.Length };
        }
        if (action == "treatmentReferences")
            return new { items = await Task.Run(catalogue.ReferenceProducts, token) };
        if (action == "setTreatmentReference")
        {
            string id = ReadTextField(root, "draftId", 30);
            int index = Int(root, "index") ?? -1;
            if (!drafts.TryGetValue(id, out var draft) || index < 0 || index >= draft.Rows.Count)
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            var row = draft.Rows[index];
            string symbol = ReadTextField(root, "referenceSymbol", 128);
            if (string.Equals(symbol, row.Get("Symbol Subiekta").Trim(), StringComparison.OrdinalIgnoreCase))
                symbol = "";
            var reference = symbol.Length > 0 ? await Task.Run(() => catalogue.Read(symbol), token) : null;
            var original = row.Get("Symbol Subiekta").Trim();
            string effective = reference?.Symbol ?? original;
            var (variants, _) = await Task.Run(() => catalogue.VariantsForSymbols([effective]), token);
            variants.TryGetValue(effective, out var entries);
            row.Set("Symbol zamiennika produktu", reference?.Symbol ?? "");
            row.Set("Zamiennik produktu", reference?.Name ?? "");
            foreach (var field in new[] { "Gatunek", "Droga podania", "Dawkowanie i okres stosowania",
                "Karencja", "Wskazanie ChPL", "Automatyczna karencja", "Automatyczne dawkowanie",
                "Źródło karencji", "Status karencji", "Domyślny gatunek", "Dokument kontekstu JSON",
                "Produkt kontekstu", "Pełna sekcja karencji źródła", "Pełna sekcja dawkowania źródła",
                "Pełna sekcja gatunków źródła", "SHA256 źródła" })
                row.Set(field, "", false);
            return new { replacement = reference?.Name ?? "", referenceSymbol = reference?.Symbol ?? "",
                groups = TreatmentSpeciesGroup.CompatibleGroups((entries ?? []).Select(v => v.Species)) };
        }
        if (action == "treatmentVariants" || action == "applyTreatmentVariant")
        {
            string draftId = ReadTextField(root,"draftId",30);
            int index = Int(root,"index") ?? -1;
            if (!drafts.TryGetValue(draftId,out var draft) ||
                index<0 || index>=draft.Rows.Count)
                throw new InvalidOperationException("Ponownie otwórz dokument przed wyborem wariantu ChPL.");
            var row = draft.Rows[index];
            string symbol = TreatmentAutofillPlanner.CatalogueSymbol(row);
            if (string.IsNullOrWhiteSpace(symbol))
                throw new InvalidOperationException(
                    "Pozycja dokumentu nie zawiera symbolu Subiekta. Wariant wybierz ręcznie w katalogu.");
            var product = await Task.Run(() => catalogue.Read(symbol),token);
            if (action == "treatmentVariants")
                return new {
                    product = product.Name,symbol = product.Symbol,
                    variants = product.Variants.Where(v=>v.Id.HasValue)
                        .Select(v => new {v.Id,v.Species,v.Indication,v.Route,v.Dose,
                            v.Tissues,v.Milk,v.Eggs,v.Remarks,v.Source,v.Pdf}).ToArray()
                };
            int variantId = Int(root,"variantId") ?? -1;
            var choice = product.Variants.SingleOrDefault(v=>v.Id == variantId);
            if (choice is null || string.IsNullOrWhiteSpace(choice.Species) ||
                string.IsNullOrWhiteSpace(choice.Route) || string.IsNullOrWhiteSpace(choice.Dose))
                throw new InvalidOperationException(
                    "Wybrany wariant jest niekompletny lub nie istnieje. Nie zmieniono karty.");
            // Save only after an explicit variant choice and opt-in; the
            // remembered species/route never changes clinical values itself.
            if (Flag(root, "remember") && row.Get("Symbol zamiennika produktu").Length == 0)
                await treatmentChoices.SaveAsync(draft.Scope, symbol,
                    choice.Species, choice.Route, token);
            return TreatmentVariantApplier.Apply(row, choice);
        }
        if (action == "updateDraft")
        {
            string id = ReadTextField(root, "draftId", 30);
            int index = Int(root, "index") ?? -1;
            string field = ReadTextField(root, "field", 80);
            if (!drafts.TryGetValue(id, out var draft) || index < 0 || index >= draft.Rows.Count)
                throw new InvalidOperationException("Ponownie otwórz dokument.");
            string[] allowed = ["Gatunek", "Droga podania", "Dawkowanie i okres stosowania",
                "Karencja", "Opis zwierząt / numer siedziby stada", "Liczba zwierząt",
                "Rozpoznanie", "Seria", "Zamiennik produktu", "Sekcja (II lub III)", "Drukuj"];
            if (!allowed.Contains(field, StringComparer.Ordinal)) throw new ArgumentException("Pole nie jest edytowalne.");
            string value = field == "Drukuj" ? Flag(root, "checked").ToString() : ReadTextField(root, "value", 3000);
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
            string draftId = ReadTextField(root, "draftId", 30);
            if (!drafts.TryGetValue(draftId, out var draft))
                throw new InvalidOperationException("Ponownie otwórz dokument przed zapisaniem lub podglądem.");
            static DateTime RequiredDate(JsonElement element, string name, string label)
            {
                string text = ReadTextField(element, name, 35);
                if (!DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException("Podaj datę i godzinę: " + label);
                return date;
            }
            // The complete edited snapshot travels with save/preview to avoid a
            // WebView2 blur-vs-click race leaving the last cell unsaved.
            if (!root.TryGetProperty("rows", out var submitted) ||
                submitted.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Nie przekazano aktualnych pozycji karty.");
            var snapshot = draft with { Rows = draft.Rows.Select(row => new TreatmentRow(row.Values)).ToList() };
            if (submitted.GetArrayLength() != draft.Rows.Count || draft.Rows.Count > 500)
                throw new InvalidOperationException("Liczba pozycji zmieniła się. Ponownie otwórz dokument.");
            var indexes = new HashSet<int>();
            foreach (var item in submitted.EnumerateArray())
            {
                int index = Int(item, "index") ?? -1;
                if (index < 0 || index >= draft.Rows.Count || !indexes.Add(index))
                    throw new InvalidOperationException("Niewłaściwy indeks pozycji.");
                var treatment = snapshot.Rows[index];
                treatment.Printed = Flag(item, "selected");
                foreach (var (key, field) in new[] {
                    ("batch", "Seria"), ("replacement", "Zamiennik produktu"),
                    ("descriptionGroup", CardSchema.AnimalDescriptionGroup),
                    ("countGroup", CardSchema.AnimalCountGroup),
                    ("species", "Gatunek"),
                    ("route", "Droga podania"), ("dosing", "Dawkowanie i okres stosowania"),
                    ("withdrawal", "Karencja"),
                    ("description", "Opis zwierząt / numer siedziby stada"),
                    ("animals", "Liczba zwierząt"), ("diagnosis", "Rozpoznanie")
                })
                    treatment.Set(field, ReadTextField(item, key, 3000));
                string section = ReadTextField(item, "section", 3);
                treatment.Set("Sekcja (II lub III)", section.Length == 0 ? "II" : section);
            }
            static DateTime? OptionalDate(JsonElement element, string name)
            {
                string text = ReadTextField(element, name, 35);
                if (string.IsNullOrWhiteSpace(text)) return null;
                if (!DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException("Niepoprawna data i godzina: " + name);
                return date;
            }
            var reported = OptionalDate(root, "reported");
            var performed = RequiredDate(root, "performed", "wykonanie czynności weterynaryjnych");
            string findings = ReadTextField(root, "findings", 2000);
            if (action == "previewDraft")
                return new { html = await Task.Run(() => cards.RenderDraft(snapshot, reported, performed, findings), token) };
            bool confirmDuplicate = Flag(root, "confirmDuplicate");
            return await Task.Run(() => cards.Save(snapshot, reported, performed, findings, confirmDuplicate), token);
        }
        throw new InvalidOperationException("Nieznana operacja interfejsu: " + action);
    }

    private async Task<(int? PayerId, string Scope)> ReadSavedCardPayer(
        Guid id, bool trash, CancellationToken token)
    {
        await using var db = ArchiveDb();
        await db.OpenAsync(token);
        using var query = new SqlCommand("""
            SELECT payer_id,source_scope FROM dbo.treatment_cards
            WHERE id=@id AND ((@trash=0 AND deleted_at_utc IS NULL)
                OR (@trash=1 AND deleted_at_utc IS NOT NULL));
            """, db) { CommandTimeout = 10 };
        query.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = id;
        query.Parameters.Add("@trash", System.Data.SqlDbType.Bit).Value = trash;
        await using var reader = await query.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Karta została usunięta lub przeniesiona.");
        return (reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.GetString(1));
    }

    private async Task<object> ReadSavedCards(CancellationToken token,
        bool trash, string query, string date)
    {
        DateTime? day = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
                throw new ArgumentException("Niepoprawna data filtra kart leczenia.");
            day = parsed;
        }
        var number = SavedCardSearch.ParseNumber(query);
        // Exact K/N/YYYY searches use the existing unique SQL number index;
        // free-text queries retain the legacy search_text behavior. Never
        // derive a scan-heavy computed number per row.
        string sql = number.HasValue ? """
            SELECT TOP (500) id, treatment_date, client, document, card_year, card_sequence
            FROM dbo.treatment_cards
            WHERE ((@trash=0 AND deleted_at_utc IS NULL) OR (@trash=1 AND deleted_at_utc IS NOT NULL))
              AND card_year=@year AND card_sequence=@sequence
              AND (@date IS NULL OR treatment_date=@date)
            ORDER BY treatment_date DESC, archive_order DESC;
            """ : """
            SELECT TOP (500) id, treatment_date, client, document, card_year, card_sequence
            FROM dbo.treatment_cards
            WHERE ((@trash=0 AND deleted_at_utc IS NULL) OR (@trash=1 AND deleted_at_utc IS NOT NULL))
              AND (@query=N'' OR CHARINDEX(@query,search_text)>0)
              AND (@date IS NULL OR treatment_date=@date)
            ORDER BY treatment_date DESC, archive_order DESC;
            """;
        await using var db = ArchiveDb();
        await db.OpenAsync(token);
        using var cmd = new SqlCommand(sql,db) { CommandTimeout=15 };
        cmd.Parameters.Add("@trash", System.Data.SqlDbType.Bit).Value = trash;
        cmd.Parameters.Add("@date", System.Data.SqlDbType.Date).Value =
            day.HasValue ? day.Value : DBNull.Value;
        if (number is { } cardNumber)
        {
            cmd.Parameters.Add("@year", System.Data.SqlDbType.Int).Value = cardNumber.Year;
            cmd.Parameters.Add("@sequence", System.Data.SqlDbType.BigInt).Value = cardNumber.Sequence;
        }
        else
            cmd.Parameters.Add("@query", System.Data.SqlDbType.NVarChar,-1).Value =
                query.Trim().ToUpperInvariant();
        var result = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string printedNumber = reader.IsDBNull(4) || reader.IsDBNull(5) ? "" :
                $"K/{reader.GetInt64(5)}/{reader.GetInt32(4):D4}";
            result.Add(new { id = reader.GetGuid(0).ToString("N"),
                date = reader.GetDateTime(1), client = reader.GetString(2),
                document = reader.GetString(3), number = printedNumber });
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
        ConnectionOptions options, CancellationToken token, Guid cardId, string confirmation)
    {
        await using var db = AdministratorDb(options, "KartyLeczenia");
        try
        {
            await db.OpenAsync(token);
        }
        catch (SqlException ex)
        {
            // Do not hide database connectivity failures behind the generic
            // Subiekt message (the Subiekt connection is unrelated).
            CrashLog.Write("ReactKL.Purge.SqlConnection", ex);
            string server = options.Server;
            throw new InvalidOperationException(
                $"Nie można połączyć się z bazą KartyLeczenia na serwerze „{server}”. " +
                "Sprawdź Ustawienia → Konto administratora SQL → Sprawdź konto. " +
                $"Kod SQL: {ex.Number}. Karta pozostaje w koszu.", ex);
        }
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, token);
        using (var access = new SqlCommand("""
            IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
                THROW 50041, N'Trwałe usunięcie wymaga uprawnienia sysadmin SQL Server.', 1;
            """, db, tx))
        {
            try { await access.ExecuteNonQueryAsync(token); }
            catch (SqlException ex) when (ex.Number == 50041)
            {
                throw new InvalidOperationException(
                    "Połączenie z bazą KartyLeczenia działa, ale konto SQL nie ma " +
                    "uprawnień administratora serwera (sysadmin), wymaganych do " +
                    "trwałego usunięcia karty. Windows Hello potwierdza tożsamość " +
                    "użytkownika Windows, lecz nie nadaje uprawnień SQL. " +
                    "Sprawdź konto w Ustawienia → Konto administratora SQL.", ex);
            }
        }
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

    private ConnectionOptions SubiektConnectionFrom(JsonElement root)
    {
        string server = ReadTextField(root, "server", 200);
        string database = ReadTextField(root, "database", 200);
        string username = ReadTextField(root, "userName", 200);
        string password = ReadTextField(root, "password", 200);
        bool windows = Flag(root, "windowsAuthentication");
        var previous = subiekt.Load().Options;
        if (!windows && password.Length == 0 && previous is not null &&
            !previous.WindowsAuthentication &&
            string.Equals(previous.Server, server, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previous.UserName, username, StringComparison.OrdinalIgnoreCase))
            password = previous.Password;
        return new(server, database, windows, username, password,
            Flag(root, "trustCertificate"), ReadTextField(root, "drugGroup", 80),
            Int(root, "warehouseId"));
    }

    private ConnectionOptions ArchiveConnectionFrom(JsonElement root,
        ConnectionSettingsStore? store = null, ConnectionOptions? program = null)
    {
        string server = program?.Server ?? ReadTextField(root, "server", 200);
        string userName = ReadTextField(root, "userName", 200);
        string password = ReadTextField(root, "password", 200);
        bool windows = Flag(root, "windowsAuthentication");
        var previous = (store ?? archive).Load().Options;
        if (!windows && password.Length == 0 && previous is not null &&
            !previous.WindowsAuthentication &&
            string.Equals(previous.Server, server, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previous.UserName, userName, StringComparison.OrdinalIgnoreCase))
            password = previous.Password;
        // The program database name is fixed; this never points to Subiekt.
        return new(server, "KartyLeczenia", windows, userName, password,
            program?.TrustCertificate ?? Flag(root, "trustCertificate"));
    }

    private ConnectionOptions AdministratorOptions()
    {
        var program = archive.Load().Options;
        var admin = administrator.Load().Options;
        if (program is null || admin is null ||
            !string.Equals(program.Server, admin.Server, StringComparison.OrdinalIgnoreCase) ||
            (!admin.WindowsAuthentication && string.IsNullOrEmpty(admin.Password)))
            throw new InvalidOperationException(
                "Zapisz konto dla bieżącej instancji w Ustawienia → Konto administratora SQL.");
        return admin with { Server = program.Server, TrustCertificate = program.TrustCertificate };
    }

    internal static SqlConnection AdministratorDb(ConnectionOptions settings, string database) =>
        new(new SqlConnectionStringBuilder(settings.BuildConnectionString(database))
        {
            ApplicationIntent = ApplicationIntent.ReadWrite,
            Pooling = false
        }.ConnectionString);

    private static async Task TestArchiveConnectionAsync(ConnectionOptions options,
        CancellationToken token)
    {
        await using var db = new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia"))
            { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
        await db.OpenAsync(token);
        using var command = new SqlCommand("""
            SELECT CASE WHEN
                OBJECT_ID(N'dbo.treatment_cards',N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.catalog_products',N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.clinic_settings',N'U') IS NOT NULL
                THEN 1 ELSE 0 END;
            """, db) { CommandTimeout = 8 };
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1)
            throw new InvalidOperationException(
                "Połączono z SQL, ale baza nie zawiera wymaganych tabel KartyLeczenia. " +
                "Sprawdź serwer i konfigurację instalatora.");
        foreach (string table in new[] { "treatment_cards", "catalog_products", "clinic_settings" })
        {
            using var read = new SqlCommand(
                $"SELECT TOP (0) * FROM dbo.[{table}];", db) { CommandTimeout = 8 };
            await using var reader = await read.ExecuteReaderAsync(token);
        }
    }

    private SqlConnection ArchiveDb()
    {
        var saved = archive.Load().Options ??
            throw new InvalidOperationException("Archiwum SQL nie jest skonfigurowane na tym stanowisku.");
        var connectionString = new SqlConnectionStringBuilder(
            saved.BuildConnectionString("KartyLeczenia")) {ApplicationIntent = ApplicationIntent.ReadWrite};
        return new SqlConnection(connectionString.ConnectionString);
    }

    private string ChooseArchiveFolder(string existing)
    {
        using var dialog = new FolderBrowserDialog
        { Description = "Wybierz folder archiwum SQL na pendrivie lub innym dysku" };
        if (Directory.Exists(existing)) dialog.InitialDirectory = existing;
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : "";
    }

    private static SqlBackupSettings BackupSettings(JsonElement root) => new(
        DestinationFolder: ReadTextField(root,"destinationFolder",1000),
        WorkingFolder: ReadTextField(root,"workingFolder",1000),
        Naming: ReadTextField(root,"naming",20),
        CustomName: ReadTextField(root,"customName",200),
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
    private static string ReadTextField(JsonElement root, string name, int maxLength)
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
