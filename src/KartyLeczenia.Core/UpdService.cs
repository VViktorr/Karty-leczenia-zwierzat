using HtmlAgilityPack;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KartyLeczenia;

public sealed record UpdProduct(string Id, string Name)
{
    public override string ToString() => $"{Name} [UPD {Id}]";
}

// Public website adapter, not the authenticated UPD API. Fail closed if the page layout changes.
public static class UpdService
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    const string Root = "https://medicines.health.europa.eu";
    public static string Url(string id) => Root + "/veterinary/en/" + ParseId(id);
    public static string SearchUrl(string name) => Root + "/veterinary/en/search-medicines?keys=" + Uri.EscapeDataString(name) + "&facet_upd_authorised_in%5B%5D=8407";
    public static string ParseId(string input)
    {
        string value = input.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https" || uri.Host != "medicines.health.europa.eu" || !uri.IsDefaultPort)
                throw new InvalidDataException("Wymagany oficjalny link UPD HTTPS.");
            value = Regex.Match(uri.AbsolutePath, @"^/veterinary/(?:en|pl)/(\d{12})/?$").Groups[1].Value;
        }
        if (!Regex.IsMatch(value, @"^\d{12}$")) throw new InvalidDataException("Wpisz 12-cyfrowe ID UPD lub link do produktu.");
        return value;
    }
    static bool Has(HtmlNode n, string cls) => n.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(cls);
    static HtmlNode? Field(HtmlNode n, string cls) => n.Descendants().FirstOrDefault(x => Has(x, cls));
    static string Text(HtmlNode? n) => Regex.Replace(WebUtility.HtmlDecode(n?.InnerText ?? ""), @"\s+", " ").Trim();
    static string Value(HtmlNode? n) => Text(n?.Descendants().FirstOrDefault(x => Has(x, "field__item")));
    static HtmlDocument Html(string html) { var doc = new HtmlDocument(); doc.LoadHtml(html); return doc; }
    public static List<UpdProduct> ParseSearch(string html)
    {
        var doc = Html(html);
        return doc.DocumentNode.Descendants("a").Select(n => new { Node = n, Match = Regex.Match(n.GetAttributeValue("href", ""), @"^/veterinary/en/(\d{12})$") })
            .Where(x => x.Match.Success && Text(x.Node).Length > 0)
            .Select(x => new UpdProduct(x.Match.Groups[1].Value, Text(x.Node))).DistinctBy(p => p.Id).ToList();
    }
    static readonly Dictionary<string, string> Polish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cattle"] = "Bydło", ["Sheep"] = "Owce", ["Pigs"] = "Świnie", ["Pig"] = "Świnie", ["Goats"] = "Kozy", ["Horses"] = "Konie", ["Chickens"] = "Kury", ["Turkeys"] = "Indyki",
        ["Meat and offal"] = "Tkanki jadalne", ["Milk"] = "Mleko", ["Eggs"] = "Jaja", ["Honey"] = "Miód",
        ["day"] = "dni", ["days"] = "dni", ["hour"] = "godz.", ["hours"] = "godz.", ["degree-day"] = "stopniodni", ["degree days"] = "stopniodni",
        ["Subcutaneous use"] = "Podanie podskórne", ["Intramuscular use"] = "Podanie domięśniowe", ["Intravenous use"] = "Podanie dożylne", ["Oral use"] = "Podanie doustne", ["Intramammary use"] = "Podanie dowymieniowe"
    };
    public static string Translate(string term) => Polish.GetValueOrDefault(term.Trim(), term.Trim());
    public static RegistryDocument Parse(string id, string html)
    {
        id = ParseId(id); var root = Html(html).DocumentNode;
        var countries = Field(root, "products__extra-field-upd-products-authorised-countries");
        bool poland = countries?.Descendants().Any(n => Has(n, "field__item") && Text(n) == "Poland") == true;
        if (!poland) throw new InvalidDataException("Ten wpis UPD nie potwierdza pozwolenia dla Polski. Wybierz polski produkt.");
        string status = Value(Field(root, "upd-auth-status__field-upd-term-name"));
        if (status != "Valid") throw new InvalidDataException("UPD nie potwierdza aktualnego statusu pozwolenia (Valid).");
        string name = Value(Field(root, "products__extra-field-upd-products-medicine-name"));
        if (name.Length == 0) throw new InvalidDataException("Brak nazwy produktu UPD.");
        var section = Field(root, "products__extra-field-upd-products-species-by-route") ?? throw new InvalidDataException("Brak sekcji karencji UPD. Nie przyjęto zera dni.");
        var periods = section.Descendants().Where(n => Has(n, "paragraph--type--upd-withdrawal-period")).ToList();
        if (periods.Count == 0) throw new InvalidDataException("Nie rozpoznano karencji UPD — sprawdź dokument źródłowy.");
        var lines = new List<string>();
        foreach (var period in periods)
        {
            var species = period.Ancestors("li").FirstOrDefault(n => Has(n, "list-item__upd_species")) ?? throw new InvalidDataException("Brak gatunku w karencji UPD.");
            string animal = Value(Field(species, "upd-species__field-upd-term-name"));
            var routeItem = species.Ancestors("li").FirstOrDefault() ?? throw new InvalidDataException("Brak drogi podania UPD.");
            string route = Text(routeItem.ChildNodes.FirstOrDefault(n => Has(n, "upd-items-list-title")));
            string tissue = Value(Field(period, "upd-tissue__field-upd-term-name"));
            string number = Value(Field(period, "upd-withdrawal-period__field-upd-withdrawal-value"));
            string unit = Value(Field(period, "upd-units__field-upd-term-name"));
            if (new[] { animal, route, tissue }.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Niepełny kontekst karencji UPD.");
            // Keep the complete source item, including restrictions/notes that are not numeric periods.
            string original = Text(period);
            string rendered = Regex.IsMatch(number, @"^\d+(?:[.,]\d+)?$") && unit.Length > 0
                ? $"{Translate(animal)}; {Translate(route)}; {Translate(tissue)}: {number} {Translate(unit)}."
                : $"{Translate(animal)}; {Translate(route)}; {Translate(tissue)}: {original}";
            lines.Add(rendered + $" [Oryginał UPD: {animal}; {route}; {original}]");
        }
        string form = Text(Field(root, "products__extra-field-upd-products-pharmaceutical-form"));
        return new RegistryDocument(0, Url(id), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))), DateTimeOffset.UtcNow, string.Join("\n", lines), name + " | " + form + " | Polska; status: Valid") { UpdId = id };
    }
    static async Task<string> Download(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Host != "medicines.health.europa.eu") throw new InvalidDataException("Przekierowanie poza UPD.");
        return await Read(response, timeout.Token);
    }
    static async Task<string> Read(HttpResponseMessage response, CancellationToken token)
    {
        if (response.RequestMessage?.RequestUri?.Host != "medicines.health.europa.eu") throw new InvalidDataException("Przekierowanie poza UPD.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); byte[] bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, token)) > 0) { if (buffer.Length + count > 8_000_000) throw new InvalidDataException("Zbyt duża strona UPD."); buffer.Write(bytes, 0, count); }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public static async Task<List<UpdProduct>> Search(string name, CancellationToken token)
    {
        string url = Root + "/veterinary/en/search-medicines";
        // The current portal uses a Drupal POST form; GET query strings can silently return unrelated results.
        var form = Html(await Download(url, token)).DocumentNode.Descendants("form").FirstOrDefault(n => n.GetAttributeValue("id", "") == "oe-whitelabel-search-form")
            ?? throw new InvalidDataException("Nie rozpoznano formularza wyszukiwania UPD. Wklej link produktu.");
        var values = form.Descendants("input").Where(n => n.GetAttributeValue("type", "") == "hidden")
            .ToDictionary(n => n.GetAttributeValue("name", ""), n => n.GetAttributeValue("value", ""));
        values["search_input"] = name.Trim(); values["conjunction"] = "AND"; values["op"] = "Search";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Client.PostAsync(url, new FormUrlEncodedContent(values), timeout.Token);
        response.EnsureSuccessStatusCode();
        string result = await Read(response, timeout.Token);
        string firstWord = Regex.Match(name, @"[\p{L}\p{N}]+").Value;
        if (firstWord.Length < 2) return [];
        // Never present the portal's unfiltered default first page as matching search results.
        return ParseSearch(result).Where(p => p.Name.Contains(firstWord, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    public static async Task<RegistryDocument> Fetch(string id, CancellationToken token) => Parse(id, await Download(Url(id), token));
    static string MappingPath(string key) => RegistryService.MappingPath("UPD|" + key);
    public static string? ReadMapping(string key) { string path = MappingPath(key); return File.Exists(path) ? ParseId(File.ReadAllText(path)) : null; }
    public static void SaveMapping(string key, string id) { Directory.CreateDirectory(RegistryService.Folder); File.WriteAllText(MappingPath(key), ParseId(id)); }
}
