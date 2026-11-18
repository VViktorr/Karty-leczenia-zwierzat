using System.Text.Json;

namespace KartyLeczenia.Windows;

internal sealed class MainForm : Form
{
    readonly TabControl tabs = new()
    {
        Dock = DockStyle.Fill,
        DrawMode = TabDrawMode.OwnerDrawFixed,
        Padding = new Point(30, 9)
    };
    readonly DocumentGrid documents = NewGrid<DocumentGrid>();
    readonly DataGridView prices = NewGrid(), expiry = NewGrid(), treatment = NewGrid();
    readonly TextBox search = new();
    Font? boldDocumentKindFont;
    readonly Dictionary<DocumentSearchKinds, Button> documentKinds = new();
    readonly StatusStrip statusBar = new();
    readonly ToolStripStatusLabel status = new();
    readonly ToolStripStatusLabel refreshDocuments = new() { IsLink = true, Visible = false };
    readonly ToolStripStatusLabel versionLabel = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    CancellationTokenSource? preloadWork;
    readonly ComboBox period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    readonly DateTimePicker periodStart = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly DateTimePicker periodEnd = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    readonly record struct DocumentListKey(DocumentSearchKinds Kind, string Query, DateTime? From, DateTime? Until);
    DocumentListKey? displayedDocuments;
    readonly DocumentService service = new();
    readonly ConnectionSettingsStore settings = new();
    readonly ConnectionSettingsStore archiveSettings = new(Path.Combine(SharedArchive.LocalDataFolder, "archive-connection.json"));
    SharedArchive? sharedArchive;
    readonly ClinicSettings localClinic = ClinicSettingsStore.Load();
    ClinicSettings? sharedClinic;
    readonly string installedArchiveServer = SharedArchive.ReadInstalledServer();
    bool savingTreatment;
    int descriptionEditVersion;
    readonly Dictionary<DocumentListKey, (DateTimeOffset LoadedAt, List<DocumentSuggestion> Rows, bool Truncated)> cache = new();
    ConnectionOptions? connection;
    CancellationTokenSource? searchWork;
    readonly System.Windows.Forms.Timer documentSearchTimer = new() { Interval = 350 };
    DocumentSearchKinds kind = DocumentSearchKinds.Invoices;
    DocumentData? currentCard;
    Guid currentArchiveId;
    string? currentCardNumber;
    DateTime? treatmentReportedAt;
    TimeSpan? treatmentExecutionTime;
    readonly DateTimePicker treatmentDate = new() { Format = DateTimePickerFormat.Short, Width = 110 };

    readonly int interfaceScale;
    AppearanceOptions appearance = AppearanceThemes.Load();
    Action<AppearanceOptions>? applyAppearance;
    static readonly string LastTabPath = Path.Combine(SharedArchive.LocalDataFolder, "winforms-last-tab.txt");

    internal MainForm(bool openSettings, int interfaceScale)
    {
        this.interfaceScale = interfaceScale;
        Text = $"KartyLeczenia — wersja {ServerUpdateGate.InstalledVersion}";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(850, 550);
        Size = new Size(1350, 830);
        statusBar.Items.Add(status);
        statusBar.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusBar.Items.Add(refreshDocuments);
        versionLabel.Text = $"Wersja {ServerUpdateGate.InstalledVersion}";
        versionLabel.ToolTipText = "Wersja zainstalowanego programu KartyLeczenia";
        statusBar.Items.Add(versionLabel);
        refreshDocuments.Click += async (_, _) => await Run(() => Search(true));
        tabs.DrawItem += DrawMainTab;
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (appearance.RememberLastTab && tabs.SelectedIndex >= 0)
            {
                try
                {
                    Directory.CreateDirectory(SharedArchive.LocalDataFolder);
                    File.WriteAllText(LastTabPath, tabs.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write("Appearance.SaveTab", ex); }
            }
            tabs.Invalidate();
            UpdateRefreshLabel();
            CrashLog.Write($"WinForms.TabChanged index={tabs.SelectedIndex}");
        };
        FormClosed += (_, _) =>
        {
            documentSearchTimer.Stop();
            CrashLog.Write("WinForms.MainWindow.Closed");
            preloadWork?.Cancel();
            searchWork?.Cancel();
        };
        Disposed += (_, _) => documentSearchTimer.Dispose();
        Controls.Add(tabs);
        Controls.Add(statusBar);
        statusBar.SendToBack();
        ConfigureDocuments();
        ConfigurePrices();
        ConfigureExpiry();
        ConfigureCard();
        var savedCardsGrid = ConfigureSavedCards();
        ConfigureConnection();
        var tables = new Dictionary<string, DataGridView>
        {
            ["documents"] = documents, ["prices"] = prices,
            ["expiry"] = expiry, ["treatment"] = treatment
        };
        if (interfaceScale > 100) EnlargeInterface(tables.Values.Append(savedCardsGrid));
        var layout = new WindowLayout(this, rememberTreatment, tables, interfaceScale);
        void ApplyAppearance(AppearanceOptions options)
        {
            appearance = AppearanceThemes.Normalize(options);
            AppearanceThemes.ApplySurface(this, appearance);
            AppearanceThemes.ApplyTabs(tabs, appearance);
            foreach (var grid in tables.Values.Append(savedCardsGrid))
                AppearanceThemes.ApplyGrid(grid, appearance);
            tabs.Invalidate();
        }
        applyAppearance = ApplyAppearance;
        Load += (_, _) =>
        {
            layout.Restore();
            ApplyAppearance(appearance);
            if (appearance.StartMaximized) WindowState = FormWindowState.Maximized;
            treatment.Columns["Drukuj"]!.DisplayIndex = 0;
            documents.Columns["DocumentType"]!.Visible = true;
        };
        FormClosed += (_, _) => layout.Save();
        int startupTab = 0;
        if (appearance.RememberLastTab)
        {
            try
            {
                if (int.TryParse(File.ReadAllText(LastTabPath), out int remembered))
                    startupTab = Math.Clamp(remembered, 0, tabs.TabCount - 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write("Appearance.RestoreTab", ex); }
        }
        tabs.SelectedIndex = openSettings || connection is null || sharedArchive is null ? tabs.TabCount - 1 : startupTab;
        Shown += async (_, _) => { if (connection is not null) await LoadStartupDocuments(); };
    }

    void EnlargeInterface(IEnumerable<DataGridView> grids)
    {
        float factor = interfaceScale / 100f;
        int Pixels(int value) => (int)Math.Round(value * factor);
        var gridSizes = grids.Select(grid => (Grid: grid, Height: grid.RowTemplate.Height,
            Widths: grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width).ToArray())).ToArray();

        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        tabs.Multiline = true;
        var tabPadding = tabs.Padding;
        Scale(new SizeF(factor, factor));
        tabs.Padding = new Point(Pixels(tabPadding.X), Pixels(tabPadding.Y));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, workingArea.Width), Math.Min(MinimumSize.Height, workingArea.Height));
        Size = new Size(Math.Min(Width, workingArea.Width), Math.Min(Height, workingArea.Height));

        foreach (var (grid, height, widths) in gridSizes)
        {
            grid.RowTemplate.Height = Math.Max(grid.RowTemplate.MinimumHeight, Pixels(height));
            for (int i = 0; i < widths.Length; i++)
                grid.Columns[i].Width = Math.Max(grid.Columns[i].MinimumWidth, Pixels(widths[i]));
        }
    }

    void DrawMainTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= tabs.TabCount) return;
        bool selected = e.Index == tabs.SelectedIndex;
        var bounds = e.Bounds;
        bounds.Inflate(-1, -1);
        if (!selected) bounds.Inflate(-1, -2);
        bool dark = appearance.Preset == "Ciemny";
        using var tabBrush = new SolidBrush(dark
            ? selected ? Color.FromArgb(53, 64, 78) : Color.FromArgb(38, 43, 52)
            : SystemColors.Control);
        e.Graphics.FillRectangle(tabBrush, bounds);

        using var selectedFont = selected ? new Font(tabs.Font.FontFamily, tabs.Font.SizeInPoints + 2, tabs.Font.Style | FontStyle.Bold, GraphicsUnit.Point) : null;
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, selectedFont ?? tabs.Font, bounds,
            dark ? Color.WhiteSmoke : SystemColors.ControlText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected && tabs.Focused)
        {
            bounds.Inflate(-3, -3);
            ControlPaint.DrawFocusRectangle(e.Graphics, bounds, SystemColors.ControlText, SystemColors.Control);
        }
    }

    sealed class DocumentGrid : DataGridView
    {
        public DocumentGrid() { }

        internal event EventHandler? OpenRequested;

        protected override bool ProcessDialogKey(Keys keyData)
            => TryOpen(keyData) || base.ProcessDialogKey(keyData);

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
            => TryOpen(e.KeyData) || base.ProcessDataGridViewKey(e);

        bool TryOpen(Keys keyData)
        {
            if (keyData != Keys.Enter) return false;
            if (CurrentRow?.DataBoundItem is DocumentSuggestion)
                OpenRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    static DataGridView NewGrid() => NewGrid<DataGridView>();

    static T NewGrid<T>() where T : DataGridView, new() => new T()
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

    SharedArchive Archive() => sharedArchive
        ?? throw new InvalidOperationException("Skonfiguruj połączenie z archiwum SQL w Ustawieniach.");

    static async Task<T> RunArchive<T>(Func<T> operation)
    {
        try { return await Task.Run(operation); }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            string message = ex.Number switch
            {
                50004 or 50012 or 50016 => ex.Message,
                50003 => "Najpierw uruchom nowy instalator w trybie Serwer i zaimportuj katalog leków.",
                207 or 208 or 229 or 50001 => "Sprawdź, czy przygotowano tabele archiwum i nadano rolę KartyLeczeniaOperator.",
                _ => SubiektConnectionService.FriendlyError(ex)
            };
            throw new InvalidOperationException("Archiwum SQL. " + message, ex);
        }
    }

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
        documents.OpenRequested += async (_, _) => await Run(OpenDocument);
        documents.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Run(OpenDocument); };
        documentSearchTimer.Tick += async (_, _) =>
        {
            documentSearchTimer.Stop();
            if (connection is not null) await Run(() => Search(false));
        };
        var searchField = new Panel { Width = 280, Height = search.PreferredHeight };
        var clearSearch = new Button
        {
            Text = "×", AccessibleName = "Wyczyść wyszukiwanie",
            Dock = DockStyle.Right, Width = 27, Enabled = search.TextLength > 0
        };
        search.Dock = DockStyle.Fill;
        searchField.Controls.Add(search);
        searchField.Controls.Add(clearSearch);
        clearSearch.Click += async (_, _) =>
        {
            search.Clear();
            search.Focus();
            if (connection is not null) await Run(() => Search(false));
        };
        search.TextChanged += (_, _) =>
        {
            clearSearch.Enabled = search.TextLength > 0;
            documentSearchTimer.Stop();
            if (connection is null) return;

            searchWork?.Cancel();
            documentSearchTimer.Start();
        };
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
            searchField, Action("Szukaj", () => Search(true)), Action("Otwórz dokument", OpenDocument));
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
        documentSearchTimer.Stop();
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
        const string missingLabel = "Sprawdź braki w katalogu";
        var missingButton = new Button { Text = missingLabel, AutoSize = true };
        missingButton.Click += async (_, _) => await Run(() => EditCatalogue(onlyMissing: true), missingLabel);

        async Task<MedicinePrice[]> RefreshMissing(ConnectionOptions options, SharedArchive archive, Catalogue? catalogue = null)
        {
            missingButton.Enabled = false;
            missingButton.Text = "Sprawdzanie katalogu…";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                catalogue ??= await RunArchive(archive.ReadCatalogue);
                var products = await MedicineStockService.FindCatalogueProducts(options, "", timeout.Token, limit: int.MaxValue);
                if (connection != options || sharedArchive != archive) return [];
                var missing = products.Where(row => catalogue.NeedsCompletion(row.Symbol)).ToArray();
                missingButton.Text = $"Leki do uzupełnienia ({missing.Length})";
                return missing;
            }
            finally
            {
                if (missingButton.Text == "Sprawdzanie katalogu…") missingButton.Text = missingLabel;
                missingButton.Enabled = true;
            }
        }

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
                var archive = Archive();
                if (levels.SelectedItem is not MedicinePriceLevel level)
                {
                    await RefreshMissing(options, archive);
                    return;
                }
                bool netPrice = net.Checked;
                var rows = await MedicineStockService.Prices(options, level.Number, work.Token, net: netPrice);
                var catalogue = await RunArchive(archive.ReadCatalogue);
                if (connection != options || sharedArchive != archive) return;
                prices.Columns["Gross"]!.HeaderText = netPrice ? "Cena netto" : "Cena brutto";
                prices.DataSource = rows.Select(row => row with { Withdrawal = catalogue.Summary(row.Symbol) }).ToList();
                status.Text = $"Pozycje cennika: {rows.Count}";
                await RefreshMissing(options, archive, catalogue);
            }
            finally { loading = false; }
        }

        async Task EditCatalogue(bool onlyMissing = false)
        {
            var archive = Archive();
            var options = Connection();
            var current = prices.CurrentRow?.DataBoundItem as MedicinePrice;
            var pending = onlyMissing ? await RefreshMissing(options, archive) : null;
            if (connection != options || sharedArchive != archive) return;
            if (pending is { Length: 0 })
            {
                status.Text = "W katalogu nie ma leków do uzupełnienia.";
                return;
            }
            using var picker = new CatalogueProductPicker(options, onlyMissing ? "" : current?.Symbol ?? "", pending);
            if (DialogPlacement.Show(picker, this, "catalogue-product-picker") != DialogResult.OK || picker.Selection is not { } row) return;
            var product = await RunArchive(() => archive.ReadCatalogueProduct(row));
            if (sharedArchive != archive || connection != options) return;
            using var editor = new CatalogueEditor(product, async (draft, attachments) =>
            {
                await RunArchive(() => { archive.SaveCatalogueProduct(draft, attachments); return true; });
            });
            if (DialogPlacement.Show(editor, this, "catalogue-product-editor") != DialogResult.OK) return;
            await LoadPrices();
            status.Text = $"Zapisano kartotekę {row.Symbol} w katalogu SQL.";
        }

        var page = Page("Cennik leków", prices, levels, net, Action("Odśwież", LoadPrices),
            missingButton, Action("Dodaj / edytuj w katalogu", () => EditCatalogue()),
            Action("Ustawienia wydruku", () => PreviewPrices()),
            Action("Drukuj", () => PreviewPrices(direct: true)));
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedTab != page || connection is not { } options || sharedArchive is not { } archive) return;
            if (prices.DataSource is null) await Run(LoadPrices);
            else if (missingButton.Enabled) await Run(async () => { await RefreshMissing(options, archive); });
        };
    }

    readonly PricePrintSettings pricePrintSettings = PricePrintSettings.Load();

    async Task PreviewPrices(bool direct = false)
    {
        var rows = prices.Rows.Cast<DataGridViewRow>().Select(row => row.DataBoundItem)
            .OfType<MedicinePrice>().ToArray();
        if (rows.Length == 0)
        {
            status.Text = "Wczytaj cennik przed otwarciem podglądu wydruku.";
            return;
        }
        var archive = Archive();
        var catalogue = await RunArchive(archive.ReadCatalogue);
        if (sharedArchive != archive) return;
        using var editor = new PricePrintEditor(pricePrintSettings, rows, prices.Columns["Gross"]!.HeaderText, catalogue);
        if (direct)
        {
            using var preview = new PricePrintPreview(editor.Render(rows), pricePrintSettings.Landscape, pricePrintSettings.Margin, direct: true);
            DialogPlacement.Show(preview, this, "price-print-direct");
        }
        else DialogPlacement.Show(editor, this, "price-print-settings");
    }

    async Task OpenCataloguePdf(string path)
    {
        var archive = Archive();
        var file = await RunArchive(() => archive.ExportCataloguePdf(path));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
    }

    void ConfigureExpiry()
    {
        Column(expiry, "Lek", "Name", 340);
        Column(expiry, "Seria", "Batch", 150);
        Column(expiry, "Przyjęcie", "Received", 120, "dd.MM.yyyy");
        Column(expiry, "Ważność", "Expires", 120, "dd.MM.yyyy");
        Column(expiry, "Pozostało", "Remaining", 115, "0.####");
        ConnectionOptions? loadingFor = null;
        ConnectionOptions? loadedFor = null;
        List<MedicineExpiry> deliveries = [];
        int? daysFilter = null;
        TabPage? page = null;

        void ShowExpiries()
        {
            if (loadedFor is null || !ReferenceEquals(connection, loadedFor)) return;
            var today = DateTime.Today;
            var rows = deliveries.Where(row => daysFilter switch
                {
                    null => true,
                    < 0 => row.Expires is { } date && date.Date < today,
                    _ => row.Expires is { } date && date.Date >= today && date.Date <= today.AddDays(daysFilter.GetValueOrDefault())
                })
                .OrderBy(row => row.Expires is null)
                .ThenBy(row => row.Expires)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.Batch, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            expiry.DataSource = rows;
            if (tabs.SelectedTab == page) status.Text = $"Dostawy: {rows.Count} / {deliveries.Count}";
        }

        async Task LoadExpiries()
        {
            var options = Connection();
            if (ReferenceEquals(loadingFor, options)) return;
            loadingFor = options;
            try
            {
                using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var rows = await MedicineStockService.Expiries(options, work.Token);
                if (!ReferenceEquals(connection, options) || IsDisposed) return;
                deliveries = rows;
                loadedFor = options;
                ShowExpiries();
            }
            finally
            {
                if (ReferenceEquals(loadingFor, options)) loadingFor = null;
            }
        }

        RadioButton Filter(string text, int? days)
        {
            var button = new RadioButton { Text = text, Appearance = Appearance.Button, AutoSize = true, Checked = days is null };
            button.CheckedChanged += (_, _) =>
            {
                if (!button.Checked) return;
                daysFilter = days;
                ShowExpiries();
            };
            return button;
        }

        page = Page("Terminy ważności", expiry, Action("Odśwież", LoadExpiries),
            Filter("Wszystkie", null), Filter("Przeterminowane", -1), Filter("Do 30 dni", 30), Filter("Do 60 dni", 60));
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedTab != page || connection is null) return;
            if (expiry.DataSource is null || !ReferenceEquals(connection, loadedFor)) await Run(LoadExpiries);
            else ShowExpiries();
        };
    }

    readonly CheckBox rememberTreatment = new() { Text = "Zapamiętuj wybory", AutoSize = true };
    readonly ComboBox routeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165 };
    readonly ComboBox withdrawalMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    readonly Dictionary<DataGridViewRow, Catalogue.Variant> selectedTreatmentVariants = new();
    Catalogue? treatmentCatalogue;
    bool updatingTreatmentChoices;
    const string ReferenceColumn = "Zamiennik produktu";
    const string AnimalDescriptionColumn = "Numery zwierząt";
    const string IndicationColumn = "Wskazanie z ChPL";
    readonly ComboBox cardSpeciesGroup = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    readonly Dictionary<DataGridViewRow, bool> excludedTreatmentRows = new();

    bool MatchesSpeciesGroup(string species)
    {
        if (cardSpeciesGroup.SelectedIndex <= 0) return true;
        if (species.StartsWith("WARIANT DO WERYFIKACJI", StringComparison.OrdinalIgnoreCase)
            || species.StartsWith("ZAWIESZONE", StringComparison.OrdinalIgnoreCase)) return false;
        var pattern = cardSpeciesGroup.SelectedIndex switch
        {
            1 => @"\b(?:bydł|ciel|krow|jałów|buhaj)\p{L}*",
            2 => @"\b(?:świń|świni|prosi|warchlak|tucznik|loch|losz)\p{L}*",
            3 => @"\b(?:drób|drobi|kur|brojler|niosk|indyk|indycz|kaczk|gęś|gęsi|przepiór|kuropatw)\p{L}*",
            4 => @"\b(?:pies|psy|psa|psów|suk[aięom])\p{L}*",
            5 => @"\bkot\p{L}*",
            _ => ""
        };
        return System.Text.RegularExpressions.Regex.IsMatch(species, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    void ApplyCardSpeciesGroup()
    {
        if (currentCard is null || !treatment.EndEdit()) return;
        var rows = treatment.Rows.Cast<DataGridViewRow>().ToArray();
        foreach (var row in rows)
        {
            if (row.Tag is not TreatmentRow entry) continue;
            bool matches = cardSpeciesGroup.SelectedIndex <= 0 || TreatmentVariants(row, entry).Any(v => MatchesSpeciesGroup(v.Species));
            if (matches && excludedTreatmentRows.Remove(row, out bool wasChecked))
                row.Cells["Drukuj"].Value = wasChecked;
            else if (!matches)
            {
                excludedTreatmentRows.TryAdd(row, row.Cells["Drukuj"].Value is true);
                row.Cells["Drukuj"].Value = false;
            }

            SetTreatmentChoices(row, row.Cells[TreatmentChoices.Species].Value?.ToString() ?? "",
                row.Cells[TreatmentChoices.Route].Value?.ToString() ?? "", preserveEdits: true);
        }
        if (rows.Length > 1)
            treatment.Sort(new TreatmentGroupOrder(rows, excludedTreatmentRows));
    }

    sealed class TreatmentGroupOrder(DataGridViewRow[] rows, Dictionary<DataGridViewRow, bool> excluded)
        : System.Collections.IComparer
    {
        readonly Dictionary<DataGridViewRow, int> positions = rows
            .Select((row, index) => (row, index)).ToDictionary(item => item.row, item => item.index);

        public int Compare(object? x, object? y)
        {
            if (x is not DataGridViewRow left || y is not DataGridViewRow right) return 0;
            int groupOrder = excluded.ContainsKey(left).CompareTo(excluded.ContainsKey(right));
            return groupOrder != 0 ? groupOrder : positions[left].CompareTo(positions[right]);
        }
    }

    sealed record TreatmentReference(string Symbol, string Name)
    {
        public override string ToString() => $"{Name} [{Symbol}]";
    }

    IReadOnlyList<Catalogue.Variant> TreatmentVariants(DataGridViewRow row, TreatmentRow entry)
    {
        if (treatmentCatalogue is null) return [];
        return row.Cells[ReferenceColumn].Tag is TreatmentReference reference
            ? treatmentCatalogue.ForProduct(reference.Symbol)
            : treatmentCatalogue.ForProduct(entry.Get("Symbol Subiekta"));
    }

    void PickTreatmentReference(DataGridViewRow row)
    {
        if (row.Tag is not TreatmentRow entry || treatmentCatalogue is null) return;
        var cell = row.Cells[ReferenceColumn];
        var products = treatmentCatalogue.ReferenceProducts()
            .Where(item => !string.Equals(item.Symbol, entry.Get("Symbol Subiekta").Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(item => new TreatmentReference(item.Symbol, item.Name)).ToArray();
        using var dialog = new Form
        {
            Text = ReferenceColumn, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(650, 400), MinimumSize = new Size(400, 250)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Wpisz nazwę lub symbol produktu" };
        var picker = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        var apply = new Button { Text = "Wybierz", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange([apply, cancel]);
        layout.Controls.Add(filter, 0, 0);
        layout.Controls.Add(picker, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        dialog.Controls.Add(layout);

        void FilterProducts()
        {
            var words = filter.Text.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var selected = picker.SelectedItem as TreatmentReference ?? cell.Tag as TreatmentReference;
            picker.BeginUpdate();
            try
            {
                picker.Items.Clear();
                if (words.Length == 0) picker.Items.Add("Preparat z dokumentu");
                picker.Items.AddRange(products.Where(product => words.All(word =>
                    product.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || product.Symbol.Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray());
                int index = selected is null ? -1 : picker.Items.IndexOf(selected);
                picker.SelectedIndex = index >= 0 ? index : picker.Items.Count > 0 ? 0 : -1;
            }
            finally { picker.EndUpdate(); }
        }
        picker.SelectedIndexChanged += (_, _) => apply.Enabled = picker.SelectedIndex >= 0;
        picker.MouseDoubleClick += (_, e) =>
        {
            if (picker.IndexFromPoint(e.Location) >= 0) apply.PerformClick();
        };
        filter.TextChanged += (_, _) => FilterProducts();
        FilterProducts();
        apply.Enabled = picker.SelectedIndex >= 0;
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        if (DialogPlacement.Show(dialog, this, "treatment-reference") != DialogResult.OK) return;

        var reference = picker.SelectedItem as TreatmentReference;
        if (Equals(cell.Tag, reference)) return;
        cell.Tag = reference;
        cell.Value = reference?.Name ?? "Wybierz…";
        SetTreatmentChoices(row, "", "");
        ApplyCardSpeciesGroup();
    }

    void SetTreatmentChoices(DataGridViewRow gridRow, string species, string route,
        bool changed = false, bool preserveEdits = false)
    {
        if (gridRow.Tag is not TreatmentRow row || treatmentCatalogue is null) return;
        var choices = TreatmentVariants(gridRow, row).Where(v => MatchesSpeciesGroup(v.Species)).ToArray();
        selectedTreatmentVariants.TryGetValue(gridRow, out var previousVariant);
        bool usesReference = gridRow.Cells[ReferenceColumn].Tag is TreatmentReference;
        var speciesList = choices.Select(v => v.Species).Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        species = speciesList.FirstOrDefault(item => string.Equals(item, species, StringComparison.OrdinalIgnoreCase))
            ?? (!usesReference && speciesList.Length == 1 ? speciesList[0] : "");
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
            if (!preserveEdits || previousVariant != variant)
            {
                gridRow.Cells["Dawkowanie i okres stosowania"].Value = variant?.Dose ?? "";
                gridRow.Cells["Karencja"].Value = variant?.Withdrawal ?? "";
                row.Set("Źródło karencji", variant?.Source ?? "", false);
            }
            if (changed && !usesReference && rememberTreatment.Checked && species.Length > 0 && currentCard is { } card)
                Archive().SaveTreatmentChoice(card.Scope, TreatmentChoices.Key(row), species, route);
        }
        finally { updatingTreatmentChoices = false; }
    }

    static Dictionary<string, Catalogue.Variant> TreatmentRouteOptions(IEnumerable<Catalogue.Variant> variants)
    {
        var labels = new Dictionary<string, Catalogue.Variant>(StringComparer.OrdinalIgnoreCase);
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
        var variants = TreatmentVariants(row, entry)
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

    void ShowTreatmentIndication(DataGridViewRow row)
    {
        if (!selectedTreatmentVariants.TryGetValue(row, out var variant))
        {
            status.Text = "Wybierz gatunek i drogę podania w tym wierszu, aby zobaczyć wskazanie z ChPL.";
            return;
        }

        using var dialog = new Form
        {
            Text = IndicationColumn, StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(560, 260), MinimumSize = new Size(360, 190)
        };
        var heading = new Label
        {
            Text = $"{variant.Species} · {variant.Route}", Dock = DockStyle.Top,
            AutoSize = true, Padding = new Padding(8)
        };
        var indication = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = string.IsNullOrWhiteSpace(variant.Indication)
                ? "Brak zapisanego wskazania dla wybranego wariantu."
                : variant.Indication.ReplaceLineEndings(Environment.NewLine)
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        var close = new Button { Text = "Zamknij", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close);
        dialog.Controls.Add(indication);
        dialog.Controls.Add(heading);
        dialog.Controls.Add(actions);
        dialog.CancelButton = close;
        DialogPlacement.Show(dialog, this, "treatment-indication");
    }

    async Task EditSharedAnimals(DataGridViewRow selected)
    {
        if (!treatment.EndEdit() || currentCard is not { } card || selected.Tag is not TreatmentRow entry) return;
        if (selected.Cells["Drukuj"].Value is not true)
        {
            status.Text = "Zaznacz pozycję do wydruku, aby wpisać wspólne dane zwierząt.";
            return;
        }

        string species = selected.Cells[TreatmentChoices.Species].Value?.ToString() ?? "";
        string section = entry.Get("Sekcja (II lub III)");
        var rows = treatment.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Cells["Drukuj"].Value is true && row.Tag is TreatmentRow item &&
                item.Get("Sekcja (II lub III)") == section &&
                string.Equals(row.Cells[TreatmentChoices.Species].Value?.ToString() ?? "", species, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        rows = SelectAnimalGroupRows(rows, selected);
        if (rows.Length == 0) return;
        string? numbers = PromptAnimalField("Numery zwierząt",
            $"Wpisz numery wspólne dla pozycji do wydruku ({rows.Length}).",
            selected.Cells[AnimalDescriptionColumn].Value?.ToString() ?? "", multiline: true);
        if (numbers is null) return;

        string group = Guid.NewGuid().ToString("N");
        Apply(AnimalDescriptionColumn, CardSchema.AnimalDescriptionGroup, numbers);
        const string countColumn = "Liczba zwierząt";
        string? count = PromptAnimalField("Liczba leczonych zwierząt",
            "Wpisz wspólną liczbę leczonych zwierząt.",
            selected.Cells[countColumn].Value?.ToString() ?? "", multiline: false);
        if (count is not null) Apply(countColumn, CardSchema.AnimalCountGroup, count);

        if (numbers.Length > 0 && card.PayerId is > 0)
        {
            var archive = Archive();
            await RunArchive(() =>
            {
                archive.SaveDescription(card, numbers);
                return true;
            });
        }

        void Apply(string column, string groupField, string value)
        {
            foreach (var row in rows)
            {
                row.Cells[column].Value = value;
                ((TreatmentRow)row.Tag!).Values[groupField] = group;
            }
        }
    }

    DataGridViewRow[] SelectAnimalGroupRows(DataGridViewRow[] candidates, DataGridViewRow selected)
    {
        if (candidates.Length <= 1) return candidates;
        using var dialog = new Form
        {
            Text = "Pozycje grupy zwierząt", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(570, 350), MinimumSize = new Size(420, 260), AutoScaleMode = AutoScaleMode.Dpi
        };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
        string Group(DataGridViewRow row) => ((TreatmentRow)row.Tag!).Get(CardSchema.AnimalDescriptionGroup);
        string selectedGroup = Group(selected);
        foreach (var row in candidates)
        {
            string label = $"{row.Index + 1}. {row.Cells["Produkt"].Value} · {row.Cells["Seria"].Value}";
            list.Items.Add(label, Group(row) == selectedGroup);
        }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        var next = new Button { Text = "Dalej", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange([cancel, next]);
        dialog.Controls.Add(list);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = next;
        dialog.CancelButton = cancel;
        list.ItemCheck += (_, e) =>
            next.Enabled = list.CheckedItems.Count + (e.NewValue == CheckState.Checked ? 1 : -1) > 0;
        return DialogPlacement.Show(dialog, this, "treatment-animal-group") == DialogResult.OK
            ? list.CheckedIndices.Cast<int>().Select(index => candidates[index]).ToArray()
            : [];
    }

    string? PromptAnimalField(string title, string label, string value, bool multiline)
    {
        using var dialog = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false, AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(550, multiline ? 310 : 170), MinimumSize = new Size(350, 165)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var input = new TextBox
        {
            AccessibleName = title, Text = value, Dock = DockStyle.Fill, Multiline = multiline,
            AcceptsReturn = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Zapisz", AutoSize = true };
        buttons.Controls.AddRange([cancel, save]);
        layout.Controls.Add(new Label { Text = label, AutoSize = true }, 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        save.Click += (_, _) =>
        {
            string text = input.Text.Trim();
            if (!multiline && (!int.TryParse(text, out int count) || count <= 0))
            {
                MessageBox.Show(dialog, "Wpisz dodatnią liczbę całkowitą.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                input.Focus();
                return;
            }
            dialog.DialogResult = DialogResult.OK;
        };
        return DialogPlacement.Show(dialog, this, multiline ? "treatment-animal-description" : "treatment-animal-count") == DialogResult.OK ? input.Text.Trim() : null;
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
        treatment.ScrollBars = ScrollBars.Both;
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
        treatment.Columns.Add(new DataGridViewButtonColumn
        {
            Name = ReferenceColumn, HeaderText = ReferenceColumn, Width = 160
        });
        Column(treatment, "Liczba leczonych zwierząt", "Liczba zwierząt", 115);
        Column(treatment, "Rozpoznanie", "Rozpoznanie", 215);
        treatment.Columns.Add(new DataGridViewButtonColumn
        {
            Name = IndicationColumn, HeaderText = IndicationColumn, Width = 170,
            Text = "Pokaż wskazanie z ChPL", UseColumnTextForButtonValue = true
        });
        Column(treatment, "Opis zwierzęcia", AnimalDescriptionColumn, 240);
        treatment.Columns[AnimalDescriptionColumn]!.ToolTipText = "Dwuklik otwiera edycję wspólnych numerów i liczby zwierząt.";
        treatment.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && treatment.Columns[e.ColumnIndex].Name == AnimalDescriptionColumn)
                await Run(() => EditSharedAnimals(treatment.Rows[e.RowIndex]));
        };
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
        treatment.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string column = treatment.Columns[e.ColumnIndex].Name;
            if (column is not (ReferenceColumn or IndicationColumn)) return;
            await Run(() =>
            {
                var row = treatment.Rows[e.RowIndex];
                if (column == ReferenceColumn) PickTreatmentReference(row);
                else ShowTreatmentIndication(row);
                return Task.CompletedTask;
            });
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
        treatment.EditingControlShowing += async (_, e) =>
        {
            int version = ++descriptionEditVersion;
            if (e.Control is not TextBox editor) return;
            editor.AutoCompleteMode = AutoCompleteMode.None;
            editor.AutoCompleteSource = AutoCompleteSource.None;
            editor.AutoCompleteCustomSource = null;
            if (treatment.CurrentCell is { OwningColumn.Name: "Karencja" } cell)
                editor.Text = cell.Value?.ToString() ?? "";
            if (treatment.CurrentCell?.OwningColumn?.Name != AnimalDescriptionColumn
                || currentCard is not { PayerId: > 0 } card) return;

            try
            {
                var archive = Archive();
                var suggestions = await RunArchive(() => archive.ReadDescriptions(card));
                if (IsDisposed || version != descriptionEditVersion || currentCard != card || sharedArchive != archive
                    || treatment.EditingControl != editor || suggestions.Length == 0) return;
                var source = new AutoCompleteStringCollection();
                source.AddRange(suggestions);
                editor.AutoCompleteCustomSource = source;
                editor.AutoCompleteSource = AutoCompleteSource.CustomSource;
                editor.AutoCompleteMode = AutoCompleteMode.Suggest;
            }
            catch (Exception ex)
            {
                CrashLog.Write("WinForms.AnimalDescription.Read", ex);
                if (!IsDisposed && version == descriptionEditVersion) status.Text = ex.Message;
            }
        };
        treatment.CellEndEdit += async (_, e) =>
        {
            descriptionEditVersion++;
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || treatment.Columns[e.ColumnIndex].Name != AnimalDescriptionColumn
                || currentCard is not { PayerId: > 0 } card) return;
            string description = treatment.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";
            if (description.Length == 0) return;

            await Run(async () =>
            {
                var archive = Archive();
                await RunArchive(() =>
                {
                    archive.SaveDescription(card, description);
                    return true;
                });
            }, "Zapis opisu zwierzęcia");
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
        cardSpeciesGroup.Items.AddRange(["Wszystkie gatunki", "Bydło", "Świnie", "Drób", "Pies", "Kot"]);
        cardSpeciesGroup.SelectedIndex = 0;
        cardSpeciesGroup.SelectedIndexChanged += (_, _) => ApplyCardSpeciesGroup();
        var page = Page("Karta leczenia", treatment,
            new Label { Text = "Gatunek", AutoSize = true, Anchor = AnchorStyles.Left }, cardSpeciesGroup, withdrawalMode, routeMode,
            new Label { Text = "Data leczenia", AutoSize = true, Anchor = AnchorStyles.Left }, treatmentDate,
            Action("Otwórz ChPL", async () =>
            {
                var row = treatment.CurrentRow;
                if (row?.Tag is not TreatmentRow entry)
                {
                    status.Text = "Zaznacz pozycję na karcie leczenia.";
                    return;
                }

                if (selectedTreatmentVariants.TryGetValue(row, out var variant) && !string.IsNullOrWhiteSpace(variant.Pdf))
                {
                    await OpenCataloguePdf(variant.Pdf);
                    return;
                }

                var paths = TreatmentVariants(row, entry)
                    .Select(choice => choice.Pdf)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (paths.Length == 1)
                    await OpenCataloguePdf(paths[0]);
                else
                    status.Text = paths.Length == 0
                        ? "Brak przypisanej ChPL dla tej kartoteki."
                        : "Wybierz gatunek i drogę podania, aby otworzyć właściwą ChPL.";
            }),
            Action("Eksportuj JSON", SaveCard),
            Action("Podgląd / PDF / e-mail", PreviewCard));
        var actions = page.Controls.OfType<FlowLayoutPanel>().Single();
        bool verticalTreatmentToolbar = interfaceScale > 100;
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true,
            ColumnCount = verticalTreatmentToolbar ? 1 : 3,
            RowCount = verticalTreatmentToolbar ? 3 : 1
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (!verticalTreatmentToolbar)
        {
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        page.Controls.Remove(actions);
        actions.Dock = DockStyle.Fill;
        actions.Margin = Padding.Empty;
        actions.WrapContents = true;
        toolbar.Controls.Add(actions, 0, 0);
        rememberTreatment.Anchor = AnchorStyles.Right;
        toolbar.Controls.Add(rememberTreatment, verticalTreatmentToolbar ? 0 : 1, verticalTreatmentToolbar ? 1 : 0);
        var saveTreatment = Action("Zapisz kartę", SaveTreatmentToArchive);
        saveTreatment.Anchor = AnchorStyles.Right;
        saveTreatment.MinimumSize = new Size(150, 36);
        saveTreatment.Padding = new Padding(12, 4, 12, 4);
        toolbar.Controls.Add(saveTreatment, verticalTreatmentToolbar ? 0 : 2, verticalTreatmentToolbar ? 2 : 0);
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
        var archive = Archive();
        bool remember = rememberTreatment.Checked;
        var data = await RunArchive(() => (Catalogue: archive.ReadCatalogue(), Choices: remember
            ? archive.ReadTreatmentChoices(card.Scope) : null));
        if (connection != options || sharedArchive != archive) return;
        var catalogue = data.Catalogue;
        treatmentCatalogue = catalogue;
        currentCard = card;
        currentArchiveId = Guid.NewGuid();
        currentCardNumber = null;
        treatmentReportedAt = null;
        treatmentExecutionTime = null;
        treatmentDate.Value = DateTime.Today;
        selectedTreatmentVariants.Clear();
        excludedTreatmentRows.Clear();
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
            gridRow.Cells[ReferenceColumn].Value = "Wybierz…";
            var saved = data.Choices?.GetValueOrDefault(TreatmentChoices.Key(entry)) ?? (Species: "", Route: "");
            SetTreatmentChoices(gridRow, saved.Species, saved.Route, preserveEdits: true);
        }
        ApplyCardSpeciesGroup();
        tabs.SelectedIndex = 3;
        CrashLog.Write($"WinForms.Treatment.Loaded rows={card.Rows.Count}");
        status.Text = $"Pozycje karty: {card.Rows.Count}";
    }

    Card CaptureTreatmentCard(bool forPrint = false)
    {
        var source = currentCard ?? throw new InvalidOperationException("Najpierw wczytaj dokument.");
        if (!treatment.EndEdit()) throw new InvalidOperationException("Zakończ edycję bieżącej pozycji karty.");
        var clinic = sharedClinic ?? new ClinicSettings();
        var fields = new Dictionary<string, string> { ["Dokument źródłowy"] = source.Number,
            ["Właściciel i adres"] = source.Payer, ["Zakres mapowania towarów"] = source.Scope,
            ["Nr karty"] = currentCardNumber ?? "",
            ["Zgłoszenie — data i godzina"] = treatmentReportedAt?.ToString("dd.MM.yyyy HH:mm") ?? "",
            ["Wykonanie — data i godzina"] = treatmentExecutionTime is { } time
                ? (treatmentDate.Value.Date + time).ToString("dd.MM.yyyy HH:mm")
                : treatmentDate.Value.ToString("dd.MM.yyyy"),
            ["Lecznica i adres"] = clinic.CardText,
            ["Lekarz i nr prawa wykonywania zawodu"] = clinic.Doctor };
        var rows = treatment.Rows.Cast<DataGridViewRow>().Select(item =>
        {
            var values = new Dictionary<string, string>(((TreatmentRow)item.Tag!).Values);
            foreach (DataGridViewCell cell in item.Cells)
                if (cell.OwningColumn is { } column && column.Name != IndicationColumn)
                    values[column.Name] = column.Name == TreatmentChoices.Route
                        && selectedTreatmentVariants.TryGetValue(item, out var variant)
                        ? variant.Route : cell.Value?.ToString() ?? "";
            if (string.Equals(values.GetValueOrDefault("Domyślny gatunek"), "True", StringComparison.OrdinalIgnoreCase)
                && string.Equals(values.GetValueOrDefault("Opis zwierząt / numer siedziby stada"), "Bydło", StringComparison.OrdinalIgnoreCase))
                values["Opis zwierząt / numer siedziby stada"] = "";
            values[ReferenceColumn] = "";
            if (item.Cells[ReferenceColumn].Tag is TreatmentReference reference)
            {
                values[ReferenceColumn] = reference.ToString();
                string note = selectedTreatmentVariants.ContainsKey(item)
                    ? $"Źródło danych ChPL: {reference}."
                    : $"Wskazane źródło ChPL: {reference}. Nie wybrano wariantu.";
                values["Zalecenia / uwagi"] = string.Join("\n",
                    new[] { values.GetValueOrDefault("Zalecenia / uwagi", ""), note }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
            }
            if (forPrint) values["Karencja"] = FormatWithdrawal(item, values.GetValueOrDefault("Karencja", ""));
            return values;
        }).ToList();
        return new Card(fields, rows)
        {
            DoctorStampPng = clinic.UseDoctorStamp ? clinic.DoctorStampPng : null,
            ClinicStampPng = clinic.UseClinicStamp ? clinic.ClinicStampPng : null
        };
    }

    bool ConfirmTreatmentWarnings(Card card)
    {
        var warnings = new List<(int Row, string Text)>();
        for (int index = 0; index < card.Rows.Count; index++)
        {
            var row = card.Rows[index];
            if (!string.Equals(row.GetValueOrDefault("Drukuj"), "True", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (key, label) in TreatmentWarningFields)
            {
                if (string.IsNullOrWhiteSpace(row.GetValueOrDefault(key)))
                    warnings.Add((index, $"{index + 1}. {row.GetValueOrDefault("Produkt")} — {label}"));
            }
            if (!selectedTreatmentVariants.ContainsKey(treatment.Rows[index]))
                warnings.Add((index, $"{index + 1}. {row.GetValueOrDefault("Produkt")} — brak wybranego wariantu ChPL"));
        }
        if (warnings.Count == 0) return true;

        using var dialog = new Form
        {
            Text = "Sprawdź dane karty", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(620, 350), MinimumSize = new Size(420, 260), AutoScaleMode = AutoScaleMode.Dpi
        };
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        list.Items.AddRange(warnings.Select(warning => warning.Text).ToArray());
        list.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        var proceed = new Button { Text = "Kontynuuj", AutoSize = true, DialogResult = DialogResult.Yes };
        var go = new Button { Text = "Przejdź do pozycji", AutoSize = true, DialogResult = DialogResult.Retry };
        list.DoubleClick += (_, _) => dialog.DialogResult = DialogResult.Retry;
        buttons.Controls.AddRange([cancel, proceed, go]);
        dialog.Controls.Add(list);
        dialog.Controls.Add(buttons);
        dialog.CancelButton = cancel;
        var result = DialogPlacement.Show(dialog, this, "treatment-warnings");
        if (result == DialogResult.Retry && list.SelectedIndex >= 0)
        {
            var column = treatment.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
            if (column is not null)
            {
                treatment.CurrentCell = treatment.Rows[warnings[list.SelectedIndex].Row].Cells[column.Index];
                treatment.Focus();
            }
        }
        return result == DialogResult.Yes;
    }

    static readonly (string Key, string Label)[] TreatmentWarningFields =
    [
        ("Seria", "brak numeru serii"),
        ("Liczba zwierząt", "brak liczby zwierząt"),
        ("Karencja", "brak danych o karencji")
    ];

    bool EditTreatmentDates()
    {
        using var dialog = new Form
        {
            Text = "Daty i godziny karty leczenia", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = true,
            ClientSize = new Size(610, 140), AutoScaleMode = AutoScaleMode.Dpi
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(12)
        };
        for (int i = 0; i < 3; i++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        DateTime now = DateTime.Now;
        var reportDate = new DateTimePicker
        {
            Width = 155, Format = DateTimePickerFormat.Short, ShowCheckBox = true,
            Value = treatmentReportedAt ?? treatmentDate.Value.Date, Checked = treatmentReportedAt is not null,
            AccessibleName = "Data zgłoszenia"
        };
        var reportTime = TimePicker(treatmentReportedAt ?? now, "Godzina zgłoszenia");
        var executionDate = new DateTimePicker
        {
            Width = 155, Format = DateTimePickerFormat.Short, Value = treatmentDate.Value.Date,
            AccessibleName = "Data wykonania czynności"
        };
        var executionTime = TimePicker(treatmentDate.Value.Date + (treatmentExecutionTime ?? now.TimeOfDay),
            "Godzina wykonania czynności");
        layout.Controls.Add(new Label { Text = "Zgłoszenie (zaznacz, aby wpisać)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(reportDate, 1, 0);
        layout.Controls.Add(reportTime, 2, 0);
        layout.Controls.Add(new Label { Text = "Wykonanie czynności weterynaryjnych", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(executionDate, 1, 1);
        layout.Controls.Add(executionTime, 2, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        var next = new Button { Text = "Dalej", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange([cancel, next]);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 3);
        var preferred = layout.GetPreferredSize(Size.Empty);
        dialog.ClientSize = new Size(Math.Max(dialog.ClientSize.Width, preferred.Width),
            Math.Max(dialog.ClientSize.Height, preferred.Height));
        dialog.MinimumSize = dialog.Size;
        dialog.Controls.Add(layout);
        dialog.AcceptButton = next;
        dialog.CancelButton = cancel;

        if (DialogPlacement.Show(dialog, this, "treatment-dates") != DialogResult.OK) return false;
        treatmentReportedAt = reportDate.Checked ? reportDate.Value.Date + Minutes(reportTime.Value) : null;
        treatmentDate.Value = executionDate.Value.Date;
        treatmentExecutionTime = Minutes(executionTime.Value);
        return true;

        static TimeSpan Minutes(DateTime value) => new(value.Hour, value.Minute, 0);
        static DateTimePicker TimePicker(DateTime value, string label) => new()
        {
            Width = 90, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
            Value = value, AccessibleName = label
        };
    }

    async Task SaveTreatmentToArchive()
    {
        if (savingTreatment) return;
        var archive = Archive();
        var source = currentCard ?? throw new InvalidOperationException("Najpierw wczytaj dokument.");
        var id = currentArchiveId;
        savingTreatment = true;
        try
        {
            if (!EditTreatmentDates()) return;
            var card = CaptureTreatmentCard();
            var printCard = CaptureTreatmentCard(forPrint: true);
            _ = CardHtmlRenderer.Render(printCard);
            if (!ConfirmTreatmentWarnings(card)) return;
            var date = treatmentDate.Value.Date;

            var saved = await RunArchive(() => archive.SaveCard(id, source, date, card, printCard));
            if (IsDisposed || sharedArchive != archive || currentArchiveId != id) return;
            if (saved.DuplicateId is Guid existingId)
            {
                var choice = PromptDuplicateCard(source.Number, updating: existingId == id);
                if (IsDisposed || sharedArchive != archive || currentArchiveId != id) return;
                if (choice == DialogResult.Retry)
                {
                    await PreviewSavedCard(archive, existingId.ToString("N"));
                    return;
                }
                if (choice != DialogResult.Yes) return;

                saved = await RunArchive(() => archive.SaveCard(id, source, date, card, printCard, confirmDuplicate: true));
                if (IsDisposed || sharedArchive != archive || currentArchiveId != id) return;
            }
            currentCardNumber = saved.Number ?? throw new InvalidOperationException("Nie udało się pobrać numeru zapisanej karty.");
            status.Text = $"Zapisano kartę {currentCardNumber} we wspólnym archiwum.";
            printCard.Fields["Nr karty"] = currentCardNumber;
            OpenCardPreview(printCard, source.PayerId, source.Scope);
        }
        finally { savingTreatment = false; }
    }

    DialogResult PromptDuplicateCard(string document, bool updating)
    {
        using var dialog = new Form
        {
            Text = "Karta już zapisana", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = true,
            AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(740, 160),
            MinimumSize = new Size(480, 190)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill,
            Text = $"Dokument {document} ma już zapisaną kartę.",
            Margin = new Padding(3, 3, 3, 14)
        }, 0, 0);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([
            new Button { Text = "Otwórz zapisaną kartę", AutoSize = true, DialogResult = DialogResult.Retry },
            new Button { Text = updating ? "Zaktualizuj kartę" : "Zapisz kolejną kartę", AutoSize = true, DialogResult = DialogResult.Yes },
            cancel
        ]);
        layout.Controls.Add(buttons, 0, 1);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = dialog.CancelButton = cancel;
        dialog.ActiveControl = cancel;
        return DialogPlacement.Show(dialog, this, "treatment-duplicate-card");
    }

    async Task PreviewSavedCard(SharedArchive archive, string id, bool inTrash = false)
    {
        var saved = await RunArchive(() => archive.ReadCard(id, forPrint: true, inTrash: inTrash));
        if (!IsDisposed && sharedArchive == archive)
            OpenCardPreview(saved.Card, saved.PayerId, saved.Scope);
    }

    DataGridView ConfigureSavedCards()
    {
        var grid = NewGrid();
        Column(grid, "Nr karty", nameof(SavedTreatmentCard.Number), 125);
        Column(grid, "Data leczenia", nameof(SavedTreatmentCard.Date), 115, "dd.MM.yyyy");
        Column(grid, "Klient", nameof(SavedTreatmentCard.Client), 350);
        Column(grid, "Dokument", nameof(SavedTreatmentCard.Document), 180);
        var query = new TextBox { Width = 250, PlaceholderText = "Klient, nr karty lub dokumentu" };
        var trash = new CheckBox { Text = "Pokaż kosz", AutoSize = true, Anchor = AnchorStyles.Left };
        var day = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Width = 125 };

        int refreshVersion = 0;

        async Task RefreshCards()
        {
            int version = ++refreshVersion;
            grid.DataSource = null;
            var archive = Archive();
            var text = query.Text.Trim();
            DateTime? date = day.Checked ? day.Value.Date : null;
            bool inTrash = trash.Checked;
            var rows = await RunArchive(() => archive.FindCards(text, date, inTrash));
            if (IsDisposed || version != refreshVersion || sharedArchive != archive) return;
            grid.DataSource = rows;
            status.Text = inTrash ? $"Karty w koszu: {rows.Count}" : $"Zapisane karty: {rows.Count}";
        }

        async Task OpenSavedCard()
        {
            if (grid.CurrentRow?.DataBoundItem is not SavedTreatmentCard row) return;
            await PreviewSavedCard(Archive(), row.Id, trash.Checked);
        }

        async Task ExportSavedCard()
        {
            if (grid.CurrentRow?.DataBoundItem is not SavedTreatmentCard row) return;
            var archive = Archive();
            bool inTrash = trash.Checked;
            var saved = await RunArchive(() => archive.ReadCard(row.Id, forPrint: false, inTrash: inTrash));
            if (!IsDisposed && sharedArchive == archive) await ExportCard(saved.Card);
        }

        bool movingCard = false;
        async Task MoveSavedCard()
        {
            if (movingCard || grid.CurrentRow?.DataBoundItem is not SavedTreatmentCard row) return;
            var archive = Archive();
            bool toTrash = !trash.Checked;
            movingCard = true;
            try
            {
                if (toTrash && MessageBox.Show(this,
                        $"Przenieść kartę dokumentu {row.Document} do kosza?\n{row.Client} · {row.Date:dd.MM.yyyy}",
                        "Przenieś do kosza", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                if (IsDisposed || sharedArchive != archive) return;
                bool moved = await RunArchive(() => archive.SetCardInTrash(row.Id, toTrash));
                if (IsDisposed || sharedArchive != archive) return;
                await RefreshCards();
                if (!IsDisposed && sharedArchive == archive)
                    status.Text = !moved ? "Stan karty już się zmienił."
                        : toTrash ? "Przeniesiono kartę do kosza." : "Przywrócono kartę z kosza.";
            }
            finally { movingCard = false; }
        }

        var moveCard = Action("Przenieś do kosza", MoveSavedCard);
        var page = Page("Zapisane karty leczenia", grid, query,
            new Label { Text = "Data leczenia", AutoSize = true }, day,
            Action("Szukaj", RefreshCards), Action("Podgląd / PDF / e-mail", OpenSavedCard),
            Action("Eksportuj JSON", ExportSavedCard), trash, moveCard);
        trash.CheckedChanged += async (_, _) =>
        {
            moveCard.Text = trash.Checked ? "Przywróć kartę" : "Przenieś do kosza";
            await Run(RefreshCards);
        };
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedTab == page) await Run(RefreshCards);
        };
        query.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await Run(RefreshCards);
        };
        day.ValueChanged += async (_, _) => await Run(RefreshCards);
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0) await Run(OpenSavedCard);
        };
        return grid;
    }

    Task SaveCard() => ExportCard(CaptureTreatmentCard());

    async Task ExportCard(Card card)
    {
        using var dialog = new SaveFileDialog { Filter = "Karta leczenia (*.json)|*.json", FileName = "karta-leczenia.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(card));
    }

    async Task PreviewCard()
    {
        if (savingTreatment) return;
        if (currentCard is null) throw new InvalidOperationException("Najpierw wczytaj dokument.");
        if (currentCardNumber is null)
        {
            if (MessageBox.Show(this, "Zapisz kartę, aby nadać jej numer widoczny na wydruku. Zapisać teraz?",
                    "Numer karty leczenia", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                await SaveTreatmentToArchive();
            return;
        }

        var card = CaptureTreatmentCard(forPrint: true);
        if (ConfirmTreatmentWarnings(card)) OpenCardPreview(card, currentCard.PayerId, currentCard.Scope);
    }

    void OpenCardPreview(Card card, int? payerId, string? scope)
    {
        async Task<string?> FindEmail()
        {
            if (payerId is not > 0 || connection is not { } options
                || scope != options.Server.ToUpperInvariant() + "|" + options.Database.ToUpperInvariant())
                return null;

            using var work = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { return await service.ReadPayerEmailAsync(options, payerId.Value, work.Token); }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or OperationCanceledException)
            {
                CrashLog.Write("WinForms.Card.PayerEmail", ex);
                return null;
            }
        }

        using var preview = new PricePrintPreview(CardHtmlRenderer.Render(card), landscape: true, margin: 7,
            treatmentCard: true, findCardEmail: FindEmail,
            cardDocumentNumber: card.Fields.GetValueOrDefault("Dokument źródłowy"));
        DialogPlacement.Show(preview, this, "treatment-print-preview");
    }

    void ConfigureConnection()
    {
        var subiektConfiguration = settings.Load();
        var saved = subiektConfiguration.Options;
        if (saved is { WarehouseId: not null } && (saved.WindowsAuthentication || saved.Password.Length > 0))
            connection = saved;

        var sections = new TabControl { Dock = DockStyle.Fill, Multiline = true };
        // At increased application scale use a vertical field layout. A fixed label
        // column plus 360px inputs otherwise pushes controls outside the viewport.
        bool compactSettings = interfaceScale > 100;
        TableLayoutPanel Section(string title)
        {
            var page = new TabPage(title) { AutoScroll = true, Padding = Padding.Empty };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                ColumnCount = compactSettings ? 1 : 2,
                Padding = new Padding(12)
            };
            if (compactSettings)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            else
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            }
            page.Controls.Add(layout);
            sections.TabPages.Add(page);
            return layout;
        }

        var form = Section("Archiwum SQL");
        void Add(string title, Control input)
        {
            if (compactSettings)
            {
                if (title.Length > 0)
                {
                    int labelRow = form.RowCount++;
                    form.Controls.Add(new Label
                    {
                        Text = title, AutoSize = true, MaximumSize = new Size(800, 0),
                        Margin = new Padding(3, 8, 3, 1)
                    }, 0, labelRow);
                }
                int inputRow = form.RowCount++;
                input.Anchor = AnchorStyles.Left;
                form.Controls.Add(input, 0, inputRow);
                return;
            }

            int row = form.RowCount++;
            if (title.Length > 0)
                form.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            form.Controls.Add(input, 1, row);
        }

        Label Message(TableLayoutPanel section, string message, bool multiline = false)
        {
            int row = section.RowCount++;
            var label = new Label
            {
                Text = message, AutoSize = true, Margin = new Padding(3, 8, 3, 8),
                MaximumSize = multiline ? new Size(620, 0) : Size.Empty
            };
            section.Controls.Add(label, 0, row);
            section.SetColumnSpan(label, section.ColumnCount);
            return label;
        }

        TextBox Field(string name, string? value, string hint = "", bool password = false)
        {
            var box = new TextBox
            {
                Width = 360, Text = value ?? "", PlaceholderText = hint, UseSystemPasswordChar = password,
                AccessibleName = name
            };
            Add(name, box);
            return box;
        }

        CheckBox Option(string title, bool value)
        {
            var box = new CheckBox { Text = title, AutoSize = true, Checked = value, Anchor = AnchorStyles.Left };
            Add("", box);
            return box;
        }

        ComboBox Authentication(bool useWindows)
        {
            var selector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, AccessibleName = "Logowanie"
            };
            selector.Items.AddRange(["Login i hasło SQL", "Konto Windows"]);
            selector.SelectedIndex = useWindows ? 1 : 0;
            Add("Logowanie", selector);
            return selector;
        }

        static void BindCredentials(ComboBox authentication, TextBox user, TextBox password, CheckBox remember)
        {
            void Refresh()
            {
                user.Enabled = password.Enabled = remember.Enabled = authentication.SelectedIndex == 0;
            }
            authentication.SelectedIndexChanged += (_, _) => Refresh();
            Refresh();
        }

        static void Require(Control field, string message)
        {
            if (!string.IsNullOrWhiteSpace(field.Text)) return;
            field.Focus();
            throw new InvalidOperationException(message);
        }

        var archiveConfiguration = archiveSettings.Load();
        var archiveOptions = archiveConfiguration.Options;
        if (archiveOptions is not null && (archiveOptions.WindowsAuthentication || archiveOptions.Password.Length > 0))
            sharedArchive = new SharedArchive(archiveOptions);

        Message(form, installedArchiveServer.Length > 0
            ? "Adres archiwum pochodzi z instalatora. Przy logowaniu SQL użyj hasła operatora ustawionego na serwerze."
            : "Wpisz adres komputera z bazą KartyLeczenia oraz dane logowania do archiwum.", multiline: true);
        var archiveServer = Field("Serwer archiwum", archiveOptions?.Server ?? installedArchiveServer, "np. SERWER,51433");
        var archiveAuthentication = Authentication(archiveOptions?.WindowsAuthentication ?? false);
        var archiveUser = Field("Login SQL", archiveOptions?.UserName ?? "KartyLeczeniaOperator");
        var archivePassword = Field("Hasło SQL", archiveOptions?.Password, password: true);
        var archiveRemember = Option("Zapamiętaj hasło na tym koncie Windows", archiveConfiguration.Remembered);
        BindCredentials(archiveAuthentication, archiveUser, archivePassword, archiveRemember);
        var archiveTrust = Option("Zaufaj certyfikatowi znanego serwera archiwum", archiveOptions?.TrustCertificate ?? false);
        var archiveResult = Message(form, archiveConfiguration.Warning ?? (sharedArchive is null
            ? "Stan: połączenie nie zostało skonfigurowane."
            : "Stan: wczytano zapisane ustawienia."), multiline: true);
        var archiveClinicStatus = Message(form, "", multiline: true);
        var archiveForm = form;
        bool configuringArchive = false;
        Func<SharedArchive, Task>? refreshClinic = null;
        var saveArchiveConnection = Action("Sprawdź i zapisz", async () =>
        {
            if (configuringArchive) return;
            configuringArchive = true;
            try
            {
                Require(archiveServer, "Wpisz adres serwera archiwum.");
                bool windows = archiveAuthentication.SelectedIndex == 1;
                if (!windows)
                {
                    Require(archiveUser, "Wpisz login SQL archiwum.");
                    Require(archivePassword, "Wpisz hasło SQL operatora archiwum.");
                }
                var draft = new ConnectionOptions(archiveServer.Text.Trim(), SharedArchive.DatabaseName,
                    windows, archiveUser.Text, archivePassword.Text, archiveTrust.Checked);
                bool rememberPassword = !windows && archiveRemember.Checked;
                var archive = new SharedArchive(draft);
                archiveResult.Text = "Sprawdzanie połączenia z archiwum…";
                archiveForm.Enabled = false;
                await RunArchive(() => { archive.Test(); return true; });
                archiveSettings.Save(draft, rememberPassword);
                bool changedServer = !string.Equals(sharedArchive?.Server, archive.Server, StringComparison.OrdinalIgnoreCase);
                sharedArchive = archive;
                if (changedServer)
                {
                    sharedClinic = null;
                    prices.DataSource = null;
                    treatmentCatalogue = null;
                    currentCard = null;
                    selectedTreatmentVariants.Clear();
                    excludedTreatmentRows.Clear();
                    treatment.Rows.Clear();
                }
                archiveResult.Text = status.Text = $"Połączono z archiwum na {draft.Server}. Ustawienia zapisane.";
                await refreshClinic!(archive);
            }
            catch (Exception ex)
            {
                archiveResult.Text = ex.Message;
                throw;
            }
            finally
            {
                archiveForm.Enabled = true;
                configuringArchive = false;
            }
        });
        Add("", saveArchiveConnection);
        archivePassword.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            saveArchiveConnection.PerformClick();
        };
        form = Section("Subiekt GT");
        var subiektForm = form;
        Message(form, "Podaj adres i dane logowania do Subiekta. Wybierz bazę podmiotu, pobierz magazyny i zapisz ustawienia.", multiline: true);
        var server = Field("Serwer Subiekta", saved?.Server, @"np. SERWER\INSERTGT");
        var auth = Authentication(saved?.WindowsAuthentication ?? true);
        var user = Field("Login SQL", saved?.UserName);
        var password = Field("Hasło SQL", saved?.Password, password: true);
        var remember = Option("Zapamiętaj hasło na tym koncie Windows", subiektConfiguration.Remembered);
        BindCredentials(auth, user, password, remember);
        var trust = Option("Zaufaj certyfikatowi znanego serwera Subiekta", saved?.TrustCertificate ?? false);
        var database = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown, Width = 360,
            AccessibleName = "Baza podmiotu", Text = saved?.Database ?? ""
        };
        Add("Baza podmiotu", database);
        var discoveryStatus = Message(form, "Wybierz bazę z listy lub wpisz jej nazwę.", multiline: true);
        var discoveryTimer = new System.Windows.Forms.Timer { Interval = 700 };
        CancellationTokenSource? discoveryWork = null;
        bool discoveryAttempted = false;

        bool CanDiscover() => !string.IsNullOrWhiteSpace(server.Text) &&
            (auth.SelectedIndex == 1 || (!string.IsNullOrWhiteSpace(user.Text) && password.TextLength > 0));

        void StopDiscovery()
        {
            discoveryTimer.Stop();
            var work = discoveryWork;
            discoveryWork = null;
            work?.Cancel();
        }

        void ScheduleDiscovery()
        {
            if (!discoveryAttempted && CanDiscover() && sections.Visible && sections.SelectedTab == subiektForm.Parent)
                discoveryTimer.Start();
        }

        async Task DiscoverCompanies()
        {
            StopDiscovery();
            if (!CanDiscover())
            {
                discoveryStatus.Text = "Podaj adres serwera i dane logowania, aby pobrać bazy podmiotów.";
                return;
            }

            discoveryAttempted = true;
            var options = new ConnectionOptions(server.Text.Trim(), "", auth.SelectedIndex == 1,
                user.Text, password.Text, trust.Checked);
            using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            discoveryWork = work;
            discoveryStatus.Text = "Pobieranie baz podmiotów…";
            try
            {
                var result = await new SubiektConnectionService().DiscoverAsync(options, work.Token);
                if (IsDisposed || discoveryWork != work) return;
                string typed = database.Text;
                var names = result.Companies.Select(company => company.Database).ToArray();
                database.BeginUpdate();
                try
                {
                    database.Items.Clear();
                    database.Items.AddRange(names);
                    if (string.IsNullOrWhiteSpace(typed) && names.Length == 1)
                        database.SelectedIndex = 0;
                    else if (database.Text != typed)
                        database.Text = typed;
                }
                finally { database.EndUpdate(); }
                discoveryStatus.Text = names.Length == 0
                    ? "Nie znaleziono dostępnych baz podmiotów. Możesz wpisać nazwę bazy."
                    : $"Dostępne bazy podmiotów: {names.Length}.";
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                if (!IsDisposed && discoveryWork == work)
                    discoveryStatus.Text = "Pobieranie baz trwało za długo. Ponów próbę lub wpisz nazwę bazy.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed && discoveryWork == work)
                    discoveryStatus.Text = "Pobieranie baz: " + SubiektConnectionService.FriendlyError(ex);
            }
            finally
            {
                if (discoveryWork == work) discoveryWork = null;
            }
        }

        discoveryTimer.Tick += async (_, _) => await DiscoverCompanies();
        Add("", Action("Pobierz bazy podmiotów", DiscoverCompanies));
        var group = Field("Grupa towarów", saved?.DrugGroup ?? "Leki");
        var warehouses = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, DisplayMember = nameof(MedicineWarehouse.Name),
            AccessibleName = "Magazyn", Enabled = false
        };
        Add("Magazyn", warehouses);
        var subiektResult = Message(form, subiektConfiguration.Warning ?? (connection is null
            ? "Stan: połączenie nie zostało skonfigurowane."
            : "Stan: wczytano zapisane ustawienia."), multiline: true);

        ConnectionOptions Draft()
        {
            Require(server, "Wpisz adres serwera Subiekta.");
            Require(database, "Wpisz nazwę bazy podmiotu.");
            bool windows = auth.SelectedIndex == 1;
            if (!windows)
            {
                Require(user, "Wpisz login SQL do Subiekta.");
                Require(password, "Wpisz hasło SQL do Subiekta.");
            }
            return new(server.Text.Trim(), database.Text.Trim(), windows, user.Text, password.Text, trust.Checked, group.Text.Trim());
        }

        Button? saveSubiekt = null;
        void ClearWarehouseList()
        {
            warehouses.DataSource = null;
            warehouses.Enabled = false;
            saveSubiekt!.Enabled = false;
            subiektResult.Text = "Sprawdź połączenie, aby pobrać magazyny dla tych ustawień.";
        }

        Add("", Action("Sprawdź i pobierz magazyny", async () =>
        {
            ConnectionOptions draft;
            try { draft = Draft(); }
            catch (InvalidOperationException ex)
            {
                subiektResult.Text = ex.Message;
                throw;
            }
            int? previous = (warehouses.SelectedItem as MedicineWarehouse)?.Id;
            if (previous is null && connection is { } current &&
                current.Server.Equals(draft.Server, StringComparison.OrdinalIgnoreCase) &&
                current.Database.Equals(draft.Database, StringComparison.OrdinalIgnoreCase))
                previous = current.WarehouseId;
            StopDiscovery();
            if (discoveryStatus.Text == "Pobieranie baz podmiotów…")
                discoveryStatus.Text = "Wybierz bazę z listy lub wpisz jej nazwę.";
            ClearWarehouseList();
            subiektForm.Enabled = false;
            subiektResult.Text = "Sprawdzanie połączenia z Subiektem…";
            try
            {
                using var work = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var info = await new SubiektConnectionService().TestAsync(draft, work.Token);
                var items = info.Warehouses.ToList();
                warehouses.DataSource = items;
                if (items.Count != 1) warehouses.SelectedIndex = items.FindIndex(item => item.Id == previous);
                warehouses.Enabled = items.Count > 0;
                saveSubiekt!.Enabled = warehouses.SelectedIndex >= 0;
                subiektResult.Text = items.Count == 0
                    ? $"Połączono z {info.Company}. Brak dostępnych magazynów."
                    : $"Połączono z {info.Company}. Wybierz magazyn i zapisz.";
            }
            catch (Exception ex)
            {
                subiektResult.Text = SubiektConnectionService.FriendlyError(ex);
                throw;
            }
            finally { subiektForm.Enabled = true; }
        }));

        saveSubiekt = Action("Zapisz połączenie z Subiektem", async () =>
        {
            if (warehouses.SelectedItem is not MedicineWarehouse warehouse) return;
            var draft = Draft() with { WarehouseId = warehouse.Id };
            settings.Save(draft, !draft.WindowsAuthentication && remember.Checked);
            searchWork?.Cancel();
            preloadWork?.Cancel();
            cache.Clear();
            displayedDocuments = null;
            UpdateRefreshLabel();
            documents.DataSource = prices.DataSource = expiry.DataSource = null;
            selectedTreatmentVariants.Clear();
            excludedTreatmentRows.Clear();
            treatment.Rows.Clear();
            currentCard = null;
            connection = draft;
            subiektResult.Text = $"Zapisano połączenie. Magazyn: {warehouse.Name}.";
            await SelectKind(DocumentSearchKinds.Invoices);
        });
        saveSubiekt.Enabled = false;
        Add("", saveSubiekt);
        warehouses.SelectedIndexChanged += (_, _) => saveSubiekt.Enabled = warehouses.SelectedIndex >= 0;
        void SubiektConnectionChanged()
        {
            StopDiscovery();
            discoveryAttempted = false;
            string typed = database.Text;
            database.Items.Clear();
            if (database.Text != typed) database.Text = typed;
            discoveryStatus.Text = "Wybierz bazę z listy lub wpisz jej nazwę.";
            ClearWarehouseList();
            ScheduleDiscovery();
        }

        database.TextChanged += (_, _) => ClearWarehouseList();
        group.TextChanged += (_, _) => ClearWarehouseList();
        foreach (var input in new[] { server, user, password })
            input.TextChanged += (_, _) => SubiektConnectionChanged();
        auth.SelectedIndexChanged += (_, _) => SubiektConnectionChanged();
        trust.CheckedChanged += (_, _) => SubiektConnectionChanged();
        sections.SelectedIndexChanged += (_, _) => ScheduleDiscovery();
        sections.VisibleChanged += (_, _) => ScheduleDiscovery();
        Shown += (_, _) => ScheduleDiscovery();
        Disposed += (_, _) =>
        {
            StopDiscovery();
            discoveryTimer.Dispose();
        };

        form = Section("Dane lecznicy");
        var clinicForm = form;
        var clinicName = new TextBox
        {
            Width = 360, Height = 68, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, Text = localClinic.Name, AccessibleName = "Nazwa lecznicy"
        };
        Add("Nazwa lecznicy", clinicName);
        var clinicAddress = Field("Adres lecznicy", localClinic.Address);
        var clinicPhone = Field("Telefon", localClinic.Phone);
        (Func<(bool Use, byte[]? Png)> Read, Action<bool, byte[]?> Show) StampFields(string modeLabel, string fileLabel)
        {
            var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            mode.Items.AddRange(["Tekst", "Pieczątka PNG"]);
            Add(modeLabel, mode);
            byte[]? stamp = null;
            var stampTools = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var stampLabel = new Label { AutoSize = true, Margin = new Padding(8, 8, 0, 0) };

            void UpdateStampLabel() => stampLabel.Text = stamp is null ? "Brak pliku" : "PNG dodany";

            stampTools.Controls.AddRange([
                Action("Wybierz PNG…", () =>
                {
                    using var dialog = new OpenFileDialog { Filter = "Obraz PNG (*.png)|*.png" };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
                    var bytes = File.ReadAllBytes(dialog.FileName);
                    if (!CardSchema.HasPngHeader(bytes)) throw new InvalidDataException("Wybierz poprawny obraz PNG.");
                    try
                    {
                        using var stream = new MemoryStream(bytes);
                        using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                    }
                    catch (ArgumentException ex) { throw new InvalidDataException("Nie można odczytać obrazu PNG.", ex); }
                    stamp = bytes;
                    mode.SelectedIndex = 1;
                    UpdateStampLabel();
                    return Task.CompletedTask;
                }),
                Action("Usuń", () =>
                {
                    stamp = null;
                    mode.SelectedIndex = 0;
                    UpdateStampLabel();
                    return Task.CompletedTask;
                }),
                stampLabel
            ]);
            Add(fileLabel, stampTools);

            void Show(bool useStamp, byte[]? png)
            {
                mode.SelectedIndex = useStamp ? 1 : 0;
                stamp = png;
                UpdateStampLabel();
            }

            return (() => (mode.SelectedIndex == 1, stamp), Show);
        }

        var clinicStamp = StampFields("Lecznica na karcie", "Pieczątka lecznicy");
        var doctor = Field("Lekarz", localClinic.Doctor);
        var doctorStamp = StampFields("Lekarz na karcie", "Pieczątka lekarza");
        clinicStamp.Show(localClinic.UseClinicStamp, localClinic.ClinicStampPng);
        doctorStamp.Show(localClinic.UseDoctorStamp, localClinic.DoctorStampPng);
        var clinicResult = Message(form, "Połącz się w zakładce „Archiwum SQL”, aby pobrać wspólne dane lecznicy.", multiline: true);

        void SetClinicStatus(string message, bool reportInStatusBar = false)
        {
            clinicResult.Text = archiveClinicStatus.Text = message;
            if (reportInStatusBar) status.Text = message;
        }

        ClinicSettings ClinicDraft()
        {
            var (useClinicStamp, clinicPng) = clinicStamp.Read();
            var (useDoctorStamp, doctorPng) = doctorStamp.Read();
            return new(clinicName.Text, clinicAddress.Text, doctor.Text)
            {
                Phone = clinicPhone.Text,
                UseClinicStamp = useClinicStamp,
                ClinicStampPng = clinicPng,
                UseDoctorStamp = useDoctorStamp,
                DoctorStampPng = doctorPng
            };
        }

        void ShowClinic(ClinicSettings clinic)
        {
            clinicName.Text = clinic.Name;
            clinicAddress.Text = clinic.Address;
            clinicPhone.Text = clinic.Phone;
            doctor.Text = clinic.Doctor;
            clinicStamp.Show(clinic.UseClinicStamp, clinic.ClinicStampPng);
            doctorStamp.Show(clinic.UseDoctorStamp, clinic.DoctorStampPng);
        }

        void ReportClinicError(Exception ex)
        {
            string message = ex is Microsoft.Data.SqlClient.SqlException { Number: 207 or 208 or 229 }
                ? "Sprawdź, czy aktualny instalator w trybie Serwer zaktualizował dane lecznicy na tej instancji i przyznał do nich dostęp operatorowi."
                : SubiektConnectionService.FriendlyError(ex);
            CrashLog.Write("WinForms.Clinic.SyncFailed", ex);
            SetClinicStatus($"Dane lecznicy — {sharedArchive?.Server}: {message}", reportInStatusBar: true);
        }

        async Task RefreshClinic(SharedArchive archive)
        {
            clinicForm.Enabled = false;
            SetClinicStatus($"Pobieranie danych lecznicy z {archive.Server}…");
            var initial = localClinic;
            if (sharedClinic is null) ShowClinic(initial);
            try
            {
                var clinic = await Task.Run(() => archive.LoadClinic(initial));
                if (IsDisposed || sharedArchive != archive) return;
                sharedClinic = clinic;
                ShowClinic(clinic ?? initial);
                SetClinicStatus(clinic is null || clinic == new ClinicSettings()
                    ? $"Archiwum na {archive.Server} nie zawiera danych lecznicy. Uzupełnij pola i zapisz je w zakładce „Dane lecznicy”."
                    : $"Wczytano dane lecznicy z archiwum na {archive.Server}.");
            }
            catch (Exception ex)
            {
                if (!IsDisposed && sharedArchive == archive) ReportClinicError(ex);
            }
            finally
            {
                if (!IsDisposed && sharedArchive == archive) clinicForm.Enabled = true;
            }
        }
        refreshClinic = RefreshClinic;

        Add("", Action("Zapisz dane lecznicy", async () =>
        {
            var clinic = ClinicDraft();
            if (clinic.UseDoctorStamp && clinic.DoctorStampPng is null)
                throw new InvalidOperationException("Dodaj pieczątkę lekarza w PNG lub wybierz tekst.");
            if (clinic.UseClinicStamp && clinic.ClinicStampPng is null)
                throw new InvalidOperationException("Dodaj pieczątkę lecznicy w PNG lub wybierz tekst.");
            var archive = Archive();
            clinicForm.Enabled = false;
            try
            {
                await Task.Run(() => archive.SaveClinic(clinic));
                if (IsDisposed || sharedArchive != archive) return;
                sharedClinic = clinic;
                SetClinicStatus($"Zapisano wspólne dane lecznicy w bazie {SharedArchive.DatabaseName} na {archive.Server}.", reportInStatusBar: true);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && sharedArchive == archive) ReportClinicError(ex);
            }
            finally
            {
                if (!IsDisposed && sharedArchive == archive) clinicForm.Enabled = true;
            }
        }));
        Add("", Action("Pobierz dane z archiwum", () => RefreshClinic(Archive())));
        sections.SelectedIndexChanged += async (_, _) =>
        {
            if (sections.SelectedTab == clinicForm.Parent && clinicForm.Enabled &&
                sharedArchive is { } archive && ClinicDraft() == (sharedClinic ?? localClinic))
                await RefreshClinic(archive);
        };
        Shown += async (_, _) =>
        {
            if (sharedArchive is { } archive) await RefreshClinic(archive);
        };
        form = Section("Kopia zapasowa SQL");
        Message(form, "Archiwizacja obejmuje całą bazę KartyLeczenia na zapisanym serwerze archiwum. " +
            "Do odtworzenia możesz wskazać inną instancję. Podaj konto administratora SQL. " +
            "Po kliknięciu przycisku otworzy się wybór miejsca oraz kompresji kopii (zależnej od edycji SQL).",
            multiline: true);
        var maintenanceAuth = Authentication(useWindows: true);
        var maintenanceUser = Field("Login administratora SQL", "");
        var maintenancePassword = Field("Hasło administratora SQL", "", password: true);
        maintenanceAuth.SelectedIndexChanged += (_, _) =>
            maintenanceUser.Enabled = maintenancePassword.Enabled = maintenanceAuth.SelectedIndex == 0;
        maintenanceUser.Enabled = maintenancePassword.Enabled = false;
        var backupFolder = Field("Folder kopii na serwerze", "", "Puste pole = domyślny folder SQL Servera");
        var restoreServer = Field("Serwer docelowy odtwarzania", "", "Puste pole = zapisany serwer archiwum");
        var restoreTrust = Option("Zaufaj certyfikatowi wpisanego serwera docelowego", false);
        var restoreFile = Field("Plik kopii na serwerze", "", @"np. D:\Kopie\KartyLeczenia.bak");
        var maintenanceStatus = Message(form, "", multiline: true);
        bool maintainingArchive = false;
        FormClosing += (_, e) =>
        {
            if (!maintainingArchive) return;
            e.Cancel = true;
            status.Text = "Poczekaj na zakończenie operacji na bazie KartyLeczenia.";
        };

        async Task MaintainArchive(bool restoring)
        {
            if (maintainingArchive) return;
            string target = restoring ? restoreServer.Text.Trim() : "";
            var archive = target.Length == 0 ? Archive() : new SharedArchive(new ConnectionOptions(
                target, SharedArchive.DatabaseName, true, "", "", restoreTrust.Checked));
            bool windows = maintenanceAuth.SelectedIndex == 1;
            if (!windows)
            {
                Require(maintenanceUser, "Wpisz login administratora SQL.");
                Require(maintenancePassword, "Wpisz hasło administratora SQL.");
            }

            using var archiveDialog = new ArchiveBackupDialog(restoring,
                (restoring ? restoreFile : backupFolder).Text.Trim());
            if (DialogPlacement.Show(archiveDialog, this,
                    restoring ? "database-restore-options" : "database-backup-options") != DialogResult.OK)
                return;
            string path = archiveDialog.SelectedPath;
            bool compressBackup = archiveDialog.Compressed;
            if (restoring) restoreFile.Text = path;
            else backupFolder.Text = path;
            if (restoring && !path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Podaj pełną ścieżkę do pliku .bak dostępnego z serwera SQL.");
            if (path.Length > 0 && !Path.IsPathFullyQualified(path) && !path.StartsWith('/'))
                throw new InvalidOperationException("Podaj pełną ścieżkę na serwerze SQL lub ścieżkę sieciową.");

            if (restoring && MessageBox.Show(this,
                    $"Odtworzyć bazę KartyLeczenia na serwerze {archive.Server} z pliku:\n{path}\n\n" +
                    "Operacja zastąpi aktualne dane i rozłączy pozostałe stanowiska. Zamknij na nich aplikację przed kontynuowaniem. " +
                    "Jeśli baza istnieje, program najpierw wykona kopię obecnego stanu.",
                    "Odtwarzanie bazy KartyLeczenia", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var administrator = new ConnectionOptions(archive.Server, SharedArchive.DatabaseName, windows,
                maintenanceUser.Text, maintenancePassword.Text, false);
            maintainingArchive = true;
            tabs.Enabled = false;
            UseWaitCursor = true;
            maintenanceStatus.Text = status.Text = restoring
                ? "Sprawdzanie kopii, zabezpieczanie bieżących danych i odtwarzanie bazy…"
                : "Wykonywanie i sprawdzanie kopii bazy KartyLeczenia…";
            try
            {
                string? backup = await Task.Run(() => restoring
                    ? archive.RestoreDatabase(administrator, path)
                    : archive.BackupDatabase(administrator, path, compressBackup));
                if (restoring && ReferenceEquals(archive, sharedArchive))
                {
                    sharedClinic = null;
                    treatmentCatalogue = null;
                    currentCard = null;
                    selectedTreatmentVariants.Clear();
                    excludedTreatmentRows.Clear();
                    treatment.Rows.Clear();
                    prices.DataSource = null;
                    await RefreshClinic(archive);
                }
                else if (!restoring)
                {
                    restoreFile.Text = backup ?? "";
                }
                maintenanceStatus.Text = !restoring
                    ? $"Zapisano i sprawdzono kopię na serwerze: {backup}"
                    : backup is null
                        ? $"Odtworzono bazę na {archive.Server}."
                        : $"Odtworzono bazę na {archive.Server}. Kopia sprzed odtworzenia: {backup}";
                status.Text = restoring ? "Odtworzono bazę KartyLeczenia." : "Zapisano kopię bazy KartyLeczenia.";
            }
            catch (Exception ex)
            {
                maintenanceStatus.Text = ex.Message;
                throw;
            }
            finally
            {
                maintenancePassword.Clear();
                maintainingArchive = false;
                UseWaitCursor = false;
                tabs.Enabled = true;
            }
        }

        Add("", Action("Archiwizuj bazę", () => MaintainArchive(restoring: false)));
        Add("", Action("Odtwórz bazę z kopii…", () => MaintainArchive(restoring: true)));

        form = Section("Aktualizacje");
        string? updateShare = ServerUpdateGate.UpdateShare;
        Message(form, "Poświadczenia dostępu do instalatorów na serwerze. Hasło jest zapisywane wyłącznie w Menedżerze poświadczeń Windows bieżącego użytkownika, nie w ustawieniach KartyLeczenia.", multiline: true);
        Message(form, string.IsNullOrWhiteSpace(updateShare)
            ? "Nie skonfigurowano udziału aktualizacji w instalatorze. Zainstaluj program w trybie stanowiska zdalnego."
            : "Udział aktualizacji: " + updateShare, multiline: true);
        string? updateHost = null;
        if (!string.IsNullOrWhiteSpace(updateShare))
        {
            try { updateHost = UpdateCredentials.ServerFromShare(updateShare); }
            catch (ArgumentException ex) { CrashLog.Write("UpdateCredentials.Share", ex); }
        }
        var credentialsUser = Field("Konto Windows na serwerze", "", @"np. LEWY\KartyAktualizacje");
        var credentialsPassword = Field("Hasło konta aktualizacji", "", password: true);
        var credentialsState = Message(form, "", multiline: true);
        if (updateHost is not null)
        {
            try
            {
                credentialsUser.Text = UpdateCredentials.SavedUser(updateHost) ?? "";
                credentialsState.Text = credentialsUser.TextLength > 0
                    ? "Zapisano poświadczenia dla serwera " + updateHost + ". Hasło nie jest wyświetlane."
                    : "Poświadczenia nie są jeszcze zapisane dla tego konta Windows.";
            }
            catch (Exception ex)
            {
                CrashLog.Write("UpdateCredentials.Read", ex);
                credentialsState.Text = ex.Message;
            }
        }
        var updateCredentialsHost = updateHost;
        var saveCredentials = Action("Zapisz poświadczenia", () =>
        {
            if (updateCredentialsHost is null)
                throw new InvalidOperationException("Brak skonfigurowanego adresu serwera aktualizacji.");
            Require(credentialsUser, "Podaj konto Windows na serwerze aktualizacji.");
            Require(credentialsPassword, "Podaj hasło konta aktualizacji.");
            try
            {
                UpdateCredentials.Save(updateCredentialsHost, credentialsUser.Text, credentialsPassword.Text);
                credentialsState.Text = status.Text = "Zapisano poświadczenia w Menedżerze poświadczeń Windows.";
            }
            finally { credentialsPassword.Clear(); }
            return Task.CompletedTask;
        });
        saveCredentials.Enabled = updateHost is not null;
        Add("", saveCredentials);
        var testCredentials = Action("Sprawdź dostęp do aktualizacji", async () =>
        {
            if (string.IsNullOrWhiteSpace(updateShare))
                throw new InvalidOperationException("Brak skonfigurowanego udziału aktualizacji.");
            try
            {
                string json = await Task.Run(() => UpdateCredentials.Test(updateShare));
                using var data = JsonDocument.Parse(json);
                string? version = data.RootElement.TryGetProperty("version", out var versionValue)
                    ? versionValue.GetString() : null;
                credentialsState.Text = status.Text = "Dostęp do aktualizacji działa. Wersja na serwerze: " +
                    (version ?? "nieznana") + ".";
            }
            catch (Exception ex)
            {
                credentialsState.Text = "Nie udało się odczytać aktualizacji: " + ex.Message +
                    " Jeśli dostęp SMB korzysta ze starej sesji, wyloguj się z Windows i zaloguj ponownie.";
                throw;
            }
        });
        testCredentials.Enabled = updateHost is not null;
        Add("", testCredentials);
        var deleteCredentials = Action("Usuń zapisane poświadczenia", () =>
        {
            if (updateCredentialsHost is null)
                throw new InvalidOperationException("Brak skonfigurowanego adresu serwera aktualizacji.");
            if (MessageBox.Show(this, "Usunąć zapisane poświadczenia aktualizacji z Windows?",
                "KartyLeczenia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return Task.CompletedTask;
            UpdateCredentials.Delete(updateCredentialsHost);
            credentialsUser.Clear();
            credentialsPassword.Clear();
            credentialsState.Text = status.Text = "Usunięto poświadczenia aktualizacji.";
            return Task.CompletedTask;
        });
        deleteCredentials.Enabled = updateHost is not null;
        Add("", deleteCredentials);

        form = Section("Wygląd");
        Add("Wersja programu", new Label
        {
            Text = ServerUpdateGate.InstalledVersion,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        });
        Add("", AppearanceEditor.Create(this, options => applyAppearance?.Invoke(options)));
        var scaleSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, AccessibleName = "Skala interfejsu" };
        scaleSelector.Items.AddRange(InterfaceScale.Percentages.Select(value => $"{value}%").ToArray());
        int savedScale = InterfaceScale.Load();
        scaleSelector.SelectedIndex = Array.IndexOf(InterfaceScale.Percentages.ToArray(), savedScale);
        Add("Skala interfejsu", scaleSelector);
        var scaleMessage = Message(form, "Zmiana zacznie działać po ponownym uruchomieniu programu.", multiline: true);
        Add("", Action("Zapisz skalę", () =>
        {
            int selectedScale = InterfaceScale.Percentages[scaleSelector.SelectedIndex];
            InterfaceScale.Save(selectedScale);
            scaleMessage.Text = status.Text = selectedScale == interfaceScale
                ? $"Zapisano skalę {selectedScale}%."
                : $"Zapisano skalę {selectedScale}%. Zamknij i uruchom ponownie program.";
            return Task.CompletedTask;
        }));

        var settingsPage = Page("Ustawienia", sections);
        if (sharedArchive is not null && connection is null) sections.SelectedIndex = 1;
        Shown += (_, _) =>
        {
            if (tabs.SelectedTab == settingsPage && sections.SelectedIndex == 0 && sharedArchive is null)
            {
                if (archiveServer.TextLength == 0) archiveServer.Focus();
                else if (archivePassword.Enabled) archivePassword.Focus();
            }
        };

        foreach (var grid in new[] { documents, prices, expiry, treatment })
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Parametry listy…", null, (_, _) => ListParameters.Show(this, grid));
            grid.ContextMenuStrip = menu;
        }
    }
}
