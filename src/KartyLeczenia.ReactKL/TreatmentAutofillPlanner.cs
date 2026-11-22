using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

internal sealed record TreatmentSuggestion(
    int Index, string Symbol, string Status, string Message,
    int? VariantId = null, string Species = "", string Route = "",
    string Dosing = "", string Withdrawal = "", string Source = "",
    bool HasPdf = false, string PdfPath = "", bool Remembered = false, bool RequiresReview = false);

/// <summary>
/// Conservative, read-only matching against the catalogue. Never guess by product name.
/// </summary>
internal static class TreatmentAutofillPlanner
{
    internal static string CatalogueSymbol(TreatmentRow row)
    {
        string reference = row.Get("Symbol zamiennika produktu").Trim();
        return reference.Length > 0 ? reference : row.Get("Symbol Subiekta").Trim();
    }

    private static readonly string[] ReviewWords =
    [
        "do weryfikacji", "weryfikacji", "wg chpl", "zgodnie z ulotką",
        "do ustalenia", "brak danych", "nie zweryfikowano", "niepotwierdzon"
    ];

    /// <summary>
    /// A concurrent SQL edit must never replace the values the user reviewed.
    /// Compare the dose, withdrawal, source and exact PDF reference, not just ID.
    /// </summary>
    internal static bool MatchesReviewed(
        TreatmentSuggestion suggestion, string species, string route,
        string dosing, string withdrawal, string source, string pdfPath,
        bool hasPdf) =>
        Same(species, suggestion.Species) &&
        Same(route, suggestion.Route) &&
        string.Equals(dosing.Trim(), suggestion.Dosing.Trim(), StringComparison.Ordinal) &&
        string.Equals(withdrawal.Trim(), suggestion.Withdrawal.Trim(), StringComparison.Ordinal) &&
        string.Equals(source.Trim(), suggestion.Source.Trim(), StringComparison.Ordinal) &&
        string.Equals(pdfPath.Trim(), suggestion.PdfPath.Trim(), StringComparison.Ordinal) &&
        hasPdf == suggestion.HasPdf;

    private static bool Same(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasReviewPlaceholder(string text) =>
        ReviewWords.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsComplete(CatalogueVariantModel variant) =>
        variant.Id is > 0 &&
        !string.IsNullOrWhiteSpace(variant.Species) &&
        !string.IsNullOrWhiteSpace(variant.Route) &&
        !string.IsNullOrWhiteSpace(variant.Dose);

    internal static IReadOnlyList<CatalogueVariantModel> ForSpeciesGroup(
        IReadOnlyList<CatalogueVariantModel> variants, string group) =>
        group == "all" ? variants :
        variants.Where(variant => TreatmentSpeciesGroup.Matches(group, [variant.Species])).ToArray();

    /// <summary>
    /// Return a suggestion and explain why it cannot be applied in bulk.
    /// The default Bydło value is NOT a deliberate species constraint.
    /// </summary>
    internal static TreatmentSuggestion Propose(
        int index, TreatmentRow row, IReadOnlyList<CatalogueVariantModel> variants,
        string speciesGroup = "all", SavedTreatmentChoice? preference = null)
    {
        string symbol = CatalogueSymbol(row);
        if (!row.Printed)
            return new(index, symbol, "unselected", "Pozycja jest odznaczona na karcie.");
        if (symbol.Length == 0)
            return new(index, symbol, "missingSymbol", "Towar nie ma symbolu Subiekta.");
        if (!string.IsNullOrWhiteSpace(row.Get("Dawkowanie i okres stosowania")) ||
            !string.IsNullOrWhiteSpace(row.Get("Karencja")))
            return new(index, symbol, "existingData",
                "Istniejące dawkowanie lub karencja nie są nadpisywane automatycznie.");
        if (variants.Count == 0)
            return new(index, symbol, "notFound", "Nie znaleziono wariantów w katalogu SQL.");

        variants = ForSpeciesGroup(variants, speciesGroup);
        string species = row.Get("Domyślny gatunek") == "True"
            ? "" : row.Get("Gatunek").Trim();
        string route = row.Get("Droga podania").Trim();
        bool MatchesRow(CatalogueVariantModel v) =>
            (species.Length == 0 || Same(species, v.Species)) &&
            (route.Length == 0 || Same(route, v.Route));
        var remembered = preference is null ? [] : variants.Where(v =>
            Same(preference.Species, v.Species) && Same(preference.Route, v.Route) &&
            (!preference.VariantId.HasValue || preference.VariantId == v.Id))
            .ToArray();
        bool useRemembered = remembered.Length == 1 && IsComplete(remembered[0]) && MatchesRow(remembered[0]);
        if (!useRemembered && speciesGroup != "all" && variants.Count > 1)
            return new(index, symbol, "ambiguous",
                $"{variants.Count} warianty dla wybranego gatunku. Sprawdź pozycję w podsumowaniu.");

        var valid = useRemembered ? remembered : variants.Where(IsComplete).Where(MatchesRow).ToArray();

        if (valid.Length == 0)
            return new(index, symbol, "noMatch",
                "Brak kompletnego wariantu zgodnego z wybranym gatunkiem i drogą podania.");
        if (valid.Length > 1)
            return new(index, symbol, "ambiguous",
                $"{valid.Length} pasujące warianty. Wybierz właściwy ręcznie.");

        var candidate = valid[0];
        string withdrawal = TreatmentVariantApplier.FormatWithdrawal(candidate);

        string target = candidate.Species.Trim().ToLowerInvariant();
        bool poultry = TreatmentSpeciesGroup.CompatibleGroups([candidate.Species]).Contains("poultry");
        bool dairy = new[] {"bydło","krowy","owce","kozy"}
            .Any(name => target.Contains(name, StringComparison.Ordinal));
        bool needsReview = poultry ||
            string.IsNullOrWhiteSpace(candidate.Tissues) ||
            (dairy && string.IsNullOrWhiteSpace(candidate.Milk)) ||
            string.IsNullOrWhiteSpace(withdrawal) ||
            string.IsNullOrWhiteSpace(candidate.Source) ||
            string.IsNullOrWhiteSpace(candidate.Pdf) ||
            new[] { candidate.Dose, candidate.Indication, candidate.Tissues,
                candidate.Milk, candidate.Eggs, candidate.Source, candidate.Remarks }
                .Any(HasReviewPlaceholder);
        return new(index, symbol, needsReview && !(useRemembered && preference?.VariantId is > 0) ? "review" : "ready",
            needsReview
                ? "Jeden wariant, ale brakuje potwierdzonych danych, karencji lub PDF. Sprawdź ręcznie."
                : "Jednoznaczny wariant z katalogu SQL — wymaga zatwierdzenia przed zapisem.",
            candidate.Id, candidate.Species, candidate.Route, candidate.Dose,
            withdrawal, candidate.Source, candidate.Pdf.Length > 0, candidate.Pdf, useRemembered, needsReview);
    }
}
