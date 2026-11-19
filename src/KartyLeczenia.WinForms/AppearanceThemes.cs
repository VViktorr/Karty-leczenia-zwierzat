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
    // 0 = line, 1 = filled card, 2 = tonal card, 3 = minimal contrast card.
    public int MainTabStyle { get; set; } = 1;
    public int MainTabPosition { get; set; } = 0; // top or bottom
    public int MainTabSpacing { get; set; } = 8;
    public bool UseCustomAccent { get; set; }
    public int AccentArgb { get; set; } = Color.FromArgb(0, 100, 190).ToArgb();
    public int PrintPreviewZoom { get; set; } = 100;
    public int SwitchStyle { get; set; } = 3; // 0 subtle, 1 filled, 2 outline, 3 tonal, 4 pill
    public int CornerRadius { get; set; } = 8;
    public bool ColorButtons { get; set; } = true;

    // All contemporary presets use AntdUI's built-in color algorithm.
    // Klasyczny is the sole retained legacy/compatibility preset.
    internal static string[] Names { get; } =
        ["Antd Jasny", "Antd Ciemny", "Antd Morski", "Antd Śliwkowy", "Antd Fioletowy", "Antd Pomarańczowy", "Antd Grafitowy", "Material 3 Jasny", "Material 3 Ciemny", "Klasyczny"];
    internal static AppearanceOptions ForPreset(string name) => name switch
    {
        "Antd Jasny" => new() { Preset = name, RowHeight = 28, AccentArgb = Color.FromArgb(22, 119, 255).ToArgb() },
        "Antd Ciemny" => new() { Preset = name, RowHeight = 28, AccentArgb = Color.FromArgb(82, 153, 255).ToArgb() },
        "Antd Morski" => new() { Preset = name, RowHeight = 28, WideTabs = true, AccentArgb = Color.FromArgb(0, 116, 112).ToArgb() },
        "Antd Śliwkowy" => new() { Preset = name, RowHeight = 30, WideTabs = true, AccentArgb = Color.FromArgb(193, 146, 244).ToArgb() },
        "Antd Fioletowy" => new() { Preset = name, RowHeight = 30, WideTabs = true, AccentArgb = Color.FromArgb(114, 46, 209).ToArgb() },
        "Antd Pomarańczowy" => new() { Preset = name, RowHeight = 30, AccentArgb = Color.FromArgb(250, 140, 22).ToArgb() },
        "Antd Grafitowy" => new() { Preset = name, RowHeight = 28, AccentArgb = Color.FromArgb(93, 122, 150).ToArgb() },
        // Material-inspired palette on AntdUI controls, not a second UI toolkit.
        "Material 3 Jasny" => new() { Preset = name, SwitchStyle = 3, MainTabStyle = 2, RowHeight = 32, CornerRadius = 16, AccentArgb = Color.FromArgb(103, 80, 164).ToArgb() },
        "Material 3 Ciemny" => new() { Preset = name, SwitchStyle = 3, MainTabStyle = 2, RowHeight = 32, CornerRadius = 16, AccentArgb = Color.FromArgb(208, 188, 255).ToArgb() },
        "Klasyczny" => new() { Preset = name, MainTabStyle = 3, SwitchStyle = 0 },
        _ => ForPreset("Antd Jasny")
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
        value.SwitchStyle = Math.Clamp(value.SwitchStyle, 0, 4);
        value.CornerRadius = Math.Clamp(value.CornerRadius, 0, 20);
        value.MainTabStyle = Math.Clamp(value.MainTabStyle, 0, 3);
        value.MainTabPosition = Math.Clamp(value.MainTabPosition, 0, 1);
        value.MainTabSpacing = Math.Clamp(value.MainTabSpacing, 2, 24);
        // Migrate earlier settings without losing the operator's fonts,
        // layout preferences, print zoom or a manually chosen accent.
        if (!AppearanceOptions.Names.Contains(value.Preset))
        {
            value.Preset = value.Preset switch
            {
                "Ciemny" => "Antd Ciemny",
                "Grafitowy" => "Antd Grafitowy",
                "Zielony" => "Antd Morski",
                "Fioletowy" => "Antd Fioletowy",
                "Wygodny" or "Kompaktowy" or "Jasny" or "Błękitny" or "Własny" => "Antd Jasny",
                _ => "Antd Jasny"
            };
            // Existing custom accent still works after the legacy preset is removed.
        }
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

    internal static bool IsDark(AppearanceOptions options) => options.Preset is "Antd Ciemny" or "Antd Śliwkowy" or "Antd Grafitowy" or "Material 3 Ciemny";
    internal static bool IsClassic(AppearanceOptions options) =>
        options.Preset == "Klasyczny";
    internal static Color Accent(AppearanceOptions options) =>
        Color.FromArgb(options.UseCustomAccent ? options.AccentArgb : AppearanceOptions.ForPreset(options.Preset).AccentArgb);

    internal static AppearanceOptions Current { get; set; } = Load();

    internal static Color Canvas(AppearanceOptions options) => options.Preset switch
    {
        "Antd Ciemny" => Color.FromArgb(20, 20, 24),
        "Antd Śliwkowy" => Color.FromArgb(35, 28, 47),
        "Antd Morski" => Color.FromArgb(236, 247, 246),
        "Antd Grafitowy" => Color.FromArgb(29, 35, 44),
        "Material 3 Jasny" => Color.FromArgb(255, 251, 254),
        "Material 3 Ciemny" => Color.FromArgb(28, 27, 31),
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
        if (IsClassic(appearance))
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
        bool dark = IsDark(options);
        bool classic = IsClassic(options);
        int style = classic ? 0 : options.SwitchStyle;
        Color surface = Canvas(options);
        Color accent = Accent(options);
        Color ink = Ink(options);

        // AntdUI has independent normal and Toggle surfaces. Previously both
        // were written with the *current* Toggle colour and Type/DefaultBack
        // were additionally mixed in, causing uneven patches on redraw.
        // Set stable colours for both states instead; changing Toggle only
        // switches the state and never requires reconstructing the palette.
        Color normal = surface;
        Color active = style switch
        {
            1 => accent,
            2 => Blend(surface, accent, dark ? .14f : .08f),
            3 or 4 => Blend(surface, accent, dark ? .36f : .22f),
            _ => Blend(surface, accent, dark ? .30f : .18f)
        };
        Color selectedFore = style == 1 ? ContrastingText(accent) : accent;
        bool hasBorder = style == 2;
        int radius = style == 4 ? 20 : Math.Clamp(options.CornerRadius, 4, 20);

        button.ColorScheme = dark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        button.Type = AntdUI.TTypeMini.Default;
        button.ToggleType = AntdUI.TTypeMini.Default;
        button.Radius = radius;
        button.BorderWidth = hasBorder ? (button.Toggle ? 2.25f : 1f) : 0f;
        button.DefaultBorderColor = hasBorder
            ? (button.Toggle ? accent : Blend(surface, ink, dark ? .45f : .22f))
            : Color.Transparent;
        button.Ghost = false;
        button.BackExtend = null;
        button.ToggleBackExtend = null;

        // Do not set Control.BackColor and DefaultBack independently. The
        // AntdUI renderer owns its background, avoiding a second WinForms fill.
        button.BackColor = normal;
        button.DefaultBack = normal;
        button.ForeColor = ink;
        button.ForeHover = ink;
        button.ForeActive = ink;
        button.BackHover = Blend(normal, accent, dark ? .18f : .09f);
        button.BackActive = Blend(normal, accent, dark ? .25f : .15f);

        if (selector)
        {
            button.ToggleBack = active;
            button.ToggleFore = selectedFore;
            button.ToggleForeHover = selectedFore;
            button.ToggleForeActive = selectedFore;
            button.ToggleBackHover = Blend(active, style == 1 ? Color.White : accent, .12f);
            button.ToggleBackActive = Blend(active, style == 1 ? Color.Black : accent, .15f);
            button.AutoToggle = false;
            button.WaveSize = 0;
            button.Margin = new Padding(3);
            button.MinimumSize = new Size(0, 34);
            button.AccessibleRole = AccessibleRole.RadioButton;
        }
        else
        {
            // Normal action buttons stay neutral; the "Color buttons" setting
            // controls only their hover treatment, not a blanket primary fill.
            button.ToggleBack = null;
            button.ToggleFore = null;
            button.ToggleForeHover = null;
            button.ToggleForeActive = null;
            button.ToggleBackHover = null;
            button.ToggleBackActive = null;
            if (!options.ColorButtons)
            {
                button.BackHover = Blend(normal, ink, dark ? .12f : .06f);
                button.BackActive = Blend(normal, ink, dark ? .20f : .10f);
            }
        }
        if (button.Tag is string shortcut)
            button.AccessibleDescription = button.Toggle ? shortcut + " · wybrano" : shortcut;
        button.Invalidate();
    }

    /// <summary>
    /// Make related AntdUI selectors the same height/width. The widest caption
    /// sets the minimum; no one tab gets a different fill because AutoSize grew.
    /// </summary>
    internal static void EqualizeSelectors(IEnumerable<ThemeSelector> selectors)
    {
        var group = selectors.ToArray();
        if (group.Length == 0) return;
        int width = Math.Max(92, group.Max(x =>
            TextRenderer.MeasureText(x.Text, x.Font).Width + 28));
        int height = Math.Max(34, group.Max(x => x.PreferredSize.Height));
        foreach (var selector in group)
        {
            selector.AutoSize = false;
            selector.Size = new Size(width, height);
            selector.MinimumSize = new Size(width, height);
        }
    }

    sealed class AntdTableFontState(AntdUI.Table table)
    {
        internal readonly Font OriginalFont = table.Font;
        // AntdUI may still measure cached rows after a theme update.
        // Keep previously assigned fonts alive until the table is disposed.
        internal readonly List<Font> ThemeFonts = [];
        internal Font? ThemeFont;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AntdUI.Table, AntdTableFontState>
        AntdTableFonts = new();

    static void ApplyAntdTable(AntdUI.Table table, AppearanceOptions appearance)
    {
        Color canvas = Canvas(appearance);
        Color ink = Ink(appearance);
        Color accent = Accent(appearance);
        bool dark = IsDark(appearance);
        var background = dark ? Blend(canvas, Color.White, .03f) : Color.White;
        table.ColorScheme = dark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        table.BackColor = background;
        table.ForeColor = table.ColumnFore = ink;
        table.ColumnBack = Blend(canvas, ink, .06f);
        table.BorderColor = Blend(background, ink, dark ? .23f : .13f);
        table.Bordered = appearance.GridLines;
        table.RowHoverBg = Blend(background, accent, .12f);
        table.RowSelectedBg = accent;
        table.RowSelectedFore = ContrastingText(accent);
        table.ShowTip = appearance.ShowToolTips;

        var state = AntdTableFonts.GetValue(table, t =>
        {
            var next = new AntdTableFontState(t);
            t.Disposed += (_, _) =>
            {
                foreach (var font in next.ThemeFonts) font.Dispose();
                next.ThemeFonts.Clear();
            };
            return next;
        });
        var font = new Font(state.OriginalFont.FontFamily, appearance.TableFontSize,
            state.OriginalFont.Style, GraphicsUnit.Point);
        state.ThemeFonts.Add(font);
        state.ThemeFont = font;
        // Set the new font before triggering AntdUI's synchronous row re-layout.
        table.Font = font;
        table.RowHeight = appearance.RowHeight;
        table.Invalidate();
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
            case ExpiryTable expiry:
                expiry.ApplyAppearance(appearance);
                return;
            case AntdUI.Table table:
                ApplyAntdTable(table, appearance);
                return;
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
        int horizontal = (appearance.WideTabs ? 30 : 20) + appearance.MainTabSpacing;
        int vertical = appearance.WideTabs ? 12 : 9;
        tabs.Alignment = appearance.MainTabPosition == 1 ? TabAlignment.Bottom : TabAlignment.Top;
        tabs.Padding = new Point((int)Math.Ceiling(horizontal * scale),
            (int)Math.Ceiling(vertical * scale));
        // WinForms TabControl is still used for the content pages. Its
        // owner-drawn header mirrors the AntdUI Line and Card design choices.
        tabs.Multiline = scale > 1.01f || tabs.Alignment == TabAlignment.Bottom;
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
        var tabStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        tabStyle.Items.AddRange(["Linia z wyróżnieniem", "Wyraźna aktywna karta", "Karty tonalne", "Minimalne kontrastowe"]);
        var tabPosition = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        tabPosition.Items.AddRange(["U góry", "Na dole"]);
        var tabSpacing = new NumericUpDown { Minimum = 2, Maximum = 24, Width = 80 };
        switchStyle.Items.AddRange(["Subtelne", "Pełne wypełnienie", "Kontur", "Tonalne (Material 3)", "Pastylki"]);
        var cornerRadius = new NumericUpDown { Minimum = 0, Maximum = 20, Width = 80 };
        var colorButtons = new CheckBox { Text = "Koloruj także zwykłe przyciski", AutoSize = true };
        var accent = new Button { Text = "Wybierz kolor…", AutoSize = true };
        var accentSample = new Label { Width = 28, Height = 24, AccessibleName = "Próbka akcentu" };
        // Preview the actual AntdUI.Table used by Documents and Price lists,
        // not the old native DataGridView, which has different selection paint.
        var preview = new AntdUI.Table
        {
            Width = vertical ? 390 : 600, Height = 145,
            EmptyHeader = true, Bordered = true,
            RowHeight = 28, MultipleRows = false,
            DataSource = new[]
            {
                new { Document = "FS 128/26", Customer = "Lecznica A", Amount = "248,00 zł" },
                new { Document = "FS 129/26", Customer = "Lecznica B", Amount = "560,00 zł" }
            }
        };
        preview.Columns.Add(new AntdUI.Column("Document", "Dokument") { Width = "120" });
        preview.Columns.Add(new AntdUI.Column("Customer", "Kontrahent") { Width = "190" });
        preview.Columns.Add(new AntdUI.Column("Amount", "Wartość") { Width = "120" });
        var tabPreview = new TabControl
        {
            Width = vertical ? 390 : 600, Height = 112,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            Padding = new Point(24, 8)
        };
        tabPreview.TabPages.Add("Dokumenty");
        tabPreview.TabPages.Add("Cennik leków");
        tabPreview.TabPages.Add("Karta leczenia");
        tabPreview.TabPages.Add("Ustawienia");
        tabPreview.DrawItem += (_, e) => MainTabPainter.Draw(tabPreview, e, current);
        var switchPreview = new FlowLayoutPanel
        {
            AutoSize = true, Width = vertical ? 390 : 600, WrapContents = true,
            Padding = new Padding(0, 5, 0, 5)
        };
        var previewInvoice = new ThemeSelector { Text = "Faktury", Toggle = true };
        var previewReceipts = new ThemeSelector { Text = "Paragony" };
        var previewWarehouse = new ThemeSelector { Text = "WZ" };
        var previewAll = new ThemeSelector { Text = "Wszystkie", Toggle = true };
        var previewExpired = new ThemeSelector { Text = "Przeterminowane" };
        var previewSoon = new ThemeSelector { Text = "Do 30 dni" };
        var previewFilter = new CheckBox { Text = "Netto", AutoSize = true };
        var previewAdditional = new CheckBox { Text = "Pokaż karencję", AutoSize = true, Checked = true };
        var previewAction = new Button { Text = "Zapisz", AutoSize = true };
        var previewCancel = new Button { Text = "Anuluj", AutoSize = true };
        var previewSelection = new FlowLayoutPanel
        {
            AutoSize = true, Width = vertical ? 390 : 600,
            WrapContents = true, Padding = new Padding(0, 2, 0, 4)
        };
        previewSelection.Controls.AddRange([previewAll, previewExpired, previewSoon]);
        switchPreview.Controls.AddRange([previewInvoice, previewReceipts,
            previewWarehouse, previewFilter, previewAdditional, previewAction, previewCancel]);
        void RenderPreview()
        {
            AppearanceThemes.ApplySurface(preview, current);
            AppearanceThemes.ApplyTabs(tabPreview, current, InterfaceScale.Load());
            tabPreview.Invalidate();
            AppearanceThemes.ApplySurface(switchPreview, current);
            AppearanceThemes.ApplySurface(previewSelection, current);
            accentSample.BackColor = AppearanceThemes.Accent(current);
        }
        previewInvoice.Click += (_, _) =>
        {
            previewInvoice.Toggle = true;
            previewReceipts.Toggle = previewWarehouse.Toggle = false;
            RenderPreview();
        };
        previewReceipts.Click += (_, _) =>
        {
            previewInvoice.Toggle = false;
            previewReceipts.Toggle = true;
            previewWarehouse.Toggle = false;
            RenderPreview();
        };
        previewWarehouse.Click += (_, _) =>
        {
            previewInvoice.Toggle = previewReceipts.Toggle = false;
            previewWarehouse.Toggle = true;
            RenderPreview();
        };
        void SelectPreviewFilter(ThemeSelector chosen)
        {
            foreach (var selector in new[] { previewAll, previewExpired, previewSoon })
                selector.Toggle = ReferenceEquals(selector, chosen);
            RenderPreview();
        }
        previewAll.Click += (_, _) => SelectPreviewFilter(previewAll);
        previewExpired.Click += (_, _) => SelectPreviewFilter(previewExpired);
        previewSoon.Click += (_, _) => SelectPreviewFilter(previewSoon);

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
            tabStyle.SelectedIndex = current.MainTabStyle;
            tabPosition.SelectedIndex = current.MainTabPosition;
            tabSpacing.Value = current.MainTabSpacing;
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
            current.SwitchStyle = switchStyle.SelectedIndex < 0 ? 3 : switchStyle.SelectedIndex;
            current.MainTabStyle = tabStyle.SelectedIndex < 0 ? 0 : tabStyle.SelectedIndex;
            current.MainTabPosition = tabPosition.SelectedIndex < 0 ? 0 : tabPosition.SelectedIndex;
            current.MainTabSpacing = (int)tabSpacing.Value;
            current.CornerRadius = (int)cornerRadius.Value;
            current.ColorButtons = colorButtons.Checked;
            accent.Enabled = customAccent.Checked;
            RenderPreview();
        }
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedItem is not string name) return;
            current = AppearanceOptions.ForPreset(name) with
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
        tabStyle.SelectedIndexChanged += (_, _) => Capture();
        tabPosition.SelectedIndexChanged += (_, _) => Capture();
        tabSpacing.ValueChanged += (_, _) => Capture();
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
        panel.Controls.Add(new Label { Text = "Przełączniki dokumentów i akcje", AutoSize = true });
        panel.Controls.Add(switchPreview);
        panel.Controls.Add(new Label { Text = "Filtry terminów ważności", AutoSize = true });
        panel.Controls.Add(previewSelection);
        panel.Controls.Add(preview);

        Heading("Główne zakładki");
        panel.Controls.Add(tabPreview);
        AddLabeled("Wygląd zakładek", tabStyle);
        AddLabeled("Położenie nagłówków", tabPosition);
        AddLabeled("Odstępy zakładek (px)", tabSpacing);
        panel.Controls.Add(wide);

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
        panel.Controls.Add(last);
        panel.Controls.Add(maximize);
        AddLabeled("Powiększenie podglądu wydruku (%)", zoom);
        var actions = new FlowLayoutPanel { AutoSize = true, Width = vertical ? 390 : 640, WrapContents = true };
        var applyButton = new Button { Text = "Zastosuj tymczasowo", AutoSize = true };
        var save = new Button { Text = "Zapisz i zastosuj", AutoSize = true };
        var reset = new Button { Text = "Przywróć ustawienia motywu", AutoSize = true };
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
            current = AppearanceOptions.ForPreset(current.Preset);
            preset.SelectedItem = current.Preset;
            LoadControls();
        };
        LoadControls();
        return panel;
    }
}
