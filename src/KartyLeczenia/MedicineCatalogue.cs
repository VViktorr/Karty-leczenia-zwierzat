using Microsoft.Data.Sqlite;

namespace KartyLeczenia;

internal static class MedicineCatalogue
{
    internal sealed record Variant(string Species, string Indication, string Route, string Dosing,
        string Meat, string Milk, string Restrictions, string Source, string PdfPath);
    internal sealed record Entry(string Symbol, string SubiektName, string RegisterName,
        string Source, bool Excluded, Variant[] Variants);

    static readonly Lazy<Task<ILookup<string, Entry>>> Catalogue = new(Load);
    static readonly Lazy<Task<string>> DatabasePath = new(CopyDatabaseAsync);

    static async Task<string> CopyDatabaseAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, "medicine-catalog.sqlite");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var asset = await FileSystem.OpenAppPackageFileAsync("medicine-catalog.sqlite");
            using (var target = File.Create(temporary))
                await asset.CopyToAsync(target);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static async Task<SqliteConnection> OpenAsync()
    {
        var path = await DatabasePath.Value;
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
    }

    static string Text(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? "" : reader.GetString(index);

    static async Task<ILookup<string, Entry>> Load()
    {
        using var db = await OpenAsync();
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT p.symbol,p.name,p.status,p.decision,
                   v.name,v.species,v.indication,v.route,v.dose,v.edible_tissues,
                   v.milk,v.restrictions,v.source,v.pdf_path
              FROM products p LEFT JOIN variants v ON v.symbol=p.symbol
               AND COALESCE(v.scope,'') NOT LIKE '%referencyjne%'
               AND COALESCE(v.scope,'') NOT LIKE '%Archiwalne%'
             ORDER BY p.symbol,v.id
            """;
        using var reader = command.ExecuteReader();
        var entries = new Dictionary<string, (string Name, bool Excluded, string Register, string Source, List<Variant> Variants)>();
        while (reader.Read())
        {
            var symbol = Text(reader, 0);
            if (!entries.TryGetValue(symbol, out var entry))
            {
                var status = Text(reader, 2);
                var excluded = Text(reader, 3) == "NOT_MEDICINE" ||
                    new[] { "To nie jest lek", "Nie lek", "Nie jest lekiem", "Nie jest produktem leczniczym",
                        "Archiwalny", "Odniesienie" }.Any(prefix => status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                entry = (Text(reader, 1), excluded, Text(reader, 4), Text(reader, 12), []);
                entries.Add(symbol, entry);
            }
            if (!entry.Excluded && Text(reader, 5).Length > 0 &&
                Text(reader, 7).Length > 0 && Text(reader, 8).Length > 0)
                entry.Variants.Add(new Variant(Text(reader, 5), Text(reader, 6), Text(reader, 7),
                    Text(reader, 8), Text(reader, 9), Text(reader, 10), Text(reader, 11),
                    Text(reader, 12), Text(reader, 13)));
        }
        return entries.Select(x => new Entry(x.Key, x.Value.Name, x.Value.Register, x.Value.Source,
            x.Value.Excluded, x.Value.Variants.ToArray())).ToLookup(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    internal static async Task OpenPdfAsync(string path)
    {
        var logicalName = path["data/".Length..];
        using var asset = await FileSystem.OpenAppPackageFileAsync(logicalName);
        var folder = Path.Combine(FileSystem.CacheDirectory, "ChPL", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Path.GetFileName(path));
        using (var target = File.Create(file))
            await asset.CopyToAsync(target);
        await Launcher.Default.OpenAsync(new OpenFileRequest("ChPL", new ReadOnlyFile(file)));
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
        NormalizeSpecies(variant.Species).StartsWith("BYDŁO", StringComparison.Ordinal);

    static string NormalizeSpecies(string species)
    {
        string name = System.Text.RegularExpressions.Regex.Replace(species.Trim().ToUpperInvariant(), @"\s+", " ");
        return name switch
        {
            "CIELĘ" or "CIELĘTA" or "CIELĘTA BYDŁA" or "BYDŁO (CIELĘTA)" => "BYDŁO (CIELĘTA)",
            "BYDŁO (KROWY W OKRESIE LAKTACJI)" or "BYDŁO (KROWA W LAKTACJI)" => "BYDŁO (KROWY W LAKTACJI)",
            "BYDŁO (KROWY ZASUSZONE)" or "BYDŁO (KROWA W OKRESIE ZASUSZENIA)"
                or "BYDŁO (KROWY W OKRESIE ZASUSZENIA)" or "BYDŁO (KROWY W ZASUSZENIU)"
                or "BYDŁO – KROWY W ZASUSZENIU" => "BYDŁO (KROWY W ZASUSZENIU)",
            _ => name
        };
    }

    internal static bool SameSpecies(string first, string second) =>
        NormalizeSpecies(first) == NormalizeSpecies(second);

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
            (defaultSpecies ? IsCattle(v) : species.Length == 0 || SameSpecies(species, v.Species)) &&
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
