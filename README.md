# Karty leczenia zwierząt — MAUI Windows
Aplikacja dla Windows 11 x64 w C# i .NET MAUI 10. Interfejs Windows korzysta z WinUI 3. Na tym etapie projekt zawiera wyłącznie platformę Windows; nie buduje Androida.

## Uruchomienie gotowej paczki
Pobierz artefakt KartyLeczenia-MAUI-win-x64 z udanego zadania GitHub Actions. Rozpakuj również wewnętrzny ZIP i uruchom KartyLeczenia.exe z całego rozpakowanego folderu. Nie kopiuj samego EXE: obok są wymagane biblioteki .NET i Windows App SDK.

## Budowanie
Wymagany Windows, .NET 10 SDK i workload MAUI Windows:
```powershell
dotnet workload install maui-windows
dotnet publish src/KartyLeczenia -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --self-contained true -o publish
```
Do uruchomienia kontroli biblioteki net8.0 potrzebny jest również runtime .NET 8 (CI instaluje oba SDK).

## Architektura
- src/KartyLeczenia: natywne strony MAUI: połączenie, karta i ekrany UPD i zapasowego RPL; platforma WinUI Windows.
- src/KartyLeczenia.Core: SQL tylko do odczytu, konfiguracja, źródła UPD i RPL, model karty i wydruk HTML, bez zależności od MAUI / Windows Forms.
- Ochrona zapamiętanego hasła używa DPAPI na Windows. Przed dodaniem Androida potrzebny będzie osobny mechanizm poświadczeń i serwer pośredniczący; nie udostępniaj SQL przez internet.

## Zachowane funkcje i dane
- automatyczne wyszukiwanie podmiotów i ręczne wskazanie bazy;
- test odczytu i badanych uprawnień zapisu, zapis konfiguracji, opcjonalne szyfrowanie hasła;
- pobieranie tylko towarów ze wskazanej grupy Leki, rozbicie na faktyczne dostawy;
- wybór płatnika/odbiorcy i historycznych adresów;
- ręczna edycja każdego pola karty, zaznaczanie wydruku, dodawanie i usuwanie pozycji;
- pobieranie karencji UPD i charakterystyki RPL, zatwierdzanie kontekstu karencji i zapisywanie źródła;
- zapis/otwieranie JSON zgodnego ze starszą wersją;
- A4 poziomo: podgląd w aplikacji, oddzielne drukowanie i bezpośredni eksport PDF przez Windows WebView2.

Poprzednie ustawienia są wczytywane z tego samego LocalAppData/KartyLeczenia/connection.json. Powiązania RPL i kopie źródeł pozostają w tym samym katalogu registry. Nie zmienia się tożsamość użytkownika DPAPI ani format kart.

## Ustawienia i dokumenty
W zakładce **Ustawienia** są połączenie SQL oraz nazwa, adres lecznicy/podmiotu i dane lekarza do nowych kart. Dane lecznicy zapisuje się niezależnie od testu SQL; ustawienia połączenia nadal wymagają testu i korzystają z dotychczasowego magazynu hasła. Dane lecznicy są w LocalAppData/KartyLeczenia/clinic.json.

W polu dokumentu wystarczy fragment numeru lub nazwisko/nazwa klienta. Enter pobiera wybrany lub jedyny pasujący dokument; przy kilku wynikach wymaga wyboru. Zaznaczenia Faktury, WZ i Paragony ograniczają wyszukiwanie do wybranych rodzajów (domyślnie wszystkie). Po 4 znakach, z krótkim opóźnieniem, pojawiają się podpowiedzi faktur FS, paragonów PA i WZ. Wybierz dokument z listy. Gdy przy pobraniu pasuje jeden dokument, może zostać wskazany automatycznie; kilka wyników wymaga wyboru. Wyszukiwanie wykonuje wyłącznie parametryzowane SELECT, ogranicza wyniki i anuluje stare żądania. Wyszukiwanie na rzeczywistej bazie Subiekta wymaga sprawdzenia na stanowisku użytkownika.

Jednoznaczny gatunek i droga podania są proponowane z danych UPD lub rozpoznanych sekcji polskiej ChPL RPL. Przy wielu możliwościach lub nieznanym układzie pola pozostają do uzupełnienia. Gatunek uzupełnia także pusty opis zwierzęcia; program nie zgaduje liczby zwierząt, identyfikatora, płci ani numeru stada. Automatyczne propozycje nie zastępują ręcznych wpisów i wymagają kontroli lekarza.

## Podgląd, druk i PDF
Trzy osobne przyciski: **Podgląd**, **Drukuj**, **Eksport do PDF**. Podgląd działa w aplikacji bez okna zapisu HTML. Drukowanie otwiera okno drukowania WebView2. Eksport pyta o plik PDF i zapisuje bezpośrednio przez PrintToPdfAsync; wymaga dostępnego środowiska WebView2 na Windows. Te funkcje korzystają z tego samego formularza A4 poziomo, odtworzonego według przesłanego skanu: sekcje I-IV, dziewięć kolumn, dwupoziomowy nagłówek, oświadczenie i podpisy. Puste wiersze zachowują strukturę formularza; dłuższe dane mogą wymagać kolejnych stron i nie są celowo obcinane. Przy wydruku wybierz A4 poziomo i wyłącz nagłówki/stopki przeglądarki.

Sprawdzono render próby układu formularza poza aplikacją. Natywny interfejs, nowe wywołania WebView2 i nowe kontrole regresji wymagają budowania/weryfikacji na Windows; nie uruchamiano ich przy tej zmianie na życzenie użytkownika.

## Edycja karty
Po lewej wybierz lek/dostawę; po prawej popraw jego pełne dane. Pole wyboru na liście określa udział w wydruku. Dane właściciela, lekarza, lecznicy i nagłówka są nad listą. Informacje o źródle karencji są w edytorze i zapisanej karcie, bez technicznych identyfikatorów na wydruku.

## Weryfikacja i ograniczenia
CI sprawdza bibliotekę danych, zgodność JSON/HTML, zapis i ochronę hasła, publikuje pakiet Windows i wymaga utworzenia natywnego okna MAUI. To nie zastępuje pełnego przeglądu wyglądu ani sprawdzenia z rzeczywistym SQL / RPL na stanowisku użytkownika.
Wspólna baza kart, automatyczna numeracja i profile lekarzy pozostają do wdrożenia. Wydanie Alpha 1 pozostaje historycznym wydaniem Windows Forms. Ten projekt nie korzysta już z Windows Forms.

## Karencja: RPL jako źródło domyślne
Program najpierw wykorzystuje potwierdzone powiązania towar–ID RPL, zapisane osobno dla serwera i podmiotu. Dla nowych produktów próbuje jednoznacznie dopasować nazwę i moc z dostępnego katalogu, pobiera aktualną oficjalną ChPL i sprawdza identyfikację produktu w PDF. Propozycje nadal wymagają weryfikacji leczenia przed wydrukiem. Nie nadpisuje ręcznie wpisanej karencji.

Wyszukiwanie RPL pokazuje podpowiedzi po krótkiej przerwie w pisaniu, obsługuje fragment nazwy oraz zapis Subiekta ze sklejoną mocą (np. FLUNIVIS50MG/ML). Rozmiar opakowania nie musi być zgodny z nazwą rejestrową. Po Enterze lub przycisku wyszukiwania jednoznaczna propozycja jest pobierana; kilka wariantów lub niezgodna moc wymagają wyboru. Podobieństwo pisowni nie zastępuje identyfikacji leku.

Gatunek nowych pozycji to domyślnie **Bydło**, wpisywane również w pusty opis zwierzęcia. Jest to ustawienie programu, nie informacja o wskazaniach leku. Jednoznaczny gatunek odczytany z charakterystyki zastępuje niezmienioną wartość domyślną. Ręcznie wybrany gatunek pozostaje zachowany. Liczba zwierząt, masa i numery identyfikacyjne wymagają uzupełnienia. Przy kilku drogach podania program nie wybiera jednej za lekarza.

Katalog i dokumenty RPL mogą być niedostępne. W takim przypadku użyj zapamiętanego powiązania lub oficjalnego linku / ID. UPD pozostaje jawnie wybieranym źródłem zapasowym. Brak danych nie oznacza zera dni i nie powoduje automatycznego użycia opisu Subiekta. Kopia RPL wymaga jawnego potwierdzenia daty. Przy stosowaniu poza ChPL karencję ustala lekarz.

## Kontrole
```powershell
dotnet run --project tests/RegistryChecks -c Release
dotnet run --project tests/ConnectionChecks -c Release
```
Drugi zestaw wymaga Windows do sprawdzenia DPAPI.

## Wybór dokumentu i kontekst leczenia
Zaznaczenia Faktury/WZ/Paragony znajdują się nad wyszukiwaniem. Enter i przycisk wyszukiwania otwierają osobne okno wyboru dokumentu. Domyślnie jest wybrany bieżący rok (lub rok wpisany na końcu pełnego numeru), można wskazać inny rok, wszystkie lata lub zakres dat. Lista pokazuje do 100 wyników z numerem, datą i danymi klienta. Kliknięcie pobiera dokument. Wpisywanie na głównym ekranie nie uruchamia zapytań SQL. Rozpoznanie kolumny daty wykonuje SELECT metadanych i jest zapamiętywane w procesie; daty, tekst, limity i rodzaje dokumentów są parametrami SELECT. Szybkość zależy od bazy i sieci; nie mierzono jej na bazie użytkownika.

RPL zapisuje pełne sekcje docelowych gatunków, dawkowania/sposobu podania oraz karencji. Rozpoznane nagłówki gatunków i dróg podania pozwalają zaproponować odpowiedni fragment, zachowując zakazy dotyczące mleka i rozpoznane instrukcje wspólne. Przy kilku drogach podania trzeba wybrać jedną. Gdy struktura jest niejednoznaczna, docelowe pola mogą pozostać puste i wymagają sprawdzenia pełnego źródła. Nie jest to uniwersalny interpreter tabel lub wszystkich układów PDF.

Dawkowanie jest kopiowane jako tekst ChPL z częstością, okresem stosowania, jednostkami i ograniczeniami. Program nie oblicza dawki dla konkretnego zwierzęcia bez podanej masy i nie ustala rozpoznania. Zmiana gatunku lub drogi odświeża automatyczne fragmenty; ręcznie zmienione dawkowanie i karencja pozostają zachowane i wymagają ponownej weryfikacji. Zapis JSON zawiera pełne sekcje i kontekst źródła; stare karty pozostają czytelne. Starsza kopia ChPL bez tych sekcji wymaga ponownego pobrania dla automatyzacji dawkowania.

Kompilację i testy nowych zmian pozostawiono użytkownikowi. Dodano regresje dla podziału karencji, zachowania zakazów dotyczących mleka i ręcznych dawek.
