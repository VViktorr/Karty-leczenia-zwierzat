# Karty leczenia zwierząt

Aplikacja dla Windows rozwijana w **Windows Forms (WinForms)**, w C# na .NET 10, z odczytem danych z Subiekta GT i lokalnym katalogiem leków SQLite.

**Aktywna i domyślna gałąź to `winforms`. Bieżące zmiany interfejsu wprowadzamy w projekcie [KartyLeczenia.WinForms](src/KartyLeczenia.WinForms).**

## Budowanie na Windows

Wymagany jest .NET 10 SDK.

```powershell
dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release
```

## Projekty i dane

- [`src/KartyLeczenia.WinForms`](src/KartyLeczenia.WinForms) — aktywna aplikacja Windows Forms.
- [`src/KartyLeczenia.Core`](src/KartyLeczenia.Core) — wspólna biblioteka, m.in. odczyt Subiekta GT.
- [`src/KartyLeczenia`](src/KartyLeczenia) — wcześniejszy interfejs .NET MAUI. Projekt WinForms korzysta z przechowywanych tu zasobów, w tym bazy `medicine-catalog.sqlite`.
- [Gałąź `main`](https://github.com/VViktorr/Karty-leczenia-zwierzat/tree/main) — wcześniejszy etap projektu .NET MAUI i jego dokumentacja.

## Wspólne archiwum WinForms

Program otwiera dwa połączenia SQL. Połączenie z Subiektem GT służy do odczytu.
Karty i opisy zwierząt są zapisywane w osobnej bazie `KartyLeczenia`, która
może działać na innym komputerze z SQL Server, w tym SQL Server Express.
Katalog leków pozostaje lokalnym plikiem SQLite dostarczanym z programem.

1. Na komputerze przeznaczonym na archiwum utwórz pustą bazę
   `KartyLeczenia` i uruchom w niej [skrypt konfiguracji](scripts/Setup-SharedArchive.sql)
   z uprawnieniami administratora. Skrypt tworzy tabele i rolę bazodanową.
2. Dla konta Windows lub loginu SQL używanego przez program utwórz użytkownika
   w bazie `KartyLeczenia` i dodaj go do roli `KartyLeczeniaOperator`.
   Na przykład dla istniejącego użytkownika bazy `operator_archiwum`:
   `ALTER ROLE KartyLeczeniaOperator ADD MEMBER [operator_archiwum];`.
   Konto aplikacji potrzebuje dostępu sieciowego do instancji i uprawnień tej roli.
3. Na każdym stanowisku w sekcji **Wspólne archiwum SQL** w **Ustawieniach**
   podaj adres instancji archiwum, wybierz uwierzytelnianie i zapisz połączenie.
   W ustawieniach Subiekta użyj tej samej nazwy serwera i bazy podmiotu na każdym
   stanowisku. Program wykorzystuje te nazwy do przypisywania opisów i płatników.
   Zachowaj nazwy używane przy tworzeniu importowanych danych, żeby utrzymać te powiązania.
4. Na każdym stanowisku z wcześniejszymi danymi wybierz w ustawieniach
   **Importuj lokalne karty i opisy…**. Wskaż folder zawierający
   `saved-treatment-cards.sqlite` lub `animal-descriptions.sqlite`.
   Domyślny folder to `%LOCALAPPDATA%\KartyLeczenia`. Import zostawia pliki
   źródłowe i można go ponowić. Karty o tym samym identyfikatorze są pomijane,
   a dla tego samego opisu zachowywany jest wpis z nowszą datą użycia.

Komputer z SQL Server musi być dostępny podczas pracy ze wspólnym archiwum.
Lokalna wersja aplikacji jest dostępna na gałęzi
[`KarLecz_lokalnie`](https://github.com/VViktorr/Karty-leczenia-zwierzat/tree/KarLecz_lokalnie).
