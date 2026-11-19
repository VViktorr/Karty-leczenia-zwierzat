using System.Globalization;
using System.Text.Json;

namespace KartyLeczenia.Windows;

/// <summary>
/// AntdUI list of Subiekt GT documents. Keeps search/caching in MainForm and
/// stores only visual layout locally, never business data or credentials.
/// </summary>
internal sealed class DocumentsTable : AntdUI.Table
{
    static readonly string LayoutPath = Path.Combine(SharedArchive.LocalDataFolder, "documents-antdui.json");
    readonly int interfaceScale;
    readonly AntdUI.Column[] documentColumns;
    DocumentSearchKinds kind = DocumentSearchKinds.Invoices;

    sealed record DocumentsLayout(int[] Order, int[] Widths, bool[] Visible);

    internal DocumentsTable(int interfaceScale)
    {
        this.interfaceScale = interfaceScale;
        Dock = DockStyle.Fill;
        AccessibleName = "Dokumenty Subiekta – tabela AntdUI";
        EmptyText = "Brak dokumentów dla wybranych filtrów";
        EmptyHeader = true;
        EnableHeaderResizing = true;
        ColumnDragSort = true;
        MultipleRows = false;

        var relatedMarker = NewColumn("DocumentType", "R", 32);
        relatedMarker.Render = (_, record, _) =>
            kind == DocumentSearchKinds.Invoices
            && record is DocumentSuggestion doc
            && doc.Related.Count(link => link.Number.StartsWith("WZ ", StringComparison.OrdinalIgnoreCase)) > 1
                ? "▣" : "";

        documentColumns =
        [
            relatedMarker,
            NewColumn(nameof(DocumentSuggestion.Date), "Data", 100, "dd.MM.yyyy"),
            NewColumn(nameof(DocumentSuggestion.Number), "Numer", 180),
            NewColumn(nameof(DocumentSuggestion.ShortPayer), "Kontrahent", 290),
            NewColumn(nameof(DocumentSuggestion.Gross), "Wartość brutto", 145, "N2"),
            NewColumn(nameof(DocumentSuggestion.RelatedText), "Powiązane dokumenty", 300)
        ];
        documentColumns[4].Align = AntdUI.ColumnAlign.Right;
        foreach (var column in documentColumns) Columns.Add(column);
        RestoreLayout();
        SetKind(kind);
    }

    AntdUI.Column NewColumn(string key, string title, int px, string? format = null) =>
        new(key, title)
        {
            Width = Scale(px).ToString(CultureInfo.InvariantCulture),
            DisplayFormat = format,
            Ellipsis = true,
            SortOrder = key != "DocumentType"
        };

    int Scale(int px) => (int)Math.Round(px * interfaceScale / 100d);

    internal DocumentSuggestion? SelectedDocument() =>
        SelectedsReal().OfType<DocumentSuggestion>().FirstOrDefault();

    internal void SetKind(DocumentSearchKinds value)
    {
        kind = value;
        documentColumns[0].Visible = value == DocumentSearchKinds.Invoices;
        Invalidate();
    }

    internal void ShowColumnsMenu(Control anchor) =>
        AntdTableColumnMenu.Show(anchor, this, documentColumns);

    void RestoreLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath) ||
                JsonSerializer.Deserialize<DocumentsLayout>(File.ReadAllText(LayoutPath)) is not { } layout)
                return;
            if (layout.Widths?.Length == documentColumns.Length)
                for (int i = 0; i < documentColumns.Length; i++)
                    documentColumns[i].Width = Scale(Math.Clamp(layout.Widths[i], 25, 2000))
                        .ToString(CultureInfo.InvariantCulture);

            if (layout.Visible?.Length == documentColumns.Length && layout.Visible.Any(x => x))
                for (int i = 0; i < documentColumns.Length; i++)
                    documentColumns[i].Visible = layout.Visible[i];

            if (layout.Order?.Length == documentColumns.Length &&
                layout.Order.Order().SequenceEqual(Enumerable.Range(0, documentColumns.Length)))
                SetSortColumnsIndex(layout.Order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLog.Write("DocumentsTable.Restore", ex);
        }
    }

    internal void SaveLayout()
    {
        try
        {
            var widths = documentColumns.Select(column => column.WidthPixel > 0
                ? (int)Math.Round(column.WidthPixel / (Dpi * interfaceScale / 100d))
                : (int)Math.Round(int.Parse(column.Width!, CultureInfo.InvariantCulture)
                    * 100d / interfaceScale)).ToArray();
            Directory.CreateDirectory(SharedArchive.LocalDataFolder);
            string tmp = LayoutPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(
                    new DocumentsLayout(SortColumnsIndex(), widths,
                        documentColumns.Select(column => column.Visible).ToArray())));
                File.Move(tmp, LayoutPath, overwrite: true);
            }
            finally { File.Delete(tmp); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("DocumentsTable.Save", ex);
        }
    }
}
