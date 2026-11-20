# ReactKL — audyt rzeczywistego przeniesienia WinForms
**Data:** 20 listopada 2026  
**ReactKL:** commit `f701661e3f6fbfda9827dd7fa017e48b5743a631` (gałąź `ReactKL`)  
**Wzorzec funkcjonalny:** `winforms-backup-2026-11-19`  
**Bez porównania:** motywów/kolorów/skórek i oprawy WinForms.  
**Zasada zakresu:** przenosić tylko funkcje istniejące w końcowym WinForms; inne wymagają zgody użytkownika.

## Werdykt

**Nie ma podstaw, by uznać duże moduły ReactKL za przeniesione i działające w 100%.** Potwierdzenie na poziomie kodu: pliki, operacje API, formularze i kompilacja. Brakuje testów porównawczych na tym samym SQL Server i Subiekcie oraz pełnych testów scenariuszowych i rzeczywistych wydruków. W szczególności użytkownik zgłosił, że dodawanie nowego leku, trwałe usuwanie z kosza i przełączanie list dokumentów nie działają zgodnie z WinForms. Te uwagi są **zgłoszonymi usterkami**, nie „do zrobienia w przyszłości”.

**GitHub Actions:** ostatnie #268–272 na commitach do `f701661` były zielone. Wcześniej #263/#265–267 zawiodły, późniejsze poprawki usunęły wykryte błędy kompilacji. Sukces CI **nie oznacza** przetestowanego SQL, MAPI, Windows Hello, instalatora na istniejącej maszynie czy poprawnej ergonomii formularza.

### Skala oceny

- **KOD**: odpowiednie operacje istnieją; brak potwierdzenia pracy w realnym środowisku.
- **CZĘŚCIOWE**: ścieżka lub zachowanie odstaje od końcowego WinForms.
- **USTERKA ZGŁOSZONA**: użytkownik wskazał niedziałającą funkcję.
- **NIEZWERYFIKOWANE**: konieczne testy na rzeczywistej konfiguracji Windows / SQL / Subiekt.
- **100%**: wymaga zgodności zachowania plus przejścia testów scenariuszowych na stanowisku. Żadnego z poniższych rozbudowanych obszarów nie uznano w tym audycie za 100%.

## Macierz parytetu, nie mylić z listą funkcji w kodzie

| Obszar | Stan rzeczywisty | Co istnieje w ReactKL | Co brakuje lub wymaga sprawdzenia |
|---|---|---|---|
| Rozdzielone połączenia | **KOD / NIEZWERYFIKOWANE** | Oddzielne ustawienia Subiekta i bazy `KartyLeczenia`; osobna zakładka kopii; testy połączenia bez zapisania; Windows/SQL oraz DPAPI | Test odczytu i zapisu na dwóch komputerach oraz po zmianie konta Windows; prawa SQL; pełna diagnostyka błędów |
| Pobieranie podmiotów i magazynów Subiekta | **KOD / NIEZWERYFIKOWANE** | Odkrywanie podmiotów, odczyt grup i listy `sl_Magazyn`, wybór magazynu i weryfikacja | Test z instancją rzeczywistego Subiekta i różnymi magazynami |
| Hasło Subiekta po restarcie | **USTERKA ZGŁOSZONA, POPRAWKA W KODZIE** | Zapamiętane hasło DPAPI albo tylko w pamięci procesu; test jednostkowy | Użytkownik potwierdził, że „Zapamiętaj hasło” było wyłączone; test restartu aplikacji |
| Lista dokumentów FS/PA/WZ | **KOD / CZĘŚCIOWE** | Przełączniki FS/PA/WZ/Wszystkie, Alt+1/2/4, F5, szukanie, zakres dat, cache 5 min, limit 500, otwarcie z SQL | Pełny test właściwych wyników, wydajność na Subiekcie i wszystkich filtrów, zakresów, zachowania Enter; porównanie pola rodzaju dokumentu i zbiorczych |
| Liczba wyświetlanych dokumentów | **USTERKA ZGŁOSZONA, POPRAWKA W KODZIE** | Commit `2f00fe4` — 25/50/100/Wszystkie pobrane, ustawienie lokalne | Test na Windows; „Wszystkie pobrane” obejmuje tylko pobrany limit do 500, nie całe historyczne archiwum Subiekta |
| Parametry tabel | **CZĘŚCIOWE — potwierdzony brak** | Lokalne ustawienia widoczności, kolejności i szerokości dla dokumentów/cennika/zapisanych kart; w terminach osobny panel | WinForms `ListParameters.cs`: format nagłówków i danych, czcionka, rozmiar, wyrównanie, kolor, linie siatki, zaznaczanie wielu kolumn; części brak w React. Przeciąganie kolumn bezpośrednio w tabeli też nie jest pełnym odpowiednikiem |
| Nowa karta leczenia | **KOD / NIEZWERYFIKOWANE** | Dokument Subiekta, wybór/edycja leków, odrębne daty, rozpoznanie, liczba i opis zwierząt, karta z numerem `K/n/rok`, podgląd i druk | Test od dokumentu aż do SQL i PDF na rzeczywistych lekach; walidacja wszystkich pól i ostrzeżeń |
| Gatunki, warianty ChPL | **KOD / CZĘŚCIOWE** | Bydło/świnie/drób/pies/kot, sugestie ChPL, oznaczanie niezgodności, wybór wariantu | Scenariusze z błędnymi/niekompletnymi ChPL, cielętami, drogami podania; nie potwierdzono pełnej zgodności zachowań |
| Pamięć wyborów i zamiennik | **KOD / NIEZWERYFIKOWANE** | `TreatmentChoiceService`, `AnimalDescriptionService`, edycja zamiennika | Test odczytu po restarcie i na dwóch stanowiskach; unikanie pomylenia produktów o tej samej nazwie |
| Zapis/edycja kart | **KOD / NIEZWERYFIKOWANE** | SQL archiwum, numeracja, edycja istniejących, ochrona rewizji, rozstrzyganie duplikatów | Test równoczesnej edycji, odczytu dawnych zapisów i poprawnych dat; brak testów na prawdziwej bazie |
| Kosz i przywracanie | **KOD / NIEZWERYFIKOWANE** | Przeniesienie do kosza, przywracanie, SQL | Test stanów kart po kilku stanowiskach i indeksów/bazy |
| Trwałe usuwanie z kosza | **USTERKA ZGŁOSZONA** | Potwierdzenie numeru, Windows Hello, kontrola SQL `sysadmin`; commit `f701661` doprecyzowuje błędy SQL | Zrzut użytkownika: błąd połączenia z SQL w oknie trwałego usuwania. Nie ma potwierdzenia sukcesu. Nie usuwać kontroli uprawnień i Hello; zdiagnozować połączenie bazy na stanowisku |
| Nowa kartoteka leku | **USTERKA ZGŁOSZONA + CZĘŚCIOWE** | W React `startNewProduct`, ręczny symbol/nazwa, warianty i `saveCatalogueProduct`; w C# jest INSERT/UPDATE SQL | **Krytyczny brak parytetu**: WinForms prowadził operatora przez `CatalogueProductPicker` — wyszukiwanie i wybór kartoteki **z Subiekta** — a następnie `ReadCatalogueProduct` i edytor. React nie ma tego samego trybu wyboru Subiekta dla ogólnego „Dodaj/edytuj”. Użytkownik potwierdził, że dodawanie nie działa. Przetestować: produkt istniejący w SQL, produkt nieistniejący w SQL, 1+ wariantów, PDF, kolizja symbolu, zapis/reload |
| Lista leków do uzupełnienia | **KOD / CZĘŚCIOWE** | Skan grupy leków Subiekta na polecenie, lista braków, edycja kartoteki | Uwaga: React `PriceCatalogueSnapshot.NeedsCompletion` nie odwzorowuje wprost WinForms `Catalogue.NeedsCompletion` wykluczającego `NOT_MEDICINE`; możliwe fałszywe braki. Test ilości i szybkości zapytania |
| Edycja wariantów i PDF ChPL | **KOD / NIEZWERYFIKOWANE** | Edycja pól, wariant, podpięcie PDF, sugestie z PDF, odczyt binarny SQL | Test ważnych gatunków, karencji, źródeł, powiązań PDF, dopisywania bez utraty innych; urzędowe PDF i stary import |
| Cennik leków | **KOD / CZĘŚCIOWE** | Poziomy cen z Subiekta, netto/brutto, formaty karencji, cena i legenda | Test rzeczywistych kwot, danych stanów i prawidłowości karencji; nie pełne parametry tabeli WinForms |
| Wydruk cennika | **KOD / NIEZWERYFIKOWANE** | Rozbudowane ustawienia A4, PDF podgląd HTML w WebView, legenda, wielostronicowość | Fizyczne wydruki: powtórzenie nagłówka i legendy, granice stron, czcionki, zgodność z WinForms |
| Terminy ważności | **KOD / CZĘŚCIOWE** | Zapytania z Subiekta, filtry po terminie, sortowanie, ustawienia kolumn | Test magazynów, serii, scalania/rozwijania w zachowaniu końcowego WinForms, odświeżania i danych |
| PDF karty | **KOD / NIEZWERYFIKOWANE** | WebView2 PrintToPdf, plik z numerem dokumentu | Test z rzeczywistym wydrukiem i wysyłką, nazwa pliku, poprawność układu / pieczątek |
| E-mail karty | **KOD / NIEZWERYFIKOWANE** | Kompozycja MAPI z PDF, pobieranie adresu płatnika | Test domyślnego klienta pocztowego Windows, zgodności bazy źródłowej i braku adresu |
| Ustawienia lecznicy/pieczątki | **KOD / NIEZWERYFIKOWANE** | SQL, dane lekarza/lecznicy, PNG pieczątek | Test pierwszego uruchomienia, dwóch stanowisk i wydruku |
| Archiwizacja SQL ręczna | **KOD / NIEZWERYFIKOWANE** | Utworzenie .bak, opcje lokalizacji/pendrive, weryfikacja, Windows Hello | Uruchomienie na serwerze, kopia na pendrive, obsługa odmowy i błędów uprawnień |
| Pytanie o archiwizację przy zamykaniu | **USTERKA ZGŁOSZONA, POPRAWKA W KODZIE** | Commit `e097f0e`: Tak / Nie / Anuluj, backup przed zamknięciem | Test anulowania Hello, braku folderu kopii, błędu SQL i dwukrotnego kliknięcia Zamknij |
| Przywracanie SQL | **KOD / NIEZWERYFIKOWANE** | Restore .bak, kontrola docelowej bazy, kopia zabezpieczająca, Windows Hello | Test na kopii bazy (nigdy bezpośrednio na jedynej bazie produkcyjnej), awarii i odtworzenia |
| Instalacja serwer/zdalna | **KOD / NIEZWERYFIKOWANE** | Wybór roli, pamięć `archive-installation.ini`, instalacja na istniejącej instancji lub KLARCHIVE | Test ponownej instalacji WinForms/ReactKL, zmiany roli, zapamiętania serwera; zgodność z istniejącym Inno AppId |
| Aktualizacje przez SQL | **KOD / NIEZWERYFIKOWANE** | Publikacja instalatora w `dbo.program_update`, pobieranie i weryfikacja SHA-256 | Test end-to-end na serwerze i stanowisku zdalnym, brak uszkodzenia istniejących danych; wersja ręczna |
| Okna, skalowanie, skróty | **CZĘŚCIOWE** | Położenie i rozmiar głównego okna i dialogów, skala 80–200%, kilka skrótów | Test DPI/monitorów/przywracania; nie kompletne zachowanie tabel i wszystkich okien |

## Najważniejsze wykryte rozbieżności w kodzie

1. **Dodawanie nowych leków**: WinForms `MainForm.cs` -> `CatalogueProductPicker` -> `ReadCatalogueProduct(MedicinePrice)` -> `CatalogueEditor` -> `SaveCatalogueProduct`. React `CataloguePage.tsx` -> `startNewProduct` (wolny symbol/nazwa) -> `saveCatalogueProduct`. React ma osobny `missingCatalogueProducts`, ale ten nie zastępuje normalnego wyboru dowolnej kartoteki Subiekta. **Nie oznaczać jako przeniesione.**
2. **Oznaczanie produktów nieleczniczych**: WinForms `Catalogue.NeedsCompletion` wyklucza `NOT_MEDICINE`. React `PriceCatalogueSnapshot.NeedsCompletion` opiera się na wariantach; przy porównaniu pełnej grupy może raportować produkt nieleczniczy jako brak. Sprawdzić na bazie.
3. **Parametry list**: WinForms `ListParameters.cs` oferuje formatowanie (nagłówki/dane, kolor, wyrównanie, czcionka, linie). `ListColumnLayout.tsx` zarządza tylko widocznością, kolejnością i szerokością.
4. **Zakres „Wszystkie”**: występują dwa niezależne znaczenia — `Wszystkie` typy dokumentów FS/PA/WZ oraz `Wszystkie pobrane` pozycje na stronie. Opcja rozmiaru nie znosi limitu SQL 500; nie utożsamiać jej ze wszystkimi dokumentami w historii Subiekta.
5. **Trwałe usuwanie**: ekran z błędem SQL zgłoszony przez użytkownika. Samo doprecyzowanie komunikatu o błędzie nie dowodzi naprawy połączenia/uprawnień.
6. **Zamykanie**: zachowanie Tak/Nie/Anuluj dodano w React dopiero `e097f0e`. Bez realnej próby backupu i Windows Hello nie deklarować pełnego parytetu.
7. **Aktualizacje/instalator**: wspólny AppId zachowuje możliwość aktualizacji na miejscu, ale także utrudnia przełączanie na starszy instalator WinForms. Wersję trzeba sprawdzić przed próbą downgrade’u. Nie zmieniać AppId/ścieżek/baz bez zgody.

## Priorytety następnego etapu

**P0 — naprawić zgłoszone usterki przed dodawaniem kolejnych funkcji**
1. Odtworzyć w React pełny przepływ **wyboru kartoteki Subiekta → otwarcie/utworzenie kartoteki SQL → warianty → ChPL → zapis**. Testy co najmniej: nowy lek z Subiekta, lek istniejący, wariant, PDF, odczyt po restarcie, brak nadpisywania.
2. Przetestować i naprawić **trwałe usuwanie w koszu** na konfiguracji bazy KartyLeczenia (test połączenia, operator SQL, prawa sysadmin, Windows Hello), bez wyłączania zabezpieczeń.
3. Przetestować wybór typów i zakresów dokumentów oraz **25/50/100/Wszystkie pobrane**; rozdzielić błąd UI od braku danych/kontroli SQL.
4. Przetestować zamknięcie Tak/Nie/Anuluj, backup na pendrive, anulowanie Hello i błąd SQL.
5. Zrobić scenariusz instalacji serwer/zdalna i ponownego instalowania programu z zachowaniem roli.

**P1 — brakujące elementy funkcjonalne**
6. Pełne parametry tabel WinForms oraz ich pamięć lokalna (formatowanie, nagłówki, komórki, skróty).
7. Porównać klasyfikację produktów nieleczniczych i braki katalogu z WinForms.
8. Parytet edycji leku, wariantów, karencji, PDF ChPL i widoczności nieleczniczych.
9. Test scenariuszowy: FS/PA/WZ → karta → PDF/e-mail → SQL → edycja → druk → kosz.
10. Test wydajności SQL i komunikacji na dwóch stanowiskach.

**P2 — testy wydania**
11. Ujednolicić zachowanie dialogów, DPI i przechowywanie ustawień.
12. PDF cennika i karty: test wizualny wielu stron, pieczątek i karencji.
13. Pełny scenariusz backup/restore oraz aktualizacja z serwera SQL do klienta.

## Proponowana macierz akceptacji (niezbędna do oceny 100%)

Dla każdej funkcji zebrać: lokalizację WinForms (plik i metoda), lokalizację React+C# (plik i akcja), czynności testowe, oczekiwany wynik, wynik **PASS/FAIL**, log/czas SQL, potwierdzenie na serwerze i na stanowisku zdalnym, numer commita. Bez PASS na rzeczywistym stanowisku stan pozostaje **KOD / NIEZWERYFIKOWANE**.

## Stan prac i ograniczenia

- Wykonano **audyt**, nie naprawę nowych leków i nie test produkcyjny SQL. Nie modyfikowano danych katalogu ani bazy użytkownika.
- **Źródła**: końcowy WinForms `MainForm.cs`, `CatalogueProductPicker.cs`, `CatalogueEditor.cs`, `SharedArchive.Catalogue.cs`, `Catalogue.cs`, `ListParameters.cs`, `PricePrintEditor.cs`, `ExpiryTable.cs`; ReactKL `CataloguePage.tsx`, `App.tsx`, `DatabaseSettings.tsx`, `ListColumnLayout.tsx`, `PricePrint.tsx`, `ReactWindow.cs`, `CatalogueService.cs`, `PriceCatalogueService.cs`, backup/restore/update services; workflow `reactkl-checks.yml`.
- Nie dodawać nowych funkcji bez zgody. Nie porównywać motywów WinForms.
