# Karty leczenia zwierząt

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
