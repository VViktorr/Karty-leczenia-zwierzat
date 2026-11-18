# Audyt kodu KartyLeczenia WinForms — 18.11.2026

**Gałąź:** `winforms`  
**Zakres:** statyczny przegląd krytycznych ścieżek w repozytorium: `MainForm.cs`, `AppearanceThemes.cs`, `ServerUpdateGate.cs`, `UpdateCredentials.cs`, `SharedArchive.cs`, `WindowLayout.cs`, `DialogPlacement.cs`, `ListParameters.cs`, `PricePrintPreview.cs`, `installer/KartyLeczenia.WinForms.iss` i skrypt publikacji.

## Błędy poprawione

1. **Czcionki motywów / zasoby GDI** — kolejne zastosowania motywu tworzyły nowy `Font` dla tabeli bez zwalniania poprzedniego. Od tej pory motyw przechowuje i zwalnia tylko własne obiekty czcionek.
2. **Zmiana wysokości wierszy** — motyw aktualizował `RowTemplate.Height`, ale nie wszystkie istniejące wiersze. Wysokości wierszy niezmienionych ręcznie są synchronizowane także przy przełączaniu motywów, a ręczne rozmiary pozostają zachowane.
3. **Błędny odczyt statusu sieciowego aktualizacji** — `File.Exists` zwracało `false` również przy niektórych problemach z dostępem SMB, myląc problem z logowaniem z brakiem wydania. Bezpośredni odczyt manifestu zachowuje rozróżnienie; brak samego pliku nadal nie blokuje uruchomienia programu.
4. **Nieobsłużone błędy wstępnego ładowania dokumentów** — uruchamiane przy `Shown` zadanie asynchroniczne jest objęte istniejącą obsługą wyjątków i logowaniem.
5. **Niespójne metadane kart leczenia** — ponowny zapis istniejącej karty odświeża teraz także `source_scope`, `payer_id`, `client`, `document` i `search_text`, zamiast wyłącznie daty i treści JSON.

## Sprawdzone, bez zmian

- Numerowanie kart odbywa się pod transakcyjnym `sp_getapplock` w SQL Server.
- Odczyt aktualizacji sprawdza SHA-256 przed uruchomieniem instalatora.
- Publikacja na serwerze umieszcza manifest po instalatorze i weryfikuje numer wydania.
- Zapis poświadczeń SMB korzysta z Windows Credential Manager, nie z pliku JSON aplikacji.
- Ustawienia okien i kolumn przechowywane są lokalnie dla stanowiska.

## Ryzyka i kolejne testy

**Nie można na tym etapie potwierdzić poprawności binarnej ani wizualnej:** środowisko tego audytu nie udostępnia Windows/.NET SDK/Inno Setup ani instancji SQL Server/Subiekt GT. Commity wymagają kompilacji i testów przed publikacją.

Priorytet testów Windows:
1. `dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release`.
2. Przełączanie każdego motywu i powrót do Klasycznego; zachowanie ręcznych szerokości i wysokości tabel.
3. Skalowanie programu 100/110/125/150%, Windows 100/125/150/200%; wszystkie formularze i okna dialogowe.
4. Aktualizacja między dwoma stanowiskami: udziały SMB, zapisane poświadczenia, nieosiągalny serwer, błędne uprawnienia, niezgodny SHA-256.
5. Archiwum SQL: dwa stanowiska jednocześnie zapisujące karty; aktualizacja karty; kosz; przywracanie; kopia i odtworzenie na testowej bazie.
6. Subiekt GT: dokumenty FS/PA/WZ, adres płatnika, obsługa braku sieci i długich zapytań.
7. Wydruki i PDF: paginacja, karencje, dane lecznicy/lekarza, nazwy plików, wysyłka e-mail.

**Bezpieczne wdrożenie:** wykonać kopię danych SQL, nie publikować nowego instalatora, dopóki kompilacja i testy regresji nie przejdą pomyślnie. Ten dokument opisuje pierwszy etap statycznego audytu, nie kompletną certyfikację programu.
