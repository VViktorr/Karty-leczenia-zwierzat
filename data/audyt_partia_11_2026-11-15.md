# Partia 11 — przegląd 10 produktów, bez zmiany danych klinicznych
Data: 2026-11-15. Gałąź: `winforms`. Punkt odniesienia: commit `4ffb08aff6984efa7648d9b8e6f1d3cf6c2bf8d2` i `data/audyt_aktywnych_zaleglosci_partia_10_2026-11-15.md`.

## Weryfikacja zakresu
| Produkt | Aktywne | Wpisy z odesłaniem | ID wariantów do oceny |
|---|---:|---:|---|
| Febrivac Dist | 2 | 1 | 2549 |
| IBR marker żywy | 2 | 2 | 249, 250 |
| Nerfasin vet. | 5 | 1 | 926 |
| Nobivac DHPPi | 1 | 1 | 2586 |
| Nobivac Tricat Trio | 1 | 1 | 2587 |
| Oxyflex XLA | 2 | 2 | 1593, 1594 |
| Polocainum 5% | 3 | 2 | 1994, 1995 |
| Pyralgivet | 8 | 4 | 1612, 1613, 1919, 1920 |
| Rispoval 3 | 4 | 4 | 260, 1615, 1616, 1617 |
| Wellicox | 8 | 2 | wymaga odczytu z aktualnej SQLite |
| **Łącznie** | **36** | **20** | |

Odesłanie `wg ChPL` samo w sobie **nie jest wadą**. W każdym z 36 aktywnych wariantów jest dawka zawierająca liczbę. Raport partii 10 wskazuje brak problemu z nagłówkiem/powiązaniem PDF dla tych 10 grup, co **nie dowodzi poprawności treści** dokumentu.

## Ryzyka i zadania do potwierdzenia na oryginalnym PDF
1. **IBR marker żywy (249/250):** obecne pole dawki łączy donosowe podanie cielętom z domięśniowym u starszego bydła, mimo osobnych pól drogi podania. Rozdzielenie tekstu wymaga potwierdzenia dokładnego wieku, schematu i obowiązującej ChPL RPL 28639.
2. **Nobivac DHPPi (2586), Tricat Trio (2587), Rispoval 3 (260/1615–1617):** utrzymać precyzyjne odstępy szczepień, a okres odporności/przypominania zweryfikować w konkretnym urzędowym dokumencie; nie zastępować odesłania jedną arbitralną liczbą.
3. **Febrivac Dist (2549):** odrębna reguła dla osobników po nieszczepionych matkach; bez wiarygodnej ChPL nie usuwać warunku.
4. **Nerfasin (926):** wybór poziomu sedacji zależy od wskazania i stanu zwierzęcia, więc odesłanie może być istotnym zastrzeżeniem.
5. **Oxyflex XLA (1593/1594):** nie wymazywać ograniczeń maksymalnej objętości na miejsce iniekcji przed ich weryfikacją.
6. **Polocainum (1994/1995):** sprawdzić maksymalną dawkę całkowitą osobno od objętości dla poszczególnych technik, gatunków i obszarów.
7. **Pyralgivet (1612/1613/1919/1920):** nie traktować podanych zakresów ml/zwierzę jako równoważnych dawce mg/kg dla każdego osobnika; zweryfikować gatunek i drogę podania.
8. **Wellicox:** zweryfikować rozbieżność: część wpisów ma drogę IV, a w tekście dawki także IM; ustalić właściwy schemat dla każdego wskazania przed korektą.

## Audyt 35 wpisów bez liczby
Raport partii 10 podaje **35** aktywnych wariantów bez liczby w dawkowaniu, ale nie zawiera listy ich identyfikatorów i treści pól. Nie wolno kwalifikować ich jako rzeczywiste braki, opisy ani celowe `nie stosować` bez odczytu bieżącej SQLite. Wymagany odczyt tylko SELECT: `id, symbol, name, species, indication, route, dose, card_text, pdf_path` z `variants`, z uwzględnieniem faktycznej kolumny statusu/archiwizacji. Następnie kategoryzacja i ponowna kontrola liczników.

## Stan wykonania
- Poprawki kliniczne SQLite: **0** — oficjalne PDF nie zostały jeszcze niezależnie sprawdzone w treści.
- Poprawki dokumentów PDF: **0** — niczego nie zastępowano.
- Zmiany w `card_text`: **0**.
- Liczniki bazowe z ostatniego wykonanego audytu: **2944** warianty, **2902** aktywne, **42** archiwalne, **56** odesłań w pierwotnej grupie, **35** aktywnych bez liczby, **786** aktywnych z problemem PDF. Są to liczniki z raportu partii 10, a nie nowy odczyt.
- Baza nie była przebudowywana, modyfikowana ani kompilowana.

## Blokada bezpiecznego zapisu
Dostępne połączenie GitHub umożliwia zapis tekstu i odczyt workflow, ale nie udostępnia wykonania `workflow_dispatch`. Nie należy tworzyć automatycznego `push` jako obejścia ręcznej dyspozycji. Właściwa aktualizacja binarnej bazy wymaga ręcznie wywołanego GitHub Actions z prewarunkami dla identyfikatorów i gatunków, transakcją, `PRAGMA integrity_check`, `PRAGMA foreign_key_check`, sprawdzeniem liczników i commitem tylko po sukcesie. Po zakończeniu workflow należy usunąć.
