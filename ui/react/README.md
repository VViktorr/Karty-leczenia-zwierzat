# ReactKL — interfejs React / WebView2

Ten katalog zawiera frontend programu **Karty Leczenia**. Główna dokumentacja, instalacja serwera SQL i kompletne polecenia do Inno Setup znajdują się w [README repozytorium](../../README.md).

## Stos technologiczny

- **React 19 + TypeScript 7 + Vite 8**
- **Ant Design 6**, wybrane kontrolki Fluent UI i opcjonalny **AG Grid**
- TanStack Query i TanStack Virtual do obsługi danych i długich list
- **WebView2** do komunikacji z hostem `src/KartyLeczenia.ReactKL` (C#/.NET 11)

Interfejs React jest jedynym interfejsem tej gałęzi. Natywne Windows Forms służy tylko do uruchomienia okna WebView2 — nie są dostępne dawne formularze WinForms ani AntdUI. Połączeń SQL nie nawiązuje bezpośrednio JavaScript.

## Kompilacja frontendu

Na Windows 11 z Node.js zgodnym z wersją w GitHub Actions (obecnie 26.13.1):

```powershell
cd C:\Karty-leczenia-zwierzat\ui\react
npm.cmd ci --no-audit --no-fund
npm.cmd run test:bridge
npm.cmd run build
```

`npm.cmd` jest używane na Windows, by uniknąć blokady uruchamiania `npm.ps1` w PowerShell. Wynik jest w katalogu `ui/react/dist`. Sam `npm run dev` służy wyłącznie do pracy nad widokami i **nie zapewnia połączenia z SQL ani Subiektem** poza hostem Windows.

## Budowa aplikacji Windows (bez instalatora)

```powershell
cd C:\Karty-leczenia-zwierzat
dotnet publish .\src\KartyLeczenia.ReactKL\KartyLeczenia.ReactKL.csproj -c Release -r win-x64 --self-contained true -o .\ReactKL-preview
.\ReactKL-preview\KartyLeczenia.ReactKL.exe
```

**Najpierw wykonaj `npm.cmd run build`**, inaczej w `react-ui` aplikacji zabraknie aktualnych plików interfejsu. Wymagane jest .NET 11 SDK 11.0.100 (zob. [`global.json`](../../global.json)). Na komputerze docelowym wymagany jest WebView2 Runtime.

## Kompletny instalator z numerem wersji

Z katalogu głównego, po zainstalowaniu Inno Setup 7 (lub obsługiwanego 6):

```powershell
cd C:\Karty-leczenia-zwierzat
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-ReactKL.ps1 -Version "4.0.1"
```

Wynik: `Instalatory\KartyLeczenia-Setup-win-x64-v4.0.1.exe`. Numer `4.0.1` jest przykładem, podawanym ręcznie. Wersja `0.1.0` w `package.json` jest wersją paczki frontendu — nie numerem instalatora.

Skrypt buduje React, .NET, paczkę do pierwszego importu katalogu i ChPL, a następnie uruchamia kompilator Inno Setup. Alternatywnie można użyć [ręcznego workflow](../../.github/workflows/reactkl-installer.yml).

## Moduły i układy

Interfejs obejmuje ekrany dokumentów FS/PA/WZ, kreatora karty, zapisanych kart i kosza, katalogu leków z PDF ChPL, cennika, terminów ważności oraz ustawień połączeń Subiekta i archiwum. Dostępne są niezależne skórki (Modern, Windows, Fluent, Kompaktowy oraz Orchard, Material, Aero, Spectrum, Carbon, Nordic, Forest, Sandstone i Midnight), przełącznik menu **górne / boczne** działający dla każdej skórki, jasne/ciemne motywy i skalowanie.

Lista dokumentów ma pamięć odczytu na poziomie hosta; przycisk **F5** wymusza odświeżenie. Opcja **Wszystkie pobrane** dotyczy wyłącznie zwróconej porcji danych (do 500), a nie wszystkich historycznych dokumentów.

## Konfiguracja komponentów

**Ustawienia → Komponenty** to konfiguracja lokalna stanowiska (bez zmian SQL):

- **PDF:** wspólny podgląd `react-pdf`/`pdfjs-dist` dla ChPL i kart leczenia; przewijanie wszystkich stron (z wirtualizacją `@tanstack/react-virtual`) lub pojedyncze strony z przyciskami „Poprzednia/Następna”, zoom 50–200%, przełączanie na natywny podgląd WebView2, opcjonalna pamięć zoomu. Cennik korzysta z tych samych ustawień przewijania/stron w istniejącym HTML.
- **Karty leczenia:** host `previewCardPdf` generuje rzeczywisty dokument PDF identycznym mechanizmem `CardPdfService.RenderAsync` co eksport. Podgląd HTML jest awaryjnym rozwiązaniem w razie błędu lub pracy bez hosta. Do drukowania używaj trybu „Podgląd natywny / drukuj”, który drukuje wygenerowany PDF.
- **Tabele:** globalny domyślny silnik Ant Design/AG Grid z opcjonalną pamięcią per lista; istniejące indywidualne ustawienia list mogą mieć pierwszeństwo.
- **Skróty:** `react-hotkeys-hook` konfiguruje skróty kreatora (etap wstecz/dalej, podsumowanie i zapis), wraz z włączeniem/wyłączeniem. Skróty list F5 i Ctrl+Shift+L pozostają niezależne.
- **Bufor React Query:** 5/15/30/60 s czasu świeżości i opcja jednego ponowienia. Serwerowe limity i buforowanie SQL pozostają niezależne.

Zmienione ustawienia przechowuje WebView2 `localStorage` pod kluczem `ReactKL.componentPreferences.v1`. Domyślne wartości są walidowane, a panel pozwala je przywrócić. Stare nazwy układów z oddzielnym menu górnym/bocznym zostają zmigrowane do skórki z odpowiednim przełącznikiem menu.

## Testy i status integracji

```powershell
cd C:\Karty-leczenia-zwierzat\ui\react
npm.cmd run test:bridge
npm.cmd run build
npm.cmd run test:e2e
```

Testy przeglądarkowe wymagają środowiska Playwright zgodnie z konfiguracją projektu. Kompletną kontrolę Windows (frontend, C#, regresje, publish i próbna kompilacja Inno) definiuje [`reactkl-checks.yml`](../../.github/workflows/reactkl-checks.yml).

**Istnienie funkcji w kodzie i zielony CI nie dowodzą pełnej zgodności z WinForms.** Przed wdrożeniem na serwerze produkcyjnym potrzebne są testy operacji SQL, dodawania leków, backup/restore, kosza, PDF, wysyłki e-mail i aktualizacji instalatora. Szczegóły: [audyt rzeczywistego parytetu](../../docs/reactkl-audyt-rzeczywistego-parytetu-2026-11-20.md).
