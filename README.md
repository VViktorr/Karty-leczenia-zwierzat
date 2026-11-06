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
- A4 poziomo w przeglądarce, Ctrl+P do wydruku lub PDF.

Poprzednie ustawienia są wczytywane z tego samego LocalAppData/KartyLeczenia/connection.json. Powiązania RPL i kopie źródeł pozostają w tym samym katalogu registry. Nie zmienia się tożsamość użytkownika DPAPI ani format kart.

## Edycja karty
Po lewej wybierz lek/dostawę; po prawej popraw jego pełne dane. Pole wyboru na liście określa udział w wydruku. Dane właściciela, lekarza, lecznicy i nagłówka są nad listą. Informacje o źródle karencji są w edytorze i zapisanej karcie, bez technicznych identyfikatorów na wydruku.

## Weryfikacja i ograniczenia
CI sprawdza bibliotekę danych, zgodność JSON/HTML, zapis i ochronę hasła, publikuje pakiet Windows i wymaga utworzenia natywnego okna MAUI. To nie zastępuje pełnego przeglądu wyglądu ani sprawdzenia z rzeczywistym SQL / RPL na stanowisku użytkownika.
Wspólna baza kart, automatyczna numeracja i profile lekarzy pozostają do wdrożenia. Wydanie Alpha 1 pozostaje historycznym wydaniem Windows Forms. Ten projekt nie korzysta już z Windows Forms.

## Karencja: UPD jako główne źródło
Domyślne źródło to unijna baza UPD. Program odczytuje publiczne strony produktów; nie korzysta z uwierzytelnionego API EMA i nie wymaga jego konta. Adapter strony może wymagać aktualizacji po zmianie układu portalu. Nie jest to oficjalny klient API.

1. Po pobraniu pozycji z Subiekta program próbuje automatycznie pobrać aktualną karencję dla wcześniej zatwierdzonych powiązań UPD.
2. Przy pierwszym użyciu produktu wybierz **Karencja z UPD / RPL…**, wyszukaj lek lub wklej link UPD / 12-cyfrowe ID.
3. Wybierz i sprawdź nazwę, moc oraz postać. Program wymaga Polski w polu kraju pozwolenia i statusu Valid; sama wzmianka o Polsce w procedurze nie wystarcza.
4. Po pobraniu karencja jest uzupełniana automatycznie z zachowaniem gatunku, drogi podania, tkanki/żywności i jednostki. Znane terminy są tłumaczone słownikiem. Pełny oryginał każdego odczytanego wpisu pozostaje obok tłumaczenia; nieznane terminy i zastrzeżenia pozostają w języku źródła.
5. Sprawdź leczenie, ewentualnie popraw tekst i zatwierdź. Powiązanie towar–UPD zostaje zapisane osobno dla serwera i podmiotu. Program nie zgaduje tożsamości leku na podstawie podobnej nazwy.
6. Przy kolejnych kartach dla tego towaru pobierana jest aktualna karencja UPD; automatyczna propozycja nadal wymaga sprawdzenia przed wydrukiem. Ręcznie wpisany tekst nie jest nadpisywany.

RPL jest źródłem zapasowym wybieranym jawnie przyciskiem **Użyj zapasowego źródła RPL…**. Dotychczasowe powiązania RPL i kopie dokumentów pozostają zachowane, lecz nie są automatycznie zamieniane w powiązania UPD. Brak danych, awaria lub nieznana struktura źródła nie oznaczają zera dni i nie powodują cichego użycia opisu Subiekta. Zapis karty zawiera źródłowy URL, datę pobrania, SHA256 i pełny odczytany tekst; data pobrania nie oznacza daty aktualizacji źródła.

Wyszukiwanie korzysta z publicznego formularza UPD, może podlegać ograniczeniom serwisu. W razie braku wyników wklej oficjalny link lub użyj RPL. Test żywego wyszukiwania nie zwrócił poprawnych wyników (a dodatkowa próba serwisu zwróciła 429); wklejanie linku stanowi niezależną ścieżkę. Odczyt stron i parser sprawdzono na strukturze publicznej strony oraz testach lokalnych; zgodność pełnego katalogu wymaga dalszej weryfikacji. Przy stosowaniu poza warunkami ChPL okres ustala lekarz.

### Źródło zapasowe RPL
RPL pobiera oficjalny PDF i odczytuje pełną sekcję karencji, również krótki zapis „Zero dni.”. Ekran dopuszcza ID RPL lub oficjalny link charakterystyki. Przed zastosowaniem sprawdź produkt i zatwierdź kontekst. Kopię dokumentu można wykorzystać wyłącznie po jawnym potwierdzeniu jej daty. Katalog XML RPL może być niedostępny; wtedy pozostaje link lub zapamiętane powiązanie. Nie ma automatycznego powrotu do danych z Subiekta.

## Kontrole
```powershell
dotnet run --project tests/RegistryChecks -c Release
dotnet run --project tests/ConnectionChecks -c Release
```
Drugi zestaw wymaga Windows do sprawdzenia DPAPI.
