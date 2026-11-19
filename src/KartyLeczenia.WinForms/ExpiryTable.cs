using System.Globalization;
using System.Text.Json;
using AntdUI;

namespace KartyLeczenia.Windows;

internal sealed class ExpiryTable : Table
{
    static readonly string LayoutPath = Path.Combine(SharedArchive.LocalDataFolder, "expiry-antdui.json");
    readonly int interfaceScale;
    readonly Column[] expiryColumns;
    IReadOnlyList<MedicineExpiry> rows = [];
    Color? alternatingBackground;
    string? sortKey;
    SortMode sortMode;

    // ShowAntd remains only for backward compatibility with earlier saved layouts.
    sealed record Layout(bool ShowAntd, int[] Order, int[] Widths, bool[]? Visible = null);

    internal ExpiryTable(int interfaceScale)
    {
        this.interfaceScale = interfaceScale;
        Dock = DockStyle.Fill;
        AccessibleName = "Terminy ważności – tabela AntdUI";
        EmptyText = "Brak dostaw dla wybranego filtra";
        EmptyHeader = true;
        EnableHeaderResizing = true;
        ColumnDragSort = true;
        MultipleRows = true;

        expiryColumns =
        [
            CreateColumn(nameof(MedicineExpiry.Name), "Lek", 340),
            CreateColumn(nameof(MedicineExpiry.Batch), "Seria", 150),
            CreateColumn(nameof(MedicineExpiry.Received), "Przyjęcie", 120, "dd.MM.yyyy"),
            CreateColumn(nameof(MedicineExpiry.Expires), "Ważność", 120, "dd.MM.yyyy"),
            CreateColumn(nameof(MedicineExpiry.Remaining), "Pozostało", 115, "0.####")
        ];
        expiryColumns[^1].Align = ColumnAlign.Right;
        foreach (var column in expiryColumns) Columns.Add(column);
        RestoreLayout();
        SetRowStyle += (_, e) => alternatingBackground is { } background && e.Index % 2 == 1
            ? new CellStyleInfo().SetBackColor(background) : null;
        SortModeChanged += (_, e) =>
        {
            sortKey = e.Column.Key;
            sortMode = e.SortMode;
            ApplySort();
            return true;
        };
    }

    Column CreateColumn(string key, string title, int width, string? format = null) =>
        new(key, title) { Width = Scale(width).ToString(CultureInfo.InvariantCulture), DisplayFormat = format,
            Ellipsis = true, SortOrder = true };

    int Scale(int value) => (int)Math.Round(value * interfaceScale / 100d);

    internal void ShowRows(List<MedicineExpiry> values)
    {
        rows = values;
        DataSource = values;
        ApplySort();
    }

    void ApplySort()
    {
        if (sortMode == SortMode.NONE)
        {
            SetSortIndex(null);
            return;
        }
        var indices = Enumerable.Range(0, rows.Count);
        bool descending = sortMode == SortMode.DESC;
        int[] Sort<TKey>(Func<MedicineExpiry, TKey> key, IComparer<TKey>? comparer = null) =>
            (descending ? indices.OrderByDescending(i => key(rows[i]), comparer)
                : indices.OrderBy(i => key(rows[i]), comparer)).ToArray();

        SetSortIndex(sortKey switch
        {
            nameof(MedicineExpiry.Name) => Sort(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            nameof(MedicineExpiry.Batch) => Sort(row => row.Batch, StringComparer.CurrentCultureIgnoreCase),
            nameof(MedicineExpiry.Received) => Sort(row => row.Received),
            nameof(MedicineExpiry.Expires) => Sort(row => row.Expires),
            nameof(MedicineExpiry.Remaining) => Sort(row => row.Remaining),
            _ => null
        });
    }

    internal void ApplyAppearance(AppearanceOptions options)
    {
        var canvas = AppearanceThemes.Canvas(options);
        var ink = AppearanceThemes.Ink(options);
        var accent = AppearanceThemes.Accent(options);
        bool dark = AppearanceThemes.IsDark(options);
        var background = dark ? AppearanceThemes.Blend(canvas, Color.White, .03f) : Color.White;
        ColorScheme = dark ? TAMode.Dark : TAMode.Light;
        BackColor = background;
        ForeColor = ColumnFore = ink;
        ColumnBack = AppearanceThemes.Blend(canvas, ink, .06f);
        BorderColor = AppearanceThemes.Blend(background, ink, dark ? .23f : .13f);
        Bordered = options.GridLines;
        RowHeight = Scale(options.RowHeight);
        RowHoverBg = AppearanceThemes.Blend(background, accent, .12f);
        RowSelectedBg = accent;
        RowSelectedFore = AppearanceThemes.ContrastingText(accent);
        ShowTip = options.ShowToolTips;
        alternatingBackground = options.AlternatingRows ? AppearanceThemes.Blend(background, ink, .045f) : null;
        var previousFont = themeFont;
        themeFont = new Font(Font.FontFamily, options.TableFontSize * interfaceScale / 100f, Font.Style, GraphicsUnit.Point);
        Font = themeFont;
        previousFont?.Dispose();
        Invalidate();
    }

    Font? themeFont;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) themeFont?.Dispose();
    }

    void RestoreLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath) || JsonSerializer.Deserialize<Layout>(File.ReadAllText(LayoutPath)) is not { } layout) return;
            if (layout.Widths?.Length == expiryColumns.Length)
                for (int i = 0; i < expiryColumns.Length; i++)
                    expiryColumns[i].Width = Scale(Math.Clamp(layout.Widths[i], 40, 2000)).ToString(CultureInfo.InvariantCulture);
            if (layout.Visible?.Length == expiryColumns.Length)
            {
                // At least one column must remain visible.
                if (layout.Visible.Any(visible => visible))
                    for (int i = 0; i < expiryColumns.Length; i++)
                        expiryColumns[i].Visible = layout.Visible[i];
            }
            if (layout.Order?.Length == expiryColumns.Length &&
                layout.Order.Order().SequenceEqual(Enumerable.Range(0, expiryColumns.Length)))
                SetSortColumnsIndex(layout.Order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLog.Write("ExpiryTable.Restore", ex);
        }
    }

    internal void ShowColumnsMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        int visibleColumns = expiryColumns.Count(column => column.Visible);
        foreach (var column in expiryColumns)
        {
            var item = new ToolStripMenuItem(column.Title)
            {
                Checked = column.Visible,
                CheckOnClick = false,
                Enabled = !column.Visible || visibleColumns > 1
            };
            item.Click += (_, _) =>
            {
                column.Visible = !column.Visible;
                Invalidate();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Przywróć wszystkie kolumny", null, (_, _) =>
        {
            foreach (var column in expiryColumns) column.Visible = true;
            Invalidate();
        });
        // Keep the popup alive until it closes.
        menu.Show(anchor, new Point(0, anchor.Height));
        // Don't dispose the popup before WinForms has finished showing it.
        menu.Closed += (_, _) => menu.Dispose();
    }

    internal void SaveLayout()
    {
        try
        {
            var widths = expiryColumns.Select(column => column.WidthPixel > 0
                ? (int)Math.Round(column.WidthPixel / (Dpi * interfaceScale / 100d))
                : (int)Math.Round(int.Parse(column.Width!, CultureInfo.InvariantCulture) * 100d / interfaceScale)).ToArray();
            Directory.CreateDirectory(SharedArchive.LocalDataFolder);
            string temp = LayoutPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(new Layout(true, SortColumnsIndex(), widths, expiryColumns.Select(column => column.Visible).ToArray())));
                File.Move(temp, LayoutPath, overwrite: true);
            }
            finally { File.Delete(temp); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ExpiryTable.Save", ex);
        }
    }
}
