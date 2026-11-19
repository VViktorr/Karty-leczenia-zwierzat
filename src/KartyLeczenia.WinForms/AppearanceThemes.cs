using System.Text.Json;

namespace KartyLeczenia.Windows;

// Appearance preferences are local to each Windows workstation.
// They never alter the SQL archive, medicine catalogue or print templates.
internal sealed record AppearanceOptions
{
    public string Preset { get; set; } = "Klasyczny";
    public int TableFontSize { get; set; } = 10;
    public int RowHeight { get; set; } = 26;
    public bool GridLines { get; set; } = true;
    public bool AlternatingRows { get; set; } = true;
    public bool BoldSelection { get; set; } = true;
    public bool RememberLastTab { get; set; }
    public bool StartMaximized { get; set; }
    public bool ShowToolTips { get; set; } = true;
    public bool WideTabs { get; set; }
    public bool UseCustomAccent { get; set; }
    public int AccentArgb { get; set; } = Color.FromArgb(0, 100, 190).ToArgb();
    public int PrintPreviewZoom { get; set; } = 100;
    public int SwitchStyle { get; set; } = 1; // 0 neutral, 1 filled, 2 outline
    public int CornerRadius { get; set; } = 8;
    public bool ColorButtons { get; set; } = true;

    internal static string[] Names { get; } = ["Antd Jasny", "Antd Ciemny", "Antd Morski", "Antd Śliwkowy", "Klasyczny", "Jasny", "Ciemny", "Błękitny", "Zielony", "Fioletowy", "Grafitowy", "Kompaktowy", "Wygodny", "Własny"];
    internal static AppearanceOptions ForPreset(string name) => name switch
    {
        "Antd Jasny" => new() { Preset = name, RowHeight = 28, AccentArgb = Color.FromArgb(22, 119, 255).ToArgb() },
        "Antd Ciemny" => new() { Preset = name, RowHeight = 28, AccentArgb = Color.FromArgb(82, 153, 255).ToArgb() },
        "Antd Morski" => new() { Preset = name, RowHeight = 28, WideTabs = true, AccentArgb = Color.FromArgb(0, 116, 112).ToArgb() },
        "Antd Śliwkowy" => new() { Preset = name, RowHeight = 30, WideTabs = true, AccentArgb = Color.FromArgb(193, 146, 244).ToArgb() },
        "Jasny" => new() { Preset = name, RowHeight = 30, TableFontSize = 10, WideTabs = true },
        "Ciemny" => new() { Preset = name, RowHeight = 30, TableFontSize = 10, WideTabs = true, AccentArgb = Color.FromArgb(83, 157, 235).ToArgb() },
        "Błękitny" => new() { Preset = name, WideTabs = true, AccentArgb = Color.FromArgb(20, 108, 200).ToArgb() },
        "Zielony" => new() { Preset = name, WideTabs = true, AccentArgb = Color.FromArgb(32, 126, 86).ToArgb() },
        "Fioletowy" => new() { Preset = name, WideTabs = true, AccentArgb = Color.FromArgb(118, 72, 177).ToArgb() },
        "Grafitowy" => new() { Preset = name, WideTabs = true, AccentArgb = Color.FromArgb(93, 122, 150).ToArgb() },
        "Kompaktowy" => new() { Preset = name, RowHeight = 22, TableFontSize = 9, AlternatingRows = false, CornerRadius = 4 },
        "Wygodny" => new() { Preset = name, RowHeight = 36, TableFontSize = 12, WideTabs = true, CornerRadius = 12 },
        _ => new() { Preset = name }
    };
}

internal static class AppearanceThemes
{
    static readonly string FilePath = Path.Combine(SharedArchive.LocalDataFolder, "appearance.json");
    internal static AppearanceOptions Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<AppearanceOptions>(File.ReadAllText(FilePath));
            return value is null ? AppearanceOptions.ForPreset("Antd Jasny") : Normalize(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppearanceOptions.ForPreset("Antd Jasny");
        }
    }

    internal static AppearanceOptions Normalize(AppearanceOptions value)
    {
        value.TableFontSize = Math.Clamp(value.TableFontSize, 8, 18);
        value.RowHeight = Math.Clamp(value.RowHeight, 20, 60);
        value.PrintPreviewZoom = Math.Clamp(value.PrintPreviewZoom, 50, 200);
        value.SwitchStyle = Math.Clamp(value.SwitchStyle, 0, 2);
        value.CornerRadius = Math.Clamp(value.CornerRadius, 0, 20);
        if (!AppearanceOptions.Names.Contains(value.Preset)) value.Preset = "Klasyczny";
        return value;
    }

    internal static void Save(AppearanceOptions value)
    {
        Directory.CreateDirectory(SharedArchive.LocalDataFolder);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(Normalize(value), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        finally { File.Delete(temp); }
    }

    internal static bool IsDark(AppearanceOptions options) => options.Preset is "Antd Ciemny" or "Antd Śliwkowy" or "Ciemny" or "Grafitowy";
    internal static bool IsClassic(AppearanceOptions options) =>
        options == AppearanceOptions.ForPreset("Klasyczny");
    internal static Color Accent(AppearanceOptions options) =>
        Color.FromArgb(options.UseCustomAccent ? options.AccentArgb : AppearanceOptions.ForPreset(options.Preset).AccentArgb);

    internal static AppearanceOptions Current { get; set; } = Load();

    internal static Color Canvas(AppearanceOptions options) => options.Preset switch
    {
        "Antd Ciemny" => Color.FromArgb(20, 20, 24),
        "Antd Śliwkowy" => Color.FromArgb(35, 28, 47),
        "Antd Morski" => Color.FromArgb(236, 247, 246),
        _ => IsDark(options) ? Color.FromArgb(32, 37, 45) : Color.FromArgb(245, 247, 250)
    };

    internal static Color Ink(AppearanceOptions options) =>
        IsDark(options) ? Color.FromArgb(237, 239, 243) : Color.FromArgb(30, 37, 49);

    sealed class GridState(DataGridView grid)
    {
        internal AppearanceOptions Options = new();
        internal Font? BoldFont;
        internal Font? ThemedFont;
        internal int? AppliedRowHeight;
        internal readonly Color Background = grid.BackgroundColor;
        internal readonly Color GridColor = grid.GridColor;
        internal readonly Font Font = grid.Font;
        internal readonly int RowHeight = grid.RowTemplate.Height;
        internal readonly bool HeaderStylesEnabled = grid.EnableHeadersVisualStyles;
        internal readonly DataGridViewCellBorderStyle Border = grid.CellBorderStyle;
        internal readonly DataGridViewCellStyle Header = grid.ColumnHeadersDefaultCellStyle.Clone();
        internal readonly DataGridViewCellStyle Data = grid.DefaultCellStyle.Clone();
        internal readonly DataGridViewCellStyle Alternating = grid.AlternatingRowsDefaultCellStyle.Clone();
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridView, GridState> GridStates = new();

    internal static void ApplyGrid(DataGridView grid, AppearanceOptions appearance)
    {
        if (!GridStates.TryGetValue(grid, out var state))
        {
            state = new GridState(grid);
            GridStates.Add(grid, state);
            grid.CellFormatting += (_, args) =>
            {
                if (args.RowIndex >= 0 && state.Options.BoldSelection
                    && grid.Rows[args.RowIndex].Selected && state.BoldFont is not null)
                    args.CellStyle.Font = state.BoldFont;
            };
            grid.Disposed += (_, _) =>
            {
                state.BoldFont?.Dispose();
                state.ThemedFont?.Dispose();
            };
        }
        state.Options = appearance;
        Normalize(appearance);
        // Release only fonts owned by the theme engine, never a user-supplied font.
        Font? previousFont = state.ThemedFont;
        state.ThemedFont = null;
        grid.Font = state.Font;
        previousFont?.Dispose();
        state.BoldFont?.Dispose();
        state.BoldFont = new Font(state.Font.FontFamily, appearance.TableFontSize,
            FontStyle.Bold, GraphicsUnit.Point);
        if (appearance == AppearanceOptions.ForPreset("Klasyczny"))
        {
            // Restore the exact styles captured before the first theme change.
            grid.BackgroundColor = state.Background;
            grid.GridColor = state.GridColor;
            grid.EnableHeadersVisualStyles = state.HeaderStylesEnabled;
            grid.ColumnHeadersDefaultCellStyle = state.Header.Clone();
            grid.DefaultCellStyle = state.Data.Clone();
            grid.AlternatingRowsDefaultCellStyle = state.Alternating.Clone();
            grid.CellBorderStyle = state.Border;
            int previousRowHeight = state.AppliedRowHeight ?? state.RowHeight;
            grid.RowTemplate.Height = state.RowHeight;
            foreach (DataGridViewRow row in grid.Rows)
                if (!row.IsNewRow && row.Height == previousRowHeight)
                    row.Height = state.RowHeight;
            state.AppliedRowHeight = null;
            grid.Font = state.Font;
            grid.ShowCellToolTips = appearance.ShowToolTips;
            grid.Invalidate();
            return;
        }
        bool dark = IsDark(appearance);
        bool classic = appearance.Preset == "Klasyczny";
        Color canvas = Canvas(appearance);
        Color background = dark ? Blend(canvas, Color.White, .03f) : Color.White;
        Color foreground = Ink(appearance);
        Color header = classic ? SystemColors.Control : Blend(canvas, foreground, dark ? .10f : .05f);
        Color accent = Accent(appearance);

        grid.BackgroundColor = background;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = dark ? foreground : SystemColors.ControlText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = header;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = dark ? foreground : SystemColors.ControlText;
        grid.DefaultCellStyle.BackColor = background;
        grid.DefaultCellStyle.ForeColor = foreground;
        grid.DefaultCellStyle.SelectionBackColor = classic ? SystemColors.Highlight : accent;
        grid.DefaultCellStyle.SelectionForeColor = classic ? SystemColors.HighlightText : ContrastingText(accent);
        grid.AlternatingRowsDefaultCellStyle.BackColor = appearance.AlternatingRows
            ? Blend(background, foreground, dark ? .045f : .03f)
            : background;
        grid.GridColor = classic ? SystemColors.ControlDark : Blend(background, foreground, dark ? .24f : .14f);
        grid.ShowCellToolTips = appearance.ShowToolTips;
        grid.CellBorderStyle = appearance.GridLines ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.None;
        int previousAppliedHeight = state.AppliedRowHeight ?? state.RowHeight;
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.MinimumHeight, appearance.RowHeight);
        state.AppliedRowHeight = grid.RowTemplate.Height;
        state.ThemedFont = new Font(state.Font.FontFamily, appearance.TableFontSize,
            state.Font.Style, GraphicsUnit.Point);
        grid.Font = state.ThemedFont;
        // Existing rows retain their old height unless it is explicitly applied.
        // Respect an operator's manually resized rows rather than rewriting them.
        foreach (DataGridViewRow row in grid.Rows)
            if (!row.IsNewRow && row.Height == previousAppliedHeight)
                row.Height = grid.RowTemplate.Height;
        grid.Invalidate();
    }

    sealed class SurfaceColors(Control control)
    {
        internal readonly Color Background = control.BackColor;
        internal readonly Color Foreground = control.ForeColor;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, SurfaceColors> Surfaces = new();

    internal static Color Blend(Color a, Color b, float ratio) =>
        Color.FromArgb((int)(a.R * (1 - ratio) + b.R * ratio),
            (int)(a.G * (1 - ratio) + b.G * ratio),
            (int)(a.B * (1 - ratio) + b.B * ratio));

    internal static Color ContrastingText(Color fill)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }

        double luminance = .2126 * Linear(fill.R) + .7152 * Linear(fill.G) + .0722 * Linear(fill.B);
        return luminance > .179 ? Color.Black : Color.White;
    }

    internal static void ApplyButton(Button button, AppearanceOptions options)
    {
        bool selector = button is ThemeSelector;
        bool selected = selector && button.Toggle;
        bool classic = IsClassic(options);
        bool neutral = options.SwitchStyle == 0 || (!selector && !options.ColorButtons);
        bool outlined = options.SwitchStyle == 2;
        bool dark = IsDark(options);
        Color surface = button.Parent?.BackColor ?? (classic ? SystemColors.Control : Canvas(options));
        Color ink = Ink(options);
        Color accent = neutral ? (dark ? Color.LightGray : SystemColors.Highlight) : Accent(options);
        Color edge = dark ? Blend(accent, Color.White, .40f) : Blend(accent, Color.Black, .26f);
        bool filled = selected && !outlined;
        Color fill = filled ? (dark ? accent : Blend(accent, Color.Black, .12f))
            : Blend(surface, accent, selected ? .18f : neutral || outlined ? 0f : .05f);
        Color hover = Blend(fill, filled ? ink : accent, dark ? .17f : .11f);
        Color pressed = Blend(fill, filled ? ink : accent, dark ? .24f : .18f);

        button.ColorScheme = dark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        button.Radius = neutral ? 4 : options.CornerRadius;
        button.Type = filled ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        button.BorderWidth = filled ? 0 : selected && outlined ? 2 : 1;
        button.DefaultBorderColor = selected ? edge : Blend(surface, neutral ? ink : edge, .30f);
        button.DefaultBack = button.BackColor = button.ToggleBack = fill;
        button.ForeColor = button.ToggleFore = filled ? ContrastingText(fill) : ink;
        button.ForeHover = button.ToggleForeHover = filled ? ContrastingText(hover) : ink;
        button.ForeActive = button.ToggleForeActive = filled ? ContrastingText(pressed) : ink;
        button.BackHover = button.ToggleBackHover = filled ? hover : edge;
        button.BackActive = button.ToggleBackActive = filled ? pressed : edge;
        if (button.Tag is string shortcut)
            button.AccessibleDescription = selected ? shortcut + " · wybrano" : shortcut;
        button.Invalidate();
    }

    internal static void ApplySurface(Control control, AppearanceOptions appearance)
    {
        bool classic = IsClassic(appearance);
        bool dark = IsDark(appearance);
        if (control is DataGridView grid)
        {
            ApplyGrid(grid, appearance);
            return;
        }
        switch (control)
        {
            case Button button:
                ApplyButton(button, appearance);
                break;
            case CheckBox check:
                check.ColorScheme = dark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
                check.ForeColor = classic ? SystemColors.ControlText : Ink(appearance);
                check.Fill = classic ? SystemColors.Highlight : Accent(appearance);
                break;
            case DateTimePicker date:
                date.CalendarMonthBackground = classic ? SystemColors.Window : Canvas(appearance);
                date.CalendarForeColor = classic ? SystemColors.WindowText : Ink(appearance);
                date.CalendarTitleBackColor = classic ? SystemColors.ActiveCaption : Accent(appearance);
                date.CalendarTitleForeColor = classic ? SystemColors.ActiveCaptionText : ContrastingText(Accent(appearance));
                break;
            case Form or Panel or TabPage or TabControl or Label or GroupBox or ToolStrip
                or TextBoxBase or ListControl or NumericUpDown:
                var original = Surfaces.GetValue(control, item => new SurfaceColors(item));
                var canvas = Canvas(appearance);
                bool input = control is TextBoxBase or ListControl or NumericUpDown;
                control.BackColor = classic ? original.Background
                    : input ? (dark ? Blend(canvas, Color.White, .07f) : Color.White) : canvas;
                control.ForeColor = classic ? original.Foreground : Ink(appearance);
                if (control is TabPage page) page.UseVisualStyleBackColor = classic;
                break;
        }

        if (control.ContextMenuStrip is { } menu) ApplySurface(menu, appearance);
        foreach (Control child in control.Controls) ApplySurface(child, appearance);
    }

    internal static void ApplyTabs(TabControl tabs, AppearanceOptions appearance, int interfaceScale)
    {
        // Owner-drawn tab headers need their padding scaled too. Otherwise the
        // font grows but the tab hit area stays small and captions are cut off.
        float scale = Math.Max(1f, tabs.DeviceDpi / 96f) * interfaceScale / 100f;
        int horizontal = appearance.WideTabs ? 36 : 30;
        int vertical = appearance.WideTabs ? 12 : 9;
        tabs.Padding = new Point((int)Math.Ceiling(horizontal * scale),
            (int)Math.Ceiling(vertical * scale));
        tabs.Multiline = scale > 1.01f;
        tabs.Invalidate();
    }
}

internal static class AppearanceEditor
{
    internal static Control Create(Form owner, Action<AppearanceOptions> apply)
    {
        AppearanceOptions current = AppearanceThemes.Load();
        // Prefer a narrow vertical editor when application scaling is increased.
        // Its controls must fit into the settings page without horizontal clipping.
        bool vertical = InterfaceScale.Load() > 100 || owner.DeviceDpi > 120;
        var panel = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4), Width = vertical ? 420 : 660
        };
        var preset = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        preset.Items.AddRange(AppearanceOptions.Names);
        preset.SelectedItem = current.Preset;

        var size = new NumericUpDown { Minimum = 8, Maximum = 18, Width = 80 };
        var height = new NumericUpDown { Minimum = 20, Maximum = 60, Width = 80 };
        var zoom = new NumericUpDown { Minimum = 50, Maximum = 200, Increment = 10, Width = 80 };
        var lines = new CheckBox { Text = "Linie siatki tabel", AutoSize = true };
        var stripes = new CheckBox { Text = "Naprzemienne kolory wierszy", AutoSize = true };
        var bold = new CheckBox { Text = "Pogrubienie zaznaczonego wiersza", AutoSize = true };
        var last = new CheckBox { Text = "Otwieraj ostatnio używaną zakładkę", AutoSize = true };
        var maximize = new CheckBox { Text = "Otwieraj zmaksymalizowane okno", AutoSize = true };
        var tooltips = new CheckBox { Text = "Wyświetlaj podpowiedzi w tabelach", AutoSize = true };
        var wide = new CheckBox { Text = "Powiększone zakładki", AutoSize = true };
        var customAccent = new CheckBox { Text = "Własny kolor zaznaczenia i przełączników", AutoSize = true };
        var switchStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        switchStyle.Items.AddRange(["Neutralne", "Wypełnione", "Z obramowaniem"]);
        var cornerRadius = new NumericUpDown { Minimum = 0, Maximum = 20, Width = 80 };
        var colorButtons = new CheckBox { Text = "Koloruj także zwykłe przyciski", AutoSize = true };
        var accent = new Button { Text = "Wybierz kolor…", AutoSize = true };
        var accentSample = new Label { Width = 28, Height = 24, AccessibleName = "Próbka akcentu" };
        var preview = new DataGridView { Width = vertical ? 390 : 600, Height = 120, ReadOnly = true, RowHeadersVisible = false,
            AllowUserToAddRows = false, AllowUserToResizeRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        preview.Columns.Add("document", "Dokument");
        preview.Columns.Add("customer", "Kontrahent");
        preview.Columns.Add("value", "Wartość");
        preview.Rows.Add("FS 128/26", "Lecznica A", "248,00 zł");
        preview.Rows.Add("FS 129/26", "Lecznica B", "560,00 zł");
        preview.ClearSelection();
        var switchPreview = new FlowLayoutPanel
        {
            AutoSize = true, Width = vertical ? 390 : 600, WrapContents = true,
            Padding = new Padding(0, 5, 0, 5)
        };
        var previewInvoice = new ThemeSelector { Text = "Faktury", Toggle = true };
        var previewReceipts = new ThemeSelector { Text = "Paragony" };
        var previewFilter = new CheckBox { Text = "Netto", AutoSize = true };
        var previewAction = new Button { Text = "Zapisz", AutoSize = true };
        switchPreview.Controls.AddRange([previewInvoice, previewReceipts, previewFilter, previewAction]);
        void RenderPreview()
        {
            AppearanceThemes.ApplyGrid(preview, current);
            AppearanceThemes.ApplySurface(switchPreview, current);
            accentSample.BackColor = AppearanceThemes.Accent(current);
        }
        previewInvoice.Click += (_, _) =>
        {
            previewInvoice.Toggle = true;
            previewReceipts.Toggle = false;
            RenderPreview();
        };
        previewReceipts.Click += (_, _) =>
        {
            previewInvoice.Toggle = false;
            previewReceipts.Toggle = true;
            RenderPreview();
        };

        bool loading = false;
        void LoadControls()
        {
            loading = true;
            size.Value = current.TableFontSize;
            height.Value = current.RowHeight;
            zoom.Value = current.PrintPreviewZoom;
            lines.Checked = current.GridLines;
            stripes.Checked = current.AlternatingRows;
            bold.Checked = current.BoldSelection;
            last.Checked = current.RememberLastTab;
            maximize.Checked = current.StartMaximized;
            tooltips.Checked = current.ShowToolTips;
            wide.Checked = current.WideTabs;
            customAccent.Checked = current.UseCustomAccent;
            switchStyle.SelectedIndex = current.SwitchStyle;
            cornerRadius.Value = current.CornerRadius;
            colorButtons.Checked = current.ColorButtons;
            accent.Enabled = customAccent.Checked;
            RenderPreview();
            loading = false;
        }
        void Capture()
        {
            if (loading) return;
            CaptureValues();
        }
        void CaptureValues()
        {
            current.TableFontSize = (int)size.Value;
            current.RowHeight = (int)height.Value;
            current.PrintPreviewZoom = (int)zoom.Value;
            current.GridLines = lines.Checked;
            current.AlternatingRows = stripes.Checked;
            current.BoldSelection = bold.Checked;
            current.RememberLastTab = last.Checked;
            current.StartMaximized = maximize.Checked;
            current.ShowToolTips = tooltips.Checked;
            current.WideTabs = wide.Checked;
            current.UseCustomAccent = customAccent.Checked;
            current.SwitchStyle = switchStyle.SelectedIndex < 0 ? 1 : switchStyle.SelectedIndex;
            current.CornerRadius = (int)cornerRadius.Value;
            current.ColorButtons = colorButtons.Checked;
            accent.Enabled = customAccent.Checked;
            RenderPreview();
        }
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedItem is not string name) return;
            if (name == "Własny") current.Preset = name;
            else current = AppearanceOptions.ForPreset(name) with
            {
                RememberLastTab = current.RememberLastTab,
                StartMaximized = current.StartMaximized,
                PrintPreviewZoom = current.PrintPreviewZoom
            };
            LoadControls();
        };
        foreach (var checkbox in new[] { lines, stripes, bold, last, maximize, tooltips, wide, customAccent })
            checkbox.CheckedChanged += (_, _) => Capture();
        size.ValueChanged += (_, _) => Capture();
        height.ValueChanged += (_, _) => Capture();
        zoom.ValueChanged += (_, _) => Capture();
        switchStyle.SelectedIndexChanged += (_, _) => Capture();
        cornerRadius.ValueChanged += (_, _) => Capture();
        colorButtons.CheckedChanged += (_, _) => Capture();
        accent.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = AppearanceThemes.Accent(current), FullOpen = true };
            if (dialog.ShowDialog(owner) != DialogResult.OK) return;
            current.AccentArgb = dialog.Color.ToArgb();
            current.UseCustomAccent = true;
            LoadControls();
        };

        void AddLabeled(string label, Control control)
        {
            var line = new FlowLayoutPanel
            {
                AutoSize = true, WrapContents = false,
                FlowDirection = vertical ? FlowDirection.TopDown : FlowDirection.LeftToRight,
                Width = vertical ? 390 : 640
            };
            line.Controls.Add(new Label
            {
                Text = label, AutoSize = vertical, Width = vertical ? 390 : 240,
                MaximumSize = new Size(vertical ? 390 : 240, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });
            line.Controls.Add(control);
            panel.Controls.Add(line);
        }
        void Heading(string text)
        {
            panel.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(3, 12, 3, 4) });
        }
        AddLabeled("Motyw kolorystyczny", preset);
        Heading("Podgląd");
        panel.Controls.Add(switchPreview);
        panel.Controls.Add(preview);

        Heading("Kolory i przełączniki");
        AddLabeled("Styl przełączników", switchStyle);
        AddLabeled("Zaokrąglenie narożników (px)", cornerRadius);
        panel.Controls.Add(colorButtons);
        panel.Controls.Add(customAccent);
        var accentTools = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        accentTools.Controls.AddRange([accent, accentSample]);
        panel.Controls.Add(accentTools);

        Heading("Tabele");
        AddLabeled("Czcionka tabel (pkt)", size);
        AddLabeled("Wysokość wierszy (px)", height);
        panel.Controls.Add(lines);
        panel.Controls.Add(stripes);
        panel.Controls.Add(bold);
        panel.Controls.Add(tooltips);

        Heading("Okno i wydruki");
        panel.Controls.Add(wide);
        panel.Controls.Add(last);
        panel.Controls.Add(maximize);
        AddLabeled("Powiększenie podglądu wydruku (%)", zoom);
        var actions = new FlowLayoutPanel { AutoSize = true, Width = vertical ? 390 : 640, WrapContents = true };
        var applyButton = new Button { Text = "Zastosuj tymczasowo", AutoSize = true };
        var save = new Button { Text = "Zapisz i zastosuj", AutoSize = true };
        var reset = new Button { Text = "Przywróć Klasyczny", AutoSize = true };
        actions.Controls.AddRange([applyButton, save, reset]);
        panel.Controls.Add(actions);
        applyButton.Click += (_, _) =>
        {
            Capture();
            apply(current with { });
            RenderPreview();
        };
        save.Click += (_, _) =>
        {
            Capture();
            AppearanceThemes.Save(current);
            apply(current with { });
            RenderPreview();
            MessageBox.Show(owner, "Zapisano wygląd programu. Niektóre ustawienia okien będą widoczne po ponownym uruchomieniu.",
                "KartyLeczenia", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        reset.Click += (_, _) =>
        {
            current = AppearanceOptions.ForPreset("Klasyczny");
            preset.SelectedItem = "Klasyczny";
            LoadControls();
        };
        LoadControls();
        return panel;
    }
}
