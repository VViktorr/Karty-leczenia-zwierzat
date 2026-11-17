# KartyLeczenia

Aplikacja dla Windows rozwijana w **Windows Forms (WinForms)**, w C# na .NET 10, z odczytem danych z Subiekta GT i lokalnym katalogiem leków SQLite.

**Aktywna i domyślna gałąź to `winforms`. Bieżące zmiany interfejsu wprowadzamy w projekcie [KartyLeczenia.WinForms](src/KartyLeczenia.WinForms).**

## Budowanie na Windows

Wymagany jest .NET 10 SDK.

```powershell
dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release
```

## Projekty i dane

- [`src/KartyLeczenia.WinForms`](src/KartyLeczenia.WinForms) — aktywna aplikacja Windows Forms.
- [`src/KartyLeczenia.Core`](src/KartyLeczenia.Core) — wspólna biblioteka, m.in. odczyt Subiekta GT.
- [`src/KartyLeczenia`](src/KartyLeczenia) — wcześniejszy interfejs .NET MAUI. Projekt WinForms korzysta z przechowywanych tu zasobów, w tym bazy `medicine-catalog.sqlite`.
- [Gałąź `main`](https://github.com/VViktorr/Karty-leczenia-zwierzat/tree/main) — wcześniejszy etap projektu .NET MAUI i jego dokumentacja.

## Wspólne archiwum WinForms

Program otwiera dwa połączenia SQL. Połączenie z Subiektem GT służy do odczytu.
Karty i opisy zwierząt są zapisywane w osobnej bazie `KartyLeczenia`, która
może działać na innym komputerze z SQL Server, w tym SQL Server Express.
Katalog leków pozostaje lokalnym plikiem SQLite dostarczanym z programem.

1. Na komputerze przeznaczonym na archiwum utwórz pustą bazę
   `KartyLeczenia` i uruchom w niej [skrypt konfiguracji](scripts/Setup-SharedArchive.sql)
   z uprawnieniami administratora. Skrypt tworzy tabele i rolę bazodanową.
2. Dla konta Windows lub loginu SQL używanego przez program utwórz użytkownika
   w bazie `KartyLeczenia` i dodaj go do roli `KartyLeczeniaOperator`.
   Na przykład dla istniejącego użytkownika bazy `operator_archiwum`:
   `ALTER ROLE KartyLeczeniaOperator ADD MEMBER [operator_archiwum];`.
   Konto aplikacji potrzebuje dostępu sieciowego do instancji i uprawnień tej roli.
3. Na każdym stanowisku w zakładce **Ustawienia → Archiwum SQL**
   podaj adres instancji archiwum, wybierz uwierzytelnianie i zapisz połączenie.
   W ustawieniach Subiekta użyj tej samej nazwy serwera i bazy podmiotu na każdym
   stanowisku. Program wykorzystuje te nazwy do przypisywania opisów i płatników.
   Zachowaj nazwy używane przy tworzeniu importowanych danych, żeby utrzymać te powiązania.
4. Na każdym stanowisku z wcześniejszymi danymi wybierz w ustawieniach
   **Importuj lokalne karty i opisy…**. Wskaż folder zawierający
   `saved-treatment-cards.sqlite` lub `animal-descriptions.sqlite`.
   Domyślny folder to `%LOCALAPPDATA%\KartyLeczenia`. Import zostawia pliki
   źródłowe i można go ponowić. Karty o tym samym identyfikatorze są pomijane,
   a dla tego samego opisu zachowywany jest wpis z nowszą datą użycia.

Nazwa i adres lecznicy oraz dane lekarza są wspólne dla stanowisk. Program
pobiera je po połączeniu z archiwum. W zakładce **Ustawienia → Dane lecznicy**
można je edytować lub pobrać na nowo. Gdy archiwum jest puste, pierwsze
stanowisko, na którym wcześniej zapisano dane lecznicy, uzupełnia je
automatycznie. Przed aktualizacją stanowisk uruchom nowy instalator w trybie
**Serwer** na dotychczasowej instancji albo wykonaj aktualny skrypt
[Setup-SharedArchive.sql](scripts/Setup-SharedArchive.sql) w bazie archiwum
jako administrator.

Komputer z SQL Server musi być dostępny podczas pracy ze wspólnym archiwum.
Lokalna wersja aplikacji jest dostępna na gałęzi
[`KarLecz_lokalnie`](https://github.com/VViktorr/Karty-leczenia-zwierzat/tree/KarLecz_lokalnie).

## Instalator WinForms z wyborem stanowiska

Otwórz w Inno Setup 7 plik [KartyLeczenia.WinForms.iss](installer/KartyLeczenia.WinForms.iss)
po opublikowaniu programu w folderze `publish-winforms`. Przykład publikacji i kompilacji
z katalogu repozytorium:

```powershell
dotnet publish src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release -r win-x64 --self-contained true -o publish-winforms
# Uruchom z katalogu repozytorium, używając ISCC.exe z instalacji Inno Setup 7.
ISCC.exe /DReleaseVersion=1.0.0 installer\KartyLeczenia.WinForms.iss
```

Plik `KartyLeczenia-Setup-win-x64-v1.0.0.exe` pojawi się w głównym
katalogu repozytorium. Użyj tego samego pliku na serwerze i stanowiskach.
Domyślny katalog programu to `C:\Program Files\KartyLeczenia`.
Instalator tworzy folder i skrót **KartyLeczenia** w menu Start dla wszystkich
użytkowników. Skrót na pulpicie dodaje po zaznaczeniu opcji w kreatorze.
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
programu pozostawia instancję SQL i archiwum. W razie aktualizacji z lokalnej
wersji użyj importu w ustawieniach. Poprzednia aplikacja instalowana w
`%LOCALAPPDATA%` może pozostać obok nowej. Skróty nowej aplikacji mają nazwę
**KartyLeczenia**.
