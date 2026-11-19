using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

internal sealed record TreatmentSuggestion(
    int Index, string Symbol, string Status, string Message,
    int? VariantId = null, string Species = "", string Route = "",
    string Dosing = "", string Withdrawal = "", string Source = "",
    bool HasPdf = false, string PdfPath = "");

/// <summary>
/// Conservative, read-only matching. No clinical value changes until the user
/// explicitly approves a proposed variant. Never guess by product name.
/// </summary>
internal static class TreatmentAutofillPlanner
{
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

    /// <summary>
    /// Return a suggestion and explain why it cannot be applied in bulk.
    /// The default Bydło value is NOT a deliberate species constraint.
    /// </summary>
    internal static TreatmentSuggestion Propose(
        int index, TreatmentRow row, IReadOnlyList<CatalogueVariantModel> variants)
    {
        string symbol = row.Get("Symbol Subiekta").Trim();
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

        string species = row.Get("Domyślny gatunek") == "True"
            ? "" : row.Get("Gatunek").Trim();
        string route = row.Get("Droga podania").Trim();
        var valid = variants.Where(IsComplete).Where(v =>
            (species.Length == 0 || Same(species, v.Species)) &&
            (route.Length == 0 || Same(route, v.Route))).ToArray();

        if (valid.Length == 0)
            return new(index, symbol, "noMatch",
                "Brak kompletnego wariantu zgodnego z wybranym gatunkiem i drogą podania.");
        if (valid.Length > 1)
            return new(index, symbol, "ambiguous",
                $"{valid.Length} pasujące warianty. Wybierz właściwy ręcznie.");

        var candidate = valid[0];
        string withdrawal = string.Join(Environment.NewLine, new[]
        {
            string.IsNullOrWhiteSpace(candidate.Tissues) ? "" :
                "Tkanki jadalne: " + candidate.Tissues.Trim(),
            string.IsNullOrWhiteSpace(candidate.Milk) ? "" :
                "Mleko: " + candidate.Milk.Trim()
        }.Where(s => s.Length > 0));

        bool needsReview = string.IsNullOrWhiteSpace(withdrawal) ||
            string.IsNullOrWhiteSpace(candidate.Source) ||
            string.IsNullOrWhiteSpace(candidate.Pdf) ||
            new[] { candidate.Dose, candidate.Indication, candidate.Tissues,
                candidate.Milk, candidate.Source, candidate.Remarks }
                .Any(HasReviewPlaceholder);
        return new(index, symbol, needsReview ? "review" : "ready",
            needsReview
                ? "Jeden wariant, ale brakuje potwierdzonych danych, karencji lub PDF. Sprawdź ręcznie."
                : "Jednoznaczny wariant z katalogu SQL — wymaga zatwierdzenia przed zapisem.",
            candidate.Id, candidate.Species, candidate.Route, candidate.Dose,
            withdrawal, candidate.Source, candidate.Pdf.Length > 0, candidate.Pdf);
    }
}
