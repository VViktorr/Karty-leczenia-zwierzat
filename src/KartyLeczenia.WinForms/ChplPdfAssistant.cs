using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace KartyLeczenia.Windows;

/// <summary>
/// Conservative, OFFLINE ChPL extraction. This is an editable proposal, not
/// an authoritative interpretation of veterinary dosage or withdrawal times.
/// Never invent values or write directly to SQL.
/// </summary>
internal static class ChplPdfAssistant
{
    internal sealed record Proposal(
        IReadOnlyList<CatalogueVariantEdit> Variants,
        string Evidence,
        IReadOnlyList<string> Warnings);

    private static readonly (string Name, string Pattern)[] Species =
    [
        ("Bydło", @"\b(bydł[aoeu]|bydła|bydło|krowy|krów|cielęta|cieląt)\b"),
        ("Świnie", @"\b(świnie|świń|świnia|prosięta|prosiąt|lochy|loch)\b"),
        ("Owce", @"\b(owce|owiec|owca|jagnięta|jagniąt)\b"),
        ("Kozy", @"\b(kozy|kóz|koza|koźlęta|koźląt)\b"),
        ("Konie", @"\b(konie|koni|koń|klacze|klaczy|źrebięta|źrebiąt)\b"),
        ("Psy", @"\b(psy|psów|pies|psa|szczenięta|szczeniąt)\b"),
        ("Koty", @"\b(koty|kotów|kot|kota|kocięta|kociąt)\b"),
        ("Kury", @"\b(kury|kur|kurczęta|kurcząt)\b"),
        ("Indyki", @"\b(indyki|indyków|indyk)\b")
    ];

    private static readonly (string Route, string Pattern)[] Routes =
    [
        ("dożylna", @"\b(dożyln\w*|intravenous|i\.v\.)\b"),
        ("domięśniowa", @"\b(domięśniow\w*|intramuscular|i\.m\.)\b"),
        ("podskórna", @"\b(podskórn\w*|subcutaneous|s\.c\.)\b"),
        ("doustna", @"\b(doustn\w*|oralnie|per os)\b"),
        ("dosutkowa", @"\b(dosutkow\w*|intramammary)\b"),
        ("na skórę", @"\b(naskórn\w*|na skórę|spot.on|pour.on)\b")
    ];

    private static bool Has(string text, string pattern) =>
        Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Clean(string value) => Regex.Replace(
        value.Replace('\u00a0', ' ').Trim(), @"[ \t]+", " ");

    /// <summary>Only the two recognized official Polish veterinary ChPL layouts.</summary>
    private static string ExtractSection(string text, params string[] sectionNumbers)
    {
        foreach (string number in sectionNumbers)
        {
            // Headings must begin on their own line; avoid matching section numbers
            // embedded in a dosage, date, registration number or bibliography.
            var match = Regex.Match(text, @"(?im)^\s*" + Regex.Escape(number) +
                @"[.\s]+[^\r\n]{0,160}\r?\n");
            if (!match.Success) continue;

            int start = match.Index + match.Length;
            var next = Regex.Match(text[start..], @"(?m)^\s*[34]\.\d{1,2}\.?\s+\S");
            int length = next.Success ? next.Index : text.Length - start;
            return Clean(text.Substring(start, Math.Min(length, 20000)));
        }
        return "";
    }

    private static string ForSpecies(string text, string species, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (count == 1) return text;

        // Multi-species documents: associate data only when a species has an
        // explicit own heading/label. Do not apply whole-page doses to all.
        string label = species switch
        {
            "Bydło" => "Bydło|Cielęta|Krowy",
            "Świnie" => "Świnie|Prosięta|Lochy",
            "Owce" => "Owce|Jagnięta",
            "Kozy" => "Kozy|Koźlęta",
            "Konie" => "Konie|Klacze",
            "Psy" => "Psy",
            "Koty" => "Koty",
            "Kury" => "Kury|Kurczęta",
            "Indyki" => "Indyki",
            _ => Regex.Escape(species)
        };
        var match = Regex.Match(text, @"(?im)^\s*(?:" + label + @")\s*[:\-–]\s*");
        if (!match.Success) return "";
        var after = text[(match.Index + match.Length)..];
        var nextLabel = Regex.Match(after, @"(?im)^\s*(?:Bydło|Cielęta|Krowy|Świnie|Prosięta|Lochy|Owce|Jagnięta|Kozy|Koźlęta|Konie|Klacze|Psy|Koty|Kury|Kurczęta|Indyki)\s*[:\-–]\s*");
        return Clean(nextLabel.Success ? after[..nextLabel.Index] : after);
    }

    private static string ExtractWithdrawal(string text, bool milk)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Do not mistake other species' withdrawal times for this species.
        string pattern = milk
            ? @"(?im)^\s*Mleko\s*[:\-–]\s*([^\r\n]+)"
            : @"(?im)^\s*(?:Tkanki jadalne|Tkanki|Mięso i podroby|Mięso)\s*[:\-–]\s*([^\r\n]+)";
        var found = Regex.Match(text, pattern);
        return found.Success ? Clean(found.Groups[1].Value) : "";
    }

    internal static Proposal Read(string pdfFile)
    {
        var evidence = new StringBuilder();
        var warnings = new List<string>();
        using var pdf = PdfDocument.Open(pdfFile);
        if (pdf.NumberOfPages > 300)
            throw new InvalidDataException("Dokument ma zbyt wiele stron do bezpiecznej analizy.");
        for (int i = 1; i <= pdf.NumberOfPages; i++)
        {
            evidence.AppendLine(ContentOrderTextExtractor.GetText(pdf.GetPage(i)));
            evidence.AppendLine();
        }

        var text = evidence.ToString();
        if (text.Length < 250)
            throw new InvalidDataException(
                "PDF nie zawiera odczytywalnej warstwy tekstowej. Potrzebna jest tekstowa ChPL lub OCR skanu.");

        var target = ExtractSection(text, "3.1", "4.1");
        var indications = ExtractSection(text, "3.2", "4.2");
        var dose = ExtractSection(text, "3.9", "4.9");
        var withdrawal = ExtractSection(text, "3.12", "4.11");

        if (string.IsNullOrWhiteSpace(target))
            warnings.Add("Nie rozpoznano sekcji gatunków docelowych (3.1/4.1). Nie utworzono wariantów.");
        if (string.IsNullOrWhiteSpace(dose))
            warnings.Add("Nie znaleziono sekcji dawkowania (3.9/4.9). Dawki pozostają puste.");
        if (string.IsNullOrWhiteSpace(indications))
            warnings.Add("Nie znaleziono sekcji wskazań (3.2/4.2).");
        if (string.IsNullOrWhiteSpace(withdrawal))
            warnings.Add("Nie znaleziono sekcji karencji (3.12/4.11).");

        var species = Species.Where(x => Has(target, x.Pattern)).Select(x => x.Name).Distinct().ToArray();
        if (species.Length == 0 && !string.IsNullOrWhiteSpace(target))
            warnings.Add("Gatunki docelowe mają nieznany zapis. Uzupełnij je ręcznie.");
        if (species.Length > 1)
            warnings.Add("ChPL obejmuje kilka gatunków. Uzupełniane są tylko sekcje jednoznacznie przypisane do gatunku.");

        var result = new List<CatalogueVariantEdit>();
        foreach (var animal in species)
        {
            string doseForAnimal = ForSpecies(dose, animal, species.Length);
            string indicationForAnimal = ForSpecies(indications, animal, species.Length);
            string withdrawalForAnimal = ForSpecies(withdrawal, animal, species.Length);
            var routes = Routes.Where(r => Has(doseForAnimal, r.Pattern)).Select(r => r.Route).ToArray();

            if (routes.Length == 0)
            {
                warnings.Add($"{animal}: nie rozpoznano drogi podania. Wymagana ręczna weryfikacja.");
                routes = [""];
            }
            if (string.IsNullOrWhiteSpace(doseForAnimal))
                warnings.Add($"{animal}: brak jednoznacznego dawkowania dla tego gatunku.");
            foreach (string route in routes)
                result.Add(new CatalogueVariantEdit(
                    Species: animal,
                    Indication: indicationForAnimal,
                    Route: route,
                    Dose: doseForAnimal,
                    Tissues: ExtractWithdrawal(withdrawalForAnimal, false),
                    Milk: ExtractWithdrawal(withdrawalForAnimal, true),
                    Source: "Propozycja automatyczna z ChPL – wymaga weryfikacji"));
        }

        // Display full evidence to the operator, including extraction failures.
        var report = $"GATUNKI DOCELOWE\r\n{target}\r\n\r\nWSKAZANIA\r\n{indications}\r\n\r\nDAWKOWANIE\r\n{dose}\r\n\r\nKARENCJA\r\n{withdrawal}";
        return new Proposal(result, report, warnings);
    }
}
