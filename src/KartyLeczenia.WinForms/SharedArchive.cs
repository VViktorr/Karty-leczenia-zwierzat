using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace KartyLeczenia.Windows;

internal sealed record SavedTreatmentCard(string Id, DateTime Date, string Client, string Document, int? PayerId, string Scope);

internal sealed class SharedArchive(ConnectionOptions options)
{
    internal const string DatabaseName = "KartyLeczenia";
    internal static string LocalDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DatabaseName);

    internal void Test()
    {
        using var db = Open();
        using var command = Command(db, """
            IF EXISTS (
                SELECT 1
                FROM (VALUES ('dbo.treatment_cards'), ('dbo.animal_descriptions')) AS t(name)
                CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE')) AS p(permission_name)
                WHERE ISNULL(HAS_PERMS_BY_NAME(t.name, 'OBJECT', p.permission_name), 0) <> 1
            )
                THROW 50001, N'Archiwum wymaga przygotowanych tabel i uprawnień SELECT, INSERT, UPDATE.', 1;
            """);
        command.ExecuteNonQuery();
    }

    internal void SaveCard(Guid id, DocumentData source, DateTime date, Card card, Card printCard)
    {
        string client = string.Join(", ", source.Payer.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        using var db = Open();
        using var transaction = db.BeginTransaction();
        WriteCard(db, transaction, id, source.Scope, source.PayerId, client, source.Number, date,
            (client + " " + source.Number).ToUpperInvariant(), JsonSerializer.Serialize(card),
            JsonSerializer.Serialize(printCard), updateExisting: true);
        transaction.Commit();
    }

    internal List<SavedTreatmentCard> FindCards(string query, DateTime? date)
    {
        using var db = Open();
        using var command = Command(db, """
            SELECT id, treatment_date, client, document, payer_id, source_scope
            FROM dbo.treatment_cards
            WHERE (@query = N'' OR CHARINDEX(@query, search_text) > 0)
              AND (@date IS NULL OR treatment_date = @date)
            ORDER BY treatment_date DESC, archive_order DESC;
            """);
        command.Parameters.Add("@query", SqlDbType.NVarChar, -1).Value = query.ToUpperInvariant();
        command.Parameters.Add("@date", SqlDbType.Date).Value = (object?)date?.Date ?? DBNull.Value;
        using var reader = command.ExecuteReader();
        var cards = new List<SavedTreatmentCard>();
        while (reader.Read())
            cards.Add(new(reader.GetGuid(0).ToString("N"), reader.GetDateTime(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(5)));
        return cards;
    }

    internal Card ReadCard(string id, bool forPrint)
    {
        using var db = Open();
        using var command = Command(db, forPrint
            ? "SELECT print_json FROM dbo.treatment_cards WHERE id = @id;"
            : "SELECT card_json FROM dbo.treatment_cards WHERE id = @id;");
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.ParseExact(id, "N");
        if (command.ExecuteScalar() is not string json)
            throw new InvalidOperationException("Nie znaleziono zapisanej karty.");
        return JsonSerializer.Deserialize<Card>(json)
            ?? throw new InvalidOperationException("Zapisana karta jest pusta.");
    }

    internal string[] ReadDescriptions(DocumentData card)
    {
        using var db = Open();
        using var command = Command(db, """
            SELECT TOP (100) description FROM dbo.animal_descriptions
            WHERE source_scope = @scope AND payer_id = @payer
            ORDER BY last_used DESC, description;
            """);
        command.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = card.Scope;
        command.Parameters.Add("@payer", SqlDbType.Int).Value = card.PayerId!.Value;
        using var reader = command.ExecuteReader();
        var descriptions = new List<string>();
        while (reader.Read()) descriptions.Add(reader.GetString(0));
        return descriptions.ToArray();
    }

    internal void SaveDescription(DocumentData card, string description)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        WriteDescription(db, transaction, card.Scope, card.PayerId!.Value, description,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        transaction.Commit();
    }

    internal (int Cards, int Descriptions) ImportLocalData(string folder)
    {
        string cardsPath = Path.Combine(folder, "saved-treatment-cards.sqlite");
        string descriptionsPath = Path.Combine(folder, "animal-descriptions.sqlite");
        if (!File.Exists(cardsPath) && !File.Exists(descriptionsPath))
            throw new InvalidOperationException("W wybranym folderze nie znaleziono lokalnej bazy kart ani opisów zwierząt.");

        using var db = Open();
        using var transaction = db.BeginTransaction();
        int cards = 0, descriptions = 0;
        if (File.Exists(cardsPath))
        {
            using var local = OpenLocal(cardsPath);
            using var command = local.CreateCommand();
            command.CommandText = """
                SELECT id, source_scope, payer_id, client, document, treatment_date, search_text, card_json, print_json
                FROM treatment_cards ORDER BY rowid;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                cards += WriteCard(db, transaction, Guid.ParseExact(reader.GetString(0), "N"), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                    DateTime.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetString(6), reader.GetString(7), reader.GetString(8), updateExisting: false);
        }
        if (File.Exists(descriptionsPath))
        {
            using var local = OpenLocal(descriptionsPath);
            using var command = local.CreateCommand();
            command.CommandText = "SELECT scope, payer_id, description, last_used FROM animal_descriptions;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                descriptions += WriteDescription(db, transaction, reader.GetString(0), reader.GetInt32(1),
                    reader.GetString(2), reader.GetInt64(3));
        }
        transaction.Commit();
        return (cards, descriptions);
    }

    static int WriteCard(SqlConnection db, SqlTransaction transaction, Guid id, string scope, int? payerId,
        string client, string document, DateTime date, string search, string card, string print, bool updateExisting)
    {
        using var command = Command(db, """
            IF EXISTS (SELECT 1 FROM dbo.treatment_cards WITH (UPDLOCK, HOLDLOCK) WHERE id = @id)
            BEGIN
                IF @update = 1
                    UPDATE dbo.treatment_cards SET treatment_date = @date, card_json = @card, print_json = @print
                    WHERE id = @id;
                SELECT 0;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.treatment_cards
                    (id, source_scope, payer_id, client, document, treatment_date, search_text, card_json, print_json)
                VALUES (@id, @scope, @payer, @client, @document, @date, @search, @card, @print);
                SELECT 1;
            END;
            """, transaction);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        command.Parameters.Add("@payer", SqlDbType.Int).Value = (object?)payerId ?? DBNull.Value;
        command.Parameters.Add("@client", SqlDbType.NVarChar, -1).Value = client;
        command.Parameters.Add("@document", SqlDbType.NVarChar, -1).Value = document;
        command.Parameters.Add("@date", SqlDbType.Date).Value = date.Date;
        command.Parameters.Add("@search", SqlDbType.NVarChar, -1).Value = search;
        command.Parameters.Add("@card", SqlDbType.NVarChar, -1).Value = card;
        command.Parameters.Add("@print", SqlDbType.NVarChar, -1).Value = print;
        command.Parameters.Add("@update", SqlDbType.Bit).Value = updateExisting;
        return (int)command.ExecuteScalar()!;
    }

    static int WriteDescription(SqlConnection db, SqlTransaction transaction, string scope, int payerId,
        string description, long lastUsed)
    {
        // Preserve SQLite NOCASE identity, which folds only ASCII letters.
        string normalized = string.Concat(description.Select(c => c is >= 'a' and <= 'z' ? char.ToUpperInvariant(c) : c));
        using var identity = new MemoryStream();
        using var keyWriter = new BinaryWriter(identity, Encoding.UTF8, leaveOpen: true);
        keyWriter.Write(scope);
        keyWriter.Write(payerId);
        keyWriter.Write(normalized);
        keyWriter.Flush();
        byte[] key = SHA256.HashData(identity.ToArray());
        using var command = Command(db, """
            IF EXISTS (SELECT 1 FROM dbo.animal_descriptions WITH (UPDLOCK, HOLDLOCK) WHERE description_key = @key)
            BEGIN
                UPDATE dbo.animal_descriptions SET description = @description, last_used = @time
                WHERE description_key = @key AND last_used < @time;
                SELECT @@ROWCOUNT;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.animal_descriptions (description_key, source_scope, payer_id, description, last_used)
                VALUES (@key, @scope, @payer, @description, @time);
                SELECT 1;
            END;
            """, transaction);
        command.Parameters.Add("@key", SqlDbType.Binary, 32).Value = key;
        command.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        command.Parameters.Add("@payer", SqlDbType.Int).Value = payerId;
        command.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = description;
        command.Parameters.Add("@time", SqlDbType.BigInt).Value = lastUsed;
        return (int)command.ExecuteScalar()!;
    }

    SqlConnection Open()
    {
        var connectionString = new SqlConnectionStringBuilder(options.BuildConnectionString(DatabaseName))
        {
            ApplicationIntent = ApplicationIntent.ReadWrite
        };
        var db = new SqlConnection(connectionString.ConnectionString);
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

    static SqliteConnection OpenLocal(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, DefaultTimeout = 2
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

    static SqlCommand Command(SqlConnection db, string sql, SqlTransaction? transaction = null)
        => new(sql, db, transaction) { CommandTimeout = 15 };
}
