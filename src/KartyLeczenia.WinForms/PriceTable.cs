using System.Globalization;
using System.Text.Json;

namespace KartyLeczenia.Windows;

/// <summary>
/// AntdUI medicine price table. Pricing and printing stay in the existing
/// Subiekt/SQL services; this control only displays their data.
/// </summary>
internal sealed class PriceTable : AntdUI.Table
{
    static readonly string LayoutPath = Path.Combine(SharedArchive.LocalDataFolder, "price-antdui.json");

    readonly int interfaceScale;
    readonly AntdUI.Column[] priceColumns;
    sealed record PriceLayout(int[] Order, int[] Widths, bool[] Visible);

    internal string PriceHeading { get; private set; } = "Cena brutto";

    internal PriceTable(int interfaceScale)
    {
        this.interfaceScale = interfaceScale;
        Dock = DockStyle.Fill;
        AccessibleName = "Cennik leków – tabela AntdUI";
        EmptyHeader = true;
        EmptyText = "Nie wczytano pozycji cennika";
        EnableHeaderResizing = true;
        ColumnDragSort = true;

        priceColumns =
        [
            NewColumn("Name", "Lek", 340),
            NewColumn("LatestBatch", "Najnowsza seria", 130),
            NewColumn("Gross", PriceHeading, 100, "N2"),
            NewColumn("Withdrawal", "Karencja z katalogu", 520)
        ];
        priceColumns[2].Align = AntdUI.ColumnAlign.Right;
        foreach (var column in priceColumns) Columns.Add(column);
        RestoreLayout();
    }

    AntdUI.Column NewColumn(string property, string header, int width, string? format = null) =>
        new(property, header)
        {
            Width = Scale(width).ToString(CultureInfo.InvariantCulture),
            DisplayFormat = format,
            Ellipsis = true,
            SortOrder = true
        };

    int Scale(int px) => (int)Math.Round(px * interfaceScale / 100d);

    internal MedicinePrice? SelectedPrice() =>
        SelectedsReal().OfType<MedicinePrice>().FirstOrDefault();

    internal void SetPriceHeading(bool net)
    {
        PriceHeading = net ? "Cena netto" : "Cena brutto";
        priceColumns[2].Title = PriceHeading;
        Invalidate();
    }

    internal void ShowColumnsMenu(Control anchor) =>
        AntdTableColumnMenu.Show(anchor, this, priceColumns);

    void RestoreLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath) ||
                JsonSerializer.Deserialize<PriceLayout>(File.ReadAllText(LayoutPath)) is not { } layout)
                return;
            if (layout.Widths?.Length == priceColumns.Length)
                for (int i = 0; i < priceColumns.Length; i++)
                    priceColumns[i].Width = Scale(Math.Clamp(layout.Widths[i], 40, 2000))
                        .ToString(CultureInfo.InvariantCulture);

            if (layout.Visible?.Length == priceColumns.Length && layout.Visible.Any(x => x))
                for (int i = 0; i < priceColumns.Length; i++)
                    priceColumns[i].Visible = layout.Visible[i];

            if (layout.Order?.Length == priceColumns.Length &&
                layout.Order.Order().SequenceEqual(Enumerable.Range(0, priceColumns.Length)))
                SetSortColumnsIndex(layout.Order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLog.Write("PriceTable.Restore", ex);
        }
    }

    internal void SaveLayout()
    {
        try
        {
            var widths = priceColumns.Select(column => column.WidthPixel > 0
                ? (int)Math.Round(column.WidthPixel / (Dpi * interfaceScale / 100d))
                : (int)Math.Round(int.Parse(column.Width!, CultureInfo.InvariantCulture)
                    * 100d / interfaceScale)).ToArray();
            Directory.CreateDirectory(SharedArchive.LocalDataFolder);
            string tmp = LayoutPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(
                    new PriceLayout(SortColumnsIndex(), widths,
                        priceColumns.Select(column => column.Visible).ToArray())));
                File.Move(tmp, LayoutPath, overwrite: true);
            }
            finally { File.Delete(tmp); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("PriceTable.Save", ex);
        }
    }
}
