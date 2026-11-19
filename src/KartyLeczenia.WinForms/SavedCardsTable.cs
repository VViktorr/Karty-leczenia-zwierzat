using System.Globalization;
using System.Text.Json;

namespace KartyLeczenia.Windows;

/// <summary>
/// AntdUI replacement for the saved-card DataGridView.
/// Keeping it separate from the editable treatment grid avoids changing card
/// save/print behavior and lets each table retain its own layout preferences.
/// </summary>
internal sealed class SavedCardsTable : AntdUI.Table
{
    static readonly string LayoutPath = Path.Combine(
        SharedArchive.LocalDataFolder, "saved-cards-antdui.json");

    readonly int interfaceScale;
    readonly AntdUI.Column[] cardColumns;

    sealed record SavedLayout(int[] Order, int[] Widths, bool[] Visible);

    internal SavedCardsTable(int interfaceScale)
    {
        this.interfaceScale = interfaceScale;
        Dock = DockStyle.Fill;
        AccessibleName = "Zapisane karty leczenia – tabela AntdUI";
        EmptyHeader = true;
        EmptyText = "Nie znaleziono zapisanych kart";
        EnableHeaderResizing = true;
        ColumnDragSort = true;

        cardColumns =
        [
            NewColumn(nameof(SavedTreatmentCard.Number), "Nr karty", 125),
            NewColumn(nameof(SavedTreatmentCard.Date), "Data leczenia", 115, "dd.MM.yyyy"),
            NewColumn(nameof(SavedTreatmentCard.Client), "Klient", 350),
            NewColumn(nameof(SavedTreatmentCard.Document), "Dokument", 180)
        ];
        foreach (var column in cardColumns) Columns.Add(column);
        RestoreLayout();
    }

    AntdUI.Column NewColumn(string name, string title, int width, string? format = null) =>
        new(name, title)
        {
            Width = Scale(width).ToString(CultureInfo.InvariantCulture),
            DisplayFormat = format,
            Ellipsis = true,
            SortOrder = true
        };

    int Scale(int pixels) => (int)Math.Round(pixels * interfaceScale / 100d);

    internal SavedTreatmentCard? SelectedCard() =>
        SelectedsReal().OfType<SavedTreatmentCard>().FirstOrDefault();

    void RestoreLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath) ||
                JsonSerializer.Deserialize<SavedLayout>(File.ReadAllText(LayoutPath)) is not { } layout)
                return;

            if (layout.Widths?.Length == cardColumns.Length)
                for (int i = 0; i < cardColumns.Length; i++)
                    cardColumns[i].Width = Scale(Math.Clamp(layout.Widths[i], 40, 2000))
                        .ToString(CultureInfo.InvariantCulture);

            if (layout.Visible?.Length == cardColumns.Length && layout.Visible.Any(x => x))
                for (int i = 0; i < cardColumns.Length; i++)
                    cardColumns[i].Visible = layout.Visible[i];

            if (layout.Order?.Length == cardColumns.Length &&
                layout.Order.Order().SequenceEqual(Enumerable.Range(0, cardColumns.Length)))
                SetSortColumnsIndex(layout.Order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLog.Write("SavedCardsTable.Restore", ex);
        }
    }

    internal void SaveLayout()
    {
        try
        {
            var widths = cardColumns.Select(column => column.WidthPixel > 0
                ? (int)Math.Round(column.WidthPixel / (Dpi * interfaceScale / 100d))
                : (int)Math.Round(int.Parse(column.Width!, CultureInfo.InvariantCulture)
                    * 100d / interfaceScale)).ToArray();
            Directory.CreateDirectory(SharedArchive.LocalDataFolder);
            string tmp = LayoutPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(
                    new SavedLayout(SortColumnsIndex(), widths,
                        cardColumns.Select(column => column.Visible).ToArray())));
                File.Move(tmp, LayoutPath, overwrite: true);
            }
            finally { File.Delete(tmp); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("SavedCardsTable.Save", ex);
        }
    }
}
