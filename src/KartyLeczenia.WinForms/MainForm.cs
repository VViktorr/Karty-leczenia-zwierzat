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
        tabs.SelectedIndexChanged += (_, _) => UpdateRefreshLabel();
        FormClosed += (_, _) =>
        {
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
        var layout = new WindowLayout(this, new()
        {
            ["documents"] = documents, ["prices"] = prices,
            ["expiry"] = expiry, ["treatment"] = treatment
        });
        Load += (_, _) =>
        {
            layout.Restore();
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
        button.Click += async (_, _) => await Run(action);
        return button;
    }

    async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = SubiektConnectionService.FriendlyError(ex); }
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
                    System.Diagnostics.Trace.TraceWarning("Wstępne wczytanie dokumentów ({0}): {1}", type, ex.Number);
                }
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        finally { preloadWork = null; }
    }

    async Task LoadDocumentRows(ConnectionOptions options, DocumentListKey key, CancellationToken token)
    {
        var rows = await service.SearchAsync(options, key.Query, token, key.Kind, key.From, key.Until, limit: 501, allowEmpty: true);
        token.ThrowIfCancellationRequested();
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
        Page("Cennik leków", prices, levels, net, Action("Odśwież", async () =>
        {
            var options = Connection();
            using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (levels.Items.Count == 0)
            {
                levels.Items.AddRange(await MedicineStockService.PriceLevels(options, work.Token));
                levels.SelectedItem = levels.Items.Cast<MedicinePriceLevel>().FirstOrDefault(item => item.IsRetail);
                if (levels.SelectedIndex < 0 && levels.Items.Count > 0) levels.SelectedIndex = 0;
            }
            if (levels.SelectedItem is not MedicinePriceLevel level) return;
            var rows = await MedicineStockService.Prices(options, level.Number, work.Token, net: net.Checked);
            var catalogue = await Task.Run(LocalCatalogue.Load);
            if (connection != options) return;
            prices.DataSource = rows.Select(row => row with { Withdrawal = catalogue.Summary(row.Symbol, row.Name) }).ToList();
            status.Text = $"Pozycje cennika: {rows.Count}";
        }));
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

    void ConfigureCard()
    {
        treatment.ReadOnly = false;
        foreach (var (title, width) in new[] { ("Produkt", 260), ("Ilość wydana", 100), ("Seria", 125),
            ("Gatunek", 115), ("Droga podania", 125), ("Dawkowanie i okres stosowania", 235), ("Karencja", 230) })
            Column(treatment, title, title, width);
        Page("Karta leczenia", treatment, Action("Wybierz wariant ChPL", ChooseVariant),
            Action("Zapisz kartę JSON", SaveCard), Action("Podgląd HTML", PreviewCard));
    }

    async Task OpenDocument()
    {
        if (documents.CurrentRow?.DataBoundItem is not DocumentSuggestion row) return;
        var options = Connection();
        using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var card = await service.ReadAsync(options, row.Number, 0, work.Token, row.Id);
        if (connection != options) return;
        currentCard = card;
        treatment.Rows.Clear();
        foreach (var entry in card.Rows)
        {
            int i = treatment.Rows.Add(treatment.Columns.Cast<DataGridViewColumn>().Select(column => (object)entry.Get(column.Name)).ToArray());
            treatment.Rows[i].Tag = entry;
        }
        tabs.SelectedIndex = 3;
        status.Text = $"Pozycje karty: {card.Rows.Count}";
    }

    async Task ChooseVariant()
    {
        if (treatment.CurrentRow is not { Tag: TreatmentRow row } gridRow) return;
        treatment.EndEdit();
        var catalogue = await Task.Run(LocalCatalogue.Load);
        var choices = catalogue.ForProduct(row.Get("Symbol Subiekta"), row.Product);
        if (choices.Count == 0) { status.Text = "Brak wariantów w katalogu SQLite."; return; }
        using var dialog = new Form { Text = "Wariant z katalogu SQLite", Size = new Size(820, 480), StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        list.Items.AddRange(choices.Cast<object>().ToArray());
        list.SelectedIndex = 0;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var accept = Action("Użyj wariantu", () => { dialog.DialogResult = DialogResult.OK; return Task.CompletedTask; });
        actions.Controls.Add(accept);
        actions.Controls.Add(Action("Otwórz ChPL", () =>
        {
            if (list.SelectedItem is LocalCatalogue.Variant selected) LocalCatalogue.OpenPdf(selected.Pdf);
            return Task.CompletedTask;
        }));
        dialog.Controls.Add(list);
        dialog.Controls.Add(actions);
        dialog.AcceptButton = accept;
        if (dialog.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not LocalCatalogue.Variant variant) return;
        foreach (var (key, value) in new[] { ("Gatunek", variant.Species), ("Droga podania", variant.Route),
            ("Dawkowanie i okres stosowania", variant.Dose), ("Karencja", variant.Withdrawal) })
            gridRow.Cells[key].Value = value;
        row.Set("Źródło karencji", variant.Source, false);
    }

    Card Capture()
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
            foreach (DataGridViewCell cell in item.Cells) values[cell.OwningColumn.Name] = cell.Value?.ToString() ?? "";
            return values;
        }).ToList();
        return new Card(fields, rows);
    }

    async Task SaveCard()
    {
        var card = Capture();
        using var dialog = new SaveFileDialog { Filter = "Karta leczenia (*.json)|*.json", FileName = "karta-leczenia.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(card));
    }

    async Task PreviewCard()
    {
        var card = Capture();
        var file = Path.Combine(Path.GetTempPath(), $"karta-leczenia-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(file, CardHtmlRenderer.Render(card));
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
