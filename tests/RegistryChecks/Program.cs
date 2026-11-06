using KartyLeczenia;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Reject(string text)
{
    try { RegistryService.ExtractSection(text); }
    catch (InvalidDataException) { checks++; return; }
    throw new Exception("Parser accepted an ambiguous or missing withdrawal section.");
}
// A route-sensitive section must retain all alternatives and the prohibition, not just its first number.
string current = RegistryService.ExtractSection("3.11 Ograniczenia\nNie dotyczy.\n3.12 Okresy karencji\nBydło: domięśniowo 30 dni; podskórnie 44 dni.\nMleko: niedopuszczony.\nŚwinie: 18 dni.\n4. DANE FARMAKOLOGICZNE\nNie kopiuj dalszych danych.");
Check(current.Contains("30 dni") && current.Contains("44 dni") && current.Contains("18 dni") && current.Contains("niedopuszczony"), "Lost route/species restrictions.");
Check(!current.Contains("FARMAKOLOGICZNE"), "Captured text beyond withdrawal section.");
string legacy = RegistryService.ExtractSection("4.11. Okres karencji\nTkanki jadalne: 7 dni. Mleko: 36 godzin.\n5. WŁAŚCIWOŚCI FARMAKOLOGICZNE\nInne dane.");
Check(legacy.Contains("36 godzin") && !legacy.Contains("Inne dane"), "Legacy heading parsing failed.");
string wrapped = RegistryService.ExtractSection("3.12\nOkresy\nkarencji\nTkanki jadalne: 4 dni; mleko: 24 godziny.\n4.\nDANE FARMAKOLOGICZNE");
Check(wrapped.Contains("24 godziny"), "PDF line wrapping failed.");
Reject("Strona została zablokowana. Spróbuj ponownie później.");
Reject("3.12 Okresy karencji\nTkanki: 7 dni. Brak rozpoznanego końca.");
Reject("3.12 Okresy karencji\n\n4. DANE FARMAKOLOGICZNE");
Check(RegistryService.Url(50492).EndsWith("/50492/characteristic"), "Wrong official URL.");
Check(RegistryService.MappingPath("A|1") != RegistryService.MappingPath("B|1"), "Mappings leak between databases.");

var row = new TreatmentRow();
row.Set("Produkt", "<lek & test>");
row.Set("Karencja", "30 dni");
row.Set("Źródło karencji", "https://example.invalid/source", false);
row.Set("SHA256 źródła", "hash", false);
row.Set("Pełna sekcja karencji źródła", "source snapshot", false);
row.Set("Status karencji", CardSchema.RegistryVerified, false);
Check(!CardSchema.NeedsReview(row.Values), "Registry confirmation was not preserved.");
row.Set("Droga podania", "Podskórnie");
Check(CardSchema.NeedsReview(row.Values), "Changing route must invalidate registry confirmation.");
row.Set("Ilość wydana", "1 szt.");
var legacyCard = new Card(new() { ["Nr karty"] = "1/2026", ["Właściciel i adres"] = "<właściciel>" }, [new(row.Values)]);
string json = System.Text.Json.JsonSerializer.Serialize(legacyCard);
var restored = System.Text.Json.JsonSerializer.Deserialize<Card>(json)!;
Check(restored.Rows[0]["SHA256 źródła"] == "hash" && restored.Rows[0]["Pełna sekcja karencji źródła"] == "source snapshot", "Source history lost during migration.");
string html = CardHtmlRenderer.Render(restored);
Check(html.Contains("&lt;lek &amp; test&gt;") && !html.Contains("<lek & test>"), "HTML escaping failed.");
Check(html.Contains("A4 landscape"), "Landscape A4 was lost.");
restored.Rows[0]["Sekcja (II lub III)"] = "invalid";
try { CardHtmlRenderer.Render(restored); throw new Exception("Invalid section accepted."); }
catch (InvalidOperationException) { checks++; }
restored.Rows[0]["Drukuj"] = "False";
try { CardHtmlRenderer.Render(restored); throw new Exception("Empty print selection accepted."); }
catch (InvalidOperationException) { checks++; }
// BRAVOXIN: an explicit zero is valid data, not missing information.
string zero = RegistryService.ExtractSection("3.12 Okresy karencji\nZero dni.\n4. DANE IMMUNOLOGICZNE\nNie kopiuj dalej.");
Check(zero == "Zero dni.", "Explicit zero withdrawal rejected or changed.");
Check(RegistryService.ExtractSection("4.11 Okres karencji\n0 dni.\n5. WŁAŚCIWOŚCI FARMAKOLOGICZNE") == "0 dni.", "Numeric zero withdrawal rejected.");
Check(RegistryService.ExtractSection("3.12 Okresy karencji\nNie dotyczy.\n4. DANE IMMUNOLOGICZNE") == "Nie dotyczy.", "Non-applicable text must not become zero.");
Check(RegistryService.ParseProductId("https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/45232/characteristic") == 45232, "Official PDF link not parsed.");
try { RegistryService.ParseProductId("https://example.invalid/api/rpl/medicinal-products/45232/characteristic"); throw new Exception("Foreign URL accepted."); }
catch (InvalidOperationException) { checks++; }
Check(RegistryCatalog.ExactCandidates("BRAVOXIN A'100 ML").Single().Id == 45232, "Known Bravoxin identity not suggested.");
Check(RegistryCatalog.ExactCandidates("FLORDOFEN 100MG/ML A'100 ML").All(p => p.Id != 32709), "Wrong strength matched automatically.");
Check(RegistryCatalog.ExactCandidates("BRAVOXIN10").Count == 0, "Different product name guessed.");
using (var xml = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<produktyLecznicze xmlns='urn:test'><produktLeczniczy id='12' nazwaProduktu='Lek A' rodzajPreparatu='weterynaryjny' moc='20 mg/ml' postac='Roztwór'/><produktLeczniczy id='13' nazwaProduktu='Lek ludzki' rodzajPreparatu='ludzki'/></produktyLecznicze>")))
{
    var catalog = RegistryCatalog.Parse(xml);
    Check(catalog.Count == 1 && catalog[0].Id == 12, "Catalog must exclude human medicines and use product IDs.");
}
var automatic = new TreatmentRow();
automatic.Set("Produkt", "Bravoxin");
var sourceDocument = new RegistryDocument(45232, RegistryService.Url(45232), "test-hash", DateTimeOffset.UtcNow, zero, "Bravoxin zawiesina do wstrzykiwań");
AutomaticWithdrawalService.Apply(automatic, new(sourceDocument, false));
Check(automatic.Get("Karencja") == "Zero dni.", "Automatic fill did not copy source withdrawal.");
Check(CardSchema.NeedsReview(automatic.Values), "Automatic proposal must still require confirmation.");
Check(automatic.Get("Pełna sekcja karencji źródła") == zero && automatic.Get("Źródło karencji").Contains("45232"), "Automatic fill lost its source.");
var allRoutes = sourceDocument with { WithdrawalSection = current };
AutomaticWithdrawalService.Apply(automatic, new(allRoutes, true));
Check(automatic.Get("Karencja").Contains("30 dni") && automatic.Get("Karencja").Contains("44 dni"), "Automatic fill silently selected one route.");

string Field(string cls, string value) => $"<div class='{cls}'><div class='field__item'>{value}</div></div>";
string Period(string tissue, string value, string unit, string note = "") => "<div class='paragraph--type--upd-withdrawal-period'>" + Field("upd-tissue__field-upd-term-name", tissue) + Field("upd-withdrawal-period__field-upd-withdrawal-value", value) + Field("upd-units__field-upd-term-name", unit) + note + "</div>";
string Fixture(string country = "Poland", string status = "Valid") => Field("products__extra-field-upd-products-authorised-countries", country) + Field("upd-auth-status__field-upd-term-name", status) + Field("products__extra-field-upd-products-medicine-name", "Example 50 mg/ml") + "<div class='products__extra-field-upd-products-species-by-route'><ul><li><div class='upd-items-list-title'>Subcutaneous use</div><ul><li class='list-item__upd_species'>" + Field("upd-species__field-upd-term-name", "Cattle") + Period("Meat and offal", "0", "day") + Period("Milk", "36", "hour", "Not for use in pregnancy") + "</li></ul></li></ul></div>";
var upd = UpdService.Parse("600000000001", Fixture());
Check(upd.WithdrawalSection.Contains("Tkanki jadalne: 0 dni") && upd.WithdrawalSection.Contains("Mleko: 36 godz."), "UPD lost explicit zero or changed units.");
Check(upd.WithdrawalSection.Contains("Bydło; Podanie podskórne") && upd.WithdrawalSection.Contains("Not for use in pregnancy"), "UPD lost context or restriction.");
Check(upd.UpdId == "600000000001" && upd.Source == "UPD" && upd.Sha256.Length == 64, "UPD provenance missing.");
foreach (var invalid in new[] { Fixture("Norway") + "Poland", Fixture(status: "Suspended"), Fixture().Replace("paragraph--type--upd-withdrawal-period", "changed-layout") })
{
    try { UpdService.Parse("600000000001", invalid); throw new Exception("UPD accepted foreign, suspended or unknown data."); }
    catch (InvalidDataException) { checks++; }
}
Check(UpdService.Translate("Unknown restriction") == "Unknown restriction", "UPD guessed a translation.");
Check(UpdService.ParseId("https://medicines.health.europa.eu/veterinary/pl/600000000001") == "600000000001", "UPD link parsing failed.");
try { UpdService.ParseId("https://evil.example/veterinary/pl/600000000001"); throw new Exception("Foreign UPD link accepted."); } catch (InvalidDataException) { checks++; }
var matches = UpdService.ParseSearch("<a href='/veterinary/en/600000000001'>Example</a><a href='https://evil.example/600000000002'>Other</a>");
Check(matches.Count == 1 && matches[0].Id == "600000000001", "UPD search accepted an external product.");
automatic.ApplyRegistry(new(upd, "", "", "", upd.WithdrawalSection, upd.ProductIdentity));
Check(!CardSchema.NeedsReview(automatic.Values) && automatic.RegistryStatus.StartsWith("UPD"), "UPD confirmation not retained.");

const string oneAnimal = "4.1 Docelowe gatunki zwierząt\nBydło (cielęta ≤ 330 kg)\n4.2 Wskazania\nOpis";
Check(RegistryService.SingleSpecies(oneAnimal) == "Bydło (cielęta ≤ 330 kg)", "Single species must retain its calf/weight restriction.");
Check(RegistryService.SingleSpecies(oneAnimal.Replace("Bydło (cielęta ≤ 330 kg)", "Bydło i owce")) == "", "Multiple species must not be selected automatically.");
const string oneRoute = "4.9 Dawkowanie i droga(i) podawania\nWyłącznie podanie podskórne.\nPodawać dawkę według masy ciała.\n4.10 Przedawkowanie";
Check(RegistryService.SingleRoute(oneRoute) == "Podanie podskórne", "Explicit single route not proposed.");
Check(RegistryService.SingleRoute(oneRoute.Replace("Wyłącznie podanie podskórne.", "Podanie podskórne lub domięśniowo.")) == "", "Multiple routes must remain unselected.");
var contextRow = new TreatmentRow();
contextRow.FillMissingContext("Bydło", "Podanie podskórne");
Check(contextRow.Get("Gatunek") == "Bydło" && contextRow.Get("Opis zwierząt / numer siedziby stada") == "Bydło", "Single animal context not copied to the card.");
contextRow.Set("Gatunek", "Wpis lekarza"); contextRow.Set("Opis zwierząt / numer siedziby stada", "Stado 123"); contextRow.Set("Droga podania", "Wpisana ręcznie");
contextRow.FillMissingContext("Owce", "Doustnie");
Check(contextRow.Get("Gatunek") == "Wpis lekarza" && contextRow.Get("Opis zwierząt / numer siedziby stada") == "Stado 123" && contextRow.Get("Droga podania") == "Wpisana ręcznie", "Automatic context overwrote manual values.");
Check(upd.SuggestedSpecies == "Bydło" && upd.SuggestedRoute == "Podanie podskórne", "UPD single structured context lost.");
string multiSpecies = Fixture().Replace("</li></ul></li></ul></div>", "</li><li class='list-item__upd_species'>" + Field("upd-species__field-upd-term-name", "Sheep") + Period("Meat and offal", "7", "day") + "</li></ul></li></ul></div>");
Check(UpdService.Parse("600000000001", multiSpecies).SuggestedSpecies == "", "UPD multiple species were selected automatically.");
Check(html.Contains("rowspan=\"2\"") && html.Contains("colspan=\"4\""), "Scanned form grouped headers missing.");

DocumentDelivery Delivery(decimal quantity, string series, string expiry = "", string unit = "FL.", string productId = "123")
{
    var deliveryRow = new TreatmentRow(new() {
        ["Produkt"] = "FLUNIVIS 50MG/ML A'100ML", ["ID towaru"] = productId,
        ["Sekcja (II lub III)"] = "II", ["Drukuj"] = "True", ["Seria"] = series, ["Ważność"] = expiry });
    return new(deliveryRow, quantity, unit);
}
var mergedDeliveries = DocumentRowMerger.Merge([
    Delivery(0.29m, ""), Delivery(0.31m, "V-003A", "31.12.2027")]);
Check(mergedDeliveries.Count == 1 && decimal.Parse(mergedDeliveries[0].Get("Ilość wydana").Split(' ')[0]) == 0.60m, "Delivery quantities were not summed.");
Check(mergedDeliveries[0].Get("Seria") == "V-003A" && !mergedDeliveries[0].Summary.Contains("brak"), "Blank series was not omitted.");
Check(mergedDeliveries[0].Get("Ważność").Contains("V-003A: 31.12.2027"), "Expiry lost its batch association.");
var multipleBatches = DocumentRowMerger.Merge([
    Delivery(1m, "A", "01.01.2027"), Delivery(2m, "B", "02.02.2028"), Delivery(3m, "A", "01.01.2027")]);
Check(multipleBatches[0].Get("Seria") == "A, B" && decimal.Parse(multipleBatches[0].Get("Ilość wydana").Split(' ')[0]) == 6m, "Distinct series or total quantity incorrect.");
Check(multipleBatches[0].Get("Ważność") == "A: 01.01.2027, B: 02.02.2028", "Batch expiry pairs changed or duplicated.");
Check(DocumentRowMerger.Merge([Delivery(1m, "", unit: "FL."), Delivery(1m, "", unit: "szt.")]).Count == 2, "Incompatible units were added.");
Check(DocumentRowMerger.Merge([Delivery(1m, "", productId: "123"), Delivery(1m, "", productId: "456")]).Count == 2, "Different product identities were merged.");

Check(RegistryCatalog.ExactCandidates("FLUNIVIS50MG/ML A'100ML").Single().Id == 50492, "Joined Subiekt brand and strength not matched.");
Check(RegistryCatalog.ExactCandidates("FLUNIVIS100MG/ML A'100ML").All(p => p.Id != 50492), "Joined wrong concentration was matched.");
Check(RegistryCatalog.NameCandidates("TILKOMAY A'100 ML").Any(p => p.Id == 42147), "Package suffix prevented brand search.");
Check(RegistryCatalog.Search("fluni").Any(p => p.Id == 50492), "Partial brand search failed.");
var cattleDefault = new TreatmentRow();
Check(cattleDefault.Get("Gatunek") == "Bydło" && cattleDefault.Get("Opis zwierząt / numer siedziby stada") == "Bydło", "Default cattle missing.");
cattleDefault.FillMissingContext("Owce", "Podanie podskórne");
Check(cattleDefault.Get("Gatunek") == "Owce" && cattleDefault.Get("Opis zwierząt / numer siedziby stada") == "Owce", "Source species did not replace an untouched default.");
var manualSpecies = new TreatmentRow(); manualSpecies.Set("Gatunek", "Konie");
manualSpecies.FillMissingContext("Owce", "");
Check(manualSpecies.Get("Gatunek") == "Konie", "Source species overwrote a manual choice.");

var contextualSource = new RegistryDocument(1, "https://example.invalid/fixture", "fixture", DateTimeOffset.UtcNow,
    "Bydło:\nTkanki jadalne: podanie domięśniowe: 10 dni\npodanie podskórne: 20 dni\nMleko: Produkt niedopuszczony do stosowania.\nŚwinie:\nTkanki jadalne: 7 dni",
    "Syntetyczny dokument testowy")
{
    TargetSpeciesSection = "Bydło i świnie",
    DosingSection = "Bydło:\nLeczenie\nPodanie domięśniowe: 1 ml/10 kg, dwukrotnie.\nPodanie podskórne: 2 ml/10 kg, jednokrotnie.\nŚwinie:\nPodanie domięśniowe: 3 ml/10 kg.\nW jedno miejsce nie podawać więcej niż 5 ml."
};
var subcutaneous = TreatmentContextService.Select(contextualSource, "Bydło", "Podskórnie");
Check(subcutaneous.Withdrawal.Contains("20 dni") && !subcutaneous.Withdrawal.Contains("10 dni") && !subcutaneous.Withdrawal.Contains("7 dni"), "Route/species withdrawal filtering failed.");
Check(subcutaneous.Withdrawal.Contains("Mleko") && subcutaneous.Withdrawal.Contains("niedopuszczony"), "Milk prohibition was discarded.");
Check(subcutaneous.Dosing.Contains("2 ml/10 kg") && !subcutaneous.Dosing.Contains("1 ml/10 kg") && !subcutaneous.Dosing.Contains("3 ml/10 kg") && subcutaneous.Dosing.Contains("5 ml"), "Selected dosing or shared safety limit lost.");
Check(TreatmentContextService.Select(contextualSource, "Bydło", "").Withdrawal == "", "Multiple routes were chosen without selection.");
Check(TreatmentContextService.Select(contextualSource, "Konie", "Podskórnie").Withdrawal == "", "Unlisted species accepted.");
var automaticContext = new TreatmentRow();
automaticContext.StoreTreatmentSource(contextualSource);
automaticContext.Set("Droga podania", "Podskórnie");
Check(automaticContext.Get("Karencja").Contains("20 dni"), "Selected route not applied automatically.");
automaticContext.Set("Droga podania", "Domięśniowo");
Check(automaticContext.Get("Karencja").Contains("10 dni") && !automaticContext.Get("Karencja").Contains("20 dni"), "Route change did not refresh automatic withdrawal.");
automaticContext.Set("Dawkowanie i okres stosowania", "Indywidualny wpis lekarza");
automaticContext.Set("Droga podania", "Podskórnie");
Check(automaticContext.Get("Dawkowanie i okres stosowania") == "Indywidualny wpis lekarza", "Manual dosing was overwritten.");
Check(TreatmentContextService.ExtractDosing("3.9 Droga podania i dawkowanie\nPodanie podskórne.\n3.10 Objawy przedawkowania\nNie kopiuj dalej.").Trim() == "Podanie podskórne.", "Dosing section boundaries failed.");
var missingAmount = new DocumentSuggestion(1, "FS 1");
Check(missingAmount.AmountText == "Brutto: —", "Missing amount reported as zero.");
Check(new DocumentSuggestion(1, "FS 1", Gross: 0m, Currency: "PLN").AmountText == "Brutto: " + 0m.ToString("N2") + " PLN", "Real zero amount was discarded.");
var collectiveInvoice = new DocumentSuggestion(1, "FS 1") { Related = new[] { new DocumentLink(2, "WZ 10"), new DocumentLink(3, "WZ 11"), new DocumentLink(4, "WZ 12") } };
Check(collectiveInvoice.RelatedText.Contains("(3)") && collectiveInvoice.Related.All(d => collectiveInvoice.RelatedText.Contains(d.Number)), "Collective invoice lost linked WZ.");
Check(new DocumentSuggestion(2, "WZ 10") { Related = new[] { new DocumentLink(1, "FS 1") } }.RelatedText.Contains("FS 1"), "Reverse invoice link missing.");
// Rispoval-style QRD sections, split numbering and page breaks.
string nasalPdf = "3.1\nDocelowe gatunki zwierząt\nBydło.\n3.2Wskazania lecznicze\nInformacje.\n3.9\nDroga podania i dawkowanie\nDawka: 2 ml.\nDroga: Podanie donosowe.\nJedna dawka dla cieląt od 9 dnia życia.\n3.10Objawy przedawkowania\nNie kopiuj.\n3.12 Okresy karencji\n5\nZero dni.\n4. DANE IMMUNOLOGICZNE";
var nasalDoc = new RegistryDocument(18875, RegistryService.Url(18875), "", DateTimeOffset.UtcNow, RegistryService.ExtractSection(nasalPdf), "")
{
    DosingSection = TreatmentContextService.ExtractDosing(nasalPdf),
    TargetSpeciesSection = TreatmentContextService.ExtractSpecies(nasalPdf),
    SuggestedRoute = RegistryService.SingleRoute(nasalPdf)
};
var nasalContext = TreatmentContextService.Select(nasalDoc, "Bydło", "");
Check(RegistryService.SingleSpecies(nasalPdf) == "Bydło", "Split target-species heading was lost.");
Check(nasalContext.Route == "Podanie donosowe" && nasalContext.Dosing.Contains("2 ml"), "Nasal route/dosing not selected.");
Check(!nasalContext.Dosing.Contains("Nie kopiuj"), "Dosing crossed the next heading.");
Check(nasalContext.Withdrawal == "Zero dni.", "Page number leaked into withdrawal.");
Check(RegistryService.CleanPdfPage("Dawka: 2 ml.\n5") == "Dawka: 2 ml.", "Page footer retained.");
Check(RegistryService.CleanPdfPage("Dawka:\n2\nml.\n5").Contains("\n2\n"), "Numeric dosing row removed.");
Console.WriteLine($"Passed {checks} core checks.");

