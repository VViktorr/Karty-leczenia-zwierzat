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
        ? new() { ["Drukuj"] = "True", ["Sekcja (II lub III)"] = "II" } : new(values);
    public string Get(string key) => Values.GetValueOrDefault(key, "");
    public string Product => Get("Produkt");
    public string Summary => Get("Ilość wydana") + "  •  seria: " + (Get("Seria") == "" ? "brak" : Get("Seria"));
    public string RegistryStatus => Get("Status karencji");
    public bool Printed { get => bool.TryParse(Get("Drukuj"), out bool value) && value; set => Set("Drukuj", value.ToString()); }
    public void Set(string key, string value, bool manual = true)
    {
        if (Get(key) == value) return;
        Values[key] = value;
        if (manual && (key is "Karencja" or "Produkt" or "Gatunek" or "Droga podania" or "Produkt spożywczy" or "Produkt rejestru" or "Dawkowanie i okres stosowania" or "Opis zwierząt / numer siedziby stada"))
            Values["Status karencji"] = "Zmieniono ręcznie — sprawdź przed wydrukiem";
        PropertyChanged?.Invoke(this, new(nameof(Product)));
        PropertyChanged?.Invoke(this, new(nameof(Summary)));
        PropertyChanged?.Invoke(this, new(nameof(RegistryStatus)));
        PropertyChanged?.Invoke(this, new(nameof(Printed)));
    }
    public void ApplyRegistry(RegistrySelection selection)
    {
        Set("Karencja", selection.Text, false); Set("Gatunek", selection.Species, false);
        Set("Droga podania", selection.Route, false); Set("Produkt spożywczy", selection.Food, false);
        Set("Produkt rejestru", selection.Medicine, false);
        Set("Źródło karencji", selection.Document.Url + " | pobrano " + selection.Document.DownloadedUtc.ToString("O") + " | zatwierdzono " + DateTimeOffset.UtcNow.ToString("O"), false);
        Set("SHA256 źródła", selection.Document.Sha256, false); Set("Pełna sekcja karencji źródła", selection.Document.WithdrawalSection, false);
        Set("Status karencji", selection.Document.UpdId == null ? CardSchema.RegistryVerified : "UPD — sprawdzono i zatwierdzono", false);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
