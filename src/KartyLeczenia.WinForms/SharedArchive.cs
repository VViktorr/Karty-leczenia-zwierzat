using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed record SavedTreatmentCard(string Id, DateTime Date, string Client, string Document, string Number);

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

    internal (Guid? DuplicateId, string? Number) SaveCard(Guid id, DocumentData source, DateTime date, Card card, Card printCard,
        bool confirmDuplicate = false)
    {
        string client = string.Join(", ", source.Payer.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        using var db = Open();
        using var transaction = db.BeginTransaction();
        // Serialize allocation and duplicate checks across all WinForms stations.
        using (var guard = Command(db, """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'KartyLeczenia.CardNumber',
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 50016, N'Inne stanowisko zapisuje kartę. Spróbuj ponownie.', 1;
            """, transaction))
            guard.ExecuteNonQuery();

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
                return (existingId, null);
            }
        }

        int year = date.Year;
        long? sequence = null;
        using (var existing = Command(db, """
            SELECT card_year, card_sequence, deleted_at_utc
            FROM dbo.treatment_cards WITH (UPDLOCK, HOLDLOCK) WHERE id = @id;
            """, transaction))
        {
            existing.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            using var reader = existing.ExecuteReader();
            if (reader.Read())
            {
                if (!reader.IsDBNull(2))
                    throw new InvalidOperationException("Zapisana karta jest w koszu. Przywróć ją lub ponownie otwórz dokument, aby zapisać nową kartę.");
                if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                {
                    year = reader.GetInt32(0);
                    sequence = reader.GetInt64(1);
                }
            }
        }
        if (sequence is null)
        {
            using var next = Command(db, """
                SELECT COALESCE(MAX(card_sequence), 0) + 1
                FROM dbo.treatment_cards WITH (UPDLOCK, HOLDLOCK)
                WHERE card_year = @year AND card_sequence IS NOT NULL;
                """, transaction);
            next.Parameters.Add("@year", SqlDbType.Int).Value = year;
            sequence = (long)next.ExecuteScalar()!;
        }

        string number = FormatCardNumber(year, sequence.Value);
        card.Fields["Nr karty"] = printCard.Fields["Nr karty"] = number;
        WriteCard(db, transaction, id, source.Scope, source.PayerId, client, source.Number, date,
            (client + " " + source.Number).ToUpperInvariant(), JsonSerializer.Serialize(card),
            JsonSerializer.Serialize(printCard), year, sequence.Value);
        transaction.Commit();
        return (null, number);
    }

    static string FormatCardNumber(int year, long sequence) => FormattableString.Invariant($"K/{sequence}/{year:D4}");

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
            SELECT id, treatment_date, client, document, card_year, card_sequence
            FROM dbo.treatment_cards
            WHERE ((@trash = 0 AND deleted_at_utc IS NULL) OR (@trash = 1 AND deleted_at_utc IS NOT NULL))
              AND (@query = N'' OR CHARINDEX(@query, search_text) > 0
                   OR (card_sequence IS NOT NULL AND CHARINDEX(@query, CONCAT(N'K/', card_sequence, N'/', card_year)) > 0))
              AND (@date IS NULL OR treatment_date = @date)
            ORDER BY treatment_date DESC, archive_order DESC;
            """);
        command.Parameters.Add("@trash", SqlDbType.Bit).Value = inTrash;
        command.Parameters.Add("@query", SqlDbType.NVarChar, -1).Value = query.ToUpperInvariant();
        command.Parameters.Add("@date", SqlDbType.Date).Value = (object?)date?.Date ?? DBNull.Value;
        using var reader = command.ExecuteReader();
        var cards = new List<SavedTreatmentCard>();
        while (reader.Read())
            cards.Add(new(reader.GetGuid(0).ToString("N"), reader.GetDateTime(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) || reader.IsDBNull(5) ? "" : FormatCardNumber(reader.GetInt32(4), reader.GetInt64(5))));
        return cards;
    }

    internal (Card Card, int? PayerId, string Scope) ReadCard(string id, bool forPrint, bool inTrash = false)
    {
        using var db = Open();
        using var command = Command(db, """
            SELECT CASE WHEN @print = 1 THEN print_json ELSE card_json END, payer_id, source_scope, card_year, card_sequence
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
        if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
            card.Fields["Nr karty"] = FormatCardNumber(reader.GetInt32(3), reader.GetInt64(4));
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

    static void WriteCard(SqlConnection db, SqlTransaction transaction, Guid id, string scope, int? payerId,
        string client, string document, DateTime date, string search, string card, string print, int year, long sequence)
    {
        using var command = Command(db, """
            UPDATE dbo.treatment_cards SET treatment_date = @date, card_json = @card, print_json = @print,
                card_year = @year, card_sequence = @sequence WHERE id = @id;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.treatment_cards
                    (id, source_scope, payer_id, client, document, treatment_date, search_text, card_json, print_json, card_year, card_sequence)
                VALUES (@id, @scope, @payer, @client, @document, @date, @search, @card, @print, @year, @sequence);
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
        command.Parameters.Add("@year", SqlDbType.Int).Value = year;
        command.Parameters.Add("@sequence", SqlDbType.BigInt).Value = sequence;
        command.ExecuteNonQuery();
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
