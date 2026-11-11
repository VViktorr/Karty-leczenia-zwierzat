using System.IO.Compression;
using System.Text.Json;

namespace KartyLeczenia;

internal static class MedicineCatalogue
{
    internal sealed record Variant(string Species, string Indication, string Route, string Dosing,
        string Meat, string Milk, string Restrictions, string Source);
    internal sealed record Entry(string Symbol, string SubiektName, string RegisterName,
        string Source, bool Excluded, Variant[] Variants);

    static readonly Lazy<Task<ILookup<string, Entry>>> Catalogue = new(Load);
    static async Task<ILookup<string, Entry>> Load()
    {
        using var asset = await FileSystem.OpenAppPackageFileAsync("medicine-catalog.json.gz");
        using var stream = new GZipStream(asset, CompressionMode.Decompress);
        var entries = await JsonSerializer.DeserializeAsync<Entry[]>(stream) ?? [];
        return entries.ToLookup(entry => entry.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    internal static async Task<ILookup<string, Entry>> GetAsync() => await Catalogue.Value;

    internal static Entry? Match(ILookup<string, Entry> catalogue, TreatmentRow row)
    {
        var entries = catalogue[row.Get("Symbol Subiekta").Trim()];
        static string NormalizeName(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
        var name = NormalizeName(row.Product);
        var exact = entries.Where(entry => string.Equals(NormalizeName(entry.SubiektName), name,
            StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }

    internal static bool IsCattle(Variant variant) =>
        variant.Species.StartsWith("Bydło", StringComparison.OrdinalIgnoreCase);

    static string NormalizeSpecies(string species) => species.Trim().ToUpperInvariant()
        .Replace("(KROWY W OKRESIE LAKTACJI)", "(KROWY W LAKTACJI)", StringComparison.Ordinal);

    internal static Variant[] DistinctVariants(Entry entry) => entry.Variants
        .DistinctBy(v => (NormalizeSpecies(v.Species), v.Indication, v.Route, v.Dosing, v.Meat, v.Milk, v.Restrictions))
        .ToArray();

    internal static string Withdrawal(Variant variant) => string.Join("\n", new[] {
        variant.Meat.Length > 0 ? "Tkanki jadalne: " + variant.Meat : "",
        variant.Milk.Length > 0 ? "Mleko: " + variant.Milk : ""
    }.Where(value => value.Length > 0));

    internal static void ApplySelected(TreatmentRow row, Entry entry, Variant variant)
    {
        row.Set("Dokument kontekstu JSON", "", false);
        row.Set("Automatyczna karencja", "", false);
        row.Set("Automatyczne dawkowanie", "", false);
        row.Set("Gatunek", variant.Species);
        row.Set("Droga podania", variant.Route, false);
        row.Set("Produkt rejestru", entry.RegisterName, false);
        row.Set("Źródło karencji", variant.Source, false);
        row.Set("Karencja", Withdrawal(variant), false);
        row.Set("Dawkowanie i okres stosowania", variant.Dosing, false);
        row.Set("Status karencji", "Wybrano wariant z katalogu — " +
            string.Join(" · ", new[] { variant.Indication, variant.Restrictions }.Where(value => value.Length > 0)), false);
    }

    internal static bool Apply(TreatmentRow row, Entry? entry)
    {
        if (entry == null)
        {
            row.Set("Status karencji", "Brak dopasowania w katalogu — sprawdź w Rejestrach leków", false);
            return false;
        }
        row.Set("Produkt rejestru", entry.RegisterName, false);
        row.Set("Źródło karencji", entry.Source, false);
        var species = row.Get("Gatunek").Trim();
        var route = row.Get("Droga podania").Trim();
        bool defaultSpecies = row.Get("Domyślny gatunek") == "True";
        var variants = DistinctVariants(entry).Where(v =>
            (defaultSpecies ? IsCattle(v) : species.Length == 0 || NormalizeSpecies(species) == NormalizeSpecies(v.Species)) &&
            (route.Length == 0 || string.Equals(route, v.Route, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (variants.Length != 1)
        {
            row.Set("Status karencji", variants.Length == 0
                ? "Katalog — brak potwierdzonego wariantu dawkowania i karencji"
                : "Katalog — kilka wariantów, sprawdź gatunek, drogę i wskazanie w ChPL", false);
            return false;
        }
        var v = variants[0];
        row.FillMissingContext(v.Species, v.Route);
        var withdrawal = Withdrawal(v);
        if (row.Get("Karencja").Length == 0) row.Set("Karencja", withdrawal, false);
        if (row.Get("Dawkowanie i okres stosowania").Length == 0)
            row.Set("Dawkowanie i okres stosowania", v.Dosing, false);
        row.Set("Źródło karencji", v.Source, false);
        row.Set("Status karencji", "Katalog — sprawdź wskazanie i warunki podania: " +
            string.Join(" · ", new[] { v.Indication, v.Restrictions }.Where(value => value.Length > 0)), false);
        return withdrawal.Length > 0;
    }
}
