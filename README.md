# Karty Leczenia — ReactKL

**Aplikacja desktopowa dla Windows 11 x64 do tworzenia i archiwizowania kart leczenia zwierząt na podstawie dokumentów Subiekta GT.** Interfejs powstaje w React, a usługi, odczyt SQL i wydruki obsługuje C#/.NET 11.

> **Gałąź rozwojowa:** [`ReactKL`](https://github.com/VViktorr/Karty-leczenia-zwierzat/tree/ReactKL). Starsza implementacja WinForms jest osobna; ReactKL nie uruchamia starych formularzy ani nie korzysta z AntdUI. Część funkcji wymaga jeszcze testów integracyjnych na prawdziwym SQL Serverze i Windows.

## Jak działa program

- **Dokumenty Subiekta GT:** FS, PA i WZ, przełącznik „Wszystkie”, wyszukiwanie numeru/kontrahenta, okresy, F5 i pamięć list. „Wszystkie pobrane” oznacza maksymalnie 500 wyników jednego odczytu — nie wszystkie dokumenty z historii Subiekta.
- **Karty leczenia:** wybór pozycji, gatunków i wariantów ChPL, edycja danych, podsumowanie przed zapisem, numer `K/numer/rok`, PDF, druk oraz wysyłka e-mail.
- **Wspólne archiwum:** zapisane karty, edycja, kosz, kopie zapasowe i przywracanie bazy `KartyLeczenia`.
- **Katalog leków i ChPL:** dane w programowej bazie SQL; podgląd PDF, edycja wariantów, cennik i terminy ważności. Propozycje z tekstowych ChPL wymagają zatwierdzenia; skany bez warstwy tekstowej nie mają automatycznego OCR.
- **Dopasowanie wyglądu:** układy React, motywy, skalowanie 80–200%, ustawienia tabel i obsługa klawiaturą.

**Stan funkcjonalny:** obecność ekranu i obsługi w kodzie nie potwierdza jeszcze poprawnego działania w środowisku użytkownika. Zgłoszone obszary wymagające sprawdzenia opisuje [audyt rzeczywistego parytetu z 20.11.2026](docs/reactkl-audyt-rzeczywistego-parytetu-2026-11-20.md); przed wdrożeniem trzeba potwierdzić m.in. tworzenie nowej kartoteki leku, kosz, archiwizację, wydruki i aktualizację na dwóch stanowiskach.

## Architektura

| Warstwa | Technologia / zadanie |
| --- | --- |
| Interfejs | React 19, TypeScript 7, Ant Design 6, opcjonalnie AG Grid, Vite 8 |
| Aplikacja Windows | `src/KartyLeczenia.ReactKL` — host WinForms **wyłącznie dla WebView2**, bez dawnego interfejsu WinForms |
| Logika i integracja | C#/.NET 11; `src/KartyLeczenia.Core` i usługi ReactKL |
| Źródło dokumentów | Subiekt GT / Microsoft SQL Server — **odczyt**, bez modyfikowania dokumentów i stanów |
| Archiwum programu | Odrębna baza SQL Server `KartyLeczenia` — karty, katalog leków, ChPL, konfiguracja i aktualizacje |
| SQLite | Materiał źródłowy do **pierwszego importu** katalogu na nowym serwerze; nie jest bieżącą bazą aplikacji ReactKL |
| Instalator | Inno Setup 7 (skrypt obsługuje także Inno Setup 6), instalacja Windows x64 |

JavaScript komunikuje się z hostem przez WebView2. Połączenia z SQL wykonują usługi C#, nie kod strony.

## Instalacja na Windows

### Dostępność instalatora

Gotowe instalatory — **jeśli zostały opublikowane** — są w [GitHub Releases](https://github.com/VViktorr/Karty-leczenia-zwierzat/releases). Na 22.11.2026 nie potwierdzono wydania oznaczonego `v4.0.0` w GitHub Releases; nie należy traktować samej [notatki wydania](docs/releases/4.0.0.md) jako działającego linku do instalatora.

Instalator można zbudować lokalnie (instrukcja niżej) albo uruchomić ręcznie [workflow instalatora](https://github.com/VViktorr/Karty-leczenia-zwierzat/actions/workflows/reactkl-installer.yml) z podaniem numeru wersji.

### Serwer i stanowiska

1. Na serwerze wybierz instalację **serwerową** i przygotuj bazę `KartyLeczenia` w istniejącej instancji SQL lub w instancji `KLARCHIVE`. Instalator może pobierać SQL Server 2025 Express i WebView2 Runtime, gdy są potrzebne.
2. Na innych komputerach wybierz **stanowisko klienckie** i podaj adres serwera archiwum. Wszystkie stanowiska korzystają z tego samego katalogu leków i wspólnych kart SQL.
3. W **Ustawienia → Subiekt GT** skonfiguruj osobne połączenie z bazą podmiotu i wybierz magazyn. W **Ustawienia → Baza Karty Leczenia** sprawdź połączenie z archiwum.
4. Uzupełnij **Dane lecznicy**, sprawdź ustawienia karty i wyglądu. Przy otwieraniu dokumentu weryfikuj pola medyczne z aktualną ChPL.

**Ważne:** do instalacji nie potrzeba SDK .NET ani Node.js — skrypt publikuje aplikację `--self-contained`. Na stanowisku wymagany jest WebView2 Runtime. Zrób kopię `.bak` przed aktualizacją istniejącej bazy.

## Budowanie z kodu — ReactKL 4.x

### 1. Wymagane narzędzia na komputerze kompilującym

- Windows 11 x64 i Git (Git LFS, jeżeli katalog zawiera pliki LFS).
- **Node.js 26.13.1** — wersja używana w GitHub Actions; npm jest dołączany do Node.js.
- **.NET 11 SDK**, co najmniej `11.0.100`. Plik [`global.json`](global.json) wymaga linii SDK 11.0.100 i pozwala na nowszą wersję funkcjonalną w tej samej linii, a nie automatycznie na dowolny kolejny główny .NET.
- **Inno Setup 7** (lub obsługiwane Inno Setup 6) — tylko do budowania instalatora.

Sprawdź narzędzia w **nowym** PowerShellu:

```powershell
node --version
npm.cmd --version
dotnet --list-sdks
dotnet --version
```

Przy braku Node.js lub .NET SDK pobierz odpowiednie **SDK/instalatory x64**, a nie wyłącznie środowisko uruchomieniowe:

- [Node.js](https://nodejs.org/en/download)
- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- [Inno Setup](https://jrsoftware.org/isinfo.php)

Jeśli PowerShell nie widzi `npm`, po instalacji Node.js zamknij i otwórz terminal. Ręcznie używaj `npm.cmd`, aby ominąć ewentualną blokadę `npm.ps1`. Jeśli kompilacja zgłasza `Install the [11.0.100] .NET SDK`, sprawdź `dotnet --list-sdks` oraz `global.json`.

### 2. Pobierz lub zaktualizuj właściwą gałąź

Dla używanego katalogu `C:\Karty-leczenia-zwierzat`:

```powershell
cd C:\Karty-leczenia-zwierzat
git fetch origin
git switch ReactKL
git pull --ff-only origin ReactKL
```

Przy pobieraniu od zera:

```powershell
git clone --branch ReactKL --single-branch https://github.com/VViktorr/Karty-leczenia-zwierzat.git C:\Karty-leczenia-zwierzat
cd C:\Karty-leczenia-zwierzat
```

Jeśli repozytorium używa Git LFS, uruchom `git lfs pull`. Skrypt instalatora sprawdza, czy `medicine-catalog.sqlite` jest prawdziwą bazą, nie wskaźnikiem LFS.

### 3. Kompilacja React + .NET (bez instalatora)

```powershell
cd C:\Karty-leczenia-zwierzat
cd ui\react
npm.cmd ci --no-audit --no-fund
npm.cmd run test:bridge
npm.cmd run build
cd ..\..
dotnet publish .\src\KartyLeczenia.ReactKL\KartyLeczenia.ReactKL.csproj -c Release -r win-x64 --self-contained true -o .\ReactKL-preview
.\ReactKL-preview\KartyLeczenia.ReactKL.exe
```

Frontend musi być zbudowany **przed** `dotnet publish`, ponieważ pliki `ui/react/dist` trafiają do katalogu `react-ui` aplikacji Windows. Podgląd w samej przeglądarce nie daje dostępu do natywnych połączeń Subiekta.

### 4. Pełny instalator Inno Setup z ręcznym numerem wersji

```powershell
cd C:\Karty-leczenia-zwierzat
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-ReactKL.ps1 -Version "4.0.1"
```

Jeśli kompilator Inno jest w niestandardowej lokalizacji:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-ReactKL.ps1 -Version "4.0.1" -Iscc "C:\Program Files\Inno Setup 7\ISCC.exe"
```

Skrypt wykonuje `npm ci`, kompiluje React, publikuje aplikację .NET dla `win-x64`, buduje pakiet startowy katalogu i ChPL oraz uruchamia `ISCC.exe`. **Nie wystarczy samo uruchomienie Inno Setup bez wcześniejszej kompilacji aplikacji.**

Wynik:

```text
C:\Karty-leczenia-zwierzat\Instalatory\KartyLeczenia-Setup-win-x64-v4.0.1.exe
```

Wersja `4.0.1` to **przykład**, a nie zapewnienie, że ta wersja została opublikowana. Skrypt przyjmuje ręcznie `X.Y.Z`, `X.Y.Z.W` i `X.Y.Z-rc.N`. Numer w `ui/react/package.json` dotyczy paczki frontendu i **nie jest** numerem instalatora.

### 5. Testy i GitHub Actions

```powershell
cd C:\Karty-leczenia-zwierzat
cd ui\react
npm.cmd run test:bridge
npm.cmd run build
cd ..\..
dotnet run --project tests\ReactKLChecks -c Release
```

- [Kontrole ReactKL](.github/workflows/reactkl-checks.yml): frontend, testy przeglądarkowe, C#/.NET, regresje i próbna kompilacja Inno Setup.
- [Ręczne budowanie instalatora](.github/workflows/reactkl-installer.yml): przy wywołaniu `workflow_dispatch` wymaga podania numeru wersji, tworzy plik EXE i sumę SHA-256 jako artefakty GitHub Actions.

**Zielony wynik CI nie zastępuje testu na rzeczywistym Subiekcie GT, SQL Serverze, drukarce i dwóch stanowiskach.**

## Archiwizacja i aktualizacje

Baza Subiekta GT i baza programu `KartyLeczenia` są od siebie niezależne. Archiwizacja i przywracanie dotyczą **tylko bazy programu**; przywrócenie, usunięcie z kosza i inne operacje uprzywilejowane wymagają odpowiedniej autoryzacji, w tym Windows Hello tam, gdzie przewiduje ją program. Konfiguracja połączeń na stanowiskach jest oddzielna, a zapamiętane hasła SQL są chronione mechanizmem Windows DPAPI.

Instalator zachowuje zgodność aktualizacji na miejscu: `AppId=pl.kartyleczenia.winforms.sql`, katalog `C:\Program Files\KartyLeczenia` oraz nazwę bazy `KartyLeczenia`. Na serwerze instalator jest przygotowywany do publikacji w `dbo.program_update` po uruchomieniu programu; klient może pobrać i uruchomić udostępnioną aktualizację po potwierdzeniu i weryfikacji SHA-256. Nie zmieniaj `AppId` ani nazw baz bez planu migracji.

**Przed instalacją na produkcji:** wykonaj pełny backup `.bak`, sprawdź odtwarzanie na kopii i przetestuj ścieżkę serwer → stanowisko. Nie uruchamiaj skryptów modyfikujących dane katalogu bez podglądu na kopii bazy.

## Dokumentacja

- [Interfejs React / WebView2](ui/react/README.md)
- [Audyt rzeczywistej zgodności z WinForms](docs/reactkl-audyt-rzeczywistego-parytetu-2026-11-20.md)
- [Migracja ReactKL i obszary do weryfikacji](docs/reactkl-migration.md)
- [Skrypt pełnego instalatora](installer/Build-ReactKL.ps1)
- [Skrypt Inno Setup](installer/KartyLeczenia.ReactKL.iss)
- [Notatki planowanego wydania 4.0.0](docs/releases/4.0.0.md)
