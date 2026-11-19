# Migracja interfejsu KartyLeczenia na AntdUI — 19.11.2026

**Gałąź:** `winforms` — iteracyjna migracja bez zmiany bazy SQL, połączenia Subiekta i numeracji kart.

## Motywy

Pozostawiono jeden motyw zgodności **Klasyczny**. Wszystkie pozostałe presety korzystają z AntdUI:

- Antd Jasny — niebieski #1677FF
- Antd Ciemny — jasnoniebieski #5299FF
- Antd Morski — turkus #007470
- Antd Śliwkowy — #C192F4
- Antd Fioletowy — #722ED1
- Antd Pomarańczowy — #FA8C16
- Antd Grafitowy — #5D7A96

Palety Antd są **lokalnymi konfiguracjami programu** zbudowanymi na publicznym mechanizmie AntdUI. To nie są oddzielne motywy pobrane z internetu. Przestarzałe nazwy presetów są mapowane podczas ładowania `appearance.json`; zachowane są pozostałe preferencje wyglądu.

Oficjalne źródła:
- https://github.com/AntdUI/AntdUI/blob/main/doc/wiki/en/Theme.md — tryb jasny/ciemny, algorytm kolorów `Style.SetPrimary`.
- https://github.com/AntdUI/AntdUI-Demo — demo kontrolek i zachowań.
- https://github.com/AntdUI/AntdUI/blob/main/doc/wiki/en/Home.md — spis komponentów.

## Kontrolki już zmigrowane

| Obszar | Kontrolka | Uwagi |
| --- | --- | --- |
| Przycisk i przełączniki dokumentów | AntdUI.Button / ThemeSelector | Istniały już wcześniej |
| Terminy ważności | ExpiryTable : AntdUI.Table | Filtry, sortowanie, układ |
| Zapisane karty | SavedCardsTable : AntdUI.Table | Podgląd, eksport, kosz; nowy lokalny zapis układu |
| Cennik leków | PriceTable : AntdUI.Table | Pobieranie, wydruk, netto/brutto; nowy lokalny zapis układu |
| Dokumenty Subiekta | DocumentsTable : AntdUI.Table | Cache, wybór, otwieranie, marker zbiorczych, układ |
| Tabele AntdUI | Globalna paleta, wspólna kolorystyka | `Style.SetPrimary` + kolory tabel |

## Pozostałe obszary wymagające osobnych zmian

- **Karta leczenia:** rozbudowany, edytowalny `DataGridView` z komórkami combobox, checkbox, button, walidacją, edycją grup zwierząt, drukowaniem i zapisami; należy przenieść jako oddzielny zestaw zmian i przetestować operacje na komórkach.
- **Ustawienia i okna dialogowe:** wiele pól WinForms `TextBox`, `ComboBox`, `DateTimePicker`, `TabControl`, `TableLayoutPanel`. Nadal wymagają migracji na `AntdUI.Input`, `Select`, `DatePicker`, `Tabs` tam, gdzie API jest zgodne.
- **Edycja katalogu i wydruki:** osobne formularze; niezmieniona logika.
- **Parametry listy:** istniejący edytor dla `DataGridView` nie obsługuje `AntdUI.Table`. Dla AntdUI udostępniono podstawowy wybór widocznych kolumn i zapamiętywanie szerokości/kolejności; zaawansowane ustawienia kolumn wymagają nowej implementacji.

## Wymagana weryfikacja

**W tym przebiegu nie uruchomiono kompilacji .NET ani programu Windows**. Nie wdrażać na stanowiska produkcyjne bez testów. Wykonać:

```powershell
git pull --ff-only origin winforms
dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release
```

Po kompilacji: sprawdzić wyszukiwanie i otwieranie FS/PA/WZ, wybór leku do edycji, podgląd i eksport PDF cennika (kolejność pozycji), zaznaczenie karty, kosz/przywracanie, przełączanie wszystkich motywów, zapamiętywanie kolumn i skalę 100/125/150%. Zwrócić uwagę na `AntdUI.Table.SelectedsReal()` oraz `SortList()` przy klikaniu, sortowaniu i drukowaniu.
