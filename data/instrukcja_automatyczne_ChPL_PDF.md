# Wczytywanie pól katalogu z ChPL (PDF)

## Gdzie jest funkcja

**Cennik leków → Leki do uzupełnienia → wybierz lek → Wczytaj dane z ChPL (PDF)**.

Program otwiera lokalny plik PDF i uruchamia analizę warstwy tekstowej za
pomocą biblioteki **PdfPig**. Plik nie jest wysyłany do zewnętrznej usługi.

Następnie wyświetla podgląd z odczytanymi sekcjami i ostrzeżeniami:
- 3.1 lub 4.1 – docelowe gatunki;
- 3.2 lub 4.2 – wskazania;
- 3.9 lub 4.9 – droga podania i dawkowanie;
- 3.12 lub 4.11 – okres karencji.

Po zatwierdzeniu propozycji nowe warianty pojawiają się w edytorze.
Dla istniejących wariantów uzupełniane są **wyłącznie puste pola**. Plik PDF
zostaje dołączony jako załącznik ChPL z sumą SHA-256 w nazwie.

Wartości w wariantach są **propozycją do weryfikacji**, nie autorytatywną
interpretacją ChPL. W szczególności:
- brak rozpoznanej drogi podania albo dawkowania wymaga ręcznego uzupełnienia
  przed zapisaniem nowego wariantu;
- dla dokumentów z wieloma gatunkami nie kopiujemy wspólnego dawkowania
  na każdy gatunek; muszą istnieć jednoznacznie oznaczone podsekcje;
- złożone karencje (drogi podania, wiek, trwanie leczenia, zasuszenie itp.)
  należy ręcznie odczytać z dokumentu; program nie skraca ich automatycznie;
- skany obrazowe bez warstwy tekstowej wymagają wcześniej OCR;
- **nic nie zapisuje się do SQL bez wybrania przycisku „Zapisz w SQL”.**

## Testy na Windows przed użyciem

```powershell
git pull --ff-only origin winforms
dotnet build src/KartyLeczenia.WinForms/KartyLeczenia.WinForms.csproj -c Release
```

Sprawdzić na kopii bazy:
1. PDF tekstowy z pojedynczym gatunkiem, weryfikacja pól i wskazań.
2. ChPL z wieloma gatunkami i karencją zależną od gatunku.
3. PDF skanowany – czy pokazuje informację o wymaganym OCR.
4. Istniejące ręcznie wypełnione pola – czy nic nie zostało nadpisane.
5. Anulowanie podglądu – czy nie ma zmian w SQL.
6. Zapis nowego wariantu oraz dołączonego PDF.
7. Ponowny odczyt identycznej ChPL – czy nie powstają duplikaty gatunek + droga.

To pierwszy, konserwatywny tryb automatycznego wyciągania pól.
Nie jest to model AI i nie gwarantuje rozumienia wszystkich struktur ChPL.
