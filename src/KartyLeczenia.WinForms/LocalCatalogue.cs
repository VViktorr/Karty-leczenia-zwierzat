using Microsoft.Data.Sqlite;

namespace KartyLeczenia.Windows;

internal sealed class LocalCatalogue
{
    internal sealed record Variant(string Species, string Route, string Indication, string Dose,
        string Withdrawal, string Pdf, string Source, string ShortWithdrawal)
    {
        public override string ToString() => $"{Species} · {Route} · {Indication}";
    }

    readonly Dictionary<string, (string Name, List<Variant> Variants)> products = new(StringComparer.OrdinalIgnoreCase);
    static readonly Lazy<LocalCatalogue> Cached = new(Read);
    internal static LocalCatalogue Load() => Cached.Value;

    static LocalCatalogue Read()
    {
        var result = new LocalCatalogue();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(AppContext.BaseDirectory, "medicine-catalog.sqlite"), Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('variants') WHERE name='withdrawal_short'";
        bool hasShortWithdrawal = Convert.ToInt64(command.ExecuteScalar()) > 0;
        string shortColumn = hasShortWithdrawal ? "v.withdrawal_short" : "NULL";
        command.CommandText = $"""
            SELECT p.symbol,p.name,v.species,v.route,v.indication,v.dose,
                   v.edible_tissues,v.milk,v.pdf_path,v.source,{shortColumn}
            FROM products p JOIN variants v ON v.symbol=p.symbol
            WHERE COALESCE(p.decision,'') <> 'NOT_MEDICINE'
              AND COALESCE(v.scope,'') NOT LIKE '%referencyjne%'
              AND COALESCE(v.scope,'') NOT LIKE '%archiwalne%'
              AND COALESCE(v.dose,'') <> '' AND COALESCE(v.route,'') <> ''
            ORDER BY p.symbol,v.id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
            var symbol = Text(0);
            if (!result.products.TryGetValue(symbol, out var entry))
            {
                entry = (Text(1), new List<Variant>());
                result.products.Add(symbol, entry);
            }
            var withdrawal = string.Join(", ", new[] { Text(6).Length == 0 ? "" : "Tkanki: " + Text(6),
                Text(7).Length == 0 ? "" : "Mleko: " + Text(7) }.Where(value => value.Length > 0));
            entry.Variants.Add(new(Text(2), Text(3), Text(4), Text(5), withdrawal, Text(8), Text(9), Text(10)));
        }
        return result;
    }

    internal IReadOnlyList<Variant> ForProduct(string symbol, string name)
    {
        static string Normalize(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        return products.TryGetValue(symbol.Trim(), out var entry) &&
            string.Equals(Normalize(entry.Name), Normalize(name), StringComparison.OrdinalIgnoreCase)
            ? entry.Variants : Array.Empty<Variant>();
    }

    internal string Summary(string symbol, string name) => string.Join(" | ", ForProduct(symbol, name)
        .Where(v => v.Withdrawal.Length > 0)
        .Select(v => $"{v.Species} {v.Route} · {v.Withdrawal}").Distinct());

    internal static void OpenPdf(string path)
    {
        if (!path.StartsWith("data/ChPL/", StringComparison.Ordinal))
            throw new InvalidOperationException("Brak lokalnego PDF ChPL dla wariantu.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ChPL")) + Path.DirectorySeparatorChar;
        var file = Path.GetFullPath(Path.Combine(root, path["data/ChPL/".Length..]));
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            throw new FileNotFoundException("Nie znaleziono lokalnego PDF ChPL.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
    }
}
