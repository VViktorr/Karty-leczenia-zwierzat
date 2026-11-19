# ReactKL — kontrola niezależności i postęp migracji

Data: 19 listopada 2026.

## Architektura
- `ui/react`: React/TypeScript + Ant Design (biblioteka React, nie AntdUI).
- `src/KartyLeczenia.ReactKL`: osobny program Windows zawierający tylko host WebView2 i usługi C#.
- `src/KartyLeczenia.Core`: logika Subiekta, dane kart i renderer wydruków.
- `src/KartyLeczenia.WinForms`: usunięte wyłącznie z gałęzi ReactKL; pełna kopia na oddzielnej gałęzi.

## Zaimplementowane
- Dokumenty FS, PA, WZ; okresy i wyszukiwanie; odczyt danych przez C#.
- Karta leczenia: edycja wariantów tekstowych, data i godzina, wybór do druku,
  bezpieczny zapis SQL z blokadą numeracji i wykrywaniem duplikatów.
- Podgląd i druk karty przez `CardHtmlRenderer`.
- Zapisane karty i kosz: wyszukiwanie, podgląd, druk, przenoszenie i przywracanie.
- Cennik i terminy ważności przez `MedicineStockService`.
- Ustawienia połączenia Subiekt/SQL i dane lekarza.
- Archiwizacja: pojedynczy .bak w lokalizacji docelowej, tymczasowy plik SQL,
  weryfikacja SHA-256, postęp kopiowania, Windows Hello.

## Do ukończenia przed produkcyjnym zastąpieniem programu
- Testy GUI na Windows 11 (wygląd przy DPI 80–200%, ergonomia, klawiatura, wyświetlanie dużych dokumentów).
- Weryfikacja kliniczna automatycznego doboru gatunku, wskazania, dawkowania,
  karencji, źródeł ChPL i zapamiętywania wariantów.
- Obsługa całego edytora katalogu SQL oraz PDF ChPL.
- Odtwarzanie bazy z pendrive i publikacja/aktualizacja instalatora z SQL.
- Zaawansowane ustawienia wydruków i czyszczenie kosza po dodatkowej autoryzacji.
- Potwierdzenie zgodności druków z dotychczasowym programem.

Nie oznaczać wersji ReactKL jako produkcyjnej, dopóki nie przejdzie powyższych kontroli.
