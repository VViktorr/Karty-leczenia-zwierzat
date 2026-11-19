# ReactKL — nowy interfejs bez AntdUI

**Jedynym interfejsem użytkownika na tej gałęzi jest React + TypeScript + WebView2.**
WinForms służy wyłącznie jako natywne okno hostujące WebView2, bez dawnych formularzy.
C#/.NET oraz SQL Server odpowiadają za dane, logikę leczenia, zapis i wydruki.

## Budowanie lokalne (Windows 11)

```powershell
cd ui/react
npm install
npm run build
cd ../..
dotnet publish src/KartyLeczenia.ReactKL/KartyLeczenia.ReactKL.csproj -c Release -r win-x64 --self-contained true -o ReactKL-preview
.\ReactKL-preview\KartyLeczenia.ReactKL.exe
```

Workflow [ReactKL frontend and Windows desktop checks](../../.github/workflows/reactkl-checks.yml) kompiluje frontend,
aplikację C#, testy regresji i tworzy artefakt `ReactKL-win-x64-preview`.
Wymagany Microsoft Edge WebView2 Runtime.

## Widoki

React ma trzy wymienne układy: Modern (menu boczne), Classic (zakładki) i Fluent
(Windows 11). Niezależnie można wybrać motyw jasny, ciemny lub morski.

Interfejs obejmuje: dokumenty FS/PA/WZ, podgląd i edycję karty leczenia, podgląd
wydruku, zapis karty do SQL, listę zapisanych kart i kosz, cennik, terminy
ważności, katalog SQL (warianty leków i podgląd/dołączanie PDF ChPL), dane lecznicy,
konfigurację Subiekta i archiwum oraz pełną kopię SQL
na pendrive z Windows Hello i kontrolą SHA-256.

## Bezpieczeństwo i ograniczenia

- Brak AntdUI w nowym projekcie i brak przełączania na stare okna.
- Bezpośrednie zapytania SQL wykonuje tylko C#, nie JavaScript.
- Hasła SQL są zabezpieczane przez Windows DPAPI w istniejących ustawieniach.
- Wydruk korzysta z `CardHtmlRenderer` z `KartyLeczenia.Core`.
- Katalog React pozwala edytować/dodawać warianty i przypisywać PDF, a tekstowe
  ChPL analizuje offline jako **propozycje** z ostrzeżeniami i fragmentami źródłowymi.
  Dopiero po zatwierdzeniu przenosi proponowane dane do edytora, bez automatycznego
  zapisu w SQL. Nadal nie ma OCR dla skanów, usuwania istniejących wariantów ani
  pełnej kontroli zgodności danych klinicznych.
- Nadal brakuje wszystkich parametrów wydruków, odtwarzania kopii SQL i mechanizmu
  publikacji aktualizacji. Tych funkcji nie wolno uważać za gotowe.
- Wymagane testy na Windows z rzeczywistym SQL, Subiektem i drukarką.

Historyczna aplikacja WinForms jest zabezpieczona na gałęzi
`winforms-backup-2026-11-19` i nie stanowi części pakietu ReactKL.
