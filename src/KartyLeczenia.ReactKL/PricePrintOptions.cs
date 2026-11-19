using System.Text.Json;
using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// React print settings. The old WinForms file is read only during migration;
/// the React settings are saved to a separate JSON file so legacy profiles
/// and manually configured typography are never destroyed.
/// </summary>
internal sealed record PricePrintOptions
{
    public bool Landscape { get; init; } = true;
    public decimal Margin { get; init; } = 7;
    public decimal RowHeight { get; init; } = 0;
    public decimal Padding { get; init; } = 0.5m;
    public string Font { get; init; } = "Arial";
    public int Size { get; init; } = 10;
    public int FontWeight { get; init; } = 400;
    public bool Number { get; init; } = true;
    public bool Batch { get; init; } = true;
    public bool Price { get; init; } = true;
    public bool Withdrawal { get; init; } = true;
    public int WithdrawalFormat { get; init; } = 1;
    public bool RouteCodes { get; init; } = true;
    public string Legend { get; init; } = "start";
    public int[] Widths { get; init; } = [4, 43, 14, 13, 40];
    public string[] ColumnFonts { get; init; } = ["", "", "", "", "", ""];
    public int[] ColumnSizes { get; init; } = [10, 10, 10, 10, 10, 10];
    public int[] ColumnWeights { get; init; } = [400, 400, 400, 400, 400, 400];
    public string[] ColumnAlignments { get; init; } = ["left", "left", "left", "left", "right", "left"];
    public string[] PageFontNames { get; init; } = ["", "", ""];
    public int[] PageFontSizes { get; init; } = [10, 10, 10];

    internal PricePrintOptions Validate()
    {
        if (Margin is < 4 or > 25 || RowHeight is < 0 or > 18 ||
            Padding is < 0 or > 5 || Size is < 6 or > 48 ||
            FontWeight is < 400 or > 900 ||
            WithdrawalFormat is < 0 or > 2 ||
            Legend is not ("hidden" or "start" or "every") ||
            string.IsNullOrWhiteSpace(Font) || Font.Length > 100 ||
            Font.IndexOfAny([';', '{', '}', '\n', '\r']) >= 0)
            throw new ArgumentException("Nieprawidłowe parametry wydruku cennika.");
        if (Widths is null || Widths.Length != 5 || Widths.Any(x => x is < 1 or > 100) ||
            ColumnFonts is null || ColumnFonts.Length != 6 ||
            ColumnSizes is null || ColumnSizes.Length != 6 ||
            ColumnWeights is null || ColumnWeights.Length != 6 ||
            ColumnAlignments is null || ColumnAlignments.Length != 6 ||
            PageFontNames is null || PageFontNames.Length != 3 ||
            PageFontSizes is null || PageFontSizes.Length != 3 ||
            ColumnSizes.Any(x => x is < 6 or > 48) ||
            PageFontSizes.Any(x => x is < 6 or > 48) ||
            ColumnWeights.Any(x => x is < 400 or > 900) ||
            ColumnAlignments.Any(x => x is not ("left" or "center" or "right")) ||
            ColumnFonts.Concat(PageFontNames).Any(x => x is null || x.Length > 100 ||
                x.IndexOfAny([';', '{', '}', '\n', '\r']) >= 0))
            throw new ArgumentException("Nieprawidłowe formatowanie kolumn cennika.");
        return this;
    }
}

internal static class PricePrintOptionsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia");
    private static string CurrentPath => Path.Combine(DirectoryPath, "price-print-react.json");
    private static string LegacyPath => Path.Combine(DirectoryPath, "price-print-winforms.json");

    internal static PricePrintOptions Load()
    {
        try
        {
            if (File.Exists(CurrentPath))
                return (JsonSerializer.Deserialize<PricePrintOptions>(
                    File.ReadAllText(CurrentPath), Json) ?? new()).Validate();
            if (File.Exists(LegacyPath))
                return FromLegacyJson(File.ReadAllText(LegacyPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            CrashLog.Write("ReactKL.PricePrint.Load", ex);
        }
        return new();
    }

    internal static PricePrintOptions FromLegacyJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static JsonElement Property(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) ? v : default;
        static bool Bool(JsonElement obj, string name, bool fallback)
        {
            var value = Property(obj, name);
            return value.ValueKind == JsonValueKind.True ? true :
                value.ValueKind == JsonValueKind.False ? false : fallback;
        }
        static int Int(JsonElement obj, string name, int fallback)
        {
            var value = Property(obj, name);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : fallback;
        }
        static decimal Decimal(JsonElement obj, string name, decimal fallback)
        {
            var value = Property(obj, name);
            return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n) ? n : fallback;
        }
        static string Str(JsonElement obj, string name, string fallback)
        {
            var value = Property(obj, name);
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
        }
        static int[] Ints(JsonElement obj, string name, int[] fallback)
        {
            var value = Property(obj, name);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != fallback.Length)
                return fallback;
            // Legacy WinForms writes decimal values such as 10.0, not just integers.
            var items = value.EnumerateArray().ToArray();
            var parsed = new int[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].ValueKind != JsonValueKind.Number ||
                    !items[i].TryGetDecimal(out decimal n) || n != decimal.Truncate(n) ||
                    n < int.MinValue || n > int.MaxValue)
                    return fallback;
                parsed[i] = (int)n;
            }
            return parsed;
        }
        static string[] Strings(JsonElement obj, string name, string[] fallback)
        {
            var value = Property(obj, name);
            return value.ValueKind == JsonValueKind.Array &&
                value.GetArrayLength() == fallback.Length
                ? value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String
                    ? x.GetString() ?? "" : "").ToArray() : fallback;
        }

        var defaults = new PricePrintOptions();
        var sizes = Ints(root, "FontSizes", [0,0,0,0,0,0]);
        var legacySizes = Ints(root, "Sizes", [0,0,0,0,0,0]);
        var fontIndices = Ints(root, "Fonts", [0,0,0,0,0,0]);
        var fontNames = Strings(root, "FontNames", defaults.ColumnFonts);
        string[] oldFonts = ["Arial", "Calibri", "Tahoma", "Segoe UI", "Times New Roman"];
        for (int i = 0; i < 6; i++)
        {
            if (string.IsNullOrWhiteSpace(fontNames[i]) && fontIndices[i] is >= 1 and <= 5)
                fontNames[i] = oldFonts[fontIndices[i] - 1];
            if (sizes[i] == 0 && legacySizes[i] is >= 1 and <= 5)
                sizes[i] = 7 + legacySizes[i];
        }
        var weights = Ints(root, "FontWeights", defaults.ColumnWeights);
        var aligns = Ints(root, "Alignments", [0,0,0,0,2,0]);
        return (defaults with
        {
            Landscape = Bool(root, "Landscape", true),
            Margin = Decimal(root, "Margin", 7),
            RowHeight = Decimal(root, "RowHeight", 0),
            Padding = Decimal(root, "Padding", 0.5m),
            Font = Str(root, "Font", "Arial"),
            Size = (int)Decimal(root, "Size", 10),
            FontWeight = Int(root, "FontWeight", 400) is var weight && weight is >= 400 and <= 900 ? weight : 400,
            Number = Bool(root, "Number", true),
            Batch = Bool(root, "Batch", true),
            Price = Bool(root, "Price", true),
            Withdrawal = Bool(root, "Withdrawal", true),
            WithdrawalFormat = Int(root, "WithdrawalFormat", 1),
            RouteCodes = Bool(root, "RouteCodes", true),
            Legend = !Bool(root, "Legend", true) ? "hidden" :
                Bool(root, "LegendEveryPage", false) ? "every" : "start",
            Widths = Ints(root, "Widths", defaults.Widths),
            ColumnFonts = fontNames,
            ColumnSizes = sizes.Select(x => x is >= 6 and <= 48 ? x : 10).ToArray(),
            ColumnWeights = weights.Select(x => x is >= 400 and <= 900 ? x : 400).ToArray(),
            ColumnAlignments = aligns.Select(x => x switch
                {1 => "center", 2 => "right", _ => "left"}).ToArray(),
            PageFontNames = Strings(root, "PageFontNames", defaults.PageFontNames),
            PageFontSizes = Ints(root, "PageFontSizes", defaults.PageFontSizes)
                .Select(x => x is >= 6 and <= 48 ? x : 10).ToArray()
        }).Validate();
    }

    internal static void Save(PricePrintOptions options)
    {
        options.Validate();
        Directory.CreateDirectory(DirectoryPath);
        string temp = CurrentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(options, Json));
            File.Move(temp, CurrentPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
