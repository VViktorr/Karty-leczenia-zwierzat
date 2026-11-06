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
- src/KartyLeczenia: natywne strony MAUI: połączenie, karta i modalny ekran RPL; platforma WinUI Windows.
- src/KartyLeczenia.Core: SQL tylko do odczytu, konfiguracja, rejestr RPL, model karty i wydruk HTML, bez zależności od MAUI / Windows Forms.
- Ochrona zapamiętanego hasła używa DPAPI na Windows. Przed dodaniem Androida potrzebny będzie osobny mechanizm poświadczeń i serwer pośredniczący; nie udostępniaj SQL przez internet.

## Zachowane funkcje i dane
- automatyczne wyszukiwanie podmiotów i ręczne wskazanie bazy;
- test odczytu i badanych uprawnień zapisu, zapis konfiguracji, opcjonalne szyfrowanie hasła;
- pobieranie tylko towarów ze wskazanej grupy Leki, rozbicie na faktyczne dostawy;
- wybór płatnika/odbiorcy i historycznych adresów;
- ręczna edycja każdego pola karty, zaznaczanie wydruku, dodawanie i usuwanie pozycji;
- pobieranie charakterystyki RPL, zatwierdzanie kontekstu karencji i zapisywanie źródła;
- zapis/otwieranie JSON zgodnego ze starszą wersją;
- A4 poziomo w przeglądarce, Ctrl+P do wydruku lub PDF.

Poprzednie ustawienia są wczytywane z tego samego LocalAppData/KartyLeczenia/connection.json. Powiązania RPL i kopie źródeł pozostają w tym samym katalogu registry. Nie zmienia się tożsamość użytkownika DPAPI ani format kart.

## Edycja karty
Po lewej wybierz lek/dostawę; po prawej popraw jego pełne dane. Pole wyboru na liście określa udział w wydruku. Dane właściciela, lekarza, lecznicy i nagłówka są nad listą. Informacje o źródle RPL są w edytorze i zapisanej karcie, bez technicznych identyfikatorów na wydruku.

## Weryfikacja i ograniczenia
CI sprawdza bibliotekę danych, zgodność JSON/HTML, zapis i ochronę hasła, publikuje pakiet Windows i wymaga utworzenia natywnego okna MAUI. To nie zastępuje pełnego przeglądu wyglądu ani sprawdzenia z rzeczywistym SQL / RPL na stanowisku użytkownika.
Wspólna baza kart, automatyczna numeracja i profile lekarzy pozostają do wdrożenia. Wydanie Alpha 1 pozostaje historycznym wydaniem Windows Forms. Ten projekt nie korzysta już z Windows Forms.

## Karencja z rejestru RPL
Domyślne źródło karencji to teraz RPL. Pobranie dokumentu z Subiekta pozostawia karencję pustą do czasu wskazania produktu i zatwierdzenia danych rejestru. Nie ma automatycznego powrotu do opisu Subiekta.

1. Wybierz wiersz produktu i kliknij **Karencja z rejestru…**.
2. Dla FLORDOFENU 300 mg/ml proponowane jest ID RPL 32709, dla FLUNIVISU 50 mg/ml — 50492. To propozycja wymagająca sprawdzenia nazwy, mocy i postaci w PDF.
3. Dla innych leków użyj **Szukaj leku w RPL** i podaj ID właściwego produktu (identyfikator RPL, nie numer pozwolenia).
4. Kliknij **Pobierz aktualną ChPL**. Program pobiera PDF z oficjalnego adresu RPL i odczytuje pełną sekcję karencji.
5. Wybierz lub wpisz gatunek, drogę podania i rodzaj żywności. Zaznacz odpowiedni fragment dokumentu, skopiuj do karencji i ewentualnie popraw jego brzmienie. Program nie wybiera liczby automatycznie.
6. Potwierdź zgodność z charakterystyką i zastosuj do wybranego wiersza.

Program zapamiętuje powiązanie towar–ID RPL osobno dla serwera i bazy. Nie zapamiętuje wybranej karencji jako uniwersalnej dla następnego zwierzęcia. Zapisana karta zawiera tekst, gatunek, drogę podania, rodzaj żywności, URL, datę pobrania i zatwierdzenia, SHA256 PDF oraz pełny odczytany fragment źródła. Źródła są widoczne w podglądzie; wydruk pokazuje karencję i jej kontekst bez technicznych identyfikatorów.

Kopia odczytanej sekcji i mapowania są zapisywane w LocalAppData/KartyLeczenia/registry. Przy błędzie połączenia użytkownik musi jawnie zaakceptować użycie kopii z podaną datą. Data pobrania nie jest datą aktualizacji charakterystyki. Nieobsługiwany lub zeskanowany PDF wymaga sprawdzenia ręcznego — program nie zgaduje okresu i nie uznaje zakazu stosowania za zero dni.

Ograniczenia: nie jest to pełny lokalny katalog wszystkich leków ani oficjalna integracja UPD API. Wyszukiwanie odbywa się w publicznym RPL, a pobieranie przez udostępniony adres charakterystyki. Nie wymaga kopiowania bazy Kliniki XP. Sekcja jest odczytywana z PDF i zatwierdzana przez użytkownika, bez automatycznej interpretacji klinicznej. Przy stosowaniu poza warunkami ChPL okres ustala lekarz.


## Kontrole
```powershell
dotnet run --project tests/RegistryChecks -c Release
dotnet run --project tests/ConnectionChecks -c Release
```
Drugi zestaw wymaga Windows do sprawdzenia DPAPI.
