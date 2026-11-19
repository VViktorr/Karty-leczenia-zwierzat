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
- Katalog SQL: wyszukiwanie produktów, edycja wariantów i karencji, podgląd PDF ChPL,
  dołączanie PDF do wielu wariantów i zapis z ochroną przed nadpisaniem zmian z innego stanowiska.
  Dodatkowo offline ekstrakcja ostrożnych propozycji z tekstowych ChPL (PdfPig):
  użytkownik widzi ostrzeżenia i fragmenty źródłowe i osobno akceptuje uzupełnienie
  wyłącznie pustych pól. **Propozycja nie jest zapisywana bezpośrednio do SQL**.
  Zachowano istniejące schematy SQL.
- Ustawienia połączenia Subiekt/SQL i dane lekarza.
- Archiwizacja: pojedynczy .bak w lokalizacji docelowej, tymczasowy plik SQL,
  weryfikacja SHA-256, postęp kopiowania, Windows Hello.

## Do ukończenia przed produkcyjnym zastąpieniem programu
- Testy GUI na Windows 11 (wygląd przy DPI 80–200%, ergonomia, klawiatura, wyświetlanie dużych dokumentów).
- Weryfikacja kliniczna automatycznego doboru gatunku, wskazania, dawkowania,
  karencji, źródeł ChPL i zapamiętywania wariantów.
- Weryfikacja poprawności propozycji ChPL z dokumentami urzędowymi,
  obsługa skanów bez warstwy tekstowej (obecnie wyraźny błąd, brak OCR), zaawansowana
  edycja źródeł i wszystkie operacje na kartotekach (np. usuwanie istniejących wariantów).
  Obecny edytor pozwala edytować, dodawać, dołączać PDF i ręcznie przyjmować propozycje.
- Odtwarzanie bazy z pendrive i publikacja/aktualizacja instalatora z SQL.
- Zaawansowane ustawienia wydruków i czyszczenie kosza po dodatkowej autoryzacji.
- Potwierdzenie zgodności druków z dotychczasowym programem.

Nie oznaczać wersji ReactKL jako produkcyjnej, dopóki nie przejdzie powyższych kontroli.
Automatyczne GitHub Actions sprawdza kompilację i testy regresji; **nie** testuje
rzeczywistego SQL Servera, pobierania lokalnych PDF ani jakości interfejsu na Windows 11.



## Aktualizacje i instalacja w miejsce poprzedniej wersji

**ReactKL zastępuje dotychczasową instalację** — ten sam Inno Setup AppId
`pl.kartyleczenia.winforms.sql`, katalog `C:\Program Files\KartyLeczenia`,
skróty i rola serwer/stanowisko w `archive-installation.ini`. Istniejący plik
konfiguracji archiwum jest zachowywany. Stare EXE i AntdUI.dll są usuwane
przy instalacji, ale pliki użytkownika, bazy SQLite, PDF i .bak nie są czyszczone.

Numer wersji jest zawsze podawany ręcznie:

```powershell
.\installer\Build-ReactKL.ps1 -Version 2.2.5
```

(Powyższe `2.2.5` to przykład — faktyczny numer wybiera operator.)
Wynik: `Instalatory\KartyLeczenia-Setup-win-x64-v<wersja>.exe`.
Dostępny również workflow `.github/workflows/reactkl-installer.yml` z
ręcznym polem wersji.

W trybie serwera instalator po zainstalowaniu kopiuje się tymczasowo do
`{app}\KartyLeczenia-Update-Pending-v<wersja>.exe`. Przy pierwszym uruchomieniu
ReactKL publikuje ten plik do **istniejącej** tabeli
`KartyLeczenia.dbo.program_update`, sprawdza wersję i sumę SHA-256, a po
sukcesie usuwa kopię oczekującą. Publikacja wymaga UAC oraz uprawnień SQL.
Jeżeli się nie uda, w Ustawieniach można użyć "Opublikuj instalator".

Stanowisko zdalne po uruchomieniu sprawdza tę samą tabelę i proponuje nowszą
wersję. Pobieranie/uruchomienie wymaga zgody użytkownika; sprawdzane są
SHA-256 i metadane pliku. Przy aktualizacji instalator nadpisuje poprzednią
instalację. **Nie publikujemy już do osobnej tabeli react_program_update.**

Nie zamieniaj instalatora produkcyjnego bez wcześniejszego sprawdzenia
z prawdziwym serwerem SQL i stacjami Windows.
