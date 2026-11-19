using KartyLeczenia;
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

            var (year, treatmentDate) = CardArchiveService.GetCardDate(
                new DateTime(2027, 1, 1, 0, 15, 0));
            Check(year == 2027 && treatmentDate == new DateTime(2027, 1, 1),
                "card number year and archive treatment date must follow performance across New Year");
            var (otherYear, otherDate) = CardArchiveService.GetCardDate(
                new DateTime(2026, 12, 31, 23, 59, 0));
            Check(otherYear == 2026 && otherDate == new DateTime(2026, 12, 31),
                "card year must not follow an earlier or optional reporting date");

            var localClinic = new KartyLeczenia.ClinicSettings(
                "Przychodnia", "Warszawa", "lek. wet. Test")
            { Phone = "123456789" };
            Check(!SharedClinicService.HasDetails(new KartyLeczenia.ClinicSettings()),
                "empty SQL clinic settings must be distinguished from existing data");
            Check(SharedClinicService.ShouldImportLocal(null, localClinic),
                "old local clinic can be seeded only into missing SQL row");
            Check(SharedClinicService.ShouldImportLocal(new KartyLeczenia.ClinicSettings(), localClinic),
                "old local clinic can be seeded into blank SQL row");
            Check(!SharedClinicService.ShouldImportLocal(localClinic, new KartyLeczenia.ClinicSettings()),
                "empty local settings must never erase existing SQL settings");
            Check(!SharedClinicService.ShouldImportLocal(localClinic,
                localClinic with { Doctor = "Inny lekarz" }),
                "local settings must never overwrite populated SQL clinic");
            Check(SharedClinicService.Revision(localClinic) ==
                  SharedClinicService.Revision(localClinic with { }),
                "unchanged clinic revision is reproducible");
            Check(SharedClinicService.Revision(localClinic) !=
                  SharedClinicService.Revision(localClinic with { Doctor = "Inny lekarz" }),
                "concurrent doctor changes must invalidate stale revision");
            bool invalidPngRejected = false;
            try { SharedClinicService.ValidatePng([1, 2, 3, 4], "lecznicy"); }
            catch (InvalidDataException) { invalidPngRejected = true; }
            Check(invalidPngRejected, "invalid stamp data must not enter SQL");
            bool missingStampRejected = false;
            try
            {
                SharedClinicService.VerifyStamps(localClinic with { UseDoctorStamp = true });
            }
            catch (InvalidOperationException) { missingStampRejected = true; }
            Check(missingStampRejected,
                "enabled doctor stamp without PNG must be rejected");

            // Fast-list regression checks run without any live SQL instance.
            var clock = new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero);
            var fastLists = new DocumentListCache(() => clock);
            int databaseQueries = 0;
            Task<IReadOnlyList<KartyLeczenia.DocumentSuggestion>> FetchList(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                databaseQueries++;
                return Task.FromResult<IReadOnlyList<KartyLeczenia.DocumentSuggestion>>(
                    [new KartyLeczenia.DocumentSuggestion(1, "FS 1/2026")]);
            }
            var originalList = fastLists.GetAsync("invoices-all", false, FetchList,
                CancellationToken.None).GetAwaiter().GetResult();
            var fromMemory = fastLists.GetAsync("invoices-all", false, FetchList,
                CancellationToken.None).GetAwaiter().GetResult();
            Check(databaseQueries == 1 && !originalList.Cached && fromMemory.Cached,
                "switching back to a document list within five minutes must avoid SQL");
            Check(originalList.LoadedAt == fromMemory.LoadedAt,
                "cached document list must retain its actual SQL read timestamp");
            var explicitlyRefreshed = fastLists.GetAsync("invoices-all", true, FetchList,
                CancellationToken.None).GetAwaiter().GetResult();
            Check(databaseQueries == 2 && !explicitlyRefreshed.Cached,
                "F5 must bypass the five-minute list cache");
            clock = clock.Add(DocumentListCache.Lifetime).AddSeconds(1);
            _ = fastLists.GetAsync("invoices-all", false, FetchList,
                CancellationToken.None).GetAwaiter().GetResult();
            Check(databaseQueries == 3,
                "expired document lists must be refreshed, not silently reused");
            var gate = new TaskCompletionSource<IReadOnlyList<KartyLeczenia.DocumentSuggestion>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int concurrentQueries = 0;
            Task<IReadOnlyList<KartyLeczenia.DocumentSuggestion>> SharedFetch(CancellationToken ct)
            {
                System.Threading.Interlocked.Increment(ref concurrentQueries);
                return gate.Task;
            }
            var firstReader = fastLists.GetAsync("same-query-in-flight", false,
                SharedFetch, CancellationToken.None);
            var secondReader = fastLists.GetAsync("same-query-in-flight", false,
                SharedFetch, CancellationToken.None);
            gate.SetResult([new KartyLeczenia.DocumentSuggestion(2, "PA 2/2026")]);
            Task.WhenAll(firstReader, secondReader).GetAwaiter().GetResult();
            Check(concurrentQueries == 1,
                "two simultaneous identical list requests should share one SQL query");
            var subiektA = new KartyLeczenia.ConnectionOptions(
                "sqlhost", "Subiekt", true, "", "", true, WarehouseId: 1);
            var subiektB = subiektA with { WarehouseId = 2 };
            Check(DocumentListCache.Key(subiektA, KartyLeczenia.DocumentSearchKinds.Invoices,
                "", null, null) !=
                DocumentListCache.Key(subiektB, KartyLeczenia.DocumentSearchKinds.Invoices,
                "", null, null),
                "warehouse change must never reuse previous document list");
            Check(DocumentListCache.Key(subiektA, KartyLeczenia.DocumentSearchKinds.Invoices,
                "", null, null) !=
                DocumentListCache.Key(subiektA, KartyLeczenia.DocumentSearchKinds.Receipts,
                "", null, null),
                "document type must be part of SQL list cache identity");
            for(int i=0;i<35;i++)
                _ = fastLists.GetAsync("bounded-"+i, false, FetchList,
                    CancellationToken.None).GetAwaiter().GetResult();
            var evicted = fastLists.GetAsync("bounded-0", false, FetchList,
                CancellationToken.None).GetAwaiter().GetResult();
            Check(!evicted.Cached,
                "oldest cache entry must be evicted to bound memory usage");
            fastLists.Clear();
            var cleared = fastLists.GetAsync("invoices-all", false, FetchList,
                CancellationToken.None).GetAwaiter().GetResult();
            Check(!cleared.Cached, "changing SQL connection must clear previous cached lists");
            int errors = 0;
            Task<IReadOnlyList<KartyLeczenia.DocumentSuggestion>> Fail(CancellationToken ct)
            {
                errors++;
                throw new InvalidOperationException("Test failure");
            }
            for(int retry=0;retry<2;retry++)
            {
                try
                {
                    _ = fastLists.GetAsync("failed-query", false, Fail,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException) { /* expected */ }
            }
            Check(errors==2, "SQL errors must not be cached as successful lists");

            var cancelledSearch = new CancellationTokenSource();
            async Task<IReadOnlyList<KartyLeczenia.DocumentSuggestion>> SlowDocuments(
                CancellationToken ct)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Array.Empty<KartyLeczenia.DocumentSuggestion>();
            }
            var superseded = fastLists.GetAsync("superseded-search", false,
                SlowDocuments, cancelledSearch.Token);
            cancelledSearch.Cancel();
            bool cancelled = false;
            try { _ = superseded.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "superseded document filters must cancel obsolete work");
            var retried = fastLists.GetAsync("superseded-search", false,
                FetchList, CancellationToken.None).GetAwaiter().GetResult();
            Check(!retried.Cached,
                "a cancelled document query must not poison future search results");

            var priceClock = new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero);
            var stockCache = new TimedSqlCache<int>(TimeSpan.FromSeconds(30),
                clock: () => priceClock);
            int stockQueries=0;
            Task<int> FetchStock(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(++stockQueries);
            }
            var stockOne = stockCache.ReadAsync("stock", false,
                FetchStock, CancellationToken.None).GetAwaiter().GetResult();
            var stockTwo = stockCache.ReadAsync("stock", false,
                FetchStock, CancellationToken.None).GetAwaiter().GetResult();
            Check(stockQueries==1 && !stockOne.Cached && stockTwo.Cached,
                "switching between price and expiry screens should reuse recent stock snapshot");
            var forceStock = stockCache.ReadAsync("stock", true,
                FetchStock, CancellationToken.None).GetAwaiter().GetResult();
            Check(stockQueries==2 && !forceStock.Cached,
                "manual stock refresh must bypass short cache");
            priceClock=priceClock.AddSeconds(31);
            _ = stockCache.ReadAsync("stock", false, FetchStock,
                CancellationToken.None).GetAwaiter().GetResult();
            Check(stockQueries==3,
                "stock and prices must not use stale snapshots after thirty seconds");
            var stop = new CancellationTokenSource();
            async Task<int> SlowStock(CancellationToken ct)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return 1;
            }
            var staleStock = stockCache.ReadAsync("obsolete-filter", false, SlowStock, stop.Token);
            stop.Cancel();
            bool stopped = false;
            try { _ = staleStock.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "obsolete price/stock SQL reads must be cancellable");
            var freshStock = stockCache.ReadAsync("obsolete-filter", false,
                FetchStock, CancellationToken.None).GetAwaiter().GetResult();
            Check(!freshStock.Cached,
                "cancelled SQL query must never be reused by the next viewer");

            var cataloguePrices = PriceCatalogueSnapshot.FromRows(
            [
                ("LEK-A", new PriceVariant("Bydło","domięśniowo","Wskazanie",
                    "1 ml","7 dni","60 godzin","T: 7 dni, M: 60 h")),
                ("LEK-A", new PriceVariant("Świnie","doustnie","Wskazanie",
                    "2 ml","3 dni","","T: 3 dni")),
                ("LEK-B", new PriceVariant("Koty","doustnie","Wskazanie",
                    "1 ml","","",""))
            ]);
            var formatted = cataloguePrices.Format(
                new KartyLeczenia.MedicinePrice("Próba", 12.5m)
                { Symbol = "LEK-A" });
            Check(formatted.FullCodes.Contains("Bydło IM") &&
                  formatted.FullCodes.Contains("Tkanki: 7 dni") &&
                  formatted.FullCodes.Contains("Mleko: 60 godzin"),
                "full price withdrawal should include both tissues and milk by species");
            Check(formatted.FullNames.Contains("domięśniowo") &&
                  !formatted.FullNames.Contains("Bydło IM"),
                "price route presentation must offer both Polish names and codes");
            Check(formatted.VariantsCodes.Contains("Bydło IM: T: 7 dni, M: 60 h") &&
                  formatted.VariantsCodes.Contains("Świnie PO: T: 3 dni"),
                "variant compact format should come from catalog_price_withdrawal");
            Check(formatted.SpeciesCodes.Contains("Bydło — IM:") &&
                  formatted.SpeciesNames.Contains("Świnie — doustnie:"),
                "species compact grouping should retain route and species");
            Check(!formatted.NeedsCompletion,
                "complete official catalogue variants should not be flagged missing");
            var unknownPrice = cataloguePrices.Format(
                new KartyLeczenia.MedicinePrice("Brak w katalogu", null)
                { Symbol = "BRAK" });
            Check(unknownPrice.NeedsCompletion && unknownPrice.FullCodes == "" &&
                  unknownPrice.VariantsCodes == "",
                "missing catalogue medicine must not invent withdrawal data");
            Check(cataloguePrices.Format(
                new KartyLeczenia.MedicinePrice("Wariant bez karencji", null)
                { Symbol = "LEK-B" }).NeedsCompletion,
                "incomplete catalogue withdrawal must require completion");
            var printDefault = new PricePrintOptions();
            Check(printDefault.Landscape && printDefault.Legend == "start" &&
                  printDefault.WithdrawalFormat == 1 && printDefault.RouteCodes,
                "price print defaults must match the final WinForms layout");
            var migratedPrice = PricePrintOptionsStore.FromLegacyJson(
                """
                {"Landscape":false,"Margin":9,"WithdrawalFormat":2,
                 "RouteCodes":false,"Legend":true,"LegendEveryPage":true,
                 "Font":"Segoe UI","FontSizes":[10,10,10,10,12,10],
                 "Widths":[5,40,15,14,35]}
                """);
            Check(!migratedPrice.Landscape && migratedPrice.Margin == 9 &&
                  migratedPrice.Legend == "every" &&
                  migratedPrice.WithdrawalFormat == 2 && !migratedPrice.RouteCodes &&
                  migratedPrice.Widths[1] == 40 && migratedPrice.ColumnSizes[4] == 12,
                "React price editor must preserve legacy WinForms print settings");
            var olderPrint = PricePrintOptionsStore.FromLegacyJson(
                """
                {"FontWeight":0,"Fonts":[0,4,0,0,0,0],
                 "Sizes":[0,2,0,0,0,0],
                 "FontNames":["","","","","",""],
                 "FontSizes":[0,0,0,0,0,0]}
                """);
            Check(olderPrint.FontWeight == 400 &&
                  olderPrint.ColumnFonts[1] == "Segoe UI" &&
                  olderPrint.ColumnSizes[1] == 9,
                "older WinForms indexed fonts and sizes should migrate without data loss");
            bool invalidPrintRejected = false;
            try { _ = (printDefault with { Widths = [0, 10, 10, 10, 10] }).Validate(); }
            catch (ArgumentException) { invalidPrintRejected = true; }
            Check(invalidPrintRejected, "invalid print widths must not be saved");

            // ReactKL species filter must match the final WinForms group rules.
            Check(TreatmentSpeciesGroup.Matches("cattle", ["Cielęta"]) &&
                  TreatmentSpeciesGroup.Matches("cattle", ["Bydło", "Konie"]) &&
                  TreatmentSpeciesGroup.Matches("cattle", ["Krowy"]),
                "calves/cows must be classified under cattle");
            Check(TreatmentSpeciesGroup.Matches("swine", ["Prosięta"]) &&
                  TreatmentSpeciesGroup.Matches("swine", ["Świnie"]) &&
                  TreatmentSpeciesGroup.Matches("swine", ["Tuczniki"]),
                "swine must include piglets and fatteners");
            Check(TreatmentSpeciesGroup.Matches("poultry", ["Kury"]) &&
                  TreatmentSpeciesGroup.Matches("poultry", ["Indyki"]) &&
                  TreatmentSpeciesGroup.Matches("poultry", ["Kaczki"]),
                "poultry must include hens, turkeys, ducks");
            Check(TreatmentSpeciesGroup.Matches("dog", ["Psy"]) &&
                  TreatmentSpeciesGroup.Matches("cat", ["Koty"]) &&
                  !TreatmentSpeciesGroup.Matches("dog", ["Koty"]),
                "dogs and cats must not be mixed");
            Check(!TreatmentSpeciesGroup.Matches("cattle", ["Koty", "Psy"]) &&
                  TreatmentSpeciesGroup.Matches("all", ["Koty"]),
                "incompatible medicines must be separated in cattle mode");
            Check(TreatmentSpeciesGroup.CompatibleGroups(
                      ["WARIANT DO WERYFIKACJI — bydło", "ZAWIESZONE — kury"])
                      .Length == 0,
                "unverified or suspended catalogue variants must be excluded");
            Check(TreatmentSpeciesGroup.CompatibleGroups(
                      ["Kury", "Bydło", "Kury"]).SequenceEqual(["cattle","poultry"]),
                "compatible groups must be stable and deduplicated");
            Check(!TreatmentSpeciesGroup.Matches("not-a-species", ["Bydło"]),
                "unknown species group must not silently select clinical rows");

            byte[] choiceKey = TreatmentChoiceService.ChoiceKey(
                "SQLSERVER|Subiekt", "lek-123");
            Check(choiceKey.Length == 32 &&
                  choiceKey.SequenceEqual(TreatmentChoiceService.ChoiceKey(
                      "SQLSERVER|Subiekt", " LEK-123 ")) &&
                  !choiceKey.SequenceEqual(TreatmentChoiceService.ChoiceKey(
                      "OTHER|Subiekt", "LEK-123")) &&
                  !choiceKey.SequenceEqual(TreatmentChoiceService.ChoiceKey(
                      "SQLSERVER|Subiekt", "LEK-456")),
                "remembered medicine choices must use scope+symbol WinForms SHA256 identity");
            Check(TreatmentChoiceService.NormalizeSymbol(" lek-123 ") == "LEK-123",
                "legacy WinForms medicine-choice symbol normalization");
            bool missingChoiceRejected = false;
            try { _ = TreatmentChoiceService.ChoiceKey("", "LEK"); }
            catch (ArgumentException) { missingChoiceRejected = true; }
            Check(missingChoiceRejected,
                "preferences with no source scope must never be shared across databases");

            // Description identity is byte-for-byte compatible with the original
            // WinForms BinaryWriter(scope, payerId, ASCII-NOCASE description).
            static byte[] OriginalDescriptionKey(string scope, int payer, string description)
            {
                using var buffer = new MemoryStream();
                using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen:true))
                {
                    writer.Write(scope);
                    writer.Write(payer);
                    writer.Write(string.Concat(description.Select(c =>
                        c is >= 'a' and <= 'z' ? char.ToUpperInvariant(c) : c)));
                }
                return System.Security.Cryptography.SHA256.HashData(buffer.ToArray());
            }
            byte[] animalKey = AnimalDescriptionService.DescriptionKey(
                "SQLSERVER|Subiekt", 12, "Krowy AB-123");
            Check(animalKey.SequenceEqual(OriginalDescriptionKey(
                    "SQLSERVER|Subiekt", 12, "Krowy AB-123")) &&
                  animalKey.SequenceEqual(AnimalDescriptionService.DescriptionKey(
                    "SQLSERVER|Subiekt", 12, "Krowy ab-123")) &&
                  !animalKey.SequenceEqual(AnimalDescriptionService.DescriptionKey(
                    "SQLSERVER|Subiekt", 13, "Krowy AB-123")) &&
                  !animalKey.SequenceEqual(AnimalDescriptionService.DescriptionKey(
                    "INNY|Subiekt", 12, "Krowy AB-123")),
                "animal description key must match WinForms and remain isolated per payer/source");
            bool invalidDescriptionRejected = false;
            try { _ = AnimalDescriptionService.DescriptionKey("", 12, "Krowy"); }
            catch (ArgumentException) { invalidDescriptionRejected = true; }
            Check(invalidDescriptionRejected,
                "animal description memory must require a scoped identity");

            // Window placement is local to each workstation. Off-screen
            // monitor geometry must not trap the application window.
            var workArea = new System.Drawing.Rectangle(0, 0, 1920, 1040);
            var fallback = new System.Drawing.Rectangle(150, 100, 1400, 850);
            var minSize = new System.Drawing.Size(900, 600);
            Check(ReactWindowPlacement.VisibleBounds(
                new System.Drawing.Rectangle(200, 150, 1100, 700),
                [workArea], fallback, minSize) ==
                new System.Drawing.Rectangle(200, 150, 1100, 700),
                "window geometry must retain size and position");
            Check(ReactWindowPlacement.VisibleBounds(
                new System.Drawing.Rectangle(-3200, 100, 1100, 700),
                [workArea], fallback, minSize) == fallback,
                "disconnected monitor must return the window on-screen");
            Check(ReactWindowPlacement.VisibleBounds(
                new System.Drawing.Rectangle(1200, 750, 1100, 700),
                [workArea], fallback, minSize) ==
                new System.Drawing.Rectangle(820, 340, 1100, 700),
                "partly visible windows should be clamped into working area");
            Check(ReactWindowPlacement.VisibleBounds(
                new System.Drawing.Rectangle(100, 100, 200, 200),
                [workArea], fallback, minSize) == fallback,
                "invalid saved window dimensions must be ignored");

            var legacy = new LegacyTreatmentChoice[] {
                new("LEK-A","Stary produkt A","Bydło","domięśniowo"),
                new("LEK-A","Stary produkt B","Bydło","domięśniowo"),
                new("LEK-B","Stary B","Bydło","doustnie"),
                new("LEK-B","Nowy B","Świnie","doustnie"),
                new("LEK-C","Stary C","Świnie","doustnie"),
                new("LEK-C","","Bydło","podskórnie"),
                new("LEK-D","","Bydło","domięśniowo"),
                new("LEK-D","","Świnie","domięśniowo")
            };
            var resolved = TreatmentChoiceService.ResolveLegacy(
                legacy, [" lek-a ","LEK-B","LEK-C","LEK-D"]);
            Check(resolved.Length == 2 &&
                  resolved.Any(item => item.Symbol == "LEK-A" &&
                      item.Species == "Bydło" && item.Route == "domięśniowo") &&
                  resolved.Any(item => item.Symbol == "LEK-C" &&
                      item.Species == "Bydło" && item.Route == "podskórnie"),
                "legacy choices must prefer symbol records and reject ambiguous variants");
            Check(TreatmentChoiceService.ResolveLegacy(legacy, ["NIE-ZNANY"]).Length == 0,
                "legacy choices must never leak into unrelated document symbols");

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
                  [swine with {Species="Bydło",Milk=""}]).Status=="noMatch",
                "incompatible cattle must not be chosen for explicit swine selection");
            var defaultCattle = new KartyLeczenia.TreatmentRow();
            defaultCattle.Set("Symbol Subiekta","VET-123",false);
            Check(TreatmentAutofillPlanner.Propose(0, defaultCattle,
                  [swine with {Species="Bydło",Milk=""}]).Status=="review",
                "cattle without milk withdrawal needs manual review");
            Check(TreatmentAutofillPlanner.Propose(0, defaultCattle,
                  [swine with {Species="Kury",Milk=""}]).Status=="review",
                "poultry eggs restrictions require manual review");
            Check(TreatmentAutofillPlanner.Propose(0, defaultCattle,
                  [swine with {Tissues="",Milk="36 godzin"}]).Status=="review",
                "milk-only entry must not bulk-apply without edible tissue withdrawal");
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
            var confirmed = TreatmentAutofillPlanner.Propose(0,
                new KartyLeczenia.TreatmentRow(new Dictionary<string,string> {
                    ["Symbol Subiekta"]="VET-123",["Drukuj"]="True",
                    ["Domyślny gatunek"]="True",["Gatunek"]="Bydło"
                }),[swine]);
            Check(confirmed.Status=="ready", "review test fixture must be eligible");
            Check(TreatmentAutofillPlanner.MatchesReviewed(confirmed,
                confirmed.Species, confirmed.Route, confirmed.Dosing,
                confirmed.Withdrawal, confirmed.Source, confirmed.PdfPath, confirmed.HasPdf),
                "reviewed suggestion must match unchanged SQL clinical data");
            Check(!TreatmentAutofillPlanner.MatchesReviewed(confirmed,
                confirmed.Species, confirmed.Route, "inna dawka",
                confirmed.Withdrawal, confirmed.Source, confirmed.PdfPath, confirmed.HasPdf),
                "concurrent dosage edit must reject prior approval");
            Check(!TreatmentAutofillPlanner.MatchesReviewed(confirmed,
                confirmed.Species, confirmed.Route, confirmed.Dosing,
                "inna karencja", confirmed.Source, confirmed.PdfPath, confirmed.HasPdf),
                "concurrent withdrawal edit must reject prior approval");
            Check(!TreatmentAutofillPlanner.MatchesReviewed(confirmed,
                confirmed.Species, confirmed.Route, confirmed.Dosing,
                confirmed.Withdrawal, "inna ChPL", confirmed.PdfPath, confirmed.HasPdf),
                "changed clinical source must reject prior approval");
            Check(!TreatmentAutofillPlanner.MatchesReviewed(confirmed,
                confirmed.Species, confirmed.Route, confirmed.Dosing,
                confirmed.Withdrawal, confirmed.Source, "ChPL/inny-dokument.pdf", true),
                "changed official PDF path must reject prior approval");
            Check(!TreatmentAutofillPlanner.MatchesReviewed(confirmed,
                confirmed.Species, confirmed.Route, confirmed.Dosing,
                confirmed.Withdrawal, confirmed.Source, confirmed.PdfPath, false),
                "missing official PDF flag must reject prior approval");
            // SQL archive revisions: editing never allocates a new number,
            // changes source identity, loses stamps or silently accepts a
            // treatment year that differs from the assigned card number.
            var savedRow = new Dictionary<string,string> {
                ["Drukuj"]="True", ["Sekcja (II lub III)"]="II",
                ["Produkt"]="Produkt źródłowy",["Symbol Subiekta"]="SUB-123",
                ["Gatunek"]="Bydło",["Droga podania"]="domięśniowo",
                ["Dawkowanie i okres stosowania"]="1 ml",["Karencja"]="28 dni",
                ["Rozpoznanie"]="Rozpoznanie",["Status karencji"]=CardSchema.RegistryVerified
            };
            var savedFields = new Dictionary<string,string> {
                ["Nr karty"]="K/14/2026",["Dokument źródłowy"]="FS 123",
                ["Zgłoszenie — data i godzina"]="",
                ["Wykonanie — data i godzina"]="19.11.2026 13:30",
                ["Wyniki badań"]="Przed zabiegiem"
            };
            var originalSaved = new Card(savedFields, [savedRow]) {
                DoctorStampPng = [0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]
            };
            var changedFields = new Dictionary<string,string>(savedFields) {
                ["Nr karty"]="K/999/2026", // tampering must not change SQL number
                ["Wyniki badań"]="Po zabiegu"
            };
            var changedRow = new Dictionary<string,string>(savedRow) {
                ["Produkt"]="Zamieniony produkt źródłowy",
                ["Symbol Subiekta"]="NIE-ISTNIEJE",
                ["Karencja"]="Sprawdzono ręcznie",
                ["Zamiennik produktu"]="Zamiennik A"
            };
            var editedSaved = SavedCardRevisionService.ApplyChanges(
                originalSaved,changedFields,[changedRow],2026,14);
            Check(editedSaved.Fields["Nr karty"]=="K/14/2026" &&
                  editedSaved.Fields["Dokument źródłowy"]=="FS 123" &&
                  editedSaved.Rows[0]["Produkt"]=="Produkt źródłowy" &&
                  editedSaved.Rows[0]["Symbol Subiekta"]=="SUB-123" &&
                  editedSaved.Rows[0]["Zamiennik produktu"]=="Zamiennik A" &&
                  editedSaved.Rows[0]["Status karencji"]==
                    "Zmieniono ręcznie — sprawdź przed wydrukiem" &&
                  editedSaved.DoctorStampPng?.SequenceEqual(originalSaved.DoctorStampPng!)==true,
                "saved card editing must preserve numbering, source and stamps, flag manual clinical changes");
            Check(SavedCardRevisionService.Revision(
                  System.Text.Json.JsonSerializer.Serialize(originalSaved)) !=
                  SavedCardRevisionService.Revision(
                    System.Text.Json.JsonSerializer.Serialize(editedSaved)),
                "saved-card revision must change after edits");
            Check(CardHtmlRenderer.Render(editedSaved).Contains("Zamiennik A"),
                "revised card print must include explicitly entered replacement");
            bool changedYearBlocked=false;
            try {
                var wrongYear = new Dictionary<string,string>(savedFields) {
                    ["Wykonanie — data i godzina"]="01.01.2027 09:00"
                };
                _=SavedCardRevisionService.ApplyChanges(
                    originalSaved,wrongYear,[savedRow],2026,14);
            } catch (ArgumentException) { changedYearBlocked=true; }
            Check(changedYearBlocked,"updating a card must not silently change numbering year");
            bool rowCountBlocked=false;
            try {
                _=SavedCardRevisionService.ApplyChanges(
                    originalSaved,savedFields,[],2026,14);
            } catch (InvalidOperationException) { rowCountBlocked=true; }
            Check(rowCountBlocked,"saved-card updates must reject row count changes");

            Check(SavedCardSearch.ParseNumber(" K/00014/2026 ") is (2026,14) &&
                  SavedCardSearch.ParseNumber("k/8/2027") is (2027,8) &&
                  SavedCardSearch.ParseNumber("K/0/2026") is null &&
                  SavedCardSearch.ParseNumber("K/18446744073709551616/2026") is null &&
                  SavedCardSearch.ParseNumber("FS 123") is null,
                "card-number search must use a safe, exact indexed year/sequence");
            Check(CardArchiveService.SafeJsonFileName("K/14/2026")=="K_14_2026" &&
                  CardArchiveService.SafeJsonFileName(" : / ")=="Karta-leczenia",
                "saved-card JSON filenames must be Windows-safe");
            Check(CardPdfService.AttachmentName("FS/123/2026") ==
                    "karta-leczenia-FS-123-2026.pdf" &&
                  CardPdfService.AttachmentName("PA:12*34?") ==
                    "karta-leczenia-PA-12-34-.pdf" &&
                  CardPdfService.AttachmentName("") == "karta-leczenia.pdf" &&
                  CardPdfService.AttachmentName("   ") == "karta-leczenia.pdf",
                "PDF attachments must use the source document number and Windows-safe characters");
            Console.WriteLine($"ReactKL regression checks passed: {checks}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
