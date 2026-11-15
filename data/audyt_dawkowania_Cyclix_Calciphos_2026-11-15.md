# Weryfikacja RPL: Cyclix i Calciphos — 15.11.2026

**Zakres:** etap A, bez zmian leczniczych w SQLite. Ten raport utrwala ustalenia z urzędowych ChPL; nie zastępuje oryginalnych PDF w `data/ChPL/RPL/`.

## Cyclix — RPL 18835

Urzędowa ChPL (sekcja 3.9): https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/18835/characteristic

PDF w repozytorium: `data/ChPL/RPL/RPL_18835_Cyclix_250_mcg_ml_roztwór_do_wstrzykiwań_dla__RPL_ChPL.pdf`

**Dawka podstawowa (we wszystkich wskazaniach):** 2 ml domięśniowo na krowę = 500 µg kloprostenolu.

| Wskazanie | Schemat potwierdzony w ChPL |
| --- | --- |
| Indukcja i synchronizacja rui | Jedna dawka; gdy nie widać objawów rui, możliwa druga po 11 dniach |
| Leczenie zapalenia błony śluzowej macicy przy czynnym ciałku żółtym | Jedna dawka; w razie potrzeby powtórzenie po 10–14 dniach |
| Torbiele lutealne jajników | Jedna dawka |
| Indukcja porodu | Jedna dawka, nie wcześniej niż 10 dni przed spodziewanym wycieleniem |
| Indukcja poronienia do 150. dnia ciąży | Jedna dawka między 5. a 150. dniem ciąży |

**Warianty do dopasowania według faktycznego wskazania:** 197–204, 219–226. Każdemu przypisać odpowiedni schemat **dopiero po odczytaniu kolumn `indication`, `route` i `dose` z aktualnej SQLite**. Nie podstawiać jednakowego schematu powtórzeń wszystkim wariantom.

**Rozbieżność do ręcznej weryfikacji przed zmianą karencji:** aktualna urzędowa ChPL RPL 18835 (sekcja 3.12) podaje tkanki 1 dzień i mleko 0 godzin, podczas gdy krążą także opisy ze starszą wartością tkanek 2 dni. Nie nadpisano żadnej karencji bez ustalenia aktualnej podstawy.

## Calciphos — RPL 20485

Urzędowa ChPL (sekcja 3.9): https://rejestrymedyczne.ezdrowie.gov.pl/api/rpl/medicinal-products/20485/characteristic

PDF w repozytorium: `data/ChPL/RPL/RPL_20485_Calciphos_RPL_ChPL.pdf`

Podanie dożylne, domięśniowe albo podskórne, zależnie od wskazania i zasad podania.

| Rodzaj wskazania | Dawka i powtarzanie z ChPL |
| --- | --- |
| Ostre i zaawansowane hipokalcemie/hipomagnezemie | 0,6–0,8 ml/kg; po braku poprawy powtórzenie nie wcześniej niż po 6 h, kolejne nie wcześniej niż po 24 h |
| Choroby szkieletu | 0,4–0,5 ml/kg; 2–3 iniekcje IV lub IM co 48 h |
| Stany zapalne, alergiczne, toksyczne, skazy krwotoczne | 0,2–0,3 ml/kg; 2–3 iniekcje IV lub IM co 48 h |

Przy ostrych niedoborach ChPL opisuje też typowe 1–2 iniekcje, rzadko 3, z interwałami 12 i 24 h, oraz minimalne odstępy przy braku poprawy. Nie wolno traktować wszystkich schematów jako identycznych.

**Warianty do dopasowania do wskazań:** 1859–1864, 771–773, 1697–1699. Sama obecność `0,6–0,8 ml/kg` nie potwierdza poprawności wskazania; pozostawiono bez edycji.

## Warunek kolejnej korekty SQLite

1. Odczytać rzeczywiste `id, rpl_id, species, indication, route, dose, edible_tissues, milk, pdf_path` dla wymienionych ID z bieżącej bazy `winforms`.
2. Zweryfikować, czy warianty nie są archiwalne, i zidentyfikować konkretne wskazanie.
3. Modyfikować tylko jednoznacznie rozpoznane wiersze z kontrolą starej wartości `dose` i liczby zmienionych rekordów; nie zmieniać gatunku, drogi podania, karencji i innych pól.
4. Sprawdzić `PRAGMA integrity_check`, `PRAGMA foreign_key_check`, zachowanie liczby wariantów i produktów; commit każdej zakończonej partii.
