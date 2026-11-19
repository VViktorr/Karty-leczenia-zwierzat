using System.Data;
using System.Security.Cryptography;
using System.Text;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Historical WinForms-compatible payer-scoped animal descriptions.
/// Never queries all cards or Subiekt stock; reads TOP 100 from the existing
/// payer index and uses the original SHA256 identity when saving.
/// </summary>
internal sealed class AnimalDescriptionService(ConnectionSettingsStore archive)
{
    private SqlConnection Connect()
    {
        var options = archive.Load().Options ??
            throw new InvalidOperationException("Najpierw skonfiguruj połączenie z bazą KartyLeczenia.");
        return new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia"))
        { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
    }

    internal static byte[] DescriptionKey(string scope, int payerId, string description)
    {
        if (string.IsNullOrWhiteSpace(scope) || payerId <= 0 ||
            string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Wymagane źródło dokumentu, płatnik i opis zwierząt.");
        // Original WinForms/SQLite NOCASE identity: fold ASCII letters only.
        string normalized = string.Concat(description.Select(c =>
            c is >= 'a' and <= 'z' ? char.ToUpperInvariant(c) : c));
        using var identity = new MemoryStream();
        using (var writer = new BinaryWriter(identity, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(scope);
            writer.Write(payerId);
            writer.Write(normalized);
        }
        return SHA256.HashData(identity.ToArray());
    }

    internal async Task<string[]> ReadAsync(string scope, int? payerId, CancellationToken token)
    {
        if (payerId is null || payerId <= 0) return [];
        await using var db = Connect();
        await db.OpenAsync(token).ConfigureAwait(false);
        using var command = new SqlCommand("""
            SELECT TOP (100) description FROM dbo.animal_descriptions
            WHERE source_scope=@scope AND payer_id=@payer
            ORDER BY last_used DESC, description;
            """, db) { CommandTimeout = 10 };
        command.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        command.Parameters.Add("@payer", SqlDbType.Int).Value = payerId.Value;
        var list = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false)) list.Add(reader.GetString(0));
        return list.ToArray();
    }

    internal async Task SaveAsync(string scope, int? payerId, string description, CancellationToken token)
    {
        description = description.Trim();
        if (payerId is null || payerId <= 0 ||
            description.Length is < 1 or > 2000)
            throw new ArgumentException("Wybierz kontrahenta i podaj opis zwierząt (maks. 2000 znaków).");
        long time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var db = Connect();
        await db.OpenAsync(token).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(
            IsolationLevel.Serializable, token).ConfigureAwait(false);
        using var cmd = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM dbo.animal_descriptions WITH (UPDLOCK,HOLDLOCK)
                WHERE description_key=@key)
                UPDATE dbo.animal_descriptions SET description=@description,last_used=@time
                WHERE description_key=@key AND last_used<@time;
            ELSE
                INSERT dbo.animal_descriptions
                    (description_key,source_scope,payer_id,description,last_used)
                VALUES (@key,@scope,@payer,@description,@time);
            """, db, tx) { CommandTimeout = 10 };
        cmd.Parameters.Add("@key", SqlDbType.Binary, 32).Value =
            DescriptionKey(scope, payerId.Value, description);
        cmd.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        cmd.Parameters.Add("@payer", SqlDbType.Int).Value = payerId.Value;
        cmd.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = description;
        cmd.Parameters.Add("@time", SqlDbType.BigInt).Value = time;
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await tx.CommitAsync(token).ConfigureAwait(false);
    }
}
