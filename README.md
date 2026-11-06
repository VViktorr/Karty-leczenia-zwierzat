# Karty leczenia zwierząt
Aplikacja dla Windows 11 współpracująca z Subiektem GT przez SQL Server.

## Pierwsza wersja
- Wyszukanie dokumentu po pełnym numerze, bez łączenia ilości FS i WZ.
- Adresy historyczne płatnika i odbiorcy; wybór osoby do karty.
- Wybór pozycji, podział według ruchów magazynowych i dostaw.
- Karencja z opisu albo tw_Pole1–tw_Pole8.
- Edycja wszystkich pól przed wydrukiem.
- Zapis karty do JSON i otwieranie zapisanej karty.
- Eksport HTML A4 poziomo: wydruk lub PDF przez przeglądarkę.

## Uruchomienie
Zainstaluj .NET 8 SDK na Windows i uruchom w PowerShell:
```powershell
dotnet run --project src/KartyLeczenia
```
Wydanie samodzielne:
```powershell
dotnet publish src/KartyLeczenia -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Połączenie
Podaj serwer (np. SERWER\\INSERTGT), bazę firmy i konto SQL mające wyłącznie SELECT do potrzebnych tabel. Można użyć uwierzytelniania Windows. Program wykonuje jedynie SELECT, ale uprawnienia serwera są właściwym zabezpieczeniem przed zapisem. ApplicationIntent=ReadOnly nie zastępuje uprawnień.
Domyślnie hasło pozostaje wyłącznie w pamięci procesu. Opcja zapamiętania zapisuje je zaszyfrowane przez Windows DPAPI dla bieżącego konta użytkownika. Ustawienia lecznicy zapisuj wraz z kartą lub wprowadzaj przy kolejnej sesji; trwałe profile ustawień są jeszcze do dodania.
Domyślnie wymagany jest zaufany certyfikat SQL. Opcję zaufania certyfikatowi włączaj tylko dla znanego serwera w swojej sieci.

## Wydruk
Wybierz produkty, uzupełnij dane i użyj „Podgląd / druk”. Przeglądarka otworzy lokalny HTML; Ctrl+P pozwala drukować lub zapisać PDF. Wybierz A4 poziomo i wyłącz nagłówki/stopki przeglądarki.
Układ odwzorowuje sekcje przesłanego wzoru; wymaga jeszcze porównania wydruku na rzeczywistej drukarce.

## Zakres weryfikacji
Powiązania SQL sprawdzono na eksportach użytkownika z Subiekta GT 1.89 SP1 HF1. Kompilacja Release przechodzi bez błędów i ostrzeżeń; 9 kontroli parsera przechodzi. Wydanie Alpha 1 przeszło próbę uruchomienia na Windows; pełna kontrola wyglądu nadal jest potrzebna. Nie sprawdzono jeszcze połączenia aplikacji z rzeczywistym serwerem SQL. Pobieranie dokumentów RPL w środowisku deweloperskim było blokowane przez serwer; wymaga sprawdzenia na stanowisku użytkownika.
Ruchy bez dostawy źródłowej oraz dokumenty z korektami lub innym sposobem realizacji wymagają osobnej weryfikacji. Brak danych nie jest uzupełniany przez zgadywanie.
Ilość sprzedaży nie jest dawką. Pole dawkowania pozostaje do ręcznego uzupełnienia.
Numer karty wpisuje użytkownik. Automatyczna wspólna numeracja, centralna baza kart, instalator i profile lekarzy pozostają do wdrożenia. Plik JSON można zapisać do wybranego folderu sieciowego, ale nie zastępuje on centralnej bazy wielostanowiskowej.
Nie dołączaj do repozytorium danych klientów, eksportów ani haseł.

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

## Kontrole parsera
```powershell
dotnet run --project tests/RegistryChecks
```
Sprawdzane są stare i nowe nagłówki, łamanie linii w PDF, zachowanie wszystkich dróg podania i ograniczeń, odrzucenie brakującej lub niepełnej sekcji oraz rozdzielenie mapowań baz.

## Łatwiejsze pierwsze uruchomienie
1. W zakładce połączenia wpisz serwer SQL (np. KOMPUTER\\INSERTGT lub 192.168.1.10,1433).
2. Wybierz uwierzytelnianie Windows albo wpisz login i hasło SQL. To konto SQL, nie operator Subiekta.
3. Po zakończeniu wpisywania serwera program próbuje odczytać listę dostępnych podmiotów. Możesz też użyć **Odśwież podmioty**.
4. Wybierz firmę z listy. Przy ograniczonej widoczności baz wpisz jej nazwę ręcznie — nie jest potrzebne konto administratora do samej pracy.
5. Kliknij **Test połączenia**. Sprawdzane są odczyty wymaganych tabel, uprawnienia zapisu do tabel / wybrane uprawnienia zmiany struktury i grupa leków.
6. Wybierz grupę **Leki**, jeśli występuje pod inną nazwą. Po zmianie ponów test.
7. Kliknij **Zapisz ustawienia** albo **Zapisz i przejdź do kart**.

Ustawienia trafiają do LocalAppData/KartyLeczenia/connection.json. Nie ma automatycznego zapisu hasła: zaznacz to osobno. Wyłączenie zapamiętania i ponowny zapis usuwa zaszyfrowaną wartość. Hasło zapamiętane przez DPAPI nie jest przenośne między kontami Windows. Uszkodzony plik ustawień wyświetla komunikat i pozwala skonfigurować połączenie ponownie.

Po restarcie wczytywany jest zapisany podmiot. Jeśli dostępne jest uwierzytelnianie Windows albo zapamiętane hasło, program automatycznie testuje połączenie. Zmiana konfiguracji unieważnia test; nie można zapisać nieprzetestowanego połączenia ani użyć go do pobrania dokumentu.

Lista firm jest budowana wyłącznie z widocznych, dostępnych baz o strukturze GT; nie są nadawane żadne uprawnienia. Nie wszystkie konta mają prawo wyświetlać listę baz. Brak nazwy firmy w danych licencyjnych nie blokuje wybrania bazy po jej nazwie.

Z faktur, paragonów i WZ pobierane są wyłącznie towary należące do skonfigurowanej grupy leków. Brak grupy nie powoduje pobrania wszystkich towarów. Dostęp do Subiekta nadal ogranicza się do SELECT. Test nie nadaje ani nie odbiera uprawnień.

Ekran w bieżącym wydaniu pozostaje Windows Forms. Logika konfiguracji została oddzielona od interfejsu w ConnectionSetup.cs, aby można było wykorzystać ją przy uzgodnionej migracji do MAUI dla Windows.

Kontrole konfiguracji (na Windows):
```powershell
dotnet run --project tests/ConnectionChecks -c Release
```
Nie zastępują sprawdzenia połączenia z rzeczywistym serwerem użytkownika.
