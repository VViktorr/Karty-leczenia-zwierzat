using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace KartyLeczenia.Windows;

internal sealed record SavedTreatmentCard(string Id, DateTime Date, string Client, string Document, int? PayerId, string Scope);

internal static class SavedTreatmentCardStore
{
    static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "saved-treatment-cards.sqlite");

    internal static void Save(Guid id, DocumentData source, DateTime date, Card card, Card printCard)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS treatment_cards (
                id TEXT PRIMARY KEY NOT NULL,
                source_scope TEXT NOT NULL,
                payer_id INTEGER,
                client TEXT NOT NULL,
                document TEXT NOT NULL,
                treatment_date TEXT NOT NULL,
                search_text TEXT NOT NULL,
                card_json TEXT NOT NULL,
                print_json TEXT NOT NULL
            );
            INSERT INTO treatment_cards (id, source_scope, payer_id, client, document, treatment_date, search_text, card_json, print_json)
            VALUES ($id, $scope, $payer, $client, $document, $date, $search, $card, $print)
            ON CONFLICT (id) DO UPDATE SET
                treatment_date = excluded.treatment_date,
                card_json = excluded.card_json,
                print_json = excluded.print_json;
            """;
        string client = string.Join(", ", source.Payer.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$scope", source.Scope);
        command.Parameters.AddWithValue("$payer", (object?)source.PayerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$client", client);
        command.Parameters.AddWithValue("$document", source.Number);
        command.Parameters.AddWithValue("$date", DateKey(date));
        command.Parameters.AddWithValue("$search", (client + " " + source.Number).ToUpperInvariant());
        command.Parameters.AddWithValue("$card", JsonSerializer.Serialize(card));
        command.Parameters.AddWithValue("$print", JsonSerializer.Serialize(printCard));
        command.ExecuteNonQuery();
    }

    internal static List<SavedTreatmentCard> Find(string query, DateTime? date)
    {
        var cards = new List<SavedTreatmentCard>();
        if (!File.Exists(DatabasePath)) return cards;

        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT id, treatment_date, client, document, payer_id, source_scope FROM treatment_cards
            WHERE ($query = '' OR instr(search_text, $query) > 0)
              AND ($date IS NULL OR treatment_date = $date)
            ORDER BY treatment_date DESC, rowid DESC
            """;
        command.Parameters.AddWithValue("$query", query.ToUpperInvariant());
        command.Parameters.AddWithValue("$date", date.HasValue ? DateKey(date.Value) : (object)DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            cards.Add(new(reader.GetString(0),
                DateTime.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(5)));
        return cards;
    }

    internal static Card Read(string id, bool forPrint)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = forPrint
            ? "SELECT print_json FROM treatment_cards WHERE id = $id"
            : "SELECT card_json FROM treatment_cards WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteScalar() is not string json)
            throw new InvalidOperationException("Nie znaleziono zapisanej karty.");
        return JsonSerializer.Deserialize<Card>(json)
            ?? throw new InvalidOperationException("Zapisana karta jest pusta.");
    }

    static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, DefaultTimeout = 2
        }.ToString());
        try
        {
            db.Open();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }
}
