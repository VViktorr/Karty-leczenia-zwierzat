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
    internal static AppliedTreatmentVariant Apply(TreatmentRow row, CatalogueVariantModel choice)
    {
        if (choice.Id is null || string.IsNullOrWhiteSpace(choice.Species) ||
            string.IsNullOrWhiteSpace(choice.Route) || string.IsNullOrWhiteSpace(choice.Dose))
            throw new InvalidOperationException(
                "Wybrany wariant katalogu nie ma gatunku, drogi lub dawkowania.");

        string withdrawal = string.Join(Environment.NewLine, new[]
        {
            string.IsNullOrWhiteSpace(choice.Tissues) ? "" :
                "Tkanki jadalne: " + choice.Tissues.Trim(),
            string.IsNullOrWhiteSpace(choice.Milk) ? "" :
                "Mleko: " + choice.Milk.Trim()
        }.Where(part => part.Length > 0));

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
