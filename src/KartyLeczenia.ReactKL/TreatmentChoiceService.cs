using System.Data;
using System.Security.Cryptography;
using System.Text;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record SavedTreatmentChoice(string Symbol, string Species, string Route);

/// <summary>
/// Shared choices from final WinForms, keyed by Subiekt source scope and symbol.
/// No dosing or withdrawal is stored: a remembered species/route is a hint,
/// never permission to guess a ChPL clinical variant.
/// </summary>
internal sealed class TreatmentChoiceService(ConnectionSettingsStore archive)
{
    internal static string NormalizeSymbol(string symbol) => symbol.Trim().ToUpperInvariant();

    internal static byte[] ChoiceKey(string scope, string symbol)
    {
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Do zapamiętania wyboru wymagany jest dokument i symbol.");
        // Identical to WinForms SharedArchive.SaveTreatmentChoice: BinaryWriter
        // prefixes each UTF-8 string with its 7-bit encoded length.
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(scope);
            writer.Write(NormalizeSymbol(symbol));
        }
        return SHA256.HashData(bytes.ToArray());
    }

    private SqlConnection Connect()
    {
        var options = archive.Load().Options ??
            throw new InvalidOperationException("Najpierw skonfiguruj połączenie z bazą KartyLeczenia.");
        return new SqlConnection(new SqlConnectionStringBuilder(
            options.BuildConnectionString("KartyLeczenia"))
        { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
    }

    internal async Task<SavedTreatmentChoice[]> ReadAsync(string scope,
        IEnumerable<string> symbols, CancellationToken token)
    {
        var unique = symbols.Select(NormalizeSymbol)
            .Where(s => s.Length is > 0 and <= 128)
            .Distinct(StringComparer.Ordinal).Take(500).ToArray();
        if (unique.Length == 0) return [];

        await using var db = Connect();
        await db.OpenAsync(token).ConfigureAwait(false);
        var result = new List<SavedTreatmentChoice>();
        // Indexed choice_key reads; never scan the full choices table on each
        // card and never load unrelated patients or SQL identities.
        foreach (var batch in unique.Chunk(90))
        {
            using var command = db.CreateCommand();
            command.CommandTimeout = 10;
            var keys = batch.Select((_, i) => "@key" + i).ToArray();
            command.CommandText = "SELECT symbol,species,route FROM dbo.treatment_choices " +
                "WHERE choice_key IN (" + string.Join(",", keys) + ") AND source_scope=@scope;";
            command.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
            for (int i = 0; i < batch.Length; i++)
                command.Parameters.Add(keys[i], SqlDbType.Binary, 32).Value =
                    ChoiceKey(scope, batch[i]);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return result.ToArray();
    }

    internal async Task SaveAsync(string scope, string symbol,
        string species, string route, CancellationToken token)
    {
        symbol = NormalizeSymbol(symbol);
        species = species.Trim();
        route = route.Trim();
        if (symbol.Length is < 1 or > 128 || species.Length is < 1 or > 300 ||
            route.Length is < 1 or > 300)
            throw new ArgumentException("Zapamiętany wariant wymaga symbolu, gatunku i drogi podania.");

        await using var db = Connect();
        await db.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await db.BeginTransactionAsync(
            IsolationLevel.Serializable, token).ConfigureAwait(false);
        using var command = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM dbo.treatment_choices WITH (UPDLOCK,HOLDLOCK) WHERE choice_key=@key)
                UPDATE dbo.treatment_choices SET species=@species,route=@route WHERE choice_key=@key;
            ELSE
                INSERT dbo.treatment_choices(choice_key,source_scope,symbol,product,species,route)
                VALUES (@key,@scope,@symbol,N'',@species,@route);
            """, db, transaction) { CommandTimeout = 10 };
        command.Parameters.Add("@key", SqlDbType.Binary, 32).Value = ChoiceKey(scope, symbol);
        command.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        command.Parameters.Add("@symbol", SqlDbType.NVarChar, -1).Value = symbol;
        command.Parameters.Add("@species", SqlDbType.NVarChar, -1).Value = species;
        command.Parameters.Add("@route", SqlDbType.NVarChar, -1).Value = route;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }
}
