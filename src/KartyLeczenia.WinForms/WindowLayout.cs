using System.Text.Json;

namespace KartyLeczenia.Windows;

internal sealed class WindowLayout(Form window, CheckBox rememberTreatment, Dictionary<string, DataGridView> tables)
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "winforms-layout.json");

    sealed record Layout(Rectangle Bounds, bool Maximized, Dictionary<string, TableLayout> Tables, bool RememberTreatment = false);
    sealed record TableLayout(int RowHeight, bool MultiSelect, bool RowHeaders, DataGridViewCellBorderStyle Borders,
        ColumnLayout[] Columns);
    sealed record ColumnLayout(string Name, int Width, int Order, bool Visible,
        ColumnTextStyle? Header = null, ColumnTextStyle? Data = null, string? ValueOption = null);

    internal void Restore()
    {
        Layout? layout;
        try
        {
            if (!File.Exists(FilePath)) return;
            layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return;
        }
        if (layout is null) return;

        rememberTreatment.Checked = layout.RememberTreatment;
        var bounds = layout.Bounds;
        var visible = Rectangle.Intersect(bounds, Screen.FromRectangle(bounds).WorkingArea);
        if (visible.Width >= 100 && visible.Height >= 50 &&
            bounds.Width >= window.MinimumSize.Width && bounds.Height >= window.MinimumSize.Height)
        {
            window.StartPosition = FormStartPosition.Manual;
            window.Bounds = bounds;
        }
        if (layout.Maximized) window.WindowState = FormWindowState.Maximized;

        foreach (var (name, grid) in tables)
        {
            if (layout.Tables is null || !layout.Tables.TryGetValue(name, out var saved) || saved is null) continue;
            grid.RowTemplate.Height = Math.Clamp(saved.RowHeight, grid.RowTemplate.MinimumHeight, 65536);
            grid.MultiSelect = saved.MultiSelect;
            grid.RowHeadersVisible = saved.RowHeaders;
            if (Enum.IsDefined(saved.Borders)) grid.CellBorderStyle = saved.Borders;
            foreach (var column in (saved.Columns ?? []).Where(column => column?.Name is not null).OrderBy(column => column.Order))
            {
                if (grid.Columns[column.Name] is not { } current) continue;
                current.Width = Math.Clamp(column.Width, current.MinimumWidth, 65536);
                current.DisplayIndex = Math.Clamp(column.Order, 0, grid.Columns.Count - 1);
                current.Visible = column.Visible;
                DocumentColumnOptions.Apply(current, column.ValueOption);
                column.Header?.Apply(grid, current, true);
                column.Data?.Apply(grid, current, false);
            }
            if (grid.Columns.Count > 0 && grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible) is null)
                grid.Columns[0].Visible = true;
        }
    }

    internal void Save()
    {
        var bounds = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
        var savedTables = tables.ToDictionary(entry => entry.Key, entry =>
        {
            var grid = entry.Value;
            return new TableLayout(grid.RowTemplate.Height, grid.MultiSelect, grid.RowHeadersVisible, grid.CellBorderStyle,
                grid.Columns.Cast<DataGridViewColumn>()
                    .Select(column => new ColumnLayout(column.Name, column.Width, column.DisplayIndex, column.Visible,
                        ColumnTextStyle.Get(column, true), ColumnTextStyle.Get(column, false), DocumentColumnOptions.Read(column)))
                    .ToArray());
        });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
                    new Layout(bounds, window.WindowState == FormWindowState.Maximized, savedTables, rememberTreatment.Checked)));
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
            finally { File.Delete(temporaryPath); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Zapis układu okna: {0}", ex.Message);
        }
    }
}
