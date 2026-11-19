using System.ComponentModel;
using System.Globalization;

namespace KartyLeczenia.Windows;

/// <summary>
/// AntdUI card table. The established DataGridView remains an off-screen
/// editing/data model during migration, preserving all card serialization,
/// per-medicine choices and legacy card validation. Only the AntdUI table is
/// displayed in the card tab.
/// </summary>
internal sealed class TreatmentTable : AntdUI.Table
{
    internal sealed class DisplayRow
    {
        internal DataGridViewRow Source { get; }
        internal DisplayRow(DataGridViewRow row) => Source = row;
        public string Print { get; set; } = "";
        public string Product { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Batch { get; set; } = "";
        public string Species { get; set; } = "";
        public string Route { get; set; } = "";
        public string Dosing { get; set; } = "";
        public string Withdrawal { get; set; } = "";
        public string Replacement { get; set; } = "";
        public string Animals { get; set; } = "";
        public string Diagnosis { get; set; } = "";
        public string Indication { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private readonly DataGridView source;
    private readonly BindingList<DisplayRow> displayed = [];
    private bool pending;
    private readonly string[] keys =
    [
        "Drukuj", "Produkt", "Ilość wydana", "Seria", "Gatunek",
        "Droga podania", "Dawkowanie i okres stosowania", "Karencja",
        "Zamiennik produktu", "Liczba zwierząt", "Rozpoznanie",
        "Wskazanie z ChPL", "Numery zwierząt"
    ];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    internal Func<DataGridViewRow, string, Task>? EditRequested { get; set; }

    internal TreatmentTable(DataGridView source, int interfaceScale)
    {
        this.source = source;
        Dock = DockStyle.Fill;
        AccessibleName = "Karta leczenia – tabela AntdUI";
        EmptyHeader = true;
        EmptyText = "Wybierz dokument, aby przygotować kartę leczenia";
        EnableHeaderResizing = true;
        ColumnDragSort = true;
        MultipleRows = false;

        string Scale(int px) => Math.Max(40, (int)Math.Round(px * interfaceScale / 100d))
            .ToString(CultureInfo.InvariantCulture);
        void Add(string key, string header, int width)
            => Columns.Add(new AntdUI.Column(key, header) { Width = Scale(width), Ellipsis = true });
        Add(nameof(DisplayRow.Print), "Druk", 65);
        Add(nameof(DisplayRow.Product), "Produkt", 260);
        Add(nameof(DisplayRow.Quantity), "Ilość wydana", 110);
        Add(nameof(DisplayRow.Batch), "Seria", 125);
        Add(nameof(DisplayRow.Species), "Gatunek", 125);
        Add(nameof(DisplayRow.Route), "Droga podania", 160);
        Add(nameof(DisplayRow.Dosing), "Dawkowanie i okres stosowania", 240);
        Add(nameof(DisplayRow.Withdrawal), "Karencja", 235);
        Add(nameof(DisplayRow.Replacement), "Zamiennik produktu", 170);
        Add(nameof(DisplayRow.Animals), "Liczba leczonych zwierząt", 125);
        Add(nameof(DisplayRow.Diagnosis), "Rozpoznanie", 225);
        Add(nameof(DisplayRow.Indication), "Wskazanie z ChPL", 170);
        Add(nameof(DisplayRow.Description), "Opis zwierzęcia", 240);

        DataSource = displayed;
        source.RowsAdded += (_, _) => ScheduleRefresh();
        source.RowsRemoved += (_, _) => ScheduleRefresh();
        source.CellValueChanged += (_, _) => ScheduleRefresh();
        source.Sorted += (_, _) => ScheduleRefresh();
        source.CellEndEdit += (_, _) => ScheduleRefresh();
        source.CellFormatting += (_, _) => { }; // Preserve legacy formatting subscriptions.

        CellClick += (_, e) =>
        {
            if (e.Record is not DisplayRow row) return;
            SelectSource(row.Source, e.ColumnIndex);
            if (e.ColumnIndex == 0)
            {
                row.Source.Cells["Drukuj"].Value =
                    row.Source.Cells["Drukuj"].Value is not true;
                ScheduleRefresh();
            }
        };
        CellDoubleClick += async (_, e) =>
        {
            if (e.Record is not DisplayRow row || e.ColumnIndex < 0 ||
                e.ColumnIndex >= keys.Length || e.ColumnIndex == 0)
                return;
            SelectSource(row.Source, e.ColumnIndex);
            if (EditRequested is { } edit)
                await edit(row.Source, keys[e.ColumnIndex]);
        };
        KeyDown += async (_, e) =>
        {
            if (SelectedsReal().OfType<DisplayRow>().FirstOrDefault() is not { } row) return;
            if (e.KeyCode == Keys.Space)
            {
                row.Source.Cells["Drukuj"].Value =
                    row.Source.Cells["Drukuj"].Value is not true;
                ScheduleRefresh();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F4 && EditRequested is { } edit)
            {
                e.SuppressKeyPress = true;
                await edit(row.Source, "Droga podania");
            }
        };
    }

    private void SelectSource(DataGridViewRow row, int col)
    {
        try
        {
            if (row.DataGridView != source) return;
            row.Selected = true;
            source.CurrentCell = row.Cells[Math.Clamp(col, 0, row.Cells.Count - 1)];
        }
        catch (InvalidOperationException)
        {
            // Selection is visual only when the legacy grid is not visible.
        }
    }

    private void ScheduleRefresh()
    {
        if (pending || IsDisposed) return;
        pending = true;
        if (IsHandleCreated)
            BeginInvoke(new Action(RefreshFromSource));
        else
            pending = false; // RefreshFromSource is called when card tab is shown.
    }

    internal void RefreshFromSource()
    {
        pending = false;
        if (IsDisposed || source.IsDisposed) return;
        DisplayRow? selected = SelectedsReal().OfType<DisplayRow>().FirstOrDefault();
        DataGridViewRow? selectedSource = selected?.Source;
        displayed.RaiseListChangedEvents = false;
        try
        {
            displayed.Clear();
            foreach (DataGridViewRow row in source.Rows)
            {
                if (row.IsNewRow) continue;
                string Value(string key)
                {
                    if (!source.Columns.Contains(key)) return "";
                    var cell = row.Cells[key];
                    try { return cell.FormattedValue?.ToString() ?? cell.Value?.ToString() ?? ""; }
                    catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                    { return cell.Value?.ToString() ?? ""; }
                }
                displayed.Add(new DisplayRow(row)
                {
                    Print = row.Cells["Drukuj"].Value is true ? "☑" : "☐",
                    Product = Value("Produkt"),
                    Quantity = Value("Ilość wydana"),
                    Batch = Value("Seria"),
                    Species = Value("Gatunek"),
                    Route = Value("Droga podania"),
                    Dosing = Value("Dawkowanie i okres stosowania"),
                    Withdrawal = Value("Karencja"),
                    Replacement = Value("Zamiennik produktu"),
                    Animals = Value("Liczba zwierząt"),
                    Diagnosis = Value("Rozpoznanie"),
                    Indication = "Pokaż wskazanie z ChPL",
                    Description = Value("Numery zwierząt")
                });
            }
        }
        finally
        {
            displayed.RaiseListChangedEvents = true;
            displayed.ResetBindings();
        }
        if (selectedSource is not null &&
            displayed.FirstOrDefault(row => ReferenceEquals(row.Source, selectedSource)) is { } updated)
            SetSelected(updated);
    }
}
