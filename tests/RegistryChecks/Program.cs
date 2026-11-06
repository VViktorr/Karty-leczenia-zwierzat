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
Console.WriteLine($"Passed {checks} core checks.");

