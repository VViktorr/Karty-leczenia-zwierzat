# ReactKL — zgodność funkcjonalna z WinForms (audyt 19.11.2026)

## Zakres i punkt odniesienia

- Punkt odniesienia: `winforms-backup-2026-11-19`, HEAD `592d17c24`.
- Nowy program: `ReactKL`, źródła `src/KartyLeczenia.ReactKL/` i `ui/react/src/`.
- Przejrzano **całą listę 1077 commitów** historii gałęzi WinForms (05–19.11.2026) oraz kod ostatniej wersji interfejsu, archiwum, usług i instalatora. To przegląd historii commitów oraz szczegółowy audyt *końcowego stanu kodu*, a nie indywidualny audyt każdej z 1077 zmian w diffach.
- Wszystkie 13 plików `src/KartyLeczenia.Core/` w obu gałęziach mają identyczne hashe Git. Wiele kluczowych zachowań było jednak w `MainForm.cs`, `SharedArchive.*.cs`, `PricePrintEditor.cs`, `WindowLayout.cs` i instalatorze, a nie w Core.
- **Cel:** zmienia się tylko technologia prezentacji: React + WebView2. Źródła danych, kolejność czynności, bezpieczeństwo i wydruki mają zachować działanie końcowej wersji WinForms. Nie kopiować AntdUI/WinForms ani nie przywracać historycznych błędów.

## Decyzje ostateczne i reguły niepodlegające uproszczeniu

1. **Subiekt GT** jest źródłem FS/PA/WZ, kontrahentów, cen, magazynu, partii i dat ważności. Jeden wybrany magazyn dla dokumentów, cennika i terminów.
2. **Osobna baza SQL Server `KartyLeczenia`** przechowuje wspólne zapisane karty, dane lecznicy i lekarzy, katalog leków, warianty, karencje, powiązane PDF ChPL oraz aktualizacje. Nie tworzyć drugiej, lokalnej aktywnej bazy SQLite. SQLite jest jedynie źródłem pierwszego importu.
3. Dla farmakologii zasadnicze są **aktualne, urzędowe polskie ChPL z RPL/UPD/EMA**. Angielskie pomocniczo. PDF jest oryginalny, nie zastępuje go streszczenie; niezgodne/niepełne propozycje nie mogą wprowadzać potajemnych danych klinicznych.
4. ReactKL **zastępuje instalację WinForms** w `C:\Program Files\KartyLeczenia`, zachowując konfigurację i dane; numer wersji użytkownik podaje ręcznie. Instalator na serwerze trafia do `dbo.program_update`; stanowiska sprawdzają aktualizację przy starcie.
5. Kopia archiwum SQL: jeden **pełny** plik `.bak` (nie historyczny łańcuch różnicowy), wybór pendrive/folderu, kontrola integralności i sumy SHA-256, Windows Hello, uprawnienia SQL; przywracanie wymaga weryfikacji i kopii stanu poprzedniego.
6. WinForms jest **referencją zachowania**, ale wygląd, motywy, rozmieszczenie i technologię kontrolek budować od początku w React. Rozdzielić zmianę układu od funkcji.
7. Starsze commity zawierają pomysły później cofnięte lub zastąpione (MAUI, lokalne SQLite, różnicowe archiwa, SMB jako jedyne źródło aktualizacji, AntdUI). Ostateczne decyzje mają pierwszeństwo nad pośrednimi wersjami.

## Macierz zachowań — audyt ostatniego WinForms versus ReactKL

| Obszar | Zachowanie końcowego WinForms | Stan ReactKL z audytu | Dalsze wymaganie |
|---|---|---|---|
| Instalacja serwera SQL | Wybór istniejącej instancji lub KLARCHIVE, konfiguracja bazy `KartyLeczenia`, inicjalny import katalogu/PDF i bezpieczne przyrostowe manifesty zmian | **Brak parytetu, P0**: aktualny Inno Setup ReactKL instaluje aplikację i WebView2, lecz nie wywołuje `Configure-SharedArchive.ps1`, `Setup-SharedArchive.sql` ani importera | Przenieść wyłącznie mechanizm provisioning/importu, z ochroną istniejących danych; stacja zdalna nie importuje |
| Dane lecznicy i lekarza | Wspólna konfiguracja w `dbo.clinic_settings`, nazwa, adres, lekarz, telefon, podpisy/pieczątki PNG; synchronizacja przy pierwszym zalogowaniu | **Niepełne, P0**: React `getClinic/saveClinic` używa tylko lokalnego `ClinicSettingsStore`, nie wspólnej tabeli SQL; nie udostępnia pieczątek | Wczytywać/zapisywać wspólne ustawienia i pieczątki, z polityką pierwszego logowania; wydruki muszą korzystać z tych samych danych |
| Daty i numer karty | Osobna data/godzina zgłoszenia (opcjonalna) i wykonania, rok numeracji oraz `treatment_date` wynikają z daty wykonania; format `K/numer/rok` | **Błąd parytetu, P0**: `CardArchiveService.Save` używa `reported.Year` i `reported.Date` dla numeracji i daty archiwum; zgłoszenie nie jest opcjonalne | Numerację, datę listy i kalendarzową datę leczenia oprzeć na `performed`, zgłoszenie obsługiwać niezależnie |
| Kontrola przed zapisem karty | Okno z brakami serii, liczby zwierząt, karencji i wariantu ChPL; wybór „Przejdź do pozycji”, anulowanie lub świadoma kontynuacja | **Brak parytetu, P0**: React ma walidację wymaganych pól w backendzie, ale nie tę samą zbiorczą listę ostrzeżeń z nawigacją | Dodać przegląd i ostrzeżenia przed każdą operacją zapisu/drukowania |
| Zduplikowane i ponownie otwarte karty | „Otwórz zapisaną”, „Zaktualizuj kartę” albo „Zapisz kolejną”; przy edycji istniejącej zachowanie numeru | **Częściowo, P1**: React wykrywa duplikat i pozwala zapisać kolejną, nie ma ścieżki ponownego otwarcia/aktualizacji zapisanej karty | Przywrócić wszystkie trzy wybory, bez zmiany istniejącej numeracji |
| Dokumenty Subiekta | FS/PA/WZ, ostatnie dokumenty przy pustym wyszukiwaniu, 10 okresów (w tym kwartał/rok/własny), Alt+1/2/4, Enter, F5, bufor 5 min, wstępne ładowanie grup, odświeżenie i licznik, 500 pozycji | **Częściowo, P1**: React FS/PA/WZ + „Wszystkie”, F5, dwuklik, 4 okresy, 200 pozycji; brak skrótów Alt, cache 5 min, własnych zakresów i Enter | Zachować całą logikę filtrowania i szybkość, nie tylko podstawowe wyszukiwanie |
| Tabele i ergonomia | Kolumny przesuwane/ukrywane, pamięć szerokości i kolejności, `Ctrl+Shift+L` / menu „Kolumny”, oddzielne formaty nagłówków i treści; trwałość rozmiarów okien | **Brak parytetu, P1/P2**: React Ant Design ma sortowanie w części tabel, ale bez odpowiedniego panelu personalizacji i zapamiętywania wszystkich ustawień | Odtworzyć operacje klawiaturowe, pamięć list i okien, skalowanie 80–200% |
| Filtrowanie karty gatunkiem | Wszystkie / bydło domyślnie / świnie / drób / pies / kot; cielęta w bydle; niezgodne leki na końcu, odznaczone | **Brak parytetu, P1**: React ma wybór wariantu po symbolu i propozycje, ale nie filtr grupy o końcowym zachowaniu WinForms | Wprowadzić filtr, zachować odznaczenia i kolejność |
| Wariant, zamiennik i pamięć | Gatunek i droga z ChPL, wyboru wariantu ręcznie; „Zamiennik produktu” wskazuje inny lek; „Zapamiętuj wybory” zapisuje gatunek/drogę do `dbo.treatment_choices` | **Częściowo, P1**: React dopasowuje symbol i wybiera wariant, ale brak zamiennika i pamięci użytkownika między kartami | Przenieść referencje zamienników i preferencje do istniejących tabel SQL |
| Opisy zwierząt | Wspólne numery zwierząt oraz liczba leczonych dla wybranych pozycji tego samego gatunku i sekcji; zapamiętywanie opisów kontrahenta w SQL | **Brak parytetu, P1**: React edytuje opis i liczbę w pojedynczych komórkach, bez pracy grupowej i pamięci opisów | Dodać grupowanie oraz `dbo.animal_descriptions` |
| Karta i PDF/e-mail | Wydruk A4 poziomo, podział II/III, poprawny numer, podgląd po zapisie, PDF i klient pocztowy z adresem **płatnika Subiekta** oraz bezpieczną nazwą pliku opartą o numer dokumentu | **Częściowo, P1**: wspólny `CardHtmlRenderer`, podgląd HTML i drukowanie; brak pełnego eksportu PDF, maila i automatycznego adresu płatnika | Przywrócić ścieżki eksport/druk/mail, bez odtwarzania okien WinForms |
| Zapisane karty | Wyszukiwanie klienta, dokumentu i numeru karty; opcjonalna data leczenia; otwieranie, ponowny druk/PDF/e-mail, eksport JSON, kosz/przywracanie | **Częściowo, P1**: React ma wyszukiwanie, podgląd, drukowanie, kosz i trwałe usuwanie; brak daty filtra, JSON, maila, aktualizacji karty i niezależnego indeksowania numeru | Przenieść pozostałe funkcje i zachować spójny status SQL |
| Cennik leków | Cztery nazwane ceny, detaliczna brutto domyślnie; wybór netto/brutto; dwa przełączniki: pełna/skrócona karencja i pełna/skrócona droga podania, dane `catalog_price_withdrawal`; oznaczenie braków w katalogu | **Brak dużej części, P0/P1**: React pobiera `MedicineStockService.Prices`, ale nie nakłada formatowania karencji z katalogu SQL; nie domyślnie wybiera poziomu detalicznego; brak opcji formatów i kontroli braków | Zachować mapowanie z katalogu SQL i oryginalne ustawienia |
| Wydruk cennika | Konfigurowalny druk wielostronicowy, nagłówki, kolumny, jednostki, marginesy, czcionki, tabela/treść/legenda, powtarzanie legendy na stronach | **Brak, P0/P1**: React nie ma ustawień ani wydruku cennika | Przenieść logikę i przygotować edytor/podgląd React, bez AntdUI/WinForms |
| Terminy ważności | Aktualne pozostałości w jednym magazynie; filtry wszystkie, przeterminowane, 30/60 dni; sortowanie najbliższej daty i personalizacja kolumn. Wyszukiwanie leku/serii i filtr dat przyjęcia są wcześniejszymi **wymaganiami użytkownika** (brak potwierdzenia pełnego wdrożenia w końcowym `MainForm`) | **Częściowo, P1**: wspólny `MedicineStockService.Expiries`, filtry 30/60/przeterminowane; brak wyszukiwania, zakresów dat przyjęcia, domyślnego sortowania i ustawień tabeli | Dokończyć zachowanie z WinForms i zgłoszone dodatkowe filtry |
| Katalog SQL i ChPL | Import z oficjalnymi PDF, edycja istniejących/dodanie nowych leków, warianty, skróty karencji, ostrożne propozycje PDF, wskazanie leków do uzupełnienia z Subiekta | **Częściowo, P1**: React edytuje istniejące produkty i warianty, dołącza i analizuje tekstowe PDF; brak dodania całkiem nowej kartoteki oraz listy braków powiązanej z Subiektem | Dodać brakujące przepływy bez zmian w modelu danych |
| Archiwizacja SQL | Pełne `.bak`, pendrive z systemowym wyborem folderu, ustawienia, Windows Hello, SHA-256, możliwość kopii przy wyjściu | **Częściowo, P1**: React ma ręczne backup/restore z zabezpieczeniami, brak automatycznego pytania przy zamykaniu jak w poprzednim programie | Zachować możliwość wyboru, nie niszczyć konfiguracji po aktualizacji; potwierdzić na SQL |
| Aktualizacje | Serwer zapisuje instalator do `dbo.program_update`, stanowiska sprawdzają przy starcie, instalator uruchamiany po zgodzie, ręczna wersja | **W znacznej części przeniesione, wymaga testu P0**: React ma odczyt/pobranie SHA-256 i publikację; brak pełnego automatycznego przygotowania serwera/catalogue | Test end-to-end: nowy serwer, aktualizacja serwera, stanowisko zdalne, offline i odmowa |
| Motywy i skalowanie | WinForms miał własne ustawienia; użytkownik zdecydował nie przenosić antycznych kontrolek | **Nowa implementacja**: React Modern/Classic/Fluent i jasny/ciemny/morski | Layout może zmieniać organizację nawigacji, ale funkcje pozostają te same; zapamiętywać ustawienia |

## Priorytet realizacji

**P0 — przed nadpisaniem produkcyjnej instalacji:**
1. Instalator serwerowy: bezpieczny provisioning SQL, inicjalny import SQLite/PDF, przyrostowe manifesty bez utraty późniejszych edycji.
2. Daty i numeracja kart: rok numeru z daty wykonania, możliwość innej/nieokreślonej daty zgłoszenia.
3. Wspólne dane lecznicy/lekarskie wraz z podpisami i pieczątkami przez SQL.
4. Powrót listy ostrzeżeń przed zapisem/drukiem oraz wymaganych scenariuszy danych klinicznych.
5. Test nowej instalacji, aktualizacji serwera/stanowiska, zgodności katalogu i kart na rzeczywistym Windows/SQL/Subiekt.
6. Przywrócenie wydruku cennika przed oznaczeniem ReactKL za pełny zamiennik WinForms.

**P1 — zgodność codziennego przepływu:**
1. Pełna lista dokumentów, zakresy dat, skróty Alt, cache, Enter, F5 i licznik odświeżenia.
2. Filtr gatunków karty, pamięć wyborów, zamiennik produktu, grupowe numery zwierząt i opis kontrahenta.
3. Karta zapisana: otwórz/aktualizuj/zapisz nową, eksport JSON, PDF i e-mail z adresu płatnika.
4. Cennik: poziom detaliczny brutto, skróty/pełne karencje i drogi podania, lista braków, ustawienia.
5. Terminy: wyszukiwanie nazwy/serii, daty przyjęcia, sortowanie i personalizacja.
6. Pamięć kolumn, okien, skalowania i skrótów w interfejsie React.

**P2 — dopracowanie interfejsu i wydajności:** preloading, optymalizacja wielu stanowisk, personalizowane układy bez regresji funkcjonalnych i testy UI/DPI.

## Reguły testowania

- W CI prowadzić testy spójności **WinForms → ReactKL** na przykładowych danych i wspólnym Core.
- Nie deklarować funkcji jako „gotowej” tylko dlatego, że kompiluje się .NET/React lub przechodzi smoke-test Inno Setup.
- Osobno testować: użyteczność na Windows 11, autentyczne SQL/Subiekt, bezpieczeństwo kliniczne i politykę aktualizacji.
- Zabroniona przypadkowa zmiana `dbo.program_update`, danych katalogu i numeracji wersji bez jawnego celu i testu.
- Nie cofać poprawek tylko dlatego, że wcześniejszy commit miał inny pośredni pomysł.

## Źródła referencyjne w repo

- Stary UI/przepływy: `src/KartyLeczenia.WinForms/MainForm.cs` (`winforms-backup-2026-11-19`).
- Katalog, preferencje i wspólne dane: `SharedArchive.cs`, `SharedArchive.Catalogue.cs`, `Catalogue.cs`.
- Wydruk cennika: `PricePrintEditor.cs`, `PricePrintPreview.cs`.
- Okna i listy: `WindowLayout.cs`, `DocumentsTable.cs`, `ListParameters.cs`, `TreatmentTable.cs`.
- Instalacja server/client: `installer/KartyLeczenia.WinForms.iss` i `installer/Configure-SharedArchive.ps1`.
- React obecnie: `ui/react/src/App.tsx`, `src/KartyLeczenia.ReactKL/ReactWindow.cs`,
  `CardArchiveService.cs`, `CatalogueService.cs`, `SqlBackupService.cs`,
  `ReactUpdateService.cs`, `installer/KartyLeczenia.ReactKL.iss`.
- Przykładowe punkty historii: `8e212453` (wspólna lecznica), `a9be4889` (symbol),
  `7ed39214` (numer i godzina), `bcf34074` (grupy zwierząt),
  `cc54faf8` (podgląd), `130a4f08` (manifesty przy instalacji),
  `5860acd4` (pełny .bak), `caa7c0c7` (aktualizacja SQL).


## Postęp po audycie — 19.11.2026

### P0: daty i rok numeracji kart — poprawiono w kodzie, oczekuje testu integracyjnego

- `CardArchiveService.Save` wyznacza `card_year`, numer `K/numer/rok` i `treatment_date` z daty **wykonania**, a nie zgłoszenia. Wspólny pomocnik `GetCardDate` pozwala sprawdzić zmianę roku bez połączenia SQL.
- Data zgłoszenia jest opcjonalna: puste pole React jest przekazywane jako `null` i zapisywane w polu treści karty jako pusty tekst; wpisana data zachowuje walidację formatu.
- `RenderDraft` oraz `Save` obsługują brak daty zgłoszenia spójnie.
- Dodano dwa testy regresyjne w `tests/ReactKLChecks/Program.cs` obejmujące przełom roku.
- **Nadal wymagane:** kompilacja Windows/React, test rzeczywistego zapisu i odczytu SQL oraz podglądu/wydruku z pustym zgłoszeniem. Nie oznaczać całego P0 za zweryfikowany produkcyjnie.

Pozostałe punkty P0/P1 z tabeli i listy priorytetów pozostają otwarte.

### P0: wspólne dane lecznicy i lekarza — implementacja ReactKL, wymagany test na SQL

- Nowa usługa `SharedClinicService` czyta i zapisuje w istniejącej tabeli `dbo.clinic_settings`: nazwę, adres, telefon, lekarza oraz dwie opcjonalne pieczątki PNG.
- Wcześniejszy `clinic.json` służy wyłącznie jako jednorazowy materiał wejściowy, i **tylko** gdy SQL nie ma żadnych danych lecznicy; przy istniejących danych SQL nie nadpisuje ich zawartością lokalną. Po odczycie SQL lokalny plik jest jedynie kopią zgodności.
- Zapis ręczny sprawdza rewizję całego rekordu; w przypadku edycji z innego stanowiska zostaje odrzucony z komunikatem nakazującym odświeżenie.
- Formularz React oferuje przyciski odświeżenia SQL, wyboru i usuwania PNG oraz włączenia/wyłączenia pieczątek; plik PNG ma limit 2 MB i jest sprawdzany przed wprowadzeniem do SQL.
- Zapis i podgląd kart leczenia odczytują obecnie dane z SQL, a nie tylko lokalnie. Oba renderer-y uwzględniają aktywne pieczątki.
- Testy `tests/ReactKLChecks/Program.cs`: pusta/pełna klinika SQL, zasady importu, zmiana rewizji i niepoprawny plik pieczątki.
- **Nadal wymagane:** CI `.NET 10 + React`, test serwera Windows 11 (uprawnienia SELECT/INSERT/UPDATE do `clinic_settings`), pierwszego importu, dwóch stanowisk, odrzucenia równoczesnej edycji, zgodności podglądu i wydruku z PNG.
- Nie ruszano automatycznych aktualizacji, numeru wersji, Subiekta ani danych katalogu.


### P0: przegląd ostrzeżeń przed kartą — podstawowa obsługa React

- Przed `saveDraft` i `previewDraft` ReactKL wyświetla zbiorczy przegląd wybranych pozycji z brakami: seria, liczba zwierząt, karencja, wskazanie niejednoznacznego/niezweryfikowanego wariantu ChPL.
- Brak wymaganych pól (produkt, gatunek, droga, dawkowanie, rozpoznanie) blokuje kontynuację; pozostałe ostrzeżenia można jawnie potwierdzić.
- Każda pozycja zawiera akcję „Przejdź do pozycji”, kierującą do odpowiedniego wiersza tabeli.
- Kontrola działa przy zapisie i otwieraniu podglądu. To pierwszy etap parytetu: **wymagany test UI i potwierdzenie scenariuszy klinicznych**; pozostałe walidacje backendu nadal pozostają obowiązujące.


### P1/P2: szybki odczyt Subiekta i niskie obciążenie SQL — implementacja 19.11.2026

**Przeniesiono mechanizmy ostatniego WinForms do ReactKL (do testów na Windows/SQL):**

- Lista dokumentów: cache po stronie hosta C# **5 minut / maksymalnie 32 kombinacje**. Klucz uwzględnia serwer, bazę, sposób logowania (również hasło bez przechowywania go w kluczu), magazyn, grupę, kategorię, filtr i zakres dat; wynik z serwera SQL jest pobierany tylko po wygaśnięciu cache albo wymuszonym F5.
- Wróciło **500 pozycji** + wykrywanie uciętej listy na podstawie 501. rekordu, 10 zakresów dat: brak okresu, dzisiaj, bieżący tydzień/miesiąc/kwartał/rok, ostatnie 7/30 dni, wybrany miesiąc i własny okres. Bieżący tydzień zaczyna się w poniedziałek.
- Przywrócono **Alt+1/2/4, Enter na wierszu, dwuklik, F5, czyszczenie pola oraz debounce 300 ms**. Widoczna jest godzina ostatniego odczytu i informacja „z pamięci”.
- Dwa jednoczesne odczyty tej samej listy współdzielą jedno zapytanie; najwyżej **dwa aktywne odczyty list** na okno ReactKL. Wyszukiwania porzucone przez UI są anulowane przez WebView2 → C# → token SQL; brakujące odpowiedzi nie zastępują nowszych.
- **Otwieranie pojedynczego dokumentu i pozycji karty nigdy nie korzysta z cache listy** — zachowuje aktualność serii, ilości i danych klinicznych.
- Cennik i terminy: niezależny, **30-sekundowy cache danych magazynowych** (maksymalnie 16 zestawów) z jednym współdzielonym odczytem `Expiries` przy przełączaniu poziomów cen i netto/brutto. Poziomy cen cache 10 minut. Wymuszone „Odśwież” omija cache.
- Cennik wybiera **cenę detaliczną brutto** przed pierwszym zapytaniem o ceny, unikając wcześniejszego niepotrzebnego pobrania poziomu 1.
- Terminy ważności: lokalne filtry nazwy/serii/magazynu, daty przyjęcia i 0/30/60 dni oraz sortowanie od najbliższych dat; używanie filtrów nie generuje nowych zapytań SQL.
- Po zmianie ustawień połączenia wszystkie zapisane zestawy danych są unieważniane. Hasła i dane osobowe nie trafiają do logów cache.
- Testy bez SQL w `tests/ReactKLChecks/Program.cs`: hit/TTL/F5, różne magazyny i typy, limit cache, deduplikacja równoczesnych zapytań, anulowanie, brak cache błędów, 30-sekundowa ważność magazynu.

**Ważne:** nie deklarować osiągnięcia czasu ładowania poniżej 1 s przed pomiarem na rzeczywistym serwerze. CI i testy z autentycznym Subiektem wymagane osobno. Dla dokumentów aktualność do 5 minut jest widoczna w UI i może być pominięta ręcznym F5; dla magazynu domyślnie maksymalnie 30 sekund. Celowo nie preloadujemy wszystkich kategorii w tle, aby nie obciążać serwera odczytami, których użytkownik nie potrzebuje.

#### Test wydajności na rzeczywistych stanowiskach

1. Otworzyć Dokumenty przy pierwszym uruchomieniu, zmierzyć czas i liczbę zapytań w SQL.
2. Przełączać FS/PA/WZ i wrócić do FS: **drugi dostęp w ciągu 5 minut bez ponownego SELECT**.
3. F5 w FS: **dokładnie jeden nowy odczyt listy**; wyświetlić nową godzinę.
4. Szybko zmieniać wyszukiwane frazy: stare zapytania anulowane, najnowsza lista prawidłowa.
5. Zmienić magazyn/połączenie: nie wolno pokazać starych wpisów z poprzedniego magazynu.
6. W cenniku przełączać netto/brutto i poziomy, następnie otworzyć Terminy; w ciągu 30 s **bez powtarzania kosztownego SELECT dostaw**.
7. Sprawdzić świeżość serii, cen i stanów przy ręcznym odświeżeniu; brak jakiegokolwiek wpływu cache na zapis kart, katalog ChPL i aktualizacje.

