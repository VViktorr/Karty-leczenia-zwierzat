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

    internal static string[] Names { get; } = ["Klasyczny", "Jasny", "Ciemny", "Kompaktowy", "Wygodny", "Własny"];
    internal static AppearanceOptions ForPreset(string name) => name switch
    {
        "Jasny" => new() { Preset = name, RowHeight = 30, TableFontSize = 10, WideTabs = true },
        "Ciemny" => new() { Preset = name, RowHeight = 30, TableFontSize = 10, WideTabs = true },
        "Kompaktowy" => new() { Preset = name, RowHeight = 22, TableFontSize = 9, AlternatingRows = false },
        "Wygodny" => new() { Preset = name, RowHeight = 36, TableFontSize = 12, WideTabs = true },
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
            return value is null ? new AppearanceOptions() : Normalize(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppearanceOptions();
        }
    }

    internal static AppearanceOptions Normalize(AppearanceOptions value)
    {
        value.TableFontSize = Math.Clamp(value.TableFontSize, 8, 18);
        value.RowHeight = Math.Clamp(value.RowHeight, 20, 60);
        value.PrintPreviewZoom = Math.Clamp(value.PrintPreviewZoom, 50, 200);
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

    sealed class GridState(DataGridView grid)
    {
        internal AppearanceOptions Options = new();
        internal Font? BoldFont;
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
            grid.Disposed += (_, _) => state.BoldFont?.Dispose();
        }
        state.Options = appearance;
        state.BoldFont?.Dispose();
        state.BoldFont = new Font(grid.Font.FontFamily, appearance.TableFontSize, FontStyle.Bold, GraphicsUnit.Point);
        Normalize(appearance);
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
            grid.RowTemplate.Height = state.RowHeight;
            grid.Font = state.Font;
            grid.ShowCellToolTips = appearance.ShowToolTips;
            grid.Invalidate();
            return;
        }
        bool dark = appearance.Preset == "Ciemny";
        bool classic = appearance.Preset == "Klasyczny";
        Color background = dark ? Color.FromArgb(31, 35, 42) : Color.White;
        Color foreground = dark ? Color.FromArgb(235, 238, 244) : SystemColors.WindowText;
        Color header = dark ? Color.FromArgb(47, 53, 63) : SystemColors.Control;
        Color accent = appearance.UseCustomAccent ? Color.FromArgb(appearance.AccentArgb) : Color.FromArgb(35, 100, 179);

        grid.BackgroundColor = dark ? background : SystemColors.Window;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = dark ? foreground : SystemColors.ControlText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = header;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = dark ? foreground : SystemColors.ControlText;
        grid.DefaultCellStyle.BackColor = background;
        grid.DefaultCellStyle.ForeColor = foreground;
        grid.DefaultCellStyle.SelectionBackColor = classic ? SystemColors.Highlight : accent;
        grid.DefaultCellStyle.SelectionForeColor = classic ? SystemColors.HighlightText : Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = appearance.AlternatingRows
            ? dark ? Color.FromArgb(39, 45, 54) : Color.FromArgb(244, 247, 250)
            : background;
        grid.GridColor = dark ? Color.FromArgb(72, 78, 89) : SystemColors.ControlDark;
        grid.ShowCellToolTips = appearance.ShowToolTips;
        grid.CellBorderStyle = appearance.GridLines ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.None;
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.MinimumHeight, appearance.RowHeight);
        grid.Font = new Font(grid.Font.FontFamily, appearance.TableFontSize, grid.Font.Style, GraphicsUnit.Point);
        grid.Invalidate();
    }

    sealed class SurfaceColors(Color background, Color foreground)
    {
        internal Color Background { get; } = background;
        internal Color Foreground { get; } = foreground;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, SurfaceColors> Surfaces = new();

    // Store each control's original palette once so returning to Classic does
    // not replace custom buttons, panels or layouts with generic system colors.
    internal static void ApplySurface(Control control, AppearanceOptions appearance)
    {
        if (control is DataGridView) return;
        if (control is Form or Panel or TableLayoutPanel or FlowLayoutPanel or TabPage
            or TabControl or Label or CheckBox or GroupBox or StatusStrip or TextBox or ComboBox)
        {
            var original = Surfaces.GetValue(control, item => new SurfaceColors(item.BackColor, item.ForeColor));
            if (appearance.Preset == "Ciemny")
            {
                bool input = control is TextBox or ComboBox;
                control.BackColor = input ? Color.FromArgb(40, 46, 55) : Color.FromArgb(32, 37, 45);
                control.ForeColor = Color.FromArgb(237, 239, 243);
            }
            else
            {
                control.BackColor = original.Background;
                control.ForeColor = original.Foreground;
            }
        }
        foreach (Control child in control.Controls)
            ApplySurface(child, appearance);
    }

    internal static void ApplyTabs(TabControl tabs, AppearanceOptions appearance)
    {
        tabs.Padding = appearance.WideTabs ? new Point(36, 12) : new Point(30, 9);
        tabs.Invalidate();
    }
}

internal static class AppearanceEditor
{
    internal static Control Create(Form owner, Action<AppearanceOptions> apply)
    {
        AppearanceOptions original = AppearanceThemes.Load();
        AppearanceOptions current = original with { };
        // Prefer a narrow vertical editor when application scaling is increased.
        // Its controls must fit into the settings page without horizontal clipping.
        bool vertical = InterfaceScale.Load() > 100;
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
        var customAccent = new CheckBox { Text = "Własny kolor zaznaczenia", AutoSize = true };
        var accent = new Button { Text = "Wybierz kolor…", AutoSize = true };
        var explanation = new Label { AutoSize = true, MaximumSize = new Size(vertical ? 390 : 620, 0),
            Text = "Motyw Klasyczny odpowiada dotychczasowemu wyglądowi. Indywidualne szerokości i kolejność kolumn pozostają bez zmian." };
        var preview = new DataGridView { Width = vertical ? 390 : 600, Height = 120, ReadOnly = true, RowHeadersVisible = false,
            AllowUserToAddRows = false, AllowUserToResizeRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        preview.Columns.Add("document", "Dokument");
        preview.Columns.Add("customer", "Kontrahent");
        preview.Columns.Add("value", "Wartość");
        preview.Rows.Add("FS 128/26", "Lecznica A", "248,00 zł");
        preview.Rows.Add("FS 129/26", "Lecznica B", "560,00 zł");
        preview.ClearSelection();

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
            accent.BackColor = Color.FromArgb(current.AccentArgb);
            AppearanceThemes.ApplyGrid(preview, current);
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
            AppearanceThemes.ApplyGrid(preview, current);
        }
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedItem is not string name) return;
            if (name == "Własny") current.Preset = name;
            else current = AppearanceOptions.ForPreset(name);
            LoadControls();
        };
        foreach (var checkbox in new[] { lines, stripes, bold, last, maximize, tooltips, wide, customAccent })
            checkbox.CheckedChanged += (_, _) => Capture();
        size.ValueChanged += (_, _) => Capture();
        height.ValueChanged += (_, _) => Capture();
        zoom.ValueChanged += (_, _) => Capture();
        accent.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = Color.FromArgb(current.AccentArgb), FullOpen = true };
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
        AddLabeled("Motyw", preset);
        panel.Controls.Add(explanation);
        AddLabeled("Czcionka tabel (pkt)", size);
        AddLabeled("Wysokość wierszy (px)", height);
        panel.Controls.Add(lines);
        panel.Controls.Add(stripes);
        panel.Controls.Add(bold);
        panel.Controls.Add(last);
        panel.Controls.Add(maximize);
        panel.Controls.Add(tooltips);
        panel.Controls.Add(wide);
        panel.Controls.Add(customAccent);
        panel.Controls.Add(accent);
        AddLabeled("Powiększenie podglądu wydruku (%)", zoom);
        panel.Controls.Add(new Label { Text = "Podgląd motywu", AutoSize = true });
        panel.Controls.Add(preview);
        var actions = new FlowLayoutPanel { AutoSize = true, Width = vertical ? 390 : 640, WrapContents = true };
        var applyButton = new Button { Text = "Zastosuj", AutoSize = true };
        var save = new Button { Text = "Zapisz motyw", AutoSize = true };
        var reset = new Button { Text = "Przywróć Klasyczny", AutoSize = true };
        actions.Controls.AddRange([applyButton, save, reset]);
        panel.Controls.Add(actions);
        applyButton.Click += (_, _) =>
        {
            Capture();
            apply(current with { });
        };
        save.Click += (_, _) =>
        {
            Capture();
            AppearanceThemes.Save(current);
            apply(current);
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
