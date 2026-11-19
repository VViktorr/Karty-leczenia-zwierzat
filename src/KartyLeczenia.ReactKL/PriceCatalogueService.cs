using System.Data;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record PriceVariant(
    string Species, string Route, string Indication, string Dose,
    string Tissues, string Milk, string CompactText)
{
    internal string Withdrawal => string.Join(", ", new[]
    {
        Tissues.Length == 0 ? "" : "Tkanki: " + Tissues,
        Milk.Length == 0 ? "" : "Mleko: " + Milk
    }.Where(value => value.Length > 0));

    internal bool Incomplete =>
        string.IsNullOrWhiteSpace(Species) ||
        string.IsNullOrWhiteSpace(Route) ||
        string.IsNullOrWhiteSpace(Indication) ||
        string.IsNullOrWhiteSpace(Dose) ||
        string.IsNullOrWhiteSpace(CompactText);
}

internal sealed record PricePresentation(
    string Symbol, string Name, decimal? Gross, string LatestBatch,
    string FullCodes, string FullNames,
    string VariantsCodes, string VariantsNames,
    string SpeciesCodes, string SpeciesNames, bool NeedsCompletion);

/// <summary>
/// All six display formats are calculated in memory. Switching full/compact
/// withdrawal or route labels in React never queries either SQL database.
/// </summary>
internal sealed class PriceCatalogueSnapshot(
    IReadOnlyDictionary<string, List<PriceVariant>> variants)
{
    internal static PriceCatalogueSnapshot FromRows(
        IEnumerable<(string Symbol, PriceVariant Variant)> values)
    {
        var dictionary = new Dictionary<string, List<PriceVariant>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, variant) in values)
        {
            if (!dictionary.TryGetValue(symbol.Trim(), out var group))
            {
                group = [];
                dictionary.Add(symbol.Trim(), group);
            }
            group.Add(variant);
        }
        return new(dictionary);
    }

    internal bool NeedsCompletion(string symbol)
    {
        if(!variants.TryGetValue(symbol.Trim(),out var all) || all.Count==0)
            return true;
        return all.Any(variant=>variant.Incomplete) ||
            !all.Any(variant=>variant.Withdrawal.Length>0);
    }

    internal PricePresentation Format(MedicinePrice price)
    {
        if (!variants.TryGetValue(price.Symbol.Trim(), out var all)) all = [];
        string Compose(int format, bool codes)
        {
            string Route(string route) => codes
                ? AdministrationRouteLegend.Abbreviate(route)
                : AdministrationRouteLegend.Expand(route);

            var withWithdrawal = all.Where(v => v.Withdrawal.Length > 0).ToArray();
            if (format == 0)
                return string.Join(" | ", withWithdrawal.Select(v =>
                    $"{v.Species} {Route(v.Route)} · {v.Withdrawal}").Distinct());

            var compact = withWithdrawal
                .Select(v => (v.Species, Route: Route(v.Route),
                    Text: v.CompactText.ReplaceLineEndings(" ").Trim()))
                .Where(v => v.Text.Length > 0).Distinct().ToArray();
            return format == 2
                ? string.Join("; ", compact.GroupBy(v => v.Species).Select(group =>
                    $"{group.Key} — {string.Join(" / ", group.Select(v => $"{v.Route}: {v.Text}"))}"))
                : string.Join("; ", compact.Select(v => $"{v.Species} {v.Route}: {v.Text}"));
        }

        return new(price.Symbol, price.Name, price.Gross, price.LatestBatch,
            Compose(0, true), Compose(0, false),
            Compose(1, true), Compose(1, false),
            Compose(2, true), Compose(2, false),
            NeedsCompletion(price.Symbol));
    }

    internal PricePresentation[] FormatPrices(IEnumerable<MedicinePrice> items)
        => items.Select(Format).ToArray();
}

/// <summary>
/// Read-only SQL catalogue snapshot. Uses the same active-variant rules and
/// catalog_price_withdrawal as final WinForms; no SQLite and no writes.
/// </summary>
internal sealed class PriceCatalogueService(ConnectionSettingsStore archive)
{
    internal async Task<PriceCatalogueSnapshot> ReadAsync(CancellationToken token)
    {
        var options = archive.Load().Options ??
            throw new InvalidOperationException(
                "Skonfiguruj archiwum SQL KartyLeczenia, aby pobrać karencje do cennika.");
        await using var db = new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia"))
        { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString);
        await db.OpenAsync(token).ConfigureAwait(false);
        using var cmd = new SqlCommand("""
            SELECT p.symbol, v.species, v.route, v.indication, v.dose,
                   v.edible_tissues, v.milk, COALESCE(w.compact_text, N'')
            FROM dbo.catalog_products p
            JOIN dbo.catalog_variants v ON v.symbol = p.symbol
            LEFT JOIN dbo.catalog_price_withdrawal w ON w.id = v.id
            WHERE COALESCE(p.decision,N'') <> N'NOT_MEDICINE'
              AND COALESCE(v.scope,N'') NOT LIKE N'%referencyjne%'
              AND COALESCE(v.scope,N'') NOT LIKE N'%archiwalne%'
            ORDER BY p.symbol,v.id;
            """, db) { CommandTimeout = 25 };
        var rows = new List<(string Symbol, PriceVariant Variant)>();
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            string Value(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
            rows.Add((Value(0), new PriceVariant(Value(1), Value(2), Value(3),
                Value(4), Value(5), Value(6), Value(7))));
        }
        return PriceCatalogueSnapshot.FromRows(rows);
    }
}
