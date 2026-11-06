using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace KartyLeczenia;

public sealed record RegistryDocument(int Id, string Url, string Sha256, DateTimeOffset DownloadedUtc, string WithdrawalSection, string ProductIdentity)
{
    public string? UpdId { get; init; }
    public string Source => UpdId == null ? "RPL" : "UPD";
}
public sealed record RegistrySelection(RegistryDocument Document, string Species, string Route, string Food, string Text, string Medicine);

public static class RegistryService
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "registry");
    // Verified correction; never infer a generic conversion by removing digits.
    public static int DocumentId(int id) => id == 100421477 ? 42147 : id;
    public static string Url(int id) => $"https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/{DocumentId(id)}/characteristic";
    public static string ExtractSection(string text)
    {
        text = text.Replace('\u00a0', ' ');
        var start = Regex.Match(text, @"(?im)(?:3\s*\.\s*12|4\s*\.\s*11)\s*\.?\s*Okresy?\s+karencji\b");
        if (!start.Success) throw new InvalidDataException("Nie znaleziono jednoznacznie sekcji karencji w PDF. Otwórz dokument i sprawdź go ręcznie. Nie pobrano wartości z Subiekta.");
        string remainder = text[(start.Index + start.Length)..];
        var end = Regex.Match(remainder, @"(?im)^\s*(?:4\s*\.\s*(?:DANE|WŁAŚCIWOŚCI)|5\s*\.\s*(?:DANE|WŁAŚCIWOŚCI))\b");
        if (!end.Success) throw new InvalidDataException("Nie rozpoznano końca sekcji karencji. Wymagana ręczna kontrola PDF.");
        string section = remainder[..end.Index].Trim();
        if (!Regex.IsMatch(section, @"\p{L}{2}") || section.Length > 12000) throw new InvalidDataException("Nietypowa sekcja karencji — sprawdź PDF ręcznie.");
        return section;
    }
    public static async Task<RegistryDocument> Fetch(int id, CancellationToken token = default)
    {
        id = DocumentId(id);
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); token = timeout.Token;
        using var response = await Client.GetAsync(Url(id), HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 12 * 1024 * 1024) throw new InvalidDataException("Zbyt duży dokument.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > 12 * 1024 * 1024) throw new InvalidDataException("Zbyt duży dokument.");
            buffer.Write(chunk, 0, count);
        }
        byte[] bytes = buffer.ToArray();
        if (bytes.Length < 5 || System.Text.Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new InvalidDataException("Rejestr nie zwrócił PDF — możliwa przerwa w działaniu. Nie użyto danych z Subiekta.");
        using var pdf = PdfDocument.Open(bytes);
        string text = string.Join("\n", pdf.GetPages().Select(p => ContentOrderTextExtractor.GetText(p)));
        var result = new RegistryDocument(id, Url(id), Convert.ToHexString(SHA256.HashData(bytes)), DateTimeOffset.UtcNow, ExtractSection(text), text[..Math.Min(text.Length, 750)]);
        Directory.CreateDirectory(Folder);
        // Temporary file + replacement: an interrupted refresh cannot truncate the last good copy.
        string target = Path.Combine(Folder, id + ".json");
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(result), token); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return result;
    }
    public static RegistryDocument? Cached(int id)
    {
        string file = Path.Combine(Folder, id + ".json");
        if (!File.Exists(file)) return null;
        var data = JsonSerializer.Deserialize<RegistryDocument>(File.ReadAllText(file));
        if (data is null || data.Id != id || data.Url != Url(id) || string.IsNullOrWhiteSpace(data.WithdrawalSection)) return null;
        return data;
    }
    public static int ParseProductId(string input)
    {
        if (int.TryParse(input.Trim(), out int id) && id > 0) return DocumentId(id);
        if (Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.Host is "rejestrymedyczne.ezdrowie.gov.pl" or "rejestry.ezdrowie.gov.pl")
        {
            var match = Regex.Match(uri.AbsolutePath, @"^/api/rpl/medicinal-products/(?<id>\d+)/(?:characteristic|leaflet)/?$");
            if (match.Success && int.TryParse(match.Groups["id"].Value, out id) && id > 0) return DocumentId(id);
        }
        throw new InvalidOperationException("Wpisz ID RPL albo wklej oficjalny link do charakterystyki leku.");
    }
    public static string MappingPath(string key) => Path.Combine(Folder, "map-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + ".txt");
    public static int? ReadMapping(string key)
    {
        string path = MappingPath(key);
        return File.Exists(path) && int.TryParse(File.ReadAllText(path), out int id) && id > 0 ? DocumentId(id) : null;
    }
    public static void SaveMapping(string key, int id)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(MappingPath(key), id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

