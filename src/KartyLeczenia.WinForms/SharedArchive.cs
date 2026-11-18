using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed record SavedTreatmentCard(string Id, DateTime Date, string Client, string Document);

internal sealed partial class SharedArchive(ConnectionOptions options)
{
    internal const string DatabaseName = "KartyLeczenia";
    internal string Server => options.Server;
    internal static string LocalDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DatabaseName);

    internal static string ReadInstalledServer()
    {
        try
        {
            return File.ReadLines(Path.Combine(AppContext.BaseDirectory, "archive-installation.ini"))
                .FirstOrDefault(line => line.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))?[7..].Trim() ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

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
        RequireImportedCatalogue(db);
    }

    internal ClinicSettings? LoadClinic(ClinicSettings local)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        if (!string.IsNullOrWhiteSpace(local.Name) || !string.IsNullOrWhiteSpace(local.Address) ||
            !string.IsNullOrWhiteSpace(local.Doctor) || !string.IsNullOrWhiteSpace(local.Phone) ||
            local.DoctorStampPng is { Length: > 0 } || local.ClinicStampPng is { Length: > 0 })
            WriteClinic(db, transaction, local, replaceExisting: false);

        ClinicSettings? clinic;
        using (var command = Command(db, """
            SELECT name, address, doctor, use_doctor_stamp, doctor_stamp_png, use_clinic_stamp, clinic_stamp_png, phone
            FROM dbo.clinic_settings WHERE id = 1;
            """, transaction))
        using (var reader = command.ExecuteReader())
            clinic = reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            {
                UseDoctorStamp = reader.GetBoolean(3),
                DoctorStampPng = reader.IsDBNull(4) ? null : reader.GetFieldValue<byte[]>(4),
                UseClinicStamp = reader.GetBoolean(5),
                ClinicStampPng = reader.IsDBNull(6) ? null : reader.GetFieldValue<byte[]>(6),
                Phone = reader.GetString(7)
            } : null;

        transaction.Commit();
        return clinic;
    }

    internal void SaveClinic(ClinicSettings clinic)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        WriteClinic(db, transaction, clinic, replaceExisting: true);
        transaction.Commit();
    }

    static void WriteClinic(SqlConnection db, SqlTransaction transaction, ClinicSettings clinic, bool replaceExisting)
    {
        using var command = Command(db, """
            IF EXISTS (SELECT 1 FROM dbo.clinic_settings WITH (UPDLOCK, HOLDLOCK) WHERE id = 1)
            BEGIN
                IF @replace = 1
                    UPDATE dbo.clinic_settings SET name = @name, address = @address, doctor = @doctor, phone = @phone,
                        use_doctor_stamp = @use_stamp, doctor_stamp_png = @stamp,
                        use_clinic_stamp = @use_clinic_stamp, clinic_stamp_png = @clinic_stamp WHERE id = 1;
            END
            ELSE
                INSERT INTO dbo.clinic_settings (id, name, address, doctor, use_doctor_stamp, doctor_stamp_png, use_clinic_stamp, clinic_stamp_png, phone)
                    VALUES (1, @name, @address, @doctor, @use_stamp, @stamp, @use_clinic_stamp, @clinic_stamp, @phone);
            """, transaction);
        command.Parameters.Add("@replace", SqlDbType.Bit).Value = replaceExisting;
        command.Parameters.Add("@name", SqlDbType.NVarChar, -1).Value = clinic.Name;
        command.Parameters.Add("@address", SqlDbType.NVarChar, -1).Value = clinic.Address;
        command.Parameters.Add("@doctor", SqlDbType.NVarChar, -1).Value = clinic.Doctor;
        command.Parameters.Add("@phone", SqlDbType.NVarChar, -1).Value = clinic.Phone;
        command.Parameters.Add("@use_stamp", SqlDbType.Bit).Value = clinic.UseDoctorStamp;
        command.Parameters.Add("@stamp", SqlDbType.VarBinary, -1).Value = (object?)clinic.DoctorStampPng ?? DBNull.Value;
        command.Parameters.Add("@use_clinic_stamp", SqlDbType.Bit).Value = clinic.UseClinicStamp;
        command.Parameters.Add("@clinic_stamp", SqlDbType.VarBinary, -1).Value = (object?)clinic.ClinicStampPng ?? DBNull.Value;
        command.ExecuteNonQuery();
    }

    // Return the existing card ID if confirmation is needed, or null after saving.
    internal Guid? SaveCard(Guid id, DocumentData source, DateTime date, Card card, Card printCard,
        bool confirmDuplicate = false)
    {
        string client = string.Join(", ", source.Payer.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        using var db = Open();
        using var transaction = db.BeginTransaction();
        if (!confirmDuplicate)
        {
            using var existing = Command(db, """
                SELECT TOP (1) id FROM dbo.treatment_cards WITH (UPDLOCK, HOLDLOCK)
                WHERE source_scope = @scope AND document = @document AND deleted_at_utc IS NULL
                ORDER BY CASE WHEN id = @id THEN 0 ELSE 1 END, archive_order DESC;
                """, transaction);
            existing.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = source.Scope;
            existing.Parameters.Add("@document", SqlDbType.NVarChar, -1).Value = source.Number;
            existing.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            if (existing.ExecuteScalar() is Guid existingId)
            {
                transaction.Commit();
                return existingId;
            }
        }

        WriteCard(db, transaction, id, source.Scope, source.PayerId, client, source.Number, date,
            (client + " " + source.Number).ToUpperInvariant(), JsonSerializer.Serialize(card),
            JsonSerializer.Serialize(printCard), updateExisting: true);
        transaction.Commit();
        return null;
    }

    internal bool SetCardInTrash(string id, bool inTrash)
    {
        using var db = Open();
        using var command = Command(db, inTrash
            ? "UPDATE dbo.treatment_cards SET deleted_at_utc = SYSUTCDATETIME() WHERE id = @id AND deleted_at_utc IS NULL;"
            : "UPDATE dbo.treatment_cards SET deleted_at_utc = NULL WHERE id = @id AND deleted_at_utc IS NOT NULL;");
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.ParseExact(id, "N");
        return command.ExecuteNonQuery() > 0;
    }

    internal List<SavedTreatmentCard> FindCards(string query, DateTime? date, bool inTrash)
    {
        using var db = Open();
        using var command = Command(db, """
            SELECT id, treatment_date, client, document
            FROM dbo.treatment_cards
            WHERE ((@trash = 0 AND deleted_at_utc IS NULL) OR (@trash = 1 AND deleted_at_utc IS NOT NULL))
              AND (@query = N'' OR CHARINDEX(@query, search_text) > 0)
              AND (@date IS NULL OR treatment_date = @date)
            ORDER BY treatment_date DESC, archive_order DESC;
            """);
        command.Parameters.Add("@trash", SqlDbType.Bit).Value = inTrash;
        command.Parameters.Add("@query", SqlDbType.NVarChar, -1).Value = query.ToUpperInvariant();
        command.Parameters.Add("@date", SqlDbType.Date).Value = (object?)date?.Date ?? DBNull.Value;
        using var reader = command.ExecuteReader();
        var cards = new List<SavedTreatmentCard>();
        while (reader.Read())
            cards.Add(new(reader.GetGuid(0).ToString("N"), reader.GetDateTime(1), reader.GetString(2), reader.GetString(3)));
        return cards;
    }

    internal (Card Card, int? PayerId, string Scope) ReadCard(string id, bool forPrint, bool inTrash = false)
    {
        using var db = Open();
        using var command = Command(db, """
            SELECT CASE WHEN @print = 1 THEN print_json ELSE card_json END, payer_id, source_scope
            FROM dbo.treatment_cards
            WHERE id = @id AND ((@trash = 0 AND deleted_at_utc IS NULL) OR (@trash = 1 AND deleted_at_utc IS NOT NULL));
            """);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.ParseExact(id, "N");
        command.Parameters.Add("@print", SqlDbType.Bit).Value = forPrint;
        command.Parameters.Add("@trash", SqlDbType.Bit).Value = inTrash;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Karta zmieniła stan lub nie jest już dostępna. Odśwież listę kart.");
        var card = JsonSerializer.Deserialize<Card>(reader.GetString(0))
            ?? throw new InvalidOperationException("Zapisana karta jest pusta.");
        return (card, reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2));
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

    static int WriteCard(SqlConnection db, SqlTransaction transaction, Guid id, string scope, int? payerId,
        string client, string document, DateTime date, string search, string card, string print, bool updateExisting)
    {
        using var command = Command(db, """
            IF EXISTS (SELECT 1 FROM dbo.treatment_cards WITH (UPDLOCK, HOLDLOCK) WHERE id = @id)
            BEGIN
                IF @update = 1
                BEGIN
                    UPDATE dbo.treatment_cards SET treatment_date = @date, card_json = @card, print_json = @print
                    WHERE id = @id AND deleted_at_utc IS NULL;
                    IF @@ROWCOUNT = 0
                        THROW 50012, N'Zapisana karta jest w koszu. Przywróć ją lub ponownie otwórz dokument, aby zapisać nową kartę.', 1;
                END;
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

    static SqlCommand Command(SqlConnection db, string sql, SqlTransaction? transaction = null)
        => new(sql, db, transaction) { CommandTimeout = 15 };
}
