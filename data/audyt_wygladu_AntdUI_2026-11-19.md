# Audyt wyglądu SQL KartyLeczenia (WinForms + AntdUI)
Data: 2026-11-19
Gałąź: winforms

## Błędy potwierdzone w kodzie

1. **Główne zakładki miały utracone wyraźne wyróżnienie**. W starym rendererze `MainTabPainter.Draw` wariant Line rysował tylko cienką kreskę; etykieta wybranej zakładki miała zaledwie pogrubienie i mało kontrastowy kolor. Dodatkowo `ControlPaint.DrawFocusRectangle` celowo rysował przerywaną ramkę Windows. Zastąpiono to wyraźnie większym aktywnym nagłówkiem, wypełnieniem, zaokrągleniami i bez przerywanej ramki.
2. **Ustawienia były rysowane inną biblioteką wizualną**. `MainForm.ConfigureConnection` tworzył drugi natywny `TabControl` bez `OwnerDrawFixed`, inaczej niż zakładki główne. Włączono wspólny renderer i aktualizację kolorów.
3. **Podgląd ustawień motywu był mylący**. W `AppearanceEditor` nadal używano `DataGridView` do demonstracji wyglądu tabel, mimo że dokumenty i cennik mają `AntdUI.Table`. Zastąpiono podgląd tabelą AntdUI.
4. **Kolory przełączników były zdublowane**. Konfiguracja AntdUI w `AppearanceThemes.ApplyButton` nadpisywała zwykłe tło i `ToggleBack` zależnie od bieżącego stanu. Rozdzielono normalne i aktywne kolory w spójny sposób. Grupom przełączników dokumentów i ważności wyrównano wymiar.
5. **Modalne okna nie przechodziły przez wspólną aplikację motywu**. Ujednolicono edytor leków, wybór kartoteki, archiwizację, edytor wydruku i podgląd.

## Aktualne kontrolek i zależności

- `AntdUI` jest bezpośrednią biblioteką wyglądu. **Nie wykryto bezpośrednich zależności NuGet od MaterialSkin, ReaLTaiizor ani Krypton** w projekcie WinForms.
- `Microsoft.Web.WebView2` pozostaje niezbędny dla podglądu PDF/HTML oraz drukowania; `PdfPig` dla odczytu tekstu ChPL; `KartyLeczenia.Core` dla funkcji wspólnych. Usunięcie ich nie jest „czyszczeniem wyglądu”, tylko zepsuciem funkcji.
- `DataGridView` pozostaje w formularzu karty leczenia i edytorze wariantów. Są to tabelki z edycją, walidacją i inną semantyką niż listy dokumentów. Usunięcie ich wymaga osobnej migracji, testów edycji i wydruku.
- `TabControl` nadal przechowuje stronice. Nagłówki mają renderer wzorowany na AntdUI, ale nie są jeszcze fizycznym `AntdUI.Tabs`. Przeniesienie powinno być wykonane całościowo, z kontrolą indeksów stron, przywracania zakładki i zdarzeń.
- `ListParameters` oraz część ustawień wydruku nadal mają natywne ComboBox/NumericUpDown/TextBox. Wygląd jest objęty wspólną paletą, ale nie każda kontrolka jest natywna AntdUI. To kolejny etap migracji, nie należy usuwać ich w ciemno.

## Osobny problem ze zdjęcia: nieaktualny mechanizm aktualizacji

Na zdjęciu komunikat `Kontrola aktualizacji` informuje o braku
`\\LEWY\KartyLeczenia-Updates\release.json`.
W aktualnej gałęzi `winforms` mechanizm `ServerUpdateGate` oraz `SharedArchive.Updates` pobierają wersję instalatora z **SQL: `dbo.program_update`**, a nie z tego pliku UNC.
Nie należy traktować problemu `release.json` jako błędu rysowania zakładek. Należy sprawdzić, czy uruchamiany jest stary program/skrót lub zewnętrzny launcher oraz czy bieżący instalator jest opublikowany w SQL. Nie usuwać zabezpieczeń aktualizacji bez potwierdzenia wersji.

## Kontrole po pobraniu commitów

Kompilacja na Windows:
```powershell
dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release
```
Przejścia manualne:
1. Motywy jasny, ciemny, morski, grafitowy, Material 3.
2. Cztery style zakładek; aktywna karta ma być wyraźnie wyższa, pogrubiona i wypełniona, bez przerywanej natywnej ramki.
3. Przełączniki faktury/paragony/WZ oraz terminy ważności; każda pozycja jednolicie wyrównana.
4. Klawiatura: Tab, Shift+Tab, strzałki i Alt+1/2/4; dostępność i stan aktywny.
5. Skala 80%, 100%, 125%, 150%, 200%; żadne przyciski nie powinny zasłaniać formularzy.
6. Otwarcie/zamknięcie edytora leków, podglądu wydruków i okna archiwizacji.
7. Aktualizacja z SQL, a osobno poprawne przekierowanie nieaktualnego skrótu/launchera UNC.

## Zakres sprawdzony

Przejrzano `MainForm`, `MainTabPainter`, `AppearanceThemes`, `AntdControls`,
`PriceTable`, `DocumentsTable`, `CatalogueEditor`, `CatalogueProductPicker`,
`ArchiveBackupDialog`, `PricePrintEditor`, `PricePrintPreview`,
`ListParameters`, `ServerUpdateGate` oraz `SharedArchive.Updates`.
Nie wykonano testów graficznych na Windows w tej sesji; nie należy ogłaszać migracji całego UI jako zakończonej.
