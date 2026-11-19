# ReactKL — uruchamialny interfejs testowy

React + TypeScript + Ant Design wewnątrz Windows WebView2. Dane SQL są obsługiwane przez istniejący kod C#.

## Jak zbudować

1. W katalogu `ui/react`: wykonaj `npm install`, następnie `npm run build`.
2. W katalogu głównym wykonaj `dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release`.
3. Uruchom program z parametrem `--react` (np. `KartyLeczenia.WinForms.exe --react`).
4. Jeśli ustawienia połączenia Subiekta nie są zapisane, uruchom zwykły WinForms i skonfiguruj je najpierw tam.

Build .NET kopiuje wcześniej zbudowany frontend do katalogu `react-ui` obok programu.

## Wybór skórki

Układy: Modern (menu boczne), Classic (zakładki u góry), Fluent (Windows 11).
Motywy barwne: jasny, ciemny, morski. Układ i kolor zapamiętywane są w profilu WebView2 na stanowisku.

## Etap pierwszy

- Lista FS / PA / WZ z rzeczywistymi danymi Subiekta i sortowaniem.
- Wyszukiwanie, filtry rodzaju, okresu i podgląd pozycji dokumentu.
- Przycisk umożliwiający uruchomienie pełnej funkcjonalności w niezmienionym WinForms.

**Nieukończone:** edytor i wydruk karty leczenia, cennik, zapisane karty, terminy ważności, archiwizacja i aktualizacje pozostają w WinForms. Nie wyłączamy ich przed migracją.

## Bezpieczeństwo

React nie przechowuje haseł SQL. WebView2 dopuszcza tylko lokalny wirtualny host, a most C# akceptuje tylko jawne polecenia searchDocuments, readDocument, openClassic. Frontend nie korzysta z CDN ani z zewnętrznego serwera WWW.

Do pełnego wdrożenia potrzebne są testy z prawdziwym SQL Serverem i aktualizacja workflow produkcyjnego instalatora. Numer aplikacji pozostaje pod ręczną kontrolą.
