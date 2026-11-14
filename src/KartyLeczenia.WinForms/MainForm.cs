using System.Text.Json;

namespace KartyLeczenia.Windows;

internal sealed class MainForm : Form
{
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly DataGridView documents = NewGrid(), prices = NewGrid(), expiry = NewGrid(), treatment = NewGrid();
    readonly TextBox search = new() { Width = 280 };
    Font? boldDocumentKindFont;
    readonly Dictionary<DocumentSearchKinds, Button> documentKinds = new();
    readonly StatusStrip statusBar = new();
    readonly ToolStripStatusLabel status = new();
    readonly ToolStripStatusLabel refreshDocuments = new() { IsLink = true, Visible = false };
    CancellationTokenSource? preloadWork;
    readonly ComboBox period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    readonly DateTimePicker periodStart = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly DateTimePicker periodEnd = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly record struct DocumentListKey(DocumentSearchKinds Kind, string Query, DateTime? From, DateTime? Until);
    DocumentListKey? displayedDocuments;
    readonly DocumentService service = new();
    readonly ConnectionSettingsStore settings = new();
    readonly Dictionary<DocumentListKey, (DateTimeOffset LoadedAt, List<DocumentSuggestion> Rows, bool Truncated)> cache = new();
    ConnectionOptions? connection;
    CancellationTokenSource? searchWork;
    DocumentSearchKinds kind = DocumentSearchKinds.Invoices;
    DocumentData? currentCard;

    internal MainForm()
    {
        Text = "Karty leczenia zwierząt";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(850, 550);
        Size = new Size(1350, 830);
        statusBar.Items.Add(status);
        statusBar.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusBar.Items.Add(refreshDocuments);
        refreshDocuments.Click += async (_, _) => await Run(() => Search(true));
        tabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateRefreshLabel();
            CrashLog.Write($"WinForms.TabChanged index={tabs.SelectedIndex}");
        };
        FormClosed += (_, _) =>
        {
            CrashLog.Write("WinForms.MainWindow.Closed");
            preloadWork?.Cancel();
            searchWork?.Cancel();
        };
        Controls.Add(tabs);
        Controls.Add(statusBar);
        statusBar.BringToFront();
        ConfigureDocuments();
        ConfigurePrices();
        ConfigureExpiry();
        ConfigureCard();
        ConfigureConnection();
        var layout = new WindowLayout(this, rememberTreatment, new()
        {
            ["documents"] = documents, ["prices"] = prices,
            ["expiry"] = expiry, ["treatment"] = treatment
        });
        Load += (_, _) =>
        {
            layout.Restore();
            treatment.Columns["Drukuj"]!.DisplayIndex = 0;
            documents.Columns["DocumentType"]!.Visible = true;
        };
        FormClosed += (_, _) => layout.Save();
        tabs.SelectedIndex = connection is null ? 4 : 0;
        Shown += async (_, _) => { if (connection is not null) await LoadStartupDocuments(); };
    }

    static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToOrderColumns = true, AllowUserToResizeColumns = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window, GridColor = SystemColors.ControlDark,
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText,
            SelectionBackColor = SystemColors.Control,
            SelectionForeColor = SystemColors.ControlText,
            Alignment = DataGridViewContentAlignment.MiddleLeft
        },
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(244, 247, 250) }
    };

    static void Column(DataGridView grid, string name, string property, int width, string? format = null)
    {
        var column = new DataGridViewTextBoxColumn { Name = property, HeaderText = name, DataPropertyName = property, Width = width };
        if (format is not null) column.DefaultCellStyle.Format = format;
        grid.Columns.Add(column);
    }

    TabPage Page(string title, Control content, params Control[] controls)
    {
        var page = new TabPage(title) { Padding = new Padding(6) };
        page.Controls.Add(content);
        if (controls.Length > 0)
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 2, 0, 4) };
            bar.Controls.AddRange(controls);
            page.Controls.Add(bar);
        }
        tabs.TabPages.Add(page);
        return page;
    }

    Button Action(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) => await Run(action, text);
        return button;
    }

    async Task Run(Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var name = $"WinForms.Action [{operation}]";
        CrashLog.Write(name + ".Start");
        try
        {
            await action();
            CrashLog.Write($"{name}.Complete elapsedMs={timer.ElapsedMilliseconds}");
        }
        catch (OperationCanceledException)
        {
            CrashLog.Write($"{name}.Cancelled elapsedMs={timer.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            CrashLog.Write($"{name}.Failed elapsedMs={timer.ElapsedMilliseconds}", ex);
            status.Text = SubiektConnectionService.FriendlyError(ex);
        }
    }

    ConnectionOptions Connection() => connection ?? throw new InvalidOperationException("Skonfiguruj połączenie z Subiektem w Ustawieniach.");

    void ConfigureDocuments()
    {
        bool applyingRowHeight = false;
        documents.RowHeightChanged += (_, e) =>
        {
            if (applyingRowHeight || e.Row.Index < 0) return;
            applyingRowHeight = true;
            try
            {
                int height = e.Row.Height;
                documents.RowTemplate.Height = height;
                foreach (DataGridViewRow row in documents.Rows)
                    if (row.Height != height) row.Height = height;
            }
            finally { applyingRowHeight = false; }
        };
        var stack = new Bitmap(14, 14);
        using (var graphics = Graphics.FromImage(stack))
        {
            for (int offset = 4; offset >= 0; offset -= 2)
            {
                graphics.FillRectangle(SystemBrushes.Window, 1 + offset, 5 - offset, 7, 8);
                graphics.DrawRectangle(SystemPens.ControlDarkDark, 1 + offset, 5 - offset, 7, 8);
            }
        }
        Disposed += (_, _) => stack.Dispose();
        var documentType = new DataGridViewImageColumn
        {
            Name = "DocumentType",
            HeaderText = "R",
            ToolTipText = "Rodzaj dokumentu – stos kartek oznacza fakturę zbiorczą",
            Width = 30,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = null }
        };
        documents.Columns.Add(documentType);
        documents.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != documentType.Index) return;
            e.Value = kind == DocumentSearchKinds.Invoices
                && documents.Rows[e.RowIndex].DataBoundItem is DocumentSuggestion row
                && row.Related.Count(link => link.Number.StartsWith("WZ ", StringComparison.OrdinalIgnoreCase)) > 1
                ? stack : null;
            e.FormattingApplied = true;
        };
        Column(documents, "Data", "Date", 100, "dd.MM.yyyy");
        Column(documents, "Numer", "Number", 180);
        Column(documents, "Kontrahent", "Payer", 290);
        documents.Columns["Payer"]!.DataPropertyName = nameof(DocumentSuggestion.ShortPayer);
        Column(documents, "Wartość brutto", "Gross", 145, "N2");
        Column(documents, "Powiązane dokumenty", "RelatedText", 300);
        documents.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Run(OpenDocument); };
        search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Run(() => Search(true)); } };
        period.Items.AddRange(["(nieokreślony)", "bieżący dzień", "bieżący tydzień", "bieżący miesiąc",
            "bieżący kwartał", "bieżący rok", "ostatnie 7 dni", "ostatnie 30 dni", "dowolny miesiąc", "dowolny okres"]);
        period.SelectedIndex = 0;
        void UpdatePeriodControls()
        {
            periodStart.Visible = period.SelectedIndex >= 8;
            periodEnd.Visible = period.SelectedIndex == 9;
            periodStart.Format = period.SelectedIndex == 8 ? DateTimePickerFormat.Custom : DateTimePickerFormat.Short;
            periodStart.CustomFormat = "MMMM yyyy";
        }
        UpdatePeriodControls();
        period.SelectedIndexChanged += async (_, _) =>
        {
            UpdatePeriodControls();
            if (connection is not null) await Run(() => Search(false));
        };
        periodStart.ValueChanged += async (_, _) => { if (connection is not null) await Run(() => Search(false)); };
        periodEnd.ValueChanged += async (_, _) => { if (connection is not null) await Run(() => Search(false)); };
        var page = Page("Dokumenty", documents,
            DocumentKindButton(DocumentSearchKinds.Invoices, "Faktury sprzedaży", "Alt+1"),
            DocumentKindButton(DocumentSearchKinds.Receipts, "Paragony", "Alt+2"),
            DocumentKindButton(DocumentSearchKinds.WarehouseReleases, "Wydania zewnętrzne", "Alt+4"),
            new Label { Text = "Okres", AutoSize = true }, period, periodStart, periodEnd,
            search, Action("Szukaj", () => Search(true)), Action("Otwórz dokument", OpenDocument));
        var toolbar = page.Controls.OfType<FlowLayoutPanel>().Single();
        foreach (Control control in toolbar.Controls)
        {
            control.Anchor = AnchorStyles.None;
            control.Margin = new Padding(3);
        }
        UpdateDocumentKindButtons();
    }

    Button DocumentKindButton(DocumentSearchKinds value, string label, string shortcut)
    {
        var button = Action(label, () => SelectKind(value));
        if (boldDocumentKindFont is null)
        {
            boldDocumentKindFont = new Font(Font, Font.Style | FontStyle.Bold);
            Disposed += (_, _) => boldDocumentKindFont.Dispose();
        }
        button.Paint += (_, e) =>
        {
            if (kind != value) return;
            var bounds = button.ClientRectangle;
            int diameter = Math.Max(2, (int)Math.Round(8 * button.DeviceDpi / 96.0));
            diameter = Math.Min(diameter, Math.Min(bounds.Width, bounds.Height));
            if (diameter < 2) return;
            using var outline = new System.Drawing.Drawing2D.GraphicsPath();
            outline.AddArc(0, 0, diameter, diameter, 180, 90);
            outline.AddArc(bounds.Width - diameter, 0, diameter, diameter, 270, 90);
            outline.AddArc(bounds.Width - diameter, bounds.Height - diameter, diameter, diameter, 0, 90);
            outline.AddArc(0, bounds.Height - diameter, diameter, diameter, 90, 90);
            outline.CloseFigure();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(SystemBrushes.Highlight, outline);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, bounds, SystemColors.HighlightText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (button.Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3),
                    SystemColors.HighlightText, SystemColors.Highlight);
        };
        button.Font = boldDocumentKindFont;
        button.MinimumSize = button.GetPreferredSize(Size.Empty);
        button.AccessibleDescription = shortcut;
        documentKinds.Add(value, button);
        return button;
    }

    void UpdateDocumentKindButtons()
    {
        foreach (var (value, button) in documentKinds)
        {
            bool selected = value == kind;
            button.Font = selected ? boldDocumentKindFont : Font;
            button.Invalidate();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.L) && ActiveGrid() is { } grid)
        {
            ListParameters.Show(this, grid);
            return true;
        }
        if (keyData == Keys.F5 && tabs.SelectedIndex == 0)
        {
            _ = Run(() => Search(true));
            return true;
        }
        var selected = keyData switch
        {
            Keys.Alt | Keys.D1 => DocumentSearchKinds.Invoices,
            Keys.Alt | Keys.D2 => DocumentSearchKinds.Receipts,
            Keys.Alt | Keys.D4 => DocumentSearchKinds.WarehouseReleases,
            _ => DocumentSearchKinds.None
        };
        if (selected != DocumentSearchKinds.None)
        {
            _ = Run(() => SelectKind(selected));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    DataGridView? ActiveGrid() => tabs.SelectedIndex switch { 0 => documents, 1 => prices, 2 => expiry, 3 => treatment, _ => null };

    Task SelectKind(DocumentSearchKinds selected)
    {
        tabs.SelectedIndex = 0;
        kind = selected;
        documents.Columns["DocumentType"]!.Visible = kind == DocumentSearchKinds.Invoices;
        UpdateDocumentKindButtons();
        return Search(false);
    }

    DocumentListKey DocumentKey(DocumentSearchKinds type, string query)
    {
        var today = DateTime.Today;
        var week = today.AddDays(-((6 + (int)today.DayOfWeek) % 7));
        var month = new DateTime(today.Year, today.Month, 1);
        var quarter = new DateTime(today.Year, 1 + 3 * ((today.Month - 1) / 3), 1);
        var year = new DateTime(today.Year, 1, 1);
        var selectedMonth = new DateTime(periodStart.Value.Year, periodStart.Value.Month, 1);
        (DateTime? from, DateTime? until) = period.SelectedIndex switch
        {
            1 => (today, today.AddDays(1)),
            2 => (week, week.AddDays(7)),
            3 => (month, month.AddMonths(1)),
            4 => (quarter, quarter.AddMonths(3)),
            5 => (year, year.AddYears(1)),
            6 => (today.AddDays(-6), today.AddDays(1)),
            7 => (today.AddDays(-29), today.AddDays(1)),
            8 => (selectedMonth, selectedMonth.AddMonths(1)),
            9 => (periodStart.Value.Date, periodEnd.Value.Date.AddDays(1)),
            _ => ((DateTime?)null, (DateTime?)null)
        };
        if (from >= until) throw new InvalidOperationException("Data końcowa musi przypadać w dniu rozpoczęcia okresu lub później.");
        return new(type, query, from, until);
    }

    async Task LoadStartupDocuments()
    {
        var options = Connection();
        int startupPeriod = period.SelectedIndex;
        await Run(() => Search(false));
        if (IsDisposed || connection != options || searchWork is not null || period.SelectedIndex != startupPeriod) return;

        using var work = new CancellationTokenSource();
        preloadWork = work;
        try
        {
            foreach (var type in documentKinds.Keys)
            {
                work.Token.ThrowIfCancellationRequested();
                var key = DocumentKey(type, "");
                if (cache.ContainsKey(key)) continue;
                using var request = CancellationTokenSource.CreateLinkedTokenSource(work.Token);
                request.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    await LoadDocumentRows(options, key, request.Token);
                }
                catch (OperationCanceledException) when (work.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { }
                catch (Microsoft.Data.SqlClient.SqlException ex)
                {
                    CrashLog.Write($"WinForms.Documents.Preload.Failed kind={type}", ex);
                }
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        finally { preloadWork = null; }
    }

    async Task LoadDocumentRows(ConnectionOptions options, DocumentListKey key, CancellationToken token)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        CrashLog.Write($"WinForms.Documents.Query.Start kind={key.Kind} textFilter={key.Query.Length > 0} dateFilter={key.From.HasValue}");
        var rows = await service.SearchAsync(options, key.Query, token, key.Kind, key.From, key.Until, limit: 501, allowEmpty: true);
        token.ThrowIfCancellationRequested();
        CrashLog.Write($"WinForms.Documents.Query.Complete kind={key.Kind} rows={rows.Count} elapsedMs={timer.ElapsedMilliseconds}");
        var shown = rows.Take(500).ToList();
        if (cache.TryGetValue(key, out var previous)
            && previous.Rows.SequenceEqual(shown, DocumentRowComparer.Instance))
            shown = previous.Rows;
        if (cache.Count >= 32 && !cache.ContainsKey(key))
        {
            var oldestSearch = cache
                .MinBy(entry => entry.Value.LoadedAt);
            cache.Remove(oldestSearch.Key);
        }
        cache[key] = (DateTimeOffset.Now, shown, rows.Count > 500);
    }

    void UpdateRefreshLabel()
    {
        refreshDocuments.Visible = tabs.SelectedIndex == 0;
        refreshDocuments.Text = displayedDocuments is { } key && cache.TryGetValue(key, out var entry)
            ? $"Odśwież listę [F5] (ostatnie odświeżenie {entry.LoadedAt:HH:mm})"
            : "Odśwież listę [F5]";
    }

    async Task Search(bool refresh)
    {
        var options = Connection();
        searchWork?.Cancel();
        var key = DocumentKey(kind, search.Text.Trim());
        if (cache.TryGetValue(key, out var hit))
        {
            CrashLog.Write($"WinForms.Documents.CacheHit kind={key.Kind} rows={hit.Rows.Count}");
            ShowDocumentRows(key, cached: true);
            if (!refresh && DateTimeOffset.Now - hit.LoadedAt < TimeSpan.FromMinutes(5)) return;
        }
        else if (displayedDocuments is { } displayed && (displayed.Kind != key.Kind || displayed.From != key.From || displayed.Until != key.Until))
        {
            documents.DataSource = null;
            displayedDocuments = null;
            UpdateRefreshLabel();
        }

        preloadWork?.Cancel();
        using var work = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        searchWork = work;
        try
        {
            status.Text = documents.DataSource is null ? "Wczytywanie dokumentów…" : "Odświeżanie dokumentów…";
            await LoadDocumentRows(options, key, work.Token);
            work.Token.ThrowIfCancellationRequested();
            ShowDocumentRows(key, cached: false);
        }
        finally { if (ReferenceEquals(searchWork, work)) searchWork = null; }
    }

    void ShowDocumentRows(DocumentListKey key, bool cached)
    {
        var result = cache[key];
        if (!ReferenceEquals(documents.DataSource, result.Rows)) documents.DataSource = result.Rows;
        displayedDocuments = key;
        UpdateRefreshLabel();
        status.Text = $"{key.Kind} · {result.Rows.Count} dokumentów"
            + (cached ? " z pamięci" : "") + (result.Truncated ? " · zawęź wyszukiwanie" : "");
    }

    sealed class DocumentRowComparer : IEqualityComparer<DocumentSuggestion>
    {
        internal static readonly DocumentRowComparer Instance = new();

        public bool Equals(DocumentSuggestion? left, DocumentSuggestion? right) =>
            ReferenceEquals(left, right) || (left is not null && right is not null
                && (left with { Related = right.Related }) == right
                && left.Related.SequenceEqual(right.Related));

        public int GetHashCode(DocumentSuggestion row) => row.Id;
    }

    void ConfigurePrices()
    {
        Column(prices, "Lek", "Name", 340);
        Column(prices, "Najnowsza seria", "LatestBatch", 130);
        Column(prices, "Cena", "Gross", 100, "N2");
        Column(prices, "Karencja z katalogu", "Withdrawal", 520);
        var levels = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        var net = new CheckBox { Text = "Netto", AutoSize = true };
        bool loading = false;
        ConnectionOptions? levelsFor = null;
        async Task LoadPrices()
        {
            if (loading) return;
            loading = true;
            try
            {
                if (levelsFor != connection)
                {
                    levels.Items.Clear();
                    levelsFor = connection;
                }
                var options = Connection();
                using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (levels.Items.Count == 0)
                {
                    var available = await MedicineStockService.PriceLevels(options, work.Token);
                    if (connection != options) return;
                    levels.Items.AddRange(available);
                    levels.SelectedItem = levels.Items.Cast<MedicinePriceLevel>().FirstOrDefault(item => item.IsRetail);
                    if (levels.SelectedIndex < 0 && levels.Items.Count > 0) levels.SelectedIndex = 0;
                }
                if (levels.SelectedItem is not MedicinePriceLevel level) return;
                bool netPrice = net.Checked;
                var rows = await MedicineStockService.Prices(options, level.Number, work.Token, net: netPrice);
                var catalogue = await Task.Run(LocalCatalogue.Load);
                if (connection != options) return;
                prices.Columns["Gross"]!.HeaderText = netPrice ? "Cena netto" : "Cena brutto";
                prices.DataSource = rows.Select(row => row with { Withdrawal = catalogue.Summary(row.Symbol, row.Name) }).ToList();
                status.Text = $"Pozycje cennika: {rows.Count}";
            }
            finally { loading = false; }
        }

        var page = Page("Cennik leków", prices, levels, net, Action("Odśwież", LoadPrices), Action("Podgląd wydruku", PreviewPrices));
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedTab == page && connection is not null && prices.DataSource is null)
                await Run(LoadPrices);
        };
    }

    readonly PricePrintSettings pricePrintSettings = new();

    async Task PreviewPrices()
    {
        var rows = prices.Rows.Cast<DataGridViewRow>().Select(row => row.DataBoundItem)
            .OfType<MedicinePrice>().ToArray();
        if (rows.Length == 0)
        {
            status.Text = "Wczytaj cennik przed otwarciem podglądu wydruku.";
            return;
        }
        var catalogue = await Task.Run(LocalCatalogue.Load);
        using var editor = new PricePrintEditor(pricePrintSettings, rows, prices.Columns["Gross"]!.HeaderText, catalogue);
        if (editor.ShowDialog(this) == DialogResult.OK)
            await OpenPrintPreview("cennik-lekow", editor.Render(rows));
    }

    void ConfigureExpiry()
    {
        Column(expiry, "Lek", "Name", 340);
        Column(expiry, "Seria", "Batch", 150);
        Column(expiry, "Przyjęcie", "Received", 120, "dd.MM.yyyy");
        Column(expiry, "Ważność", "Expires", 120, "dd.MM.yyyy");
        Column(expiry, "Pozostało", "Remaining", 115, "0.####");
        Page("Terminy ważności", expiry, Action("Odśwież", async () =>
        {
            var options = Connection();
            using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var rows = await MedicineStockService.Expiries(options, work.Token);
            if (connection == options) expiry.DataSource = rows;
            status.Text = $"Dostawy: {rows.Count}";
        }));
    }

    readonly CheckBox rememberTreatment = new() { Text = "Zapamiętuj wybory", AutoSize = true };
    readonly ComboBox routeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
    readonly ComboBox withdrawalMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    readonly Dictionary<DataGridViewRow, LocalCatalogue.Variant> selectedTreatmentVariants = new();
    LocalCatalogue? treatmentCatalogue;
    bool updatingTreatmentChoices;

    void SetTreatmentChoices(DataGridViewRow gridRow, string species, string route,
        bool changed = false)
    {
        if (gridRow.Tag is not TreatmentRow row || treatmentCatalogue is null) return;
        var choices = treatmentCatalogue.ForProduct(row.Get("Symbol Subiekta"), row.Product);
        if (choices.Count == 0) return;
        var speciesList = choices.Select(v => v.Species).Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        species = speciesList.FirstOrDefault(item => string.Equals(item, species, StringComparison.OrdinalIgnoreCase))
            ?? speciesList.FirstOrDefault(item => string.Equals(item, "bydło", StringComparison.OrdinalIgnoreCase))
            ?? (speciesList.Length == 1 ? speciesList[0] : "");
        var matching = choices.Where(v => string.Equals(v.Species, species, StringComparison.OrdinalIgnoreCase)).ToArray();
        var labels = TreatmentRouteOptions(matching);
        var routes = labels.Keys.ToArray();
        route = routes.FirstOrDefault(item => string.Equals(item, route, StringComparison.OrdinalIgnoreCase))
            ?? (routes.Length == 1 ? routes[0] : "");
        updatingTreatmentChoices = true;
        try
        {
            SetCombo(TreatmentChoices.Species, speciesList, species);
            SetCombo(TreatmentChoices.Route, routes, route);

            void SetCombo(string column, string[] items, string value)
            {
                var cell = (DataGridViewComboBoxCell)gridRow.Cells[column];
                cell.Value = null;
                cell.Items.Clear();
                cell.Items.AddRange(items);
                cell.Value = value.Length > 0 ? value : null;
            }

            labels.TryGetValue(route, out var variant);
            if (variant is null) selectedTreatmentVariants.Remove(gridRow);
            else selectedTreatmentVariants[gridRow] = variant;
            treatment.InvalidateCell(gridRow.Cells[TreatmentChoices.Route]);
            gridRow.Cells["Dawkowanie i okres stosowania"].Value = variant?.Dose ?? "";
            gridRow.Cells["Karencja"].Value = variant?.Withdrawal ?? "";
            row.Set("Źródło karencji", variant?.Source ?? "", false);
            if (changed && rememberTreatment.Checked && species.Length > 0)
                TreatmentChoices.Save(row, species, route);
        }
        finally { updatingTreatmentChoices = false; }
    }

    static Dictionary<string, LocalCatalogue.Variant> TreatmentRouteOptions(IEnumerable<LocalCatalogue.Variant> variants)
    {
        var labels = new Dictionary<string, LocalCatalogue.Variant>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in variants.Distinct().GroupBy(v => v.Route, StringComparer.OrdinalIgnoreCase))
        {
            bool multiple = group.Skip(1).Any();
            foreach (var option in group)
            {
                string label = multiple && !string.IsNullOrWhiteSpace(option.Indication)
                    ? $"{option.Route} – {option.Indication}" : option.Route;
                string unique = label;
                for (int i = 2; labels.ContainsKey(unique); i++) unique = $"{label} ({i})";
                labels.Add(unique, option);
            }
        }
        return labels;
    }

    Task PickTreatmentRoute(DataGridViewRow row)
    {
        var cell = (DataGridViewComboBoxCell)row.Cells[TreatmentChoices.Route];
        if (cell.Items.Count == 0 || row.Tag is not TreatmentRow entry || treatmentCatalogue is null)
        {
            status.Text = "Brak wariantów ChPL dla wybranego gatunku.";
            return Task.CompletedTask;
        }
        var species = row.Cells[TreatmentChoices.Species].Value?.ToString() ?? "";
        var variants = treatmentCatalogue.ForProduct(entry.Get("Symbol Subiekta"), entry.Product)
            .Where(v => string.Equals(v.Species, species, StringComparison.OrdinalIgnoreCase)).ToArray();
        var options = TreatmentRouteOptions(variants).ToArray();
        var list = new ListBox { HorizontalScrollbar = true, IntegralHeight = false };
        var labels = options.Select(option => string.Join(" · ", new[]
        {
            option.Value.Route, option.Value.Species, option.Value.Indication,
            option.Value.Dose, option.Value.Withdrawal
        }.Where(value => !string.IsNullOrWhiteSpace(value))).ReplaceLineEndings("; ")).ToArray();
        list.Items.AddRange(labels);
        list.SelectedIndex = Array.FindIndex(options, option => option.Key == cell.Value as string);
        Rectangle bounds = treatment.GetCellDisplayRectangle(cell.ColumnIndex, row.Index, true);
        var origin = treatment.PointToScreen(new Point(bounds.Left, bounds.Bottom));
        var screen = Screen.FromPoint(origin).WorkingArea;
        int textWidth = labels.Select(label => TextRenderer.MeasureText(label, list.Font).Width).DefaultIfEmpty(0).Max();
        list.Size = new Size(Math.Min(screen.Width - 12, Math.Max(bounds.Width, textWidth + 30)),
            list.ItemHeight * Math.Clamp(labels.Length, 1, 10) + SystemInformation.HorizontalScrollBarHeight + 4);
        var popup = new ToolStripDropDown { Padding = Padding.Empty };
        popup.Items.Add(new ToolStripControlHost(list) { Padding = Padding.Empty, Margin = Padding.Empty });
        void Choose()
        {
            if (list.SelectedIndex < 0) return;
            var key = options[list.SelectedIndex].Key;
            popup.Close();
            _ = Run(() =>
            {
                SetTreatmentChoices(row, species, key, changed: true);
                return Task.CompletedTask;
            });
        }
        list.MouseClick += (_, e) =>
        {
            if (list.IndexFromPoint(e.Location) >= 0) Choose();
        };
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Choose(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; popup.Close(); }
        };
        popup.Closed += (_, _) => BeginInvoke((Action)popup.Dispose);
        int x = Math.Clamp(origin.X, screen.Left, screen.Right - list.Width - 4);
        int y = origin.Y + list.Height <= screen.Bottom ? origin.Y : Math.Max(screen.Top, origin.Y - bounds.Height - list.Height);
        popup.Show(new Point(x, y));
        list.Focus();
        return Task.CompletedTask;
    }

    string FormatWithdrawal(DataGridViewRow row, string text)
    {
        if (withdrawalMode.SelectedIndex != 1) return text;
        if (selectedTreatmentVariants.TryGetValue(row, out var variant)
            && text == variant.Withdrawal && !string.IsNullOrWhiteSpace(variant.ShortWithdrawal))
            return variant.ShortWithdrawal;
        return string.Join(", ", text
            .Replace("Tkanki jadalne:", "tk.", StringComparison.OrdinalIgnoreCase)
            .Replace("Tkanki:", "tk.", StringComparison.OrdinalIgnoreCase)
            .Replace("Mleko:", "ml.", StringComparison.OrdinalIgnoreCase)
            .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    void ConfigureCard()
    {
        treatment.DataError += (_, e) =>
        {
            CrashLog.Write($"WinForms.Treatment.GridError row={e.RowIndex} column={e.ColumnIndex} context={e.Context}", e.Exception);
            e.ThrowException = false;
            status.Text = "Błąd danych w tabeli karty leczenia. Szczegóły zapisano w logu.";
        };
        treatment.ReadOnly = false;
        treatment.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Drukuj", HeaderText = "Druk", Width = 48
        });
        foreach (var (title, width) in new[] { ("Produkt", 260), ("Ilość wydana", 100), ("Seria", 125),
            ("Gatunek", 115), ("Droga podania", 125), ("Dawkowanie i okres stosowania", 235), ("Karencja", 230) })
        {
            if (title is TreatmentChoices.Species or TreatmentChoices.Route)
                treatment.Columns.Add(new DataGridViewComboBoxColumn { Name = title, HeaderText = title, Width = width,
                    ReadOnly = title == TreatmentChoices.Route });
            else Column(treatment, title, title, width);
        }
        var hint = new ToolTip();
        hint.SetToolTip(rememberTreatment, "Zapisuje gatunek i drogę podania dla leku na kolejne karty leczenia.");
        Disposed += (_, _) => hint.Dispose();
        treatment.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || treatment.Columns[e.ColumnIndex].Name != TreatmentChoices.Route) return;
            if (selectedTreatmentVariants.TryGetValue(treatment.Rows[e.RowIndex], out var variant))
            {
                e.Value = routeMode.SelectedIndex == 0 ? AdministrationRouteLegend.Expand(variant.Route) : AdministrationRouteLegend.Abbreviate(variant.Route);
                e.FormattingApplied = true;
            }
        };
        treatment.CellClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && treatment.Columns[e.ColumnIndex].Name == TreatmentChoices.Route)
                await Run(() => PickTreatmentRoute(treatment.Rows[e.RowIndex]));
        };
        treatment.KeyDown += async (_, e) =>
        {
            if (e.KeyCode is not (Keys.Enter or Keys.F4) || treatment.CurrentCell?.OwningColumn?.Name != TreatmentChoices.Route
                || treatment.CurrentRow is not { } row) return;
            e.SuppressKeyPress = true;
            await Run(() => PickTreatmentRoute(row));
        };
        treatment.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (treatment.IsCurrentCellDirty && treatment.CurrentCell is DataGridViewComboBoxCell)
                treatment.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        treatment.CellValueChanged += async (_, e) =>
        {
            if (updatingTreatmentChoices || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string column = treatment.Columns[e.ColumnIndex].Name;
            if (column is not (TreatmentChoices.Species or TreatmentChoices.Route)) return;
            var gridRow = treatment.Rows[e.RowIndex];
            string species = gridRow.Cells[TreatmentChoices.Species].Value?.ToString() ?? "";
            string route = gridRow.Cells[TreatmentChoices.Route].Value?.ToString() ?? "";
            await Run(() =>
            {
                SetTreatmentChoices(gridRow, species, column == TreatmentChoices.Species ? "" : route, changed: true);
                return Task.CompletedTask;
            });
        };
        withdrawalMode.Items.AddRange(["Pełna karencja", "Skrót karencji"]);
        withdrawalMode.SelectedIndex = 0;
        withdrawalMode.SelectedIndexChanged += (_, _) =>
        {
            treatment.EndEdit();
            treatment.InvalidateColumn(treatment.Columns["Karencja"]!.Index);
        };
        treatment.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || treatment.Columns[e.ColumnIndex].Name != "Karencja"
                || e.Value is not string text || withdrawalMode.SelectedIndex != 1) return;
            if (treatment.IsCurrentCellInEditMode && treatment.CurrentCellAddress == new Point(e.ColumnIndex, e.RowIndex)) return;
            e.Value = FormatWithdrawal(treatment.Rows[e.RowIndex], text);
            e.FormattingApplied = true;
        };
        treatment.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox editor && treatment.CurrentCell is { OwningColumn.Name: "Karencja" } cell)
                editor.Text = cell.Value?.ToString() ?? "";
        };
        routeMode.Items.AddRange(["Pełna droga podania", "Skróty dróg podania"]);
        routeMode.SelectedIndex = 1;
        routeMode.SelectedIndexChanged += (_, _) =>
        {
            var column = treatment.Columns[TreatmentChoices.Route]!;
            bool full = routeMode.SelectedIndex == 0;
            column.DefaultCellStyle.WrapMode = full ? DataGridViewTriState.True : DataGridViewTriState.False;
            treatment.AutoSizeRowsMode = full ? DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders : DataGridViewAutoSizeRowsMode.None;
            if (!full)
                foreach (DataGridViewRow row in treatment.Rows) row.Height = treatment.RowTemplate.Height;
            treatment.InvalidateColumn(column.Index);
        };
        var page = Page("Karta leczenia", treatment, withdrawalMode, routeMode,
            Action("Otwórz ChPL", () =>
            {
                if (treatment.CurrentRow is { } row && selectedTreatmentVariants.TryGetValue(row, out var variant))
                    LocalCatalogue.OpenPdf(variant.Pdf);
                return Task.CompletedTask;
            }),
            Action("Zapisz kartę JSON", SaveCard), Action("Podgląd wydruku", PreviewCard));
        var actions = page.Controls.OfType<FlowLayoutPanel>().Single();
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.Controls.Remove(actions);
        actions.Dock = DockStyle.Fill;
        actions.Margin = Padding.Empty;
        toolbar.Controls.Add(actions, 0, 0);
        rememberTreatment.Anchor = AnchorStyles.Right;
        toolbar.Controls.Add(rememberTreatment, 1, 0);
        var routeLegend = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6, 4, 6, 6),
            Text = "Skróty dróg podania: " + AdministrationRouteLegend.Description,
            Visible = routeMode.SelectedIndex == 1
        };
        void ResizeRouteLegend() => routeLegend.MaximumSize = new Size(Math.Max(1, page.ClientSize.Width - page.Padding.Horizontal), 0);
        page.ClientSizeChanged += (_, _) => ResizeRouteLegend();
        routeMode.SelectedIndexChanged += (_, _) => routeLegend.Visible = routeMode.SelectedIndex == 1;
        ResizeRouteLegend();
        page.Controls.Add(routeLegend);
        page.Controls.Add(toolbar);
    }

    async Task OpenDocument()
    {
        if (documents.CurrentRow?.DataBoundItem is not DocumentSuggestion row) return;
        var options = Connection();
        using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var card = await service.ReadAsync(options, row.Number, 0, work.Token, row.Id);
        if (connection != options) return;
        var catalogue = await Task.Run(LocalCatalogue.Load);
        if (connection != options) return;
        treatmentCatalogue = catalogue;
        currentCard = card;
        selectedTreatmentVariants.Clear();
        treatment.Rows.Clear();
        foreach (var entry in card.Rows)
            if (catalogue.IsNonMedicine(entry.Get("Symbol Subiekta")))
                entry.Printed = false;
        foreach (var entry in card.Rows.OrderByDescending(entry => entry.Printed))
        {
            var gridRow = treatment.Rows[treatment.Rows.Add()];
            foreach (DataGridViewCell cell in gridRow.Cells)
                if (cell is not DataGridViewComboBoxCell && cell.OwningColumn is { } column)
                    cell.Value = entry.Get(column.Name);
            gridRow.Cells["Drukuj"].Value = entry.Printed;
            gridRow.Tag = entry;
            var saved = rememberTreatment.Checked ? TreatmentChoices.Read(entry) : (Species: "", Route: "");
            SetTreatmentChoices(gridRow, saved.Species, saved.Route);
        }
        tabs.SelectedIndex = 3;
        CrashLog.Write($"WinForms.Treatment.Loaded rows={card.Rows.Count}");
        status.Text = $"Pozycje karty: {card.Rows.Count}";
    }

    Card CaptureTreatmentCard(bool forPrint = false)
    {
        var source = currentCard ?? throw new InvalidOperationException("Najpierw wczytaj dokument.");
        treatment.EndEdit();
        var clinic = ClinicSettingsStore.Load();
        var fields = new Dictionary<string, string> { ["Dokument źródłowy"] = source.Number,
            ["Właściciel i adres"] = source.Payer, ["Zakres mapowania towarów"] = source.Scope,
            ["Nazwa i adres zakładu leczniczego dla zwierząt"] = clinic.Name + "\n" + clinic.Address };
        var rows = treatment.Rows.Cast<DataGridViewRow>().Select(item =>
        {
            var values = new Dictionary<string, string>(((TreatmentRow)item.Tag!).Values);
            foreach (DataGridViewCell cell in item.Cells)
                if (cell.OwningColumn is { } column)
                    values[column.Name] = column.Name == TreatmentChoices.Route
                        && selectedTreatmentVariants.TryGetValue(item, out var variant)
                        ? variant.Route : cell.Value?.ToString() ?? "";
            if (forPrint) values["Karencja"] = FormatWithdrawal(item, values.GetValueOrDefault("Karencja", ""));
            return values;
        }).ToList();
        return new Card(fields, rows);
    }

    async Task SaveCard()
    {
        var card = CaptureTreatmentCard();
        using var dialog = new SaveFileDialog { Filter = "Karta leczenia (*.json)|*.json", FileName = "karta-leczenia.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(card));
    }

    async Task PreviewCard()
    {
        var card = CaptureTreatmentCard(forPrint: true);
        await OpenPrintPreview("karta-leczenia", CardHtmlRenderer.Render(card));
    }

    static async Task OpenPrintPreview(string name, string html)
    {
        var file = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(file, html);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
    }

    void ConfigureConnection()
    {
        var saved = settings.Load().Options;
        if (saved is { WarehouseId: not null } && (saved.WindowsAuthentication || saved.Password.Length > 0)) connection = saved;
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        void Add(string title, Control input)
        {
            int row = form.RowCount++;
            form.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            form.Controls.Add(input, 1, row);
        }
        TextBox Field(string name, string? value, bool password = false)
        {
            var box = new TextBox { Width = 350, Text = value ?? "", UseSystemPasswordChar = password };
            Add(name, box);
            return box;
        }
        var server = Field("Serwer SQL", saved?.Server);
        var database = Field("Baza podmiotu", saved?.Database);
        var windows = new CheckBox { Checked = saved?.WindowsAuthentication ?? true };
        Add("Uwierzytelnianie Windows", windows);
        var user = Field("Użytkownik SQL", saved?.UserName);
        var password = Field("Hasło SQL", saved?.Password, true);
        var group = Field("Grupa towarów", saved?.DrugGroup ?? "Leki");
        var trust = new CheckBox { Checked = saved?.TrustCertificate ?? false };
        Add("Zaufaj certyfikatowi serwera", trust);
        var warehouses = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350, DisplayMember = "Name" };
        Add("Magazyn", warehouses);
        var clinic = ClinicSettingsStore.Load();
        var clinicName = Field("Nazwa lecznicy", clinic.Name);
        var clinicAddress = Field("Adres lecznicy", clinic.Address);
        var doctor = Field("Lekarz", clinic.Doctor);
        Add("", Action("Zapisz dane lecznicy", () =>
        {
            ClinicSettingsStore.Save(new(clinicName.Text, clinicAddress.Text, doctor.Text));
            status.Text = "Zapisano dane lecznicy.";
            return Task.CompletedTask;
        }));
        var remember = new CheckBox { Checked = settings.Load().Remembered };
        Add("Zapamiętaj hasło", remember);
        ConnectionOptions Draft() => new(server.Text, database.Text, windows.Checked, user.Text, password.Text, trust.Checked, group.Text);
        Add("", Action("Sprawdź połączenie i pobierz magazyny", async () =>
        {
            using var work = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var info = await new SubiektConnectionService().TestAsync(Draft(), work.Token);
            warehouses.DataSource = info.Warehouses.ToList();
            status.Text = "Połączono: " + info.Company;
        }));
        Add("", Action("Zapisz połączenie", async () =>
        {
            if (warehouses.SelectedItem is not MedicineWarehouse warehouse) throw new InvalidOperationException("Wybierz magazyn.");
            var draft = Draft() with { WarehouseId = warehouse.Id };
            using var work = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var info = await new SubiektConnectionService().TestAsync(draft, work.Token);
            if (!info.Warehouses.Any(item => item.Id == warehouse.Id)) throw new InvalidOperationException("Odśwież listę magazynów.");
            settings.Save(draft, remember.Checked);
            searchWork?.Cancel();
            preloadWork?.Cancel();
            cache.Clear();
            displayedDocuments = null;
            UpdateRefreshLabel();
            documents.DataSource = prices.DataSource = expiry.DataSource = null;
            selectedTreatmentVariants.Clear();
            treatment.Rows.Clear();
            currentCard = null;
            connection = draft;
            await SelectKind(DocumentSearchKinds.Invoices);
        }));
        Page("Ustawienia", form);

        foreach (var grid in new[] { documents, prices, expiry, treatment })
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Parametry listy…", null, (_, _) => ListParameters.Show(this, grid));
            grid.ContextMenuStrip = menu;
        }
    }
}
