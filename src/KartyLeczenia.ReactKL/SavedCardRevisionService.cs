using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Edits existing SQL cards without consuming a new card number. The caller
/// can change selected editorial/clinical fields but never document identity,
/// source symbol, existing stamps or unrelated JSON metadata. SQL row lock and
/// SHA-256 revision prevent overwriting a concurrent workstation's changes.
/// </summary>
internal sealed class SavedCardRevisionService(ConnectionSettingsStore settings)
{
    internal sealed record SaveResult(string Id, string Number, string Revision);

    internal static string Revision(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    internal static readonly string[] EditableRows =
    [
        "Drukuj", "Seria", "Gatunek", "Droga podania",
        "Dawkowanie i okres stosowania", "Karencja",
        "Liczba zwierząt", "Opis zwierząt / numer siedziby stada",
        "Rozpoznanie", "Zamiennik produktu", "Sekcja (II lub III)",
        "Zalecenia / uwagi"
    ];

    private static readonly HashSet<string> ClinicalFields =
    [
        "Gatunek", "Droga podania", "Dawkowanie i okres stosowania", "Karencja"
    ];

    internal static Card ApplyChanges(
        Card original, IReadOnlyDictionary<string,string> fields,
        IReadOnlyList<Dictionary<string,string>> requested,
        int? archiveYear, long? archiveSequence, string? requestedNumber = null)
    {
        if (requested.Count != original.Rows.Count || requested.Count > 500)
            throw new InvalidOperationException("Liczba pozycji karty nie może się zmienić podczas aktualizacji.");
        var updatedFields = new Dictionary<string,string>(original.Fields);
        foreach (string key in new[] { "Wyniki badań", "Zgłoszenie — data i godzina",
                                      "Wykonanie — data i godzina" })
        {
            if (!fields.TryGetValue(key, out var value)) continue;
            if (value is null || value.Length > 2000)
                throw new ArgumentException("Nieprawidłowa długość pola: " + key);
            updatedFields[key] = value.Trim();
        }
        static DateTime? ReadDate(string date, bool required)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                if (required) throw new ArgumentException("Data wykonania nie może być pusta.");
                return null;
            }
            return DateTime.TryParseExact(date, "dd.MM.yyyy HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed : throw new ArgumentException(
                    "Wprowadź datę i godzinę w formacie dd.MM.rrrr gg:mm.");
        }
        _ = ReadDate(updatedFields.GetValueOrDefault("Zgłoszenie — data i godzina", ""), false);
        DateTime performed = ReadDate(
            updatedFields.GetValueOrDefault("Wykonanie — data i godzina", ""), true)!.Value;
        if (archiveYear is int year && performed.Year != year)
            throw new ArgumentException(
                "Zmiana roku wykonania wymaga oddzielnej procedury numeracji. " +
                "Rok zapisanej karty nie może zostać zmieniony.");
        if (archiveYear is int y && archiveSequence is long n)
        {
            var proposed = requestedNumber is null ? (y, n) :
                ValidateCardNumber(requestedNumber, y);
            updatedFields["Nr karty"] = $"K/{proposed.Item2}/{proposed.Item1:D4}";
        }
        else if (requestedNumber is not null)
            throw new InvalidOperationException("Ta karta nie posiada numeru SQL, który można zmienić.");

        var updatedRows = new List<Dictionary<string,string>>(requested.Count);
        for (int i = 0; i < requested.Count; i++)
        {
            var source = original.Rows[i];
            var newValues = new Dictionary<string,string>(source);
            var edited = requested[i];
            foreach (var key in EditableRows)
            {
                if (!edited.TryGetValue(key, out var value)) continue;
                if (value is null || value.Length > 3000)
                    throw new ArgumentException($"Pozycja {i + 1}: nieprawidłowa długość pola {key}.");
                value = value.Trim();
                if (key == "Drukuj")
                {
                    if (!bool.TryParse(value, out var selected))
                        throw new ArgumentException($"Pozycja {i + 1}: niepoprawny wybór do wydruku.");
                    value = selected.ToString();
                }
                if (key == "Sekcja (II lub III)" && value is not ("II" or "III"))
                    throw new ArgumentException($"Pozycja {i + 1}: wybierz sekcję II albo III.");
                if (ClinicalFields.Contains(key) &&
                    !string.Equals(source.GetValueOrDefault(key), value, StringComparison.Ordinal))
                {
                    newValues["Status karencji"] =
                        "Zmieniono ręcznie — sprawdź przed wydrukiem";
                    newValues["Automatyczna karencja"] = "";
                    newValues["Automatyczne dawkowanie"] = "";
                }
                newValues[key] = value;
            }
            if (newValues.GetValueOrDefault("Drukuj") == "True")
            {
                foreach (var key in new[] { "Produkt", "Gatunek", "Droga podania",
                                             "Dawkowanie i okres stosowania", "Rozpoznanie" })
                    if (string.IsNullOrWhiteSpace(newValues.GetValueOrDefault(key)))
                        throw new ArgumentException($"Pozycja {i + 1}: uzupełnij {key}.");
            }
            updatedRows.Add(newValues);
        }
        CardArchiveService.ValidateAnimalCounts(updatedRows);
        var updated = original with { Fields = updatedFields, Rows = updatedRows };
        // Validate the same print payload before touching the SQL record.
        _ = CardHtmlRenderer.Render(updated);
        return updated;
    }

    internal static (int, long) ValidateCardNumber(string requested, int year)
    {
        var parsed = SavedCardSearch.ParseNumber(requested);
        if (parsed is null)
            throw new ArgumentException("Wpisz numer karty w formacie K/numer/rok (np. K/25/2026).");
        if (parsed.Value.Year != year)
            throw new ArgumentException("Nie można zmienić roku numeracji zapisanej karty.");
        return (parsed.Value.Year, parsed.Value.Sequence);
    }

    internal SaveResult Update(string id, string expectedRevision,
        IReadOnlyDictionary<string,string> fields,
        IReadOnlyList<Dictionary<string,string>> rows,
        string? requestedNumber = null)
    {
        if (!Guid.TryParseExact(id, "N", out var cardId) ||
            expectedRevision.Length != 64 ||
            !expectedRevision.All(Uri.IsHexDigit))
            throw new ArgumentException("Nieprawidłowy identyfikator lub wersja zapisanej karty.");
        var options = settings.Load().Options ??
            throw new InvalidOperationException("Brak połączenia do bazy KartyLeczenia.");
        using var db = new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia"))
            { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
        db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        // Serialize number changes and new-card allocations across all stations.
        using (var guard = new SqlCommand("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'KartyLeczenia.CardNumber',
              @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
            IF @result<0 THROW 50016, N'Inne stanowisko zapisuje kartę. Spróbuj ponownie.', 1;
            """, db, tx) { CommandTimeout = 15 })
            guard.ExecuteNonQuery();
        string currentJson;
        int? year;
        long? sequence;
        using (var read = new SqlCommand("""
            SELECT card_json, card_year, card_sequence
            FROM dbo.treatment_cards WITH (UPDLOCK, ROWLOCK)
            WHERE id=@id AND deleted_at_utc IS NULL;
            """, db, tx) { CommandTimeout = 15 })
        {
            read.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = cardId;
            using var reader = read.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Nie znaleziono aktywnej karty. Sprawdź archiwum.");
            currentJson = reader.GetString(0);
            year = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            sequence = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        }
        if (!string.Equals(Revision(currentJson), expectedRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Karta została zmieniona na innym stanowisku. Zamknij edycję i otwórz ją ponownie.");
        var original = JsonSerializer.Deserialize<Card>(currentJson) ??
            throw new InvalidDataException("Zapisana karta zawiera nieprawidłowy JSON.");
        var updated = ApplyChanges(original, fields, rows, year, sequence, requestedNumber);
        long? newSequence = requestedNumber is null ? sequence :
            ValidateCardNumber(requestedNumber, year ??
                throw new InvalidOperationException("Karta nie ma roku numeracji.")).Item2;
        if (newSequence != sequence)
        {
            // Include cards in the trash: restoring one must never create a collision.
            using var collision = new SqlCommand("""
                SELECT TOP(1) id FROM dbo.treatment_cards WITH (UPDLOCK,HOLDLOCK)
                WHERE card_year=@year AND card_sequence=@sequence AND id<>@id;
                """, db, tx) { CommandTimeout = 15 };
            collision.Parameters.Add("@year", SqlDbType.Int).Value = year!.Value;
            collision.Parameters.Add("@sequence", SqlDbType.BigInt).Value = newSequence!.Value;
            collision.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = cardId;
            if (collision.ExecuteScalar() is not null)
                throw new InvalidOperationException(
                    "Ten numer karty jest już zajęty (także przez kartę w koszu).");
        }
        string json = JsonSerializer.Serialize(updated);
        DateTime performed = DateTime.ParseExact(
            updated.Fields["Wykonanie — data i godzina"],
            "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        using (var save = new SqlCommand("""
            UPDATE dbo.treatment_cards
            SET card_json=@card,print_json=@card,treatment_date=@date,card_sequence=@sequence
            WHERE id=@id AND deleted_at_utc IS NULL;
            """, db, tx) { CommandTimeout = 15 })
        {
            save.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = cardId;
            save.Parameters.Add("@card", SqlDbType.NVarChar,-1).Value = json;
            save.Parameters.Add("@date", SqlDbType.Date).Value = performed.Date;
            save.Parameters.Add("@sequence", SqlDbType.BigInt).Value =
                (object?)newSequence ?? DBNull.Value;
            if (save.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Karta została przeniesiona lub zmieniona.");
        }
        tx.Commit();
        return new(id, updated.Fields.GetValueOrDefault("Nr karty", ""), Revision(json));
    }
}
