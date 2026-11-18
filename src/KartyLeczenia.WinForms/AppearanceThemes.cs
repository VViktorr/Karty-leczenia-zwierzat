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
    public int SwitchStyle { get; set; } = 1; // 0 Windows, 1 filled, 2 outline
    public int CornerRadius { get; set; } = 8;
    public bool ColorButtons { get; set; } = true;

    internal static string[] Names { get; } = ["Klasyczny", "Jasny", "Ciemny", "Błękitny", "Zielony", "Fioletowy", "Grafitowy", "Kompaktowy", "Wygodny", "Własny"];
    internal static AppearanceOptions ForPreset(string name) => name switch
    {
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

    internal static bool IsDark(AppearanceOptions options) => options.Preset is "Ciemny" or "Grafitowy";
    internal static bool IsClassic(AppearanceOptions options) =>
        options == AppearanceOptions.ForPreset("Klasyczny");
    internal static Color Accent(AppearanceOptions options) =>
        Color.FromArgb(options.AccentArgb);

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
        bool dark = IsDark(appearance);
        bool classic = appearance.Preset == "Klasyczny";
        Color background = dark ? Color.FromArgb(31, 35, 42) : Color.White;
        Color foreground = dark ? Color.FromArgb(235, 238, 244) : SystemColors.WindowText;
        Color header = dark ? Color.FromArgb(47, 53, 63) : SystemColors.Control;
        Color accent = Accent(appearance);

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

    sealed class SurfaceColors(Control control)
    {
        internal readonly Color Background = control.BackColor;
        internal readonly Color Foreground = control.ForeColor;
        internal readonly FlatStyle? ButtonFlat = control is ButtonBase button ? button.FlatStyle : null;
        internal readonly bool? VisualStyle = control is ButtonBase styled ? styled.UseVisualStyleBackColor : null;
        internal readonly int? Border = control is ButtonBase bordered ? bordered.FlatAppearance.BorderSize : null;
        internal readonly Color? BorderColor = control is ButtonBase colored ? colored.FlatAppearance.BorderColor : null;
        internal readonly Color? MouseOver = control is ButtonBase hovered ? hovered.FlatAppearance.MouseOverBackColor : null;
        internal readonly Color? MouseDown = control is ButtonBase pressed ? pressed.FlatAppearance.MouseDownBackColor : null;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, SurfaceColors> Surfaces = new();

    static Color Blend(Color a, Color b, float ratio) =>
        Color.FromArgb((int)(a.R * (1 - ratio) + b.R * ratio),
            (int)(a.G * (1 - ratio) + b.G * ratio),
            (int)(a.B * (1 - ratio) + b.B * ratio));

    static void UpdateRoundCorners(ButtonBase button, int radius)
    {
        var oldRegion = button.Region;
        if (radius == 0 || button.Width < 5 || button.Height < 5)
        {
            button.Region = null;
            oldRegion?.Dispose();
            return;
        }
        int diameter = Math.Min(radius * 2, Math.Min(button.Width, button.Height));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int right = button.Width - diameter - 1, bottom = button.Height - diameter - 1;
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(right, 0, diameter, diameter, 270, 90);
        path.AddArc(right, bottom, diameter, diameter, 0, 90);
        path.AddArc(0, bottom, diameter, diameter, 90, 90);
        path.CloseFigure();
        button.Region = new Region(path);
        oldRegion?.Dispose();
    }

    static void StyleButton(ButtonBase button, AppearanceOptions appearance, SurfaceColors original)
    {
        if (IsClassic(appearance) || appearance.SwitchStyle == 0 ||
            (button is Button && !appearance.ColorButtons))
        {
            if (original.ButtonFlat is { } flat) button.FlatStyle = flat;
            if (original.VisualStyle is { } visual) button.UseVisualStyleBackColor = visual;
            if (original.Border is { } border) button.FlatAppearance.BorderSize = border;
            if (original.BorderColor is { } borderColor) button.FlatAppearance.BorderColor = borderColor;
            if (original.MouseOver is { } hover) button.FlatAppearance.MouseOverBackColor = hover;
            if (original.MouseDown is { } down) button.FlatAppearance.MouseDownBackColor = down;
            UpdateRoundCorners(button, 0);
            return;
        }

        // Do not redraw regular checkboxes/radio circles. Only button-like
        // selectors (Appearance.Button) get the rounded switch treatment.
        if (button is CheckBox check && check.Appearance != Appearance.Button) return;
        if (button is RadioButton radio && radio.Appearance != Appearance.Button) return;
        Color accent = Accent(appearance);
        bool selected = button switch
        {
            CheckBox cb => cb.Checked,
            RadioButton rb => rb.Checked,
            _ => false
        };
        bool dark = IsDark(appearance);
        Color background = dark ? Color.FromArgb(49, 56, 66) : Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = appearance.SwitchStyle == 2 ? 2 : 1;
        button.FlatAppearance.BorderColor = selected ? accent : Blend(accent, background, .65f);
        button.FlatAppearance.MouseOverBackColor = Blend(background, accent, dark ? .35f : .16f);
        button.FlatAppearance.MouseDownBackColor = Blend(background, accent, .45f);
        if (selected)
        {
            button.BackColor = accent;
            button.ForeColor = Color.White;
        }
        else
        {
            button.BackColor = appearance.SwitchStyle == 2
                ? background : Blend(background, accent, dark ? .17f : .07f);
            button.ForeColor = dark ? Color.WhiteSmoke : Color.FromArgb(35, 40, 52);
        }
        UpdateRoundCorners(button, appearance.CornerRadius);
    }

    // Revert each individual control to its captured appearance when returning
    // to Classic; don't force the standard Windows palette over custom controls.
    internal static void ApplySurface(Control control, AppearanceOptions appearance)
    {
        if (control is DataGridView) return;
        bool classic = IsClassic(appearance);
        bool dark = IsDark(appearance);
        var original = Surfaces.GetValue(control, item => new SurfaceColors(item));
        if (control is Form or Panel or TableLayoutPanel or FlowLayoutPanel or TabPage
            or TabControl or Label or CheckBox or RadioButton or Button or GroupBox
            or StatusStrip or TextBox or ComboBox)
        {
            bool input = control is TextBox or ComboBox;
            if (classic)
            {
                control.BackColor = original.Background;
                control.ForeColor = original.Foreground;
            }
            else if (dark)
            {
                control.BackColor = input ? Color.FromArgb(40, 46, 55) : Color.FromArgb(32, 37, 45);
                control.ForeColor = Color.FromArgb(237, 239, 243);
            }
            else
            {
                control.BackColor = original.Background;
                control.ForeColor = original.Foreground;
            }
        }
        if (control is ButtonBase button)
        {
            if (original.ButtonFlat is not null && !button.IsDisposed)
                StyleButton(button, appearance, original);
            if (!classic && appearance.SwitchStyle != 0 && button is CheckBox or RadioButton)
            {
                // Keep selector visual state in sync when toggled.
                if (!ThemeHandlers.TryGetValue(button, out _))
                {
                    ThemeHandlers.Add(button, new ButtonThemeMarker());
                    if (button is CheckBox cb) cb.CheckedChanged += (_, _) => StyleButton(cb, CurrentStyles.GetValue(cb, _ => appearance), original);
                    if (button is RadioButton rb) rb.CheckedChanged += (_, _) => StyleButton(rb, CurrentStyles.GetValue(rb, _ => appearance), original);
                    button.SizeChanged += (_, _) => UpdateRoundCorners(button, CurrentStyles.GetValue(button, _ => appearance).CornerRadius);
                }
            }
            CurrentStyles.Remove(button);
            CurrentStyles.Add(button, appearance);
        }
        foreach (Control child in control.Controls)
            ApplySurface(child, appearance);
    }

    sealed class ButtonThemeMarker { }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ButtonBase, ButtonThemeMarker> ThemeHandlers = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ButtonBase, AppearanceOptions> CurrentStyles = new();

    internal static void ApplyTabs(TabControl tabs, AppearanceOptions appearance)
    {
        // Owner-drawn tab headers need their padding scaled too. Otherwise the
        // font grows but the tab hit area stays small and captions are cut off.
        float scale = Math.Max(1f, tabs.DeviceDpi / 96f) * InterfaceScale.Load() / 100f;
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
        AppearanceOptions original = AppearanceThemes.Load();
        AppearanceOptions current = original with { };
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
        switchStyle.Items.AddRange(["Systemowe", "Wypełnione", "Z obramowaniem"]);
        var cornerRadius = new NumericUpDown { Minimum = 0, Maximum = 20, Width = 80 };
        var colorButtons = new CheckBox { Text = "Koloruj także zwykłe przyciski", AutoSize = true };
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
        var switchPreview = new FlowLayoutPanel
        {
            AutoSize = true, Width = vertical ? 390 : 600, WrapContents = true,
            Padding = new Padding(0, 5, 0, 5)
        };
        var previewInvoice = new RadioButton { Text = "Faktury", Appearance = Appearance.Button, AutoSize = true, Checked = true };
        var previewReceipts = new RadioButton { Text = "Paragony", Appearance = Appearance.Button, AutoSize = true };
        var previewFilter = new CheckBox { Text = "Do 30 dni", Appearance = Appearance.Button, AutoSize = true };
        var previewAction = new Button { Text = "Zapisz", AutoSize = true };
        switchPreview.Controls.AddRange([previewInvoice, previewReceipts, previewFilter, previewAction]);

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
            accent.BackColor = Color.FromArgb(current.AccentArgb);
            AppearanceThemes.ApplyGrid(preview, current);
            AppearanceThemes.ApplySurface(switchPreview, current);
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
            AppearanceThemes.ApplyGrid(preview, current);
            AppearanceThemes.ApplySurface(switchPreview, current);
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
        switchStyle.SelectedIndexChanged += (_, _) => Capture();
        cornerRadius.ValueChanged += (_, _) => Capture();
        colorButtons.CheckedChanged += (_, _) => Capture();
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
        AddLabeled("Styl przełączników i przycisków", switchStyle);
        AddLabeled("Zaokrąglenie narożników (px)", cornerRadius);
        panel.Controls.Add(colorButtons);
        AddLabeled("Powiększenie podglądu wydruku (%)", zoom);
        panel.Controls.Add(new Label { Text = "Podgląd motywu", AutoSize = true });
        panel.Controls.Add(preview);
        panel.Controls.Add(new Label { Text = "Podgląd przełączników i przycisków", AutoSize = true });
        panel.Controls.Add(switchPreview);
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
