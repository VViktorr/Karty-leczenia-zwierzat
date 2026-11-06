using System.ComponentModel;

namespace KartyLeczenia;

public sealed record Card(Dictionary<string, string> Fields, List<Dictionary<string, string>> Rows);

public static class CardSchema
{
    public static readonly string[] Fields = ["Nr karty", "Lecznica i adres", "Lekarz i nr prawa wykonywania zawodu", "Właściciel i adres", "Zgłoszenie — data i godzina", "Wykonanie — data i godzina", "Wyniki badań", "Dokument źródłowy"];
    public static readonly string[] RowFields = ["Opis zwierząt / numer siedziby stada", "Liczba zwierząt", "Rozpoznanie", "Produkt", "Seria", "Ważność", "Ilość wydana", "Dawkowanie i okres stosowania", "Karencja", "Zalecenia / uwagi", "Sekcja (II lub III)"];
    public static readonly string[] ContextFields = ["Gatunek", "Droga podania", "Produkt spożywczy", "Produkt rejestru"];
    public const string RegistryVerified = "RPL — sprawdzono i zatwierdzono";
    public static bool NeedsReview(Dictionary<string, string> row) => string.IsNullOrWhiteSpace(row.GetValueOrDefault("Karencja")) || row.GetValueOrDefault("Status karencji") is not (RegistryVerified or "UPD — sprawdzono i zatwierdzono");
}

public sealed class TreatmentRow : INotifyPropertyChanged
{
    public Dictionary<string, string> Values { get; }
    public TreatmentRow(Dictionary<string, string>? values = null) => Values = values is null
        ? new() { ["Drukuj"] = "True", ["Sekcja (II lub III)"] = "II", ["Gatunek"] = "Bydło", ["Opis zwierząt / numer siedziby stada"] = "Bydło", ["Domyślny gatunek"] = "True" } : new(values);
    public string Get(string key) => Values.GetValueOrDefault(key, "");
    public string Product => Get("Produkt");
    public string Summary => Get("Ilość wydana") + (string.IsNullOrWhiteSpace(Get("Seria")) ? "" : "  •  serie: " + Get("Seria"));
    public string RegistryStatus => Get("Status karencji");
    public bool Printed { get => bool.TryParse(Get("Drukuj"), out bool value) && value; set => Set("Drukuj", value.ToString()); }
    public void Set(string key, string value, bool manual = true)
    {
        if (Get(key) == value) return;
        string previousSpecies = Get("Gatunek");
        if (manual && key == "Gatunek" && Get("Domyślny gatunek") == "True" && Get("Opis zwierząt / numer siedziby stada") == previousSpecies)
            Values["Opis zwierząt / numer siedziby stada"] = value;
        Values[key] = value;
        if (manual && (key is "Gatunek" or "Opis zwierząt / numer siedziby stada")) Values["Domyślny gatunek"] = "False";
        if (manual && (key is "Karencja" or "Produkt" or "Gatunek" or "Droga podania" or "Produkt spożywczy" or "Produkt rejestru" or "Dawkowanie i okres stosowania" or "Opis zwierząt / numer siedziby stada"))
            Values["Status karencji"] = "Zmieniono ręcznie — sprawdź przed wydrukiem";
        if (manual && (key is "Gatunek" or "Droga podania")) RecalculateTreatmentContext();
        PropertyChanged?.Invoke(this, new(nameof(Product)));
        PropertyChanged?.Invoke(this, new(nameof(Summary)));
        PropertyChanged?.Invoke(this, new(nameof(RegistryStatus)));
        PropertyChanged?.Invoke(this, new(nameof(Printed)));
    }
    public void FillMissingContext(string species, string route)
    {
        if (Get("Domyślny gatunek") == "True" && !string.IsNullOrWhiteSpace(species))
        {
            string previous = Get("Gatunek");
            Set("Gatunek", species, false);
            if (string.IsNullOrWhiteSpace(Get("Opis zwierząt / numer siedziby stada")) || Get("Opis zwierząt / numer siedziby stada") == previous)
                Set("Opis zwierząt / numer siedziby stada", species, false);
            Set("Domyślny gatunek", "False", false);
        }
        if (string.IsNullOrWhiteSpace(Get("Gatunek")) && species.Length > 0) Set("Gatunek", species, false);
        if (string.IsNullOrWhiteSpace(Get("Opis zwierząt / numer siedziby stada")) && species.Length > 0) Set("Opis zwierząt / numer siedziby stada", species, false);
        if (string.IsNullOrWhiteSpace(Get("Droga podania")) && route.Length > 0) Set("Droga podania", route, false);
    }
    public void StoreTreatmentSource(RegistryDocument document)
    {
        Set("Dokument kontekstu JSON", System.Text.Json.JsonSerializer.Serialize(document), false);
        Set("Produkt kontekstu", Product, false);
        Set("Pełna sekcja dawkowania źródła", document.DosingSection, false);
        Set("Pełna sekcja gatunków źródła", document.TargetSpeciesSection, false);
    }
    public void RecalculateTreatmentContext()
    {
        if (Get("Produkt kontekstu") != Product || string.IsNullOrWhiteSpace(Get("Dokument kontekstu JSON"))) return;
        RegistryDocument? source;
        try { source = System.Text.Json.JsonSerializer.Deserialize<RegistryDocument>(Get("Dokument kontekstu JSON")); }
        catch (System.Text.Json.JsonException) { return; }
        if (source == null) return;
        var context = TreatmentContextService.Select(source, Get("Gatunek"), Get("Droga podania"));
        if (string.IsNullOrWhiteSpace(Get("Droga podania")) && context.Route.Length > 0) Set("Droga podania", context.Route, false);
        if (string.IsNullOrWhiteSpace(Get("Karencja")) || Get("Karencja") == Get("Automatyczna karencja"))
        {
            Set("Karencja", context.Withdrawal, false); Set("Automatyczna karencja", context.Withdrawal, false);
        }
        if (string.IsNullOrWhiteSpace(Get("Dawkowanie i okres stosowania")) || Get("Dawkowanie i okres stosowania") == Get("Automatyczne dawkowanie"))
        {
            Set("Dawkowanie i okres stosowania", context.Dosing, false); Set("Automatyczne dawkowanie", context.Dosing, false);
        }
        Set("Status karencji", source.Source + " — " + context.Notice + " Wymaga sprawdzenia przed wydrukiem.", false);
    }
    public void ApplyRegistry(RegistrySelection selection)
    {
        StoreTreatmentSource(selection.Document);
        FillMissingContext(selection.Species, selection.Route);
        Set("Karencja", selection.Text, false); if (!string.IsNullOrWhiteSpace(selection.Species)) Set("Gatunek", selection.Species, false);
        if (!string.IsNullOrWhiteSpace(selection.Route)) Set("Droga podania", selection.Route, false);
        if (!string.IsNullOrWhiteSpace(selection.Food)) Set("Produkt spożywczy", selection.Food, false);
        if (!string.IsNullOrWhiteSpace(selection.Dosing)) Set("Dawkowanie i okres stosowania", selection.Dosing, false);
        var context = TreatmentContextService.Select(selection.Document, Get("Gatunek"), Get("Droga podania"));
        Set("Automatyczna karencja", selection.Text == context.Withdrawal ? selection.Text : "", false);
        Set("Automatyczne dawkowanie", selection.Dosing == context.Dosing ? selection.Dosing : "", false);
        Set("Produkt rejestru", selection.Medicine, false);
        Set("Źródło karencji", selection.Document.Url + " | pobrano " + selection.Document.DownloadedUtc.ToString("O") + " | zatwierdzono " + DateTimeOffset.UtcNow.ToString("O"), false);
        Set("SHA256 źródła", selection.Document.Sha256, false); Set("Pełna sekcja karencji źródła", selection.Document.WithdrawalSection, false);
        Set("Status karencji", selection.Document.UpdId == null ? CardSchema.RegistryVerified : "UPD — sprawdzono i zatwierdzono", false);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
