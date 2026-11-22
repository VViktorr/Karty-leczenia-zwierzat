# Karty Leczenia — ReactKL

Program dla lecznic weterynaryjnych na Windows 11. Odczytuje dokumenty z Subiekta GT i zapisuje karty leczenia we wspólnej bazie SQL.

## Pobierz wersję 4.0.0

- [Instalator Windows x64](https://github.com/VViktorr/Karty-leczenia-zwierzat/releases/tag/v4.0.0)
- [Lista zmian](docs/releases/4.0.0.md)

Na stronie wydania znajdziesz instalator `KartyLeczenia-Setup-win-x64-v4.0.0.exe` i plik `.sha256`. Przed aktualizacją istniejącego stanowiska wykonaj kopię `.bak` bazy `KartyLeczenia`.

## Pierwsze uruchomienie

1. Zainstaluj program na serwerze albo wybierz stanowisko klienckie i podaj adres istniejącego archiwum.
2. W **Ustawienia → Subiekt GT** wybierz bazę podmiotu i magazyn. W **Ustawienia → Baza Karty Leczenia** sprawdź połączenie ze wspólnym archiwum.
3. Otwórz dokument, przejdź przez etapy kreatora, sprawdź podsumowanie, zapisz kartę i wybierz podgląd lub PDF.

W **Ustawienia → Karty leczenia** można zmienić tryb edycji i opcje skrótów. Wygląd, motyw i skalowanie są w **Ustawienia → Wygląd**.

## Budowanie lokalne w C:\React

Wymagane: Git, Node.js 26.13.1, .NET 11 SDK, Inno Setup 7.1.1 (do instalatora; skrypt obsługuje również Inno Setup 6). Na Windows 11 wymagany jest też WebView2 Runtime.

**Pierwsze pobranie do pustego folderu:**

```powershell
git clone --branch ReactKL --single-branch https://github.com/VViktorr/Karty-leczenia-zwierzat.git C:\React
cd C:\React
```

**Aktualizacja już pobranego folderu:**

```powershell
cd C:\React
git switch ReactKL
git pull --ff-only origin ReactKL
```

Jeśli repozytorium korzysta z Git LFS, wykonaj także `git lfs pull` (gdy Git LFS jest zainstalowany). Sprawdź, czy plik `src/KartyLeczenia/Resources/Raw/medicine-catalog.sqlite` zawiera rzeczywistą bazę, a nie wskaźnik LFS: skrypt budowania instalatora sam kontroluje ten warunek.

Wersję instalatora podaje się ręcznie, np. `3.0.0` albo `3.0.0-rc.1`. Aktualizator porównuje kolejne RC numerycznie, a wydanie finalne następuje po wszystkich RC tej samej wersji. Kanał SQL jest wspólny, więc publikacja RC udostępnia go wszystkim klientom z aktualizatorem obsługującym RC. Starsze stanowiska najpierw zaktualizuj stabilnym instalatorem zawierającym ten aktualizator.

**Podgląd programu bez instalacji:**

```powershell
cd C:\React\ui\react
npm ci --no-audit --no-fund
npm run test:bridge
npm run build
cd C:\React
dotnet publish src\KartyLeczenia.ReactKL\KartyLeczenia.ReactKL.csproj -c Release -r win-x64 --self-contained true -o C:\React\ReactKL-preview
.\ReactKL-preview\KartyLeczenia.ReactKL.exe
```

**Instalator z wersją wpisaną ręcznie przez operatora:**

```powershell
cd C:\React
$wersja = Read-Host 'Podaj numer wersji (np. 4.0.0)'
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-ReactKL.ps1 -Version $wersja
```

Wynik w folderze `C:\React\Instalatory\KartyLeczenia-Setup-win-x64-v<wersja>.exe`. Nie wpisujemy numeru wersji automatycznie. Kompilacja z GitHub Actions znajduje się w `.github/workflows/reactkl-checks.yml`, a ręcznie uruchamiany instalator w `.github/workflows/reactkl-installer.yml`.

## Połączenia i archiwizacja

W **Ustawienia** programu znajdują się niezależne zakładki:

- **Subiekt GT**: serwer, tryb logowania Windows/SQL, pobieranie baz podmiotów, wybór bazy, test połączenia (bez zapisu), lista grup leków, lista magazynów z nazwami i zapis. Dokumenty, ceny i stany magazynowe są z Subiekta **tylko odczytywane**. Do pracy wymagane jest konto z uprawnieniami SELECT, bez praw do modyfikacji bazy Subiekta.
- **Baza Karty Leczenia**: odrębne połączenie SQL z bazą o niezmienionej, technicznej nazwie `KartyLeczenia`, test połączenia bez zapisu oraz sprawdzenie i zapis ustawień. W bazie są karty leczenia, katalog leków, ChPL, ustawienia lecznicy i aktualizacje programu.
- **Archiwizacja**: ustawienia miejsca zapisu kopii `.bak` oraz wykonanie i odtworzenie kopii bazy `KartyLeczenia`; nie dotyka bazy Subiekta. Przywracanie korzysta z ustawień **Konto administratora SQL** i wymaga potwierdzenia Windows Hello.

Dodatkowo: **Dane lecznicy** oraz **Aktualizacje**. Ustawienia połączeń są osobne na każdym stanowisku. Hasła SQL zapisuje Windows DPAPI w profilu użytkownika, jeśli operator wybierze zapamiętywanie.

## Instalacja, nazwa i aktualizacje

Wyświetlana nazwa programu i skrótów to **Karty Leczenia** (w menu Start i na pulpicie). Nazwa pliku `KartyLeczenia.ReactKL.exe`, identyfikator instalacji Inno Setup `pl.kartyleczenia.winforms.sql`, folder `C:\Program Files\KartyLeczenia` oraz nazwa bazy SQL `KartyLeczenia` **pozostają bez zmian**. Nowy instalator aktualizuje tę samą instalację zamiast tworzyć drugą. Stare skróty `SQL KartyLeczenia` są usuwane przez instalator; nowy skrót na pulpicie jest zaznaczony domyślnie.

Na **serwerze** instalator tworzy/konfiguruje bazę `KartyLeczenia` w wybranej instancji SQL lub w `KLARCHIVE`. Pakiet uruchomieniowy dostarcza katalog i urzędowe PDF ChPL na pierwszą konfigurację, bez celowego nadpisywania ręcznych zmian istniejącej bazy. Po instalacji wersja jest publikowana w `dbo.program_update`, a stanowiska zdalne pobierają ją z SQL po zatwierdzeniu przez użytkownika i weryfikacji SHA-256.

### Istniejący katalog SQL

Instalator zachowuje dane archiwum. Pakiet startowy katalogu i ChPL służy do inicjalizacji nowego serwera. Dla starszego katalogu bez pola karencji jaj administrator może użyć [migracji jaj](scripts/Upgrade-CatalogueEggWithdrawal.sql). [Scalanie duplikatów](scripts/Merge-ReviewedCatalogueDuplicates.sql) jest osobną operacją. Uruchom najpierw podgląd na kopii bazy i sprawdź proponowane zmiany.

**Przed zastąpieniem instalacji produkcyjnej wykonaj pełną kopię .bak bazy KartyLeczenia.** Wynik kompilacji GitHub Actions nie jest testem na rzeczywistym SQL Server i Subiekcie, ani testem odtwarzania danych lub wysyłki PDF.

## Dokumentacja i dalszy zakres

- [ReactKL — dokumentacja interfejsu](ui/react/README.md)
- [Audyt funkcjonalny](docs/reactkl-audyt-stanu-2026-11-19.md)
- [Skrypt instalatora](installer/Build-ReactKL.ps1)
- [Instalator](installer/KartyLeczenia.ReactKL.iss)

Zasada: do ReactKL przenosimy wyłącznie funkcje potwierdzone w końcowej wersji WinForms; wszystkie nowe wymagają najpierw uzgodnienia. Motywy i wygląd WinForms są wyłączone z porównania.
