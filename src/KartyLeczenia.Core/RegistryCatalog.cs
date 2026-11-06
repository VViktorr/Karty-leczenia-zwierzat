using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KartyLeczenia;

public sealed record RegistryProduct(int Id, string Name, string Strength, string Form)
{
    public override string ToString() => $"{Name} {Strength} — {Form} [RPL {Id}]";
}
public sealed record RegistryCatalogData(DateTimeOffset DownloadedUtc, List<RegistryProduct> Products);

public static class RegistryCatalog
{
    public const string ExportUrl = "https://rejestry.ezdrowie.gov.pl/api/rpl/medicinal-products/public-pl-report/6.0.0/overall.xml";
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    // Only product identities are supplied; withdrawal text is always fetched from the actual RPL PDF.
    static readonly RegistryProduct[] Known = [new(32709, "Flordofen", "300 mg/ml", "Roztwór do wstrzykiwań"), new(50492, "Flunivis", "50 mg/ml", "Roztwór do wstrzykiwań"), new(45232, "Bravoxin", "", "Zawiesina do wstrzykiwań"), new(42147, "Tilkomay", "300 mg/ml + 90 mg/ml", "Roztwór do wstrzykiwań")];
    static string CachePath => Path.Combine(RegistryService.Folder, "catalog.json");
    static RegistryCatalogData? cached;
    static bool attempted;
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static string Compact(string text) => new(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)).Select(char.ToUpperInvariant).ToArray());
    static string Words(string text) => Regex.Replace(text.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    public static bool Matches(RegistryProduct product, string text, bool requireStrength = true)
    {
        string name = Words(product.Name); string value = Words(text);
        bool identity = value == name || value.StartsWith(name + " ", StringComparison.Ordinal);
        return identity && (!requireStrength || string.IsNullOrWhiteSpace(product.Strength) || Compact(text).Contains(Compact(product.Strength), StringComparison.Ordinal));
    }
    public static bool MatchesDocument(RegistryProduct product, RegistryDocument document)
    {
        string identity = Words(document.ProductIdentity);
        string name = Words(product.Name);
        return (" " + identity + " ").Contains(" " + name + " ", StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(product.Strength) || Compact(document.ProductIdentity).Contains(Compact(product.Strength), StringComparison.Ordinal));
    }
    public static List<RegistryProduct> Search(string name)
    {
        if (cached == null && File.Exists(CachePath))
        {
            try { cached = JsonSerializer.Deserialize<RegistryCatalogData>(File.ReadAllText(CachePath)); }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        var products = (cached?.Products ?? []).Select(p => p with { Id = RegistryService.DocumentId(p.Id) })
            .Where(p => p.Id < 100_000_000).Concat(Known).GroupBy(p => p.Id).Select(g => g.Last());
        string text = Words(name);
        if (text.Length < 2) return [];
        return products.Where(p => Matches(p, name, false) || Words(p.Name).StartsWith(text, StringComparison.Ordinal)).OrderBy(p => p.Name).ThenBy(p => p.Strength).ToList();
    }
    public static List<RegistryProduct> ExactCandidates(string name) => Search(name).Where(p => Matches(p, name)).ToList();

    public static int? DocumentIdFromXml(int exportId, string xml)
    {
        var link = Regex.Match(xml, @"https://(?:rejestrymedyczne|rejestry)\.ezdrowie\.gov\.pl/api/rpl/medicinal-products/(?<id>\d+)/characteristic\b");
        if (link.Success && int.TryParse(link.Groups["id"].Value, out int linkedId) && linkedId > 0)
            return RegistryService.DocumentId(linkedId);
        int corrected = RegistryService.DocumentId(exportId);
        // Unresolved extended export IDs are not used to construct PDF URLs.
        return corrected > 0 && corrected < 100_000_000 ? corrected : null;
    }
    public static List<RegistryProduct> Parse(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100_000_000 });
        var products = new List<RegistryProduct>();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "produktLeczniczy") continue;
            string kind = reader.GetAttribute("rodzajPreparatu") ?? "";
            if (!kind.Contains("weteryn", StringComparison.OrdinalIgnoreCase)) continue;
            string name = reader.GetAttribute("nazwaProduktu") ?? "";
            if (!int.TryParse(reader.GetAttribute("id"), out int id) || id <= 0 || string.IsNullOrWhiteSpace(name)) continue;
            string strength = reader.GetAttribute("moc") ?? "", form = reader.GetAttribute("postac") ?? "";
            using var subtree = reader.ReadSubtree();
            int? documentId = DocumentIdFromXml(id, XElement.Load(subtree).ToString());
            if (documentId.HasValue) products.Add(new(documentId.Value, name, strength, form));
        }
        if (products.Count == 0) throw new InvalidDataException("Eksport nie zawiera rozpoznawalnego katalogu leków weterynaryjnych.");
        return products;
    }
    public static async Task UpdateAsync(bool force = false, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); token = timeout.Token;
        await Gate.WaitAsync(token);
        try
        {
            if (attempted && !force) return;
            attempted = true;
            using var response = await Client.GetAsync(ExportUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + count > 80_000_000) throw new InvalidDataException("Eksport RPL przekracza limit rozmiaru.");
                buffer.Write(chunk, 0, count);
            }
            buffer.Position = 0;
            var data = new RegistryCatalogData(DateTimeOffset.UtcNow, await Task.Run(() => Parse(buffer), token));
            Directory.CreateDirectory(RegistryService.Folder);
            string temp = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(data), token); File.Move(temp, CachePath, true); cached = data; }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { Gate.Release(); }
    }
}

public sealed record AutomaticWithdrawal(RegistryDocument Document, bool ConfirmedMapping);

public static class AutomaticWithdrawalService
{
    public static string MappingKey(string scope, TreatmentRow row) => scope + "|" + row.Get("ID towaru") + "|" + row.Product;
    public static async Task<AutomaticWithdrawal?> ResolveAsync(string scope, TreatmentRow row, CancellationToken token)
    {
        string? id = UpdService.ReadMapping(MappingKey(scope, row));
        // Only an explicitly confirmed UPD mapping is reused automatically. A name alone is insufficient.
        if (id == null) return null;
        var document = await UpdService.Fetch(id, token);
        return new(document, true);
    }
    public static void Apply(TreatmentRow row, AutomaticWithdrawal resolved)
    {
        row.Set("Karencja", resolved.Document.WithdrawalSection, false);
        row.Set("Źródło karencji", resolved.Document.Url + " | pobrano " + resolved.Document.DownloadedUtc.ToString("O"), false);
        row.Set("SHA256 źródła", resolved.Document.Sha256, false);
        row.Set("Pełna sekcja karencji źródła", resolved.Document.WithdrawalSection, false);
        row.Set("Status karencji", resolved.Document.Source + (resolved.ConfirmedMapping ? " — uzupełniono automatycznie; sprawdź leczenie" : " — uzupełniono automatycznie; potwierdź dopasowanie leku"), false);
    }
}
