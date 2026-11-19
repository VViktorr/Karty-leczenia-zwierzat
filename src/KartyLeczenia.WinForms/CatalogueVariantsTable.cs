using System.ComponentModel;
using System.Globalization;

namespace KartyLeczenia.Windows;

/// <summary>
/// Editable AntdUI table for catalogue variants. The native DataGridView
/// remains an off-screen data model so existing SQL draft semantics, PDF
/// attachment and validation are not altered by the visual migration.
/// </summary>
internal sealed class CatalogueVariantsTable : AntdUI.Table
{
    internal sealed class Item(DataGridViewRow source)
    {
        internal DataGridViewRow Source => source;
        public string Species { get; set; } = "";
        public string Indication { get; set; } = "";
        public string Route { get; set; } = "";
        public string Dose { get; set; } = "";
        public string Tissues { get; set; } = "";
        public string Milk { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string SourceText { get; set; } = "";
        public string Short { get; set; } = "";
        public string Price { get; set; } = "";
        public string Pdf { get; set; } = "";
    }

    readonly DataGridView source;
    readonly List<Item> rows = [];
    static readonly string[] Keys =
    [
        nameof(Item.Species), nameof(Item.Indication), nameof(Item.Route),
        nameof(Item.Dose), nameof(Item.Tissues), nameof(Item.Milk),
        nameof(Item.Remarks), nameof(Item.SourceText), nameof(Item.Short),
        nameof(Item.Price), nameof(Item.Pdf)
    ];
    bool pending;

    internal CatalogueVariantsTable(DataGridView source)
    {
        this.source = source;
        Dock = DockStyle.Fill;
        AccessibleName = "Warianty katalogu leków – edytowalna tabela AntdUI";
        EmptyHeader = true;
        EmptyText = "Brak wariantów produktu";
        EnableHeaderResizing = true;
        ColumnDragSort = true;
        MultipleRows = true;
        EditMode = AntdUI.TEditMode.DoubleClick;
        for (int i = 0; i < Keys.Length; i++)
        {
            var native = source.Columns[i];
            Columns.Add(new AntdUI.Column(Keys[i], native.HeaderText)
            {
                Width = Math.Max(80, native.Width).ToString(CultureInfo.InvariantCulture),
                Ellipsis = true, ReadOnly = i == 10
            });
        }

        source.RowsAdded += (_, _) => Schedule();
        source.RowsRemoved += (_, _) => Schedule();
        source.CellValueChanged += (_, _) => Schedule();
        source.Sorted += (_, _) => Schedule();

        CellClick += (_, e) =>
        {
            if (e.Record is not Item item || item.Source.DataGridView != source) return;
            try { item.Source.Selected = true; }
            catch (InvalidOperationException) { }
        };
        CellBeginEdit += (_, e) => ModelColumn(e.ColumnIndex) is >= 0 and < 10;
        CellEndEdit += (_, e) =>
        {
            if (e.Record is not Item item || item.Source.DataGridView != source)
                return false;
            int col = ModelColumn(e.ColumnIndex);
            if (col < 0 || col >= 10) return false;
            item.Source.Cells[col].Value = e.Value;
            Schedule();
            return true;
        };
        RefreshFromSource();
    }

    int ModelColumn(int visibleIndex)
    {
        var sorted = SortColumnsList();
        if (visibleIndex < 0 || visibleIndex >= sorted.Length) return -1;
        return Array.IndexOf(Keys, sorted[visibleIndex].Key);
    }

    void Schedule()
    {
        if (pending || IsDisposed) return;
        pending = true;
        if (IsHandleCreated) BeginInvoke(new Action(RefreshFromSource));
        else pending = false;
    }

    internal IReadOnlyList<DataGridViewRow> SelectedSources() =>
        SelectedsReal().OfType<Item>()
            .Select(item => item.Source)
            .Where(row => row.DataGridView == source)
            .ToArray();

    internal void RefreshFromSource()
    {
        pending = false;
        if (IsDisposed || source.IsDisposed) return;
        DataGridViewRow[] selected = SelectedSources().ToArray();
        rows.Clear();
        foreach (DataGridViewRow row in source.Rows)
        {
            if (row.IsNewRow) continue;
            string At(int col) => row.Cells[col].Value?.ToString() ?? "";
            rows.Add(new Item(row)
            {
                Species = At(0), Indication = At(1), Route = At(2),
                Dose = At(3), Tissues = At(4), Milk = At(5),
                Remarks = At(6), SourceText = At(7), Short = At(8),
                Price = At(9), Pdf = At(10)
            });
        }
        DataSource = rows.ToArray();
        // Avoid changing selection on unrelated rows after a cell edit.
        var existing = rows.FirstOrDefault(item => selected.Any(row => ReferenceEquals(row, item.Source)));
        if (existing is not null) SetSelected(existing);
    }
}
