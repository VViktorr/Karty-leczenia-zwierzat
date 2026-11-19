# ReactKL — audyt stanu funkcjonalnego
**Data:** 19.11.2026  
**Stan bazowy ReactKL:** `fc04be6b5ad7cd260e8cee09f1ce4fffde36e35c`  
**Odniesienie funkcjonalne:** `winforms-backup-2026-11-19`, HEAD `592d17c24`.

> **Wyłączenie z audytu:** porównanie skórek, motywów, kolorów, styli, bibliotek kontrolek i układu wizualnego WinForms. ReactKL ma własny interfejs. **Nie dodawać funkcji nieobecnych w końcowym WinForms bez zgody użytkownika.** Oceniać zachowanie i wymagania pracy, nie kopiować starego wyglądu.

## Jak interpretować stan

- **ZAIMPLEMENTOWANE** — ścieżka istnieje w React i C#; nie oznacza pełnego sprawdzenia na prawdziwych danych.
- **CZĘŚCIOWE** — istnieje tylko część działania względem końcowego WinForms.
- **BRAK / NIEPOTWIERDZONE** — brak ścieżki w kontrolowanym kodzie albo brak dowodu działania. Należy rozróżnić te przypadki.
- **TESTY** — odrębna kategoria. Sukces CI nie jest testem drukarki, Windows Hello, rzeczywistego SQL Server ani Subiekta GT.

## Macierz: co jest, co zostało

| Obszar | Stan | Co już jest w ReactKL | Co zostało |
|---|---|---|---|
| Dokumenty FS/PA/WZ | **Zaimplementowane** | Wyszukiwanie z opóźnieniem, 10 zakresów, FS/PA/WZ, Alt+1/2/4, Enter, F5, ostatnie dokumenty przy pustym filtrze, 500 wyników, cache list 5 min | Test szybkości i poprawności na rzeczywistym Subiekcie oraz parametrów listy dokumentów |
| Nowa karta leczenia | **Zaimplementowane** | Wczytanie dokumentu, edycja pozycji, podgląd i druk HTML, zapis SQL, oddzielne daty zgłoszenia/wykonania, numer `K/n/rok` z daty wykonania | Test w praktyce, szczególnie daty, karencje i zachowanie pustego zgłoszenia |
| Kontrola przed zapisem | **Zaimplementowane w kodzie** | `reviewBefore("save")` i `reviewBefore("preview")`; dialog ostrzeżeń | Test wszystkich braków i przejścia do problematycznej pozycji |
| Filtr gatunków i wariant ChPL | **Zaimplementowane** | Bydło/świnie/drób/pies/kot, warianty ChPL, niezgodne pozycje na końcu i odznaczane, wczytywanie PDF | Weryfikacja na rzeczywistych produktach, w tym przypadkach niejednoznacznych |
| Preferencje gatunku/drogi, zamiennik | **Zaimplementowane** | `dbo.treatment_choices`, pamięć wyboru, `Zamiennik produktu`, kompatybilność części starych zapisów | Integracyjny test z historyczną bazą; ścieżka zgodności starych wpisów jest ograniczona |
| Numery/liczby leczonych zwierząt | **Zaimplementowane** | Grupowa edycja po gatunku/sekcji i zapamiętanie opisów kontrahenta w `dbo.animal_descriptions` | Test ze wspólnymi danymi na dwóch komputerach |
| Zapisane karty | **Zaimplementowane** | Lista, wyszukiwanie po numerze z indeksem SQL, data, podgląd, wydruk, edycja istniejącej, duplikaty, JSON, kosz, przywracanie i trwałe usuwanie | PDF i e-mail; test starych kart oraz zachowania numeru |
| Ochrona równoczesnej edycji | **Zaimplementowane w kodzie** | Sprawdzenie rewizji JSON SHA-256 i blokada rekordu SQL przy aktualizacji istniejącej karty | Faktyczny test konfliktu pomiędzy dwoma stanowiskami |
| Eksport PDF i e-mail karty | **Brak pełnej ścieżki** | HTML do podglądu i wydruku | Zapis PDF, dołączenie do e-maila, automatyczny adres **płatnika** z Subiekta i nazwa PDF na podstawie numeru dokumentu, bez niedozwolonych znaków |
| Cennik leków | **Zaimplementowane** | Poziomy cen, domyślny detal, netto/brutto, ceny z Subiekta, karencje i drogi z katalogu SQL, pełne/skrócone formaty, lista braków | Weryfikacja kwot, skrótów i zgodności karencji na rzeczywistych danych |
| Wydruk cennika | **Zaimplementowane w kodzie** | Ustawienia marginesów, kolumn i czcionek, podgląd oddzielnych stron A4, legenda, ponawiane nagłówki, druk przeglądarkowy | Test fizycznego wydruku wielostronicowego i podziału stron na Windows |
| Terminy ważności | **Zaimplementowane** | Dane Subiekta, filtry wszystkie/przeterminowane/30/60, sortowanie, personalizacja kolumn z pamięcią stanowiska | Test danych i zapamiętywania ustawień. Wyszukiwanie i daty przyjęcia istnieją w React, ale nie zostały potwierdzone w końcowym `MainForm.ConfigureExpiry` jako funkcje WinForms; nie rozwijać ich bez ustalenia zakresu |
| Katalog leków i ChPL | **Częściowe** | Wyszukiwanie, edycja **istniejącej** kartoteki, dodanie wariantu, PDF z SQL, podłączenie i wstępna analiza PDF, ochrona rewizji | Dodanie całkowicie nowej kartoteki z interfejsu; lista brakujących produktów w katalogu względem Subiekta; test urzędowych dokumentów |
| Dane lecznicy i lekarza | **Zaimplementowane w kodzie** | `dbo.clinic_settings`, nazwa/adres/telefon/lekarz, dwie pieczątki PNG, ochrona rewizji; dane na wydruku | Test pierwszego logowania, przeniesienia ustawień i widoczności na każdym stanowisku |
| Kopia SQL | **Zaimplementowane w kodzie** | Pełne `.bak`, wybór lokalizacji/pendrive, konfiguracja, Windows Hello, weryfikacja kopii i SHA-256 | Test faktycznego backupu i odtworzenia; sprawdzić zachowanie przy zamykaniu względem WinForms |
| Przywracanie SQL | **Zaimplementowane w kodzie** | Wybór pliku, potwierdzenie, Windows Hello, ochrona przeciw odtworzeniu innej bazy, kopia zabezpieczająca | Pełny test procedury i zachowania podczas awarii / przerwania |
| Instalator serwerowy | **Zaimplementowane w skryptach** | Nowa lub istniejąca instancja SQL, `KLARCHIVE`, inicjalizacja bazy i import katalogu, ścieżka `C:\Program Files\KartyLeczenia` | Test na czystym Windows, istniejącej instalacji i stanowisku zdalnym; kontrola, że starsze dane nie są nadpisane |
| Aktualizacja programu przez SQL | **Zaimplementowane w kodzie** | Publikacja instalatora serwera w `dbo.program_update`, sprawdzenie wersji, pobranie, weryfikacja integralności, instalacja po zgodzie, ręczna numeracja wersji | Test end-to-end: serwer, klient, tryb offline, odmowa aktualizacji i ponowny start |
| Ustawienia interfejsu i okien | **Zaimplementowane w kodzie** | Lokalna pamięć rozmiaru/położenia głównego okna i modalnych dialogów; skala 80–200%; kolumny karty/terminów | Test WebView2 po zmianie DPI, monitorów, wielokrotnym otwieraniu; pełne parametry kolumn wszystkich list nie są potwierdzone |

## Najważniejsze zaległości — wyłącznie z końcowego WinForms

### P0 — blokujące gotowość do zastąpienia WinForms
1. **PDF i e-mail zapisanej karty**: generowanie i zapis pliku, pobieranie e-maila płatnika z Subiekta oraz nazwa PDF oparta na numerze źródłowego dokumentu.
2. **Testy na rzeczywistym Windows/SQL/Subiekt**: nowe stanowisko, wielostanowiskowość, dane kliniczne, transakcje, wydruki i urzędowe PDF.
3. **Instalacja/aktualizacja end-to-end**: utworzenie bazy i import na nowym serwerze; uaktualnienie istniejącej instalacji **bez utraty danych**; klient zdalny i propagacja wersji.
4. **Backup i restore**: test integralności, uprawnień, folderu/pendrive, Windows Hello i awarii.

### P1 — niepełny parytet codziennych funkcji
1. **Nowa kartoteka katalogu SQL** i lista produktów Subiekta wymagających uzupełnienia.
2. **Personalizacja tabel**, tam gdzie końcowy WinForms oferował zmianę widoczności, kolejności, szerokości, czcionek i parametrów listy; React ma obecnie niepełną implementację różnych tabel.
3. **Kontrola ostrzeżeń przed kartą** i korekt po zmianie dawkowania/karencji; przetestować niejednoznaczne wskazania, gatunki i warianty.
4. **Zachowanie archiwum na dwóch stanowiskach**: zapis/odczyt, konflikt, ponowny wydruk, kosz i trwałe usuwanie.
5. **Prędkość SQL**: pomiar działania listy Subiekta i buforów na rzeczywistej bazie, bez ciężkich zapytań przy każdym kliknięciu.

### P2 — weryfikacja i dopracowanie
- Każde okno dialogowe jest przesuwalne i skalowalne, jego położenie i rozmiar osobno zapamiętywane na każdym stanowisku; wymagane fizyczne testy DPI, dwóch monitorów i po restarcie.
- Strony A4 i legenda cennika: fizyczny podgląd i druk wielostronicowy.
- Spójność skrótów klawiaturowych, sortowania i ergonomii tabel z końcowym WinForms (bez odtwarzania jego stylów).

## GitHub Actions — weryfikacja

Odczytane przebiegi #215–227: **12 success, 1 failure (#215)**. Przyczyną nieudanego #215 była później usunięta literówka C# w nazwie pliku JSON. Najnowszy **#227 jest success**, podobnie #216–226. Workflow buduje frontend TypeScript, host WebView2 Windows, uruchamia testy .NET, publikuje Windows preview i robi smoke test Inno Setup.

Nie ma dowodu, że przeprowadzono produkcyjny test dwóch stanowisk SQL, drukarki, adresu pocztowego płatnika, fizycznego backup/restore ani pełnej instalacji na czystym komputerze.

## Ścisła zasada na następne etapy

**Przenosić tylko funkcje potwierdzone w końcowym `winforms-backup-2026-11-19`.** Funkcje nowe zgłaszać najpierw do akceptacji. Wszelkie już istniejące, lecz niepotwierdzone w WinForms elementy React powinny pozostać bez dalszej rozbudowy do czasu decyzji. Nie kopiować motywów/wyglądu starego WinForms.

**Źródła w repo:** `src/KartyLeczenia.WinForms/MainForm.cs`, `ExpiryTable.cs`, `ListParameters.cs` na gałęzi kopii oraz `ui/react/src/App.tsx`, `CataloguePage.tsx`, `PricePrint.tsx`, `src/KartyLeczenia.ReactKL/*.cs`, `installer/KartyLeczenia.ReactKL.iss`, `.github/workflows/reactkl-checks.yml` na gałęzi ReactKL.
