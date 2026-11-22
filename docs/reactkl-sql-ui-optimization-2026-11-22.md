# ReactKL — optymalizacja SQL i interfejsu (2026-11-22)

Zakres: dotychczasowy ReactKL (React/Ant Design/AG Grid, WebView2, .NET, Subiekt GT). Bez zmian schematu SQL, danych klinicznych, instalatora i sposobu tworzenia kart.

## Zmiany
1. `DocumentService.ReadLatestDocumentIdAsync`: sprawdza najnowszy dokument **wyłącznie wybranego magazynu**. Nowe dokumenty z innych magazynów nie wymuszają pełnego odczytu listy po wygaśnięciu cache. Wciąż działa okresowy pełny odczyt (15 minut) i wymuszenie F5; brak dokumentów zwraca 0.
2. `useRemote` w React: gdy użytkownik zmienia filtr albo opuszcza ekran, anulowany jest także odczyt rozpoczęty przez `fetchQuery` TanStack Query. Zapobiega to pozostawianiu zbędnych zapytań WebView2/SQL po szybkim wpisywaniu wyszukiwanego tekstu.
3. Reużycie instancji `Intl.NumberFormat` i `Intl.DateTimeFormat` dla wszystkich wierszy.
4. AG Grid: brak kopiowania tablicy wierszy i zastąpienie wielokrotnych `indexOf` mapą indeksów (dla funkcyjnego `rowKey`).
5. `reactkl-checks.yml` uruchamia komplet testów także w pull requestach do `ReactKL`.

## Testy przed scaleniem
- GitHub Actions: `npm ci`, `npm run test:bridge`, `npm run build`, Playwright, `dotnet build`, `tests/ReactKLChecks`, publikacja win-x64 oraz test kompilacji Inno Setup.
- Test ręczny z Subiektem GT: porównać listy FS/PA/WZ/Wszystkie (również po wyborze okresu i magazynu), kolejność, kontrahenta, wartości i powiązane WZ.
- Szybko zmieniać wyszukiwaną frazę i zakładki: nie mogą pojawiać się wyniki nieaktualnej frazy; potwierdzić anulowanie zapytań po stronie hosta.
- Sprawdzić F5, listę z pamięci i powrót po 5 minutach. Zmiana dokumentu w bieżącym magazynie ma odświeżyć listę. Edycje istniejących dokumentów wychwytywane są przy okresowym pełnym odczycie albo F5.
- Sprawdzić oba widoki tabel (Ant Design i AG Grid), sortowanie, rozmiary kolumn, zmianę liczby pozycji i otwieranie Enter/dwuklikiem.
- W środowisku wielostanowiskowym zmierzyć medianę i p95 z co najmniej 20 powtórzeń: zimny odczyt listy, ciepły cache, F5 i zmiana filtrów. Diagnostyka: `ReactKL.Documents.List` i `Subiekt.Documents.Timing`.

## Uwagi
- Brak pomiarów z rzeczywistym SQL Server na etapie przygotowania tej gałęzi; nie deklarujemy konkretnego przyspieszenia w sekundach.
- Nie tworzyć indeksów ani nie modyfikować tabel Subiekta GT bez analizy planu wykonywania na kopii bazy.
