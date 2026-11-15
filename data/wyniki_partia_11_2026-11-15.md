# Partia 11 — wyniki potwierdzone po wykonaniu workflow

Data: 2026-11-15. Źródło: uruchomienie GitHub Actions [43747058192](https://github.com/VViktorr/Karty-leczenia-zwierzat/actions/runs/43747058192), artifact `partia11-wyniki-sqlite` (ID 14174048180), baza z commit `dea217c`. Analiza CSV `bez_liczby.csv` i `partia11_warianty.csv`.

## Integralność i zakres

- SHA256 SQLite: `170325b1c3b1ae48790932201357460fdddf50099d9d075f8d6f9fc2aa2ae3e5`.
- 2944 warianty; 2902 aktywne, 42 archiwalne; 35 aktywnych z polem `dose` bez **cyfry**.
- 10 grup z partii 11: 36 aktywnych wariantów, 20 odesłań do ChPL/ulotki, 0 problemów ścieżki/nagłówka PDF (treści PDF tym testem nie zweryfikowano).
- `integrity_check=ok`, `foreign_key_check=0`. **SQLite bez modyfikacji**.

## 35 wpisów bez cyfry — klasyfikacja na podstawie rzeczywistego tekstu dose

| Kategoria | Liczba | Identyfikatory | Uzasadnienie i działanie |
|---|---:|---|---|
| Celowo bez dawkowania — droga IM niepotwierdzona | **10** | 124, 227, 302, 318, 388, 1388, 1578, 1585, 1634, 2688 | Metacam/Melovem, Enroxil/Enrocin: tekst ostrzega przed przenoszeniem dawkowania SC/IV na IM albo zakazuje stosowania danego wariantu. **Nie wpisywać zastępczej liczby.** Ocenić, czy w ogóle powinny pozostać aktywne; nie usuwać bez potwierdzenia. |
| Dawkowanie jednostkowe wyrażone słownie | **12** | 639, 987, 1358, 1359, 1426, 1427, 1428, 1429, 1430, 1431, 1712, 1713 | Noroseal, Orbelux LA, Pathozone, Orbeseal: `jedna tubostrzykawka`/`jeden wlew`. **Nie jest to automatycznie brak dawkowania.** Możliwe ujednolicenie zapisu do `1` po potwierdzeniu ChPL i warunków podania. |
| Opisowy sposób aplikacji miejscowej | **10** | 187, 188, 535, 1964, 1982, 1983, 1984, 2419, 2421, 2422 | Pederipra Spray, Oxytet Spray, Solutio Iodi Spirituosa: natrysk/pędzlowanie do pokrycia miejsca. Brak sztywnej dawki liczbowej nie oznacza błędu. |
| Wymagają decyzji o statusie (tekst „dane archiwalne”) | **3** | 1414, 1417, 1959 | Tetra-Delta: `Dane archiwalne (brak aktualnej polskiej ChPL)` w polu dose, ale rekordy formalnie aktywne w `scope`. Zweryfikować status prawny i ewentualną archiwizację; nie dopisywać dawki w ciemno. |
| **Razem** | **35** | | **0 potwierdzonych braków wymagających automatycznego dodania dawki**; potrzebna kontrola ChPL i statusów. |

## Priorytety kliniczne partii 11

- IBR Marker żywy: osobny schemat dla podania donosowego cielętom i IM od 3. miesiąca życia, wraz z rozdzieleniem tekstu `dose` i `card_text` dopiero po sprawdzeniu urzędowego RPL 28639.
- Wellicox: sprawdzić zgodność drogi IV/IM we wszystkich wskazaniach, szczególnie przy bólu po dekornizacji.
- Szczepionki: Febrivac Dist, Nobivac DHPPi, Nobivac Tricat Trio, Rispoval 3 — zweryfikować warunki dawek przypominających, zamiast usuwać potrzebne odwołania.
- Nerfasin, Oxyflex XLA, Polocainum, Pyralgivet — utrzymać istotne ograniczenia dawkowania; nie przenosić reguł między gatunkami i drogami podania.

## Stan pracy

- Rzeczywiście poprawione warianty kliniczne w SQLite: **0**.
- Naprawione oryginalne PDF: **0**.
- Wyniki przejrzano na podstawie artifactu; ten raport jest interpretacją, nie nowym wykonaniem skryptu.
- Pozostałe liczniki globalne z partii 10 (do ponownego pomiaru po faktycznych poprawkach): 56 odesłań i 786 problemów PDF.
