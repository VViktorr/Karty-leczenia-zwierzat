using System.Data;
using System.Text.RegularExpressions;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record PriceVariant(
    string Species, string Route, string Indication, string Dose,
    string Tissues, string Milk, string? CompactText, string Eggs = "", string Restrictions = "",
    string ShortTissues = "", string ShortMilk = "", string ShortEggs = "", string ShortFoodRestriction = "")
{
    internal string Withdrawal => string.Join(", ", new[]
    {
        Tissues.Length == 0 ? "" : "Tkanki: " + Tissues,
        Milk.Length == 0 ? "" : "Mleko: " + Milk
    }.Where(value => value.Length > 0));

    internal string EffectiveTissues => Prefer(ShortTissues, Tissues);
    internal string EffectiveMilk => Prefer(ShortMilk, Milk);
    internal string EffectiveEggs => Prefer(ShortEggs, Eggs);
    internal string FoodRestriction => string.IsNullOrWhiteSpace(ShortFoodRestriction)
        ? Regex.IsMatch(Restrictions, @"spoży|konsump|rzeź|produkcj.{0,30}(?:jaj|mlek|miod)|przeznacz.{0,35}(?:jaj|mlek|miod|tkank)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? Restrictions : ""
        : ShortFoodRestriction;
    internal bool HasShort => new[] { ShortTissues, ShortMilk, ShortEggs, ShortFoodRestriction }
        .Any(value => !string.IsNullOrWhiteSpace(value));

    internal static string Prefer(string shortText, string fullText) =>
        string.IsNullOrWhiteSpace(shortText) ? fullText : shortText;

    internal string ShortWithdrawal => string.Join("; ", new[]
    {
        EffectiveTissues.Length == 0 ? "" : "Tkanki: " + EffectiveTissues,
        IsApplicable(EffectiveMilk) ? "Mleko: " + EffectiveMilk : "",
        IsApplicable(EffectiveEggs) ? "Jaja: " + EffectiveEggs : "",
        FoodRestriction
    }.Where(value => value.Length > 0));

    internal static bool IsApplicable(string text) => !string.IsNullOrWhiteSpace(text) &&
        !text.TrimStart().StartsWith("Nie dotyczy", StringComparison.OrdinalIgnoreCase) &&
        !text.TrimStart().StartsWith("Nie podano", StringComparison.OrdinalIgnoreCase);

    internal bool Incomplete =>
        string.IsNullOrWhiteSpace(Species) ||
        string.IsNullOrWhiteSpace(Route) ||
        string.IsNullOrWhiteSpace(Indication) ||
        string.IsNullOrWhiteSpace(Dose) ||
        CompactText is null;
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
    IReadOnlyDictionary<string, List<PriceVariant>> variants,
    IReadOnlySet<string> nonMedicines)
{
    private static readonly string[] NonMedicineStatusPrefixes =
        ["To nie jest lek", "To nie lek", "Nie lek", "Nie jest lekiem", "Nie jest produktem leczniczym"];

    internal static PriceCatalogueSnapshot FromRows(
        IEnumerable<(string Symbol, PriceVariant Variant)> values,
        IEnumerable<(string Symbol, string Decision, string Status)>? classifications = null)
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
        var nonMedicines = (classifications ?? [])
            .Where(entry => entry.Decision.Trim().Equals("NOT_MEDICINE", StringComparison.OrdinalIgnoreCase)
                || NonMedicineStatusPrefixes.Any(prefix =>
                    entry.Status.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => entry.Symbol.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(dictionary, nonMedicines);
    }

    internal bool NeedsCompletion(string symbol) => CompletionReason(symbol) is not null;

    internal string? CompletionReason(string symbol)
    {
        string key = symbol.Trim();
        if (nonMedicines.Contains(key)) return null;
        if (!variants.TryGetValue(key, out var all) || all.Count == 0)
            return "Brak aktywnych wariantów dla tego symbolu w katalogu SQL.";
        if (all.Any(variant => variant.Incomplete))
            return "Co najmniej jeden wariant nie ma gatunku, drogi podania, wskazania, dawkowania lub wpisu karencji w cenniku.";
        return null;
    }

    internal PricePresentation Format(MedicinePrice price)
    {
        if (nonMedicines.Contains(price.Symbol.Trim()) ||
            !variants.TryGetValue(price.Symbol.Trim(), out var all)) all = [];
        string Compose(int format, bool codes)
        {
            string Route(string route) => codes
                ? AdministrationRouteLegend.Abbreviate(route)
                : AdministrationRouteLegend.Expand(route);

            var withWithdrawal = all.Where(v => v.Withdrawal.Length > 0 &&
                v.CompactText is not null && !string.IsNullOrWhiteSpace(v.Route) &&
                !string.IsNullOrWhiteSpace(v.Dose)).ToArray();
            if (format == 2)
            {
                static string Species(string name) => name.Trim() switch
                {
                    "Bydło (cielęta)" => "Bydło",
                    "Świnie" => "Świnia",
                    "Indyki" => "Indyk",
                    "Kury" => "Kura",
                    _ => name.Trim()
                };
                string Group(string label, Func<PriceVariant, string> period, bool omitMissing)
                {
                    var entries = all.Select(v => (Species: Species(v.Species), Text: period(v).Trim(), v.Route))
                        .Where(v => v.Species.Length > 0 && v.Text.Length > 0 &&
                            (!omitMissing || PriceVariant.IsApplicable(v.Text))).Distinct().ToArray();
                    var withDifferentPeriods = entries.GroupBy(v => v.Species)
                        .Where(group => group.Select(v => v.Text).Distinct().Skip(1).Any())
                        .Select(group => group.Key).ToHashSet();
                    var groups = entries.GroupBy(v => v.Text).Select(group =>
                        string.Join(", ", group.Select(v => withDifferentPeriods.Contains(v.Species)
                            ? v.Species + " (" + Route(v.Route) + ")" : v.Species).Distinct()) +
                        " — " + group.Key).ToArray();
                    return groups.Length == 0 ? "" : label + ": " + string.Join(", ", groups);
                }

                var sections = new[] { Group("Tkanki", v => v.EffectiveTissues, true),
                    Group("Mleko", v => v.EffectiveMilk, true), Group("Jaja", v => v.EffectiveEggs, true) };
                var restrictions = all.Select(v => v.FoodRestriction.Trim())
                    .Where(value => value.Length > 0).Distinct();
                return string.Join("; ", sections.Concat(restrictions).Where(value => value.Length > 0));
            }
            if (format == 0)
                return string.Join(" | ", withWithdrawal.Select(v =>
                    $"{v.Species} {Route(v.Route)} · {v.Withdrawal}").Distinct());

            var compact = all.Where(v => v.HasShort || withWithdrawal.Contains(v))
                .Select(v => (v.Species, Route: Route(v.Route),
                    Text: (v.HasShort ? v.ShortWithdrawal : v.CompactText ?? "").ReplaceLineEndings(" ").Trim()))
                .Where(v => v.Text.Length > 0).Distinct().ToArray();
            return string.Join("; ", compact.Select(v => $"{v.Species} {v.Route}: {v.Text}"));
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
        using var probe = new SqlCommand("SELECT COL_LENGTH(N'dbo.catalog_variants', N'eggs');", db);
        string eggs = await probe.ExecuteScalarAsync(token).ConfigureAwait(false) is DBNull or null ? "NULL" : "v.eggs";
        string shortColumns = CatalogueWithdrawalSql.Columns(CatalogueWithdrawalSql.Exists(db), eggs);
        using var cmd = new SqlCommand($"""
            SELECT p.symbol, v.species, v.route, v.indication, v.dose,
                   v.edible_tissues, v.milk, w.compact_text, {eggs}, v.restrictions, {shortColumns}
            FROM dbo.catalog_products p
            JOIN dbo.catalog_variants v ON v.symbol = p.symbol
            LEFT JOIN dbo.catalog_price_withdrawal w ON w.id = v.id
            WHERE COALESCE(p.decision,N'') <> N'NOT_MEDICINE'
              AND COALESCE(v.scope,N'') NOT LIKE N'%referencyjne%'
              AND COALESCE(v.scope,N'') NOT LIKE N'%archiwalne%'
            ORDER BY p.symbol,v.id;

            SELECT symbol, COALESCE(decision,N''), COALESCE(status,N'')
            FROM dbo.catalog_products;
            """, db) { CommandTimeout = 25 };
        var rows = new List<(string Symbol, PriceVariant Variant)>();
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            string Value(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
            rows.Add((Value(0), new PriceVariant(Value(1), Value(2), Value(3),
                Value(4), Value(5), Value(6), reader.IsDBNull(7) ? null : Value(7), Value(8), Value(9), Value(10), Value(11), Value(12), Value(13))));
        }
        var classifications = new List<(string Symbol, string Decision, string Status)>();
        if (await reader.NextResultAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (!reader.IsDBNull(0))
                    classifications.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }
        return PriceCatalogueSnapshot.FromRows(rows, classifications);
    }
}
