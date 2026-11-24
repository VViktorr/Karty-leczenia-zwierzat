using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

internal sealed record AppliedTreatmentVariant(
    string Species, string Route, string Dosing, string Withdrawal,
    string Indication, string Description);

/// <summary>
/// Applies only a deliberately chosen SQL catalogue variant. Clears stale
/// registry-derived values BEFORE changing the species or route, to prevent
/// automatic recalculation from the previous ChPL. Not a clinical validator.
/// </summary>
internal static class TreatmentVariantApplier
{
    internal static string FormatWithdrawal(CatalogueVariantModel choice)
    {
        bool poultry = TreatmentSpeciesGroup.CompatibleGroups([choice.Species]).Contains("poultry");
        return string.Join(Environment.NewLine, new[]
        {
            Line("Tkanki jadalne", choice.Tissues),
            poultry ? "" : Line("Mleko", choice.Milk),
            Line("Jaja", choice.Eggs)
        }.Where(part => part.Length > 0));

        static string Line(string label, string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : label + ": " + value.Trim();
    }

    internal static AppliedTreatmentVariant Apply(TreatmentRow row, CatalogueVariantModel choice)
    {
        if (choice.Id is null || string.IsNullOrWhiteSpace(choice.Species) ||
            string.IsNullOrWhiteSpace(choice.Route) || string.IsNullOrWhiteSpace(choice.Dose))
            throw new InvalidOperationException(
                "Wybrany wariant katalogu nie ma gatunku, drogi lub dawkowania.");

        string withdrawal = FormatWithdrawal(choice);

        // Old registry context belongs to the previously selected variant;
        // keeping it can recalculate an unrelated dosing/withdrawal later.
        foreach (string key in new[]
        {
            "Dokument kontekstu JSON", "Produkt kontekstu",
            "Automatyczna karencja", "Automatyczne dawkowanie",
            "Pełna sekcja karencji źródła", "Pełna sekcja dawkowania źródła",
            "SHA256 źródła"
        })
            row.Set(key, "", false);

        row.Set("Gatunek", choice.Species);
        row.Set("Droga podania", choice.Route);
        row.Set("Dawkowanie i okres stosowania", choice.Dose);
        // An absent withdrawal means UNKNOWN, never the previous species' time.
        row.Set("Karencja", withdrawal);
        var printable = new PriceVariant(choice.Species, choice.Route, choice.Indication, choice.Dose,
            choice.Tissues, choice.Milk, choice.Price, choice.Eggs, choice.Remarks,
            choice.ShortTissues, choice.ShortMilk, choice.ShortEggs, choice.ShortFoodRestriction);
        row.Set("Krótka karencja z katalogu", printable.HasShort ? printable.ShortWithdrawal : "", false);
        row.Set("Pełna karencja odpowiadająca skrótowi", withdrawal, false);
        row.Set("Gatunek odpowiadający skrótowi", choice.Species, false);
        row.Set("Droga odpowiadająca skrótowi", choice.Route, false);
        row.Set("Wskazanie ChPL", choice.Indication);
        row.Set("Źródło karencji", choice.Source);
        row.Set("Status karencji", "Wariant katalogu SQL — zweryfikuj z aktualną ChPL");
        row.Set("Domyślny gatunek", "False");

        return new(
            row.Get("Gatunek"), row.Get("Droga podania"),
            row.Get("Dawkowanie i okres stosowania"),
            row.Get("Karencja"), row.Get("Wskazanie ChPL"),
            row.Get("Opis zwierząt / numer siedziby stada"));
    }
}
