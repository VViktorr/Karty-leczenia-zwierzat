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
    readonly HashSet<string> nonMedicines = new(StringComparer.OrdinalIgnoreCase);
    internal bool IsNonMedicine(string symbol) => nonMedicines.Contains(symbol.Trim());

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
        using var migrated = hasShortWithdrawal ? null : new SqliteConnection("Data Source=:memory:");
        if (migrated is not null)
        {
            migrated.Open();
            db.BackupDatabase(migrated);
            using var migration = migrated.CreateCommand();
            using var script = typeof(LocalCatalogue).Assembly.GetManifestResourceStream("withdrawal-short-v2.sql")
                ?? throw new InvalidOperationException("Brak skryptu skrótów karencji.");
            using var text = new StreamReader(script);
            migration.CommandText = text.ReadToEnd();
            migration.ExecuteNonQuery();
            command.Connection = migrated;
        }
        command.CommandText = """
            SELECT p.symbol,p.name,v.species,v.route,v.indication,v.dose,
                   v.edible_tissues,v.milk,v.pdf_path,v.source,v.withdrawal_short
            FROM products p JOIN variants v ON v.symbol=p.symbol
            WHERE COALESCE(p.decision,'') <> 'NOT_MEDICINE'
              AND COALESCE(v.scope,'') NOT LIKE '%referencyjne%'
              AND COALESCE(v.scope,'') NOT LIKE '%archiwalne%'
              AND COALESCE(v.dose,'') <> '' AND COALESCE(v.route,'') <> ''
            ORDER BY p.symbol,v.id
            """;
        using (var classification = db.CreateCommand())
        {
            classification.CommandText = """
                SELECT symbol FROM products
                WHERE decision = 'NOT_MEDICINE' OR TRIM(status) LIKE 'Nie lek / inne%'
                """;
            using var entries = classification.ExecuteReader();
            while (entries.Read())
                if (!entries.IsDBNull(0)) result.nonMedicines.Add(entries.GetString(0).Trim());
        }
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

    internal string PriceWithdrawal(string symbol, string name, int format, bool routeCodes)
    {
        string Route(string route) => routeCodes ? AdministrationRouteLegend.Abbreviate(route) : AdministrationRouteLegend.Expand(route);
        var variants = ForProduct(symbol, name).Where(v => !string.IsNullOrWhiteSpace(v.Withdrawal));
        if (format == 0)
            return string.Join(" | ", variants.Select(v => $"{v.Species} {Route(v.Route)} · {v.Withdrawal}").Distinct());

        var entries = variants.Select(v => (v.Species, Route: Route(v.Route), Text: CompactWithdrawal(v))).Distinct();
        return format == 2
            ? string.Join("; ", entries.GroupBy(v => v.Species).Select(group =>
                $"{group.Key} — {string.Join(" / ", group.Select(v => $"{v.Route}: {v.Text}"))}"))
            : string.Join("; ", entries.Select(v => $"{v.Species} {v.Route}: {v.Text}"));
    }

    static string CompactWithdrawal(Variant variant)
    {
        if (!string.IsNullOrWhiteSpace(variant.ShortWithdrawal)) return variant.ShortWithdrawal;
        return variant.Withdrawal
            .Replace("Tkanki: ", "tk. ", StringComparison.OrdinalIgnoreCase)
            .Replace("Mleko: ", "ml. ", StringComparison.OrdinalIgnoreCase)
            .ReplaceLineEndings(" ");
    }

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
