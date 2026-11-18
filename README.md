# KartyLeczenia

Aplikacja dla Windows rozwijana w **Windows Forms (WinForms)**, w C# na .NET 10, z odczytem danych z Subiekta GT i wspólnym katalogiem leków w SQL Server.

**Aktywna i domyślna gałąź to `winforms`. Bieżące zmiany interfejsu wprowadzamy w projekcie [KartyLeczenia.WinForms](src/KartyLeczenia.WinForms).**

## Budowanie na Windows

Wymagany jest .NET 10 SDK.

```powershell
dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release
```

## Projekty i dane

- [`src/KartyLeczenia.WinForms`](src/KartyLeczenia.WinForms) — aktywna aplikacja Windows Forms.
- [`src/KartyLeczenia.Core`](src/KartyLeczenia.Core) — wspólna biblioteka, m.in. odczyt Subiekta GT.
- [`src/KartyLeczenia`](src/KartyLeczenia) — wcześniejszy interfejs .NET MAUI. Plik `medicine-catalog.sqlite` zasila pierwszy import na serwerze.
- [Gałąź `main`](https://github.com/VViktorr/Karty-leczenia-zwierzat/tree/main) — wcześniejszy etap projektu .NET MAUI i jego dokumentacja.

## Wspólne archiwum WinForms

Program łączy się z bazą Subiekta GT do odczytu kartotek, dokumentów, cen i
stanów oraz ze wspólną bazą SQL `KartyLeczenia`. Wspólna baza przechowuje
katalog leków, warianty, karencje, PDF-y ChPL, ustawienia lecznicy,
zapamiętane wybory leczenia, karty i opisy zwierząt.

1. Na serwerze uruchom instalator w trybie **Serwer**. Wybierz tę samą
   instancję, jeśli masz już bazę `KartyLeczenia`. Instalator uzupełni schemat
   i zaimportuje katalog z pakietu: SQLite wraz z PDF-ami ChPL. Import
   dochodzi do skutku w jednej transakcji. Po jego zakończeniu kolejne
   instalacje zostawiają katalog i zmiany użytkowników w bazie.
2. Na każdym stanowisku w **Ustawienia → Archiwum SQL** zapisz połączenie
   do tej samej instancji. W zakładce **Subiekt GT** wybierz bazę podmiotu
   i magazyn. Użyj tej samej nazwy serwera i bazy podmiotu na stanowiskach,
   żeby zachować powiązania z zapisanymi kartami, wyborami i opisami.
3. W **Cennik leków → Dodaj / edytuj w katalogu** wyszukaj kartotekę
   Subiekta po symbolu lub nazwie. Wprowadź dane, dodaj warianty i dołącz
   ChPL do zaznaczonych wariantów. Zmiany będą dostępne na wszystkich
   stanowiskach po odświeżeniu cennika lub ponownym otwarciu karty.

`scripts/Setup-SharedArchive.sql` przygotowuje schemat i rolę operatora.
Pakiet importu tworzy skrypt budowania instalatora, a podczas konfiguracji
serwera uruchamia go `Configure-SharedArchive.ps1`. Aplikacja WinForms
wczytuje katalog z SQL. Podgląd ChPL otwiera tymczasowy plik z treścią pobraną
z serwera.

Dane lecznicy edytuje się w **Ustawienia → Dane lecznicy**. Jeśli wspólna
baza nie zawiera jeszcze tych danych, program może przenieść wcześniejsze
ustawienia lecznicy z tego stanowiska przy pierwszym połączeniu z archiwum.

## Instalator WinForms z wyborem stanowiska

Z głównego katalogu repozytorium uruchom skrypt z zainstalowanym .NET 10 SDK
i Inno Setup 7. Skrypt publikuje aplikację, tworzy pakiet pierwszego importu
i kompiluje instalator.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-WinForms.ps1 -Version 1.0.7
```

Gdy `ISCC.exe` jest w innej lokalizacji, wskaż ścieżkę parametrem `-Iscc`.

Plik `KartyLeczenia-Setup-win-x64-v1.0.7.exe` pojawi się w głównym
katalogu repozytorium. Użyj tego samego pliku na serwerze i stanowiskach.
Domyślny katalog programu to `C:\Program Files\KartyLeczenia`.
Instalator tworzy folder **KartyLeczenia** na liście wszystkich aplikacji menu
Start, ze skrótem do programu i skrótem **Ustawienia KartyLeczenia**. Skrót na pulpicie dodaje po zaznaczeniu opcji w kreatorze.
Brakujący WebView2 pobiera z Microsoftu. Kreator pokazuje pasek pobierania
i osobny pasek aktywności podczas instalowania składników. Wybór instancji,
logowania i hasła odbywa się w oknach dialogowych.

- **Serwer → Wybierz istniejącą instancję** — w oknie konfiguracji pokażą się
  lokalne instancje SQL Server z nazwą, wersją i stanem usługi. Wybierz
  uruchomioną instancję. Przy pierwszej instalacji kreator podpowiada tę opcję,
  jeśli wykryje działającą usługę SQL Server. Twoje konto Windows musi mieć uprawnienia
  administratora SQL (`sysadmin`). Po potwierdzeniu instalator utworzy
  osobną bazę `KartyLeczenia` albo użyje dotychczasowego archiwum o zgodnej
  strukturze. W instancji z logowaniem mieszanym można wybrać konto SQL lub
  konto albo grupę Windows. Instancja z logowaniem Windows wymaga konta lub
  grupy rozpoznawanej także na stanowiskach. Kreator nie zmienia trybu
  uwierzytelniania, usług, portów ani reguł zapory istniejącej instancji.
  Jeśli instancja nie udostępnia SQL po TCP w sieci, jej administrator
  przygotowuje dostęp ze stanowisk osobno.
- **Serwer → Zainstaluj lub użyj osobnej instancji KLARCHIVE** — po
  akceptacji licencji pobiera SQL Server 2025 Express, jeśli brakuje instancji `KLARCHIVE`. Przygotowuje osobną bazę `KartyLeczenia`,
  port TCP `51433` i regułę zapory dla lokalnej podsieci w profilach
  prywatnym i domenowym. Podczas pierwszej konfiguracji wpisz hasło dla
  konta SQL `KartyLeczeniaOperator` w oknie dialogowym (dokładnie sześć cyfr). Zachowaj je do
  ustawienia połączenia na stanowiskach. Instalator wyświetli adres archiwum.
  Jeśli instalacja nowego SQL Server zatrzyma się z błędem ODBC 17 lub 18 (1706),
  kreator pokaże instrukcję Microsoft i pozwoli otworzyć dziennik instalacji.
- **Stanowisko zdalne** — wklej adres wskazany w instalacji serwerowej,
  np. `SERWER,51433`, `SERWER,1433` albo `SERWER\INSTANCJA`. Sama nazwa
  komputera lub adres IPv4 oznacza port `51433`. Po otwarciu programu
  adres pojawi się w **Ustawienia → Archiwum SQL**. Wybierz metodę
  logowania ustawioną na serwerze. Dla logowania SQL wpisz hasło operatora,
  a następnie wybierz **Sprawdź i zapisz**. Połączenie używa szyfrowania i domyślnie weryfikuje certyfikat.
  Jeżeli dedykowana instancja korzysta z certyfikatu własnego, możesz jawnie
  zaznaczyć **Zaufaj certyfikatowi serwera archiwum** po sprawdzeniu adresu
  prywatnego serwera. Dane Subiekta ustawiasz osobno.

Ponowna instalacja zachowuje bazę i hasło istniejącego operatora. Odinstalowanie
programu pozostawia instancję SQL i archiwum. Skróty nowej aplikacji mają nazwę
**KartyLeczenia**.
