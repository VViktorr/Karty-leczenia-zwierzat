# Automatyczna aktualizacja katalogu SQL przy instalacji serwera

## Zasada działania

Od tej zmiany instalator w trybie **Serwer**, także przy aktualizacji istniejącej instancji,
wywołuje `KartyLeczenia.CatalogImport.exe`. Gdy katalog jest już zaimportowany,
używa `--apply-update`; pierwszy import działa bez tej flagi.

1. Importer kopiuje z dostarczonej SQLite **tylko nowe symbole**. Nie nadpisuje
   istniejących kartotek, wariantów ani ręcznych zmian.
2. Następnie przetwarza po kolei `catalogue-additions/*.json`, które są
   dołączane do `catalog-seed-win-x64.zip` podczas budowania instalatora.
   Każdy manifest może zawierać kartotekę i kilka wariantów.
3. Wpis manifestu dodaje kartotekę tylko wtedy, gdy nie istnieje; warianty
   sprawdza po parze **gatunek + droga podania**. Istniejące wpisy są
   pozostawione bez zmian.
4. PDF dostarczany z paczki instalatora trafia do `dbo.catalog_documents`
   pod oryginalną ścieżką. Jeżeli w SQL istnieje inny plik pod tą samą
   ścieżką, import jest przerywany zamiast nadpisania.
5. Cała aktualizacja odbywa się w jednej transakcji SQL: w przypadku błędu
   nie pozostają częściowe wpisy.

**Vigophos**: `scripts/catalogue-additions/000002691-vigophos.json`;
PDF: `data/ChPL/RPL/Charakterystyka-40841-2025-10-23-6235_W-2025-12-03.pdf`.

**Ważne:** wpis w programie wykorzystuje stężenie cyjanokobalaminy
`0,05 mg/ml` zgodne z ChPL. W Subiekcie symbol `000002691` ma zgłoszoną
nazwę z `0,5 mg/ml`; nazwa w Subiekcie NIE jest zmieniana automatycznie.
Należy sprawdzić etykietę/opakowanie i skorygować nazwę w Subiekcie osobno.

## Wdrożenie

1. Wykonać kopię bezpieczeństwa bazy `KartyLeczenia`.
2. Na komputerze serwera SQL pobrać najnowszą gałąź `winforms`.
3. Zbudować nowy instalator: `./installer/Build-WinForms.ps1`
   (PowerShell na Windows, .NET 10, Inno Setup 7).
4. **Najpierw uruchomić nowy instalator w trybie Serwer na komputerze z SQL**,
   wskazując istniejącą instancję. Instalator zaktualizuje centralny katalog.
5. Następnie uruchomić lub zaktualizować program na stanowiskach klienckich.
   Nie ma potrzeby importowania katalogu na każdym komputerze.
6. Sprawdzić efekt:
```sql
USE KartyLeczenia;
SELECT symbol, name, rpl_id, status
FROM dbo.catalog_products WHERE symbol = N'000002691';

SELECT species, route, dose, edible_tissues, milk, pdf_path
FROM dbo.catalog_variants WHERE symbol = N'000002691'
ORDER BY species, route;

SELECT path, DATALENGTH(pdf) AS PDFBytes
FROM dbo.catalog_documents
WHERE path = N'data/ChPL/RPL/Charakterystyka-40841-2025-10-23-6235_W-2025-12-03.pdf';
```

## Kolejne leki

Każda następna kartoteka wymagająca automatycznego dodania otrzymuje
osobny JSON w `scripts/catalogue-additions/` i istniejący lub nowy PDF
w `data/ChPL/`. Kompilator instalatora dołączy JSON i PDF automatycznie.
Dane leków powinny być weryfikowane według polskiej aktualnej ChPL przed
dodaniem manifestu. Nie należy wykonywać aktualizacji całej bazy SQL z
nadpisaniem zmian wprowadzonych przez użytkowników.

## Kontrole przed wydaniem

Kod nie był skompilowany w środowisku tej zmiany (brak .NET SDK/Windows).
Wymagane testy na Windows: kompilacja katalogu importera, instalacja na
bazie testowej, ponowna instalacja bez duplikatów, próba przy już istniejącej
kartotece z ręcznymi zmianami, podgląd `--preview-update`, przerwanie
importu przy błędnym PDF, test wielu stanowisk.
