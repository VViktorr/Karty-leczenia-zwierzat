using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed class Catalogue
{
    internal sealed record Variant(string Species, string Route, string Indication, string Dose,
        string Withdrawal, string Pdf, string Source, string ShortWithdrawal, string PriceCompactWithdrawal)
    {
        public override string ToString() => $"{Species} · {Route} · {Indication}";
    }

    readonly Dictionary<string, (string Name, List<Variant> Variants)> products = new(StringComparer.OrdinalIgnoreCase);
    static readonly string[] NonMedicineStatusPrefixes =
        ["To nie jest lek", "To nie lek", "Nie lek", "Nie jest lekiem", "Nie jest produktem leczniczym"];

    readonly HashSet<string> nonMedicines = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> incompleteProducts = new(StringComparer.OrdinalIgnoreCase);

    internal bool NeedsCompletion(string symbol) => !IsNonMedicine(symbol) &&
        (incompleteProducts.Contains(symbol.Trim()) || ForProduct(symbol).Count == 0);

    internal bool IsNonMedicine(string symbol) => nonMedicines.Contains(symbol.Trim());

    internal static Catalogue Read(SqlConnection db)
    {
        var result = new Catalogue();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT p.symbol,p.name,v.species,v.route,v.indication,v.dose,
                   v.edible_tissues,v.milk,v.pdf_path,v.source,v.withdrawal_short,price.compact_text
            FROM dbo.catalog_products p JOIN dbo.catalog_variants v ON v.symbol=p.symbol
            LEFT JOIN dbo.catalog_price_withdrawal price ON price.id=v.id
            WHERE COALESCE(p.decision,'') <> 'NOT_MEDICINE'
              AND COALESCE(v.scope,'') NOT LIKE '%referencyjne%'
              AND COALESCE(v.scope,'') NOT LIKE '%archiwalne%'
            ORDER BY p.symbol,v.id
            """;
        using (var classification = db.CreateCommand())
        {
            classification.CommandText = """
                SELECT symbol, COALESCE(decision, ''), COALESCE(status, '')
                FROM dbo.catalog_products
                """;
            using var entries = classification.ExecuteReader();
            while (entries.Read())
            {
                if (entries.IsDBNull(0)) continue;
                var decision = entries.GetString(1).Trim();
                var status = entries.GetString(2).TrimStart();
                if (decision.Equals("NOT_MEDICINE", StringComparison.OrdinalIgnoreCase) ||
                    NonMedicineStatusPrefixes.Any(prefix => status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    result.nonMedicines.Add(entries.GetString(0).Trim());
            }
        }
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
            var symbol = Text(0);
            string species = Text(2), route = Text(3), indication = Text(4), dose = Text(5);
            if (string.IsNullOrWhiteSpace(species) || string.IsNullOrWhiteSpace(indication) ||
                string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(dose) || reader.IsDBNull(11))
                result.incompleteProducts.Add(symbol.Trim());
            if (string.IsNullOrWhiteSpace(route) || string.IsNullOrWhiteSpace(dose) || reader.IsDBNull(11)) continue;
            if (!result.products.TryGetValue(symbol, out var entry))
            {
                entry = (Text(1), new List<Variant>());
                result.products.Add(symbol, entry);
            }
            var withdrawal = string.Join(", ", new[] { Text(6).Length == 0 ? "" : "Tkanki: " + Text(6),
                Text(7).Length == 0 ? "" : "Mleko: " + Text(7) }.Where(value => value.Length > 0));
            entry.Variants.Add(new(species, route, indication, dose, withdrawal, Text(8), Text(9), Text(10), Text(11)));
        }
        return result;
    }

    internal IReadOnlyList<Variant> ForProduct(string symbol) =>
        products.TryGetValue(symbol.Trim(), out var entry) ? entry.Variants : Array.Empty<Variant>();

    internal (string Symbol, string Name)[] ReferenceProducts() => products
        .Select(item => (Symbol: item.Key, Name: item.Value.Name))
        .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase).ToArray();

    internal string Summary(string symbol) => string.Join(" | ", ForProduct(symbol)
        .Where(v => v.Withdrawal.Length > 0)
        .Select(v => $"{v.Species} {v.Route} · {v.Withdrawal}").Distinct());

    internal string PriceWithdrawal(string symbol, int format, bool routeCodes)
    {
        string Route(string route) => routeCodes ? AdministrationRouteLegend.Abbreviate(route) : AdministrationRouteLegend.Expand(route);
        var variants = ForProduct(symbol).Where(v => !string.IsNullOrWhiteSpace(v.Withdrawal));
        if (format == 0)
            return string.Join(" | ", variants.Select(v => $"{v.Species} {Route(v.Route)} · {v.Withdrawal}").Distinct());

        var entries = variants.Select(v => (v.Species, Route: Route(v.Route), Text: v.PriceCompactWithdrawal.ReplaceLineEndings(" "))).Where(v => v.Text.Length > 0).Distinct();
        return format == 2
            ? string.Join("; ", entries.GroupBy(v => v.Species).Select(group =>
                $"{group.Key} — {string.Join(" / ", group.Select(v => $"{v.Route}: {v.Text}"))}"))
            : string.Join("; ", entries.Select(v => $"{v.Species} {v.Route}: {v.Text}"));
    }

}
