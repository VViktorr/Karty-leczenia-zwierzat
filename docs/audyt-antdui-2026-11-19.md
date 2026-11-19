# Audyt AntdUI WinForms — 19 listopada 2026

## Zakres
Gałąź `winforms`, pakiet AntdUI 2.4.19, formularze i tabele w `src/KartyLeczenia.WinForms`.
Przeprowadzono przegląd statyczny kodu i automatyczne kontrole GitHub Actions. **Nie wykonano testu interakcji na komputerze użytkownika.**

## Główne listy programu

| Obszar | Kontrolka widoczna | Wniosek |
|---|---|---|
| Dokumenty FS/PA/WZ | `DocumentsTable : AntdUI.Table` | AntdUI |
| Cennik | `PriceTable : AntdUI.Table` | AntdUI |
| Terminy ważności | `ExpiryTable : AntdUI.Table` | AntdUI |
| Zapisane karty | `SavedCardsTable : AntdUI.Table` | AntdUI |
| **Karta leczenia** | **`TreatmentTable : AntdUI.Table`** | Nowy widoczny widok AntdUI; stan danych i część logiki nadal obsługuje ukryty `DataGridView` |

### Karta leczenia — zachowanie danych
- Wybór pozycji w tabeli AntdUI kieruje do odpowiadającego wiersza modelu karty.
- Kolumna `Druk` jest rzeczywistym `AntdUI.ColumnCheck`, synchronizowanym z wartością karty.
- Gatunek, zamiennik, droga podania, wskazanie ChPL i pozostałe pola są dostępne przez dwuklik oraz okna edycji/wyboru.
- Karta, wydruk, JSON i zapis SQL wciąż korzystają z istniejącej walidacji/modelu — bez zmiany danych SQL.
- Zmiany w modelu odświeżają widok AntdUI; wybór pozostaje przypięty do wiersza modelu.
- Do weryfikacji na Windows: kolejność wyboru, interakcje klawiaturą, wielokrotne zmiany gatunku i drogi, zapis karty, wydruk, kolumna `Druk`, dopasowanie szerokości kolumn i skalowanie 80–200%.

## Motywy, przełączniki i zaznaczenie
- Globalne aliasy `Button = AntdUI.Button` oraz `CheckBox = AntdUI.Checkbox`.
- `ThemeSelector` jest przełączanym przyciskiem AntdUI; aktywne przełączniki mają **pełne wypełnienie akcentem** i kontrastowy tekst (z wyjątkiem świadomie wybranego stylu konturowego).
- Ujednolicono `RowSelectedBg`, `RowSelectedFore`, kolor hover, ramki i naprzemienne tła w tabelach AntdUI.
- Czcionki tabel są buforowane i zwalniane dopiero przy usunięciu kontrolki, aby uniknąć błędu `Graphics.MeasureString` podczas przełączania motywów.
- `ExpiryTable` otrzymuje wysokość wiersza oraz rozmiar czcionki zgodne z pozostałymi tabelami AntdUI.
- Karty główne i karty Ustawień nadal wykorzystują natywny `TabControl` WinForms z własnym rysowaniem (`MainTabPainter`); nie jest to natywna kontrolka `AntdUI.Tabs`.

## Miejsca jeszcze niew pełni przeniesione
- `CatalogueEditor.variants` — edytowalny `DataGridView` wariantów katalogu. Nie zastępowano go w tym przebiegu ze względu na wielopolową edycję i zachowanie ręcznych zmian ChPL/SQL.
- `CatalogueProductPicker` — natywny `ListBox`.
- `ListParameters` — starsze okno parametrów i wybór kolumn `CheckedListBox/ListBox` dla `DataGridView`.
- W karcie leczenia ukryty `DataGridView` pozostaje modelem migracyjnym; edycja tekstu odbywa się obecnie w osobnych oknach, a nie natywnie w komórce AntdUI.
- Wiele formularzy nadal używa kontenerów WinForms (`Panel`, `TableLayoutPanel`, `FlowLayoutPanel`); są to kontrolki układu, nie alternatywny silnik motywów.

## Testy niezbędne przed uznaniem migracji za skończoną
1. Wczytanie dokumentu i zaznaczanie/odznaczanie `Druk` na kilkunastu pozycjach.
2. Zmiana gatunku, drogi podania i zamiennika; sprawdzenie zapamiętywania wyborów.
3. Edycja karencji, dawkowania, liczby i opisu zwierząt; zapis i ponowne otwarcie karty.
4. Przełączanie 10 motywów oraz stylów wypełnienia, także w ciemnym motywie.
5. Skalowanie 80%, 100%, 125%, 150%, 200% i zmiana kolejności/szerokości kolumn.
6. Zapisana karta, kosz, terminy ważności, cennik i podglądy wydruków.
7. Test długotrwałego przełączania motywów z logowaniem błędów AntdUI.

## Bezpieczeństwo zmian
Nie modyfikowano tabel SQL, kartoteki leków, PDF-ów ani numeracji instalatora. Zmiany dotyczą tylko interfejsu.
