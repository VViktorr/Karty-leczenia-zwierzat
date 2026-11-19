using KartyLeczenia.ReactKL;

static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("ReactKL regression check failed: " + description);
    }

    private static void Main()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "reactkl-ini-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, """
                [Other]
                Role=client
                Server=not-the-archive
                [Archive]
                Role=server
                Server=SQLHOST,51433
                AppVersion=2.2.2
                [AnotherSection]
                Role=client
                """);
            Check(ReactInstallUpdates.ReadValue(path, "Role") == "server",
                "server role must come only from [Archive] section");
            Check(ReactInstallUpdates.ReadValue(path, "Server") == "SQLHOST,51433",
                "archive server address must be preserved");
            Check(ReactInstallUpdates.ReadValue(path, "AppVersion") == "2.2.2",
                "manually assigned version must be preserved");
            Check(ReactInstallUpdates.ReadValue(path, "Missing") is null,
                "unknown key must not be substituted");
            Check(ReactInstallUpdates.ReadValue(path + ".missing", "Role") is null,
                "missing config must be treated as not installed");

            File.WriteAllText(path, """
                [Archive]
                Role=client
                Server=RemoteHost\KLARCHIVE
                AppVersion=2.2.4
                """);
            Check(ReactInstallUpdates.ReadValue(path, "Role") == "client",
                "client role must survive an in-place upgrade");
            Check(ReactInstallUpdates.ReadValue(path, "Server") == @"RemoteHost\KLARCHIVE",
                "named SQL instance must survive an in-place upgrade");

            Check(ReactUpdateService.IsNewer("2.2.3", "2.2.2"),
                "higher manually assigned version offered");
            Check(ReactUpdateService.IsNewer("2.3.0", "2.2.99"),
                "version semantics, not alphabetic comparison");
            Check(!ReactUpdateService.IsNewer("2.2.2.0", "2.2.2"),
                "three/four segment versions must be equivalent");
            Check(!ReactUpdateService.IsNewer("2.2.2", "2.2.3"),
                "downgrade must never be offered");
            Check(!ReactUpdateService.IsNewer("2.2", "2.2.0"),
                "two/three segment versions must be equivalent");
            bool bad = false;
            try { _ = ReactUpdateService.IsNewer("not-a-version", "2.2.2"); }
            catch (InvalidDataException) { bad = true; }
            Check(bad, "malformed published SQL version must be rejected");

            var previous = new KartyLeczenia.TreatmentRow();
            previous.Set("Produkt", "Example product", false);
            previous.Set("Karencja", "Tkanki jadalne: 28 dni", false);
            previous.Set("Automatyczna karencja", "Tkanki jadalne: 28 dni", false);
            previous.Set("Produkt kontekstu", "Example product", false);
            previous.Set("Dokument kontekstu JSON", "previous registry", false);
            previous.Set("SHA256 źródła", "old-pdf", false);
            var choice = new CatalogueVariantModel(
                Id: 12, Species: "Świnie", Route: "im.",
                Dose: "1 ml/10 kg", Tissues: "", Milk: "",
                Indication: "Nowe wskazanie", Source: "Aktualna ChPL");
            var applied = TreatmentVariantApplier.Apply(previous, choice);
            Check(applied.Withdrawal.Length == 0,
                "missing new withdrawal must clear previous species withdrawal");
            Check(previous.Get("Karencja").Length == 0,
                "old withdrawal must never leak into printed treatment row");
            Check(previous.Get("Dokument kontekstu JSON").Length == 0 &&
                  previous.Get("Automatyczna karencja").Length == 0,
                "old registry auto-context must be reset before species change");
            Check(previous.Get("Gatunek") == "Świnie" &&
                  previous.Get("Droga podania") == "im." &&
                  previous.Get("Dawkowanie i okres stosowania") == "1 ml/10 kg",
                "new treatment species route and dose must replace old values");
            Check(previous.Get("Opis zwierząt / numer siedziby stada") == "Świnie",
                "default animal description must follow selected species");
            Check(previous.Get("Wskazanie ChPL") == "Nowe wskazanie",
                "selected ChPL indication must be retained");
            Check(previous.Get("SHA256 źródła").Length == 0,
                "old registry PDF fingerprint must not be attributed to new choice");
            Check(previous.Get("Status karencji").Contains("zweryfikuj",
                  StringComparison.OrdinalIgnoreCase),
                "new treatment variant always requires clinical verification");
            var second = choice with { Id = 13, Tissues = "5 dni", Milk = "36 godzin" };
            var updated = TreatmentVariantApplier.Apply(previous, second);
            Check(updated.Withdrawal.Contains("Tkanki jadalne: 5 dni") &&
                  updated.Withdrawal.Contains("Mleko: 36 godzin"),
                "new animal withdrawal must include tissues and milk");
            Check(previous.Get("Karencja")==updated.Withdrawal,
                "printed withdrawal must match the selected SQL catalogue variant");
            bool incompleteRejected = false;
            try { TreatmentVariantApplier.Apply(previous, choice with { Route = "" }); }
            catch (InvalidOperationException) { incompleteRejected = true; }
            Check(incompleteRejected, "incomplete variant must be rejected");
            var suggestedRow = new KartyLeczenia.TreatmentRow();
            suggestedRow.Set("Symbol Subiekta", "VET-123", false);
            suggestedRow.Set("Produkt", "Przykładowy lek", false);
            var swine = new CatalogueVariantModel(
                Id: 50, Species: "Świnie", Route: "im.", Dose: "1 ml/10 kg",
                Tissues: "5 dni", Source: "ChPL/RPL",
                Pdf: "ChPL/RPL/vet-123.pdf");
            var cattle = swine with { Id = 51, Species = "Bydło", Dose = "2 ml/10 kg" };
            var proposed = TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine]);
            Check(proposed.Status == "ready" && proposed.VariantId == 50,
                "default cattle must not prevent unique swine suggestion");
            Check(suggestedRow.Get("Dawkowanie i okres stosowania") == "" &&
                  suggestedRow.Get("Karencja") == "",
                "suggestion must not modify treatment clinical values");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine,cattle])
                  .Status == "ambiguous",
                "multiple species are ambiguous when cattle is merely a default");
            suggestedRow.Set("Gatunek", "Świnie");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine,cattle])
                  .VariantId == 50,
                "explicitly chosen species must select only compatible variant");
            suggestedRow.Set("Droga podania", "iv.");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine,cattle])
                  .Status == "noMatch",
                "a mismatching route must never be guessed");
            suggestedRow.Set("Droga podania", "im.");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow,
                  [swine, swine with {Id=52, Dose="2 ml/10 kg"}]).Status=="ambiguous",
                "multiple doses for one species and route require manual selection");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine])
                  .Status=="ready",
                "exact matching species and route may offer a single verified variant");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow,
                  [swine with {Pdf=""}]).Status=="review",
                "missing official ChPL PDF must prevent bulk autofill");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow,
                  [swine with {Tissues="",Milk=""}]).Status=="review",
                "missing withdrawal requires manual clinical review");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow,
                  [swine with {Source=""}]).Status=="review",
                "unattributed clinical source must prevent bulk autofill");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow,
                  [swine with {Dose="wg ChPL"}]).Status=="review",
                "placeholder dosing must prevent bulk autofill");
            suggestedRow.Set("Dawkowanie i okres stosowania", "Dawka ręczna");
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine])
                  .Status=="existingData",
                "existing manual dosing must never be overwritten automatically");
            suggestedRow.Set("Dawkowanie i okres stosowania", "");
            suggestedRow.Printed = false;
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine])
                  .Status=="unselected",
                "unselected row must not receive automatic suggestions");
            suggestedRow.Printed = true;
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [])
                  .Status=="notFound",
                "unknown catalogue product must be left untouched");
            suggestedRow.Set("Symbol Subiekta", "", false);
            Check(TreatmentAutofillPlanner.Propose(0, suggestedRow, [swine])
                  .Status=="missingSymbol",
                "missing exact Subiekt symbol must not fall back to fuzzy name match");
            Console.WriteLine($"ReactKL regression checks passed: {checks}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
