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
Hasło pozostaje wyłącznie w pamięci procesu. Ustawienia lecznicy zapisuj wraz z kartą lub wprowadzaj przy kolejnej sesji; trwałe profile ustawień są jeszcze do dodania.
Domyślnie wymagany jest zaufany certyfikat SQL. Opcję zaufania certyfikatowi włączaj tylko dla znanego serwera w swojej sieci.

## Wydruk
Wybierz produkty, uzupełnij dane i użyj „Podgląd / druk”. Przeglądarka otworzy lokalny HTML; Ctrl+P pozwala drukować lub zapisać PDF. Wybierz A4 poziomo i wyłącz nagłówki/stopki przeglądarki.
Układ odwzorowuje sekcje przesłanego wzoru; wymaga jeszcze porównania wydruku na rzeczywistej drukarce.

## Zakres weryfikacji
Powiązania SQL sprawdzono na eksportach użytkownika z Subiekta GT 1.89 SP1 HF1. Nie wykonano jeszcze kompilacji ani próby połączenia aplikacji z rzeczywistym serwerem.
Ruchy bez dostawy źródłowej oraz dokumenty z korektami lub innym sposobem realizacji wymagają osobnej weryfikacji. Brak danych nie jest uzupełniany przez zgadywanie.
Ilość sprzedaży nie jest dawką. Pole dawkowania pozostaje do ręcznego uzupełnienia.
Numer karty wpisuje użytkownik. Automatyczna wspólna numeracja, centralna baza kart, instalator i profile lekarzy pozostają do wdrożenia. Plik JSON można zapisać do wybranego folderu sieciowego, ale nie zastępuje on centralnej bazy wielostanowiskowej.
Nie dołączaj do repozytorium danych klientów, eksportów ani haseł.
