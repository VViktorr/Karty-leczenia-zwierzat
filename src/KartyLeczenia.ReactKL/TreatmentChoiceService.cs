using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record SavedTreatmentChoice(string Symbol, string Species, string Route);
internal sealed record LegacyTreatmentChoice(string Symbol, string Product, string Species, string Route);

/// <summary>
/// Shared choices from final WinForms, keyed by Subiekt source scope and symbol.
/// No dosing or withdrawal is stored: a remembered species/route is a hint,
/// never permission to guess a ChPL clinical variant.
/// </summary>
internal sealed class TreatmentChoiceService(ConnectionSettingsStore archive)
{
    // An older WinForms record could be keyed by medicine name as well as
    // symbol. It cannot be found through the current symbol-only SHA256 key.
    // Load legacy entries at most once per source scope per process, not once
    // for each document. A Lazy<Task> deduplicates concurrent workstation UI
    // requests using this service.
    private readonly ConcurrentDictionary<string, Lazy<Task<SavedTreatmentChoice[]>>>
        legacyChoices = new(StringComparer.Ordinal);

    internal static SavedTreatmentChoice[] ResolveLegacy(
        IEnumerable<LegacyTreatmentChoice> entries, IEnumerable<string> symbols)
    {
        var wanted = symbols.Select(NormalizeSymbol).ToHashSet(StringComparer.Ordinal);
        var result = new List<SavedTreatmentChoice>();
        foreach (var group in entries
            .Where(row => wanted.Contains(NormalizeSymbol(row.Symbol)))
            .GroupBy(row => NormalizeSymbol(row.Symbol), StringComparer.Ordinal))
        {
            // Same precedence as final WinForms: choices saved by symbol
            // (empty product) outrank older product-name-scoped records.
            var current = group.Where(row => string.IsNullOrEmpty(row.Product))
                .Select(row => (row.Species, row.Route)).Distinct().ToArray();
            var values = current.Length > 0 ? current :
                group.Select(row => (row.Species, row.Route)).Distinct().ToArray();
            // Ambiguous history is never converted to a clinical selection.
            if (values.Length == 1 &&
                !string.IsNullOrWhiteSpace(values[0].Species) &&
                !string.IsNullOrWhiteSpace(values[0].Route))
                result.Add(new(group.Key, values[0].Species, values[0].Route));
        }
        return result.ToArray();
    }

    private async Task<SavedTreatmentChoice[]> ReadLegacyAsync(string scope)
    {
        await using var db = Connect();
        await db.OpenAsync().ConfigureAwait(false);
        using var cmd = db.CreateCommand();
        cmd.CommandTimeout = 15;
        // The historical source_scope column is nvarchar(max) and has no
        // index. One bounded compatibility read avoids a scan on every card;
        // an oversized legacy table is rejected rather than guessed.
        cmd.CommandText = """
            SELECT TOP (5001) LEFT(symbol,128),LEFT(product,256),
                LEFT(species,300),LEFT(route,300)
            FROM dbo.treatment_choices WHERE source_scope=@scope;
            """;
        cmd.Parameters.Add("@scope", SqlDbType.NVarChar, -1).Value = scope;
        var rows = new List<LegacyTreatmentChoice>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(new(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3)));
            if (rows.Count > 5000)
                throw new InvalidOperationException(
                    "Zbyt wiele starych preferencji w SQL. Wymagana kontrolowana migracja indeksu.");
        }
        // Pre-resolve once per scope; the caller filters by current document.
        return ResolveLegacy(rows, rows.Select(row => row.Symbol));
    }

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
        if (result.Count < unique.Length)
        {
            var existing = result.Select(item => NormalizeSymbol(item.Symbol))
                .ToHashSet(StringComparer.Ordinal);
            var cache = legacyChoices.GetOrAdd(scope, value =>
                new Lazy<Task<SavedTreatmentChoice[]>>(
                    () => ReadLegacyAsync(value),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            var old = await cache.Value.WaitAsync(token).ConfigureAwait(false);
            foreach (var item in old)
                if (existing.Add(NormalizeSymbol(item.Symbol)) &&
                    unique.Contains(NormalizeSymbol(item.Symbol), StringComparer.Ordinal))
                    result.Add(item);
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
