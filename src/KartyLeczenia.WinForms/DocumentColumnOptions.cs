namespace KartyLeczenia.Windows;

internal static class DocumentColumnOptions
{
    internal static string[] Choices(DataGridViewColumn column)
    {
        if (column.DataGridView?.Columns.Contains("DocumentType") != true) return [];
        return column.Name switch
        {
            "Date" => ["dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy"],
            "Payer" => ["Nazwa", "Nazwa pełna"],
            "Gross" => ["Brutto", "Netto"],
            _ => []
        };
    }

    internal static string? Read(DataGridViewColumn column)
    {
        if (Choices(column).Length == 0) return null;
        return column.Name switch
        {
            "Date" => column.DefaultCellStyle.Format.Replace("'", ""),
            "Payer" => column.DataPropertyName == nameof(DocumentSuggestion.ShortPayer) ? "Nazwa" : "Nazwa pełna",
            "Gross" => column.DataPropertyName == nameof(DocumentSuggestion.Net) ? "Netto" : "Brutto",
            _ => null
        };
    }

    internal static void Apply(DataGridViewColumn column, string? value)
    {
        if (value is null || !Choices(column).Contains(value)) return;
        switch (column.Name)
        {
            case "Date":
                column.DefaultCellStyle.Format = value.Replace("/", "'/'");
                break;
            case "Payer":
                column.DataPropertyName = value == "Nazwa" ? nameof(DocumentSuggestion.ShortPayer) : nameof(DocumentSuggestion.Payer);
                break;
            case "Gross":
                column.DataPropertyName = value == "Netto" ? nameof(DocumentSuggestion.Net) : nameof(DocumentSuggestion.Gross);
                column.HeaderText = value == "Netto" ? "Wartość netto" : "Wartość brutto";
                break;
        }
    }
}
