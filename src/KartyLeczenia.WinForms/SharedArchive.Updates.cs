using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed record PublishedUpdate(Guid Id, Version Version, byte[] Sha256);

internal sealed partial class SharedArchive
{
    SqlConnection CreateUpdateConnection() => new(new SqlConnectionStringBuilder(options.BuildConnectionString(DatabaseName))
    {
        ApplicationIntent = ApplicationIntent.ReadWrite
    }.ConnectionString);

    internal async Task<PublishedUpdate?> ReadUpdate(CancellationToken token)
    {
        using var db = CreateUpdateConnection();
        await db.OpenAsync(token);
        using var command = Command(db, """
            SELECT release_id, version, sha256 FROM dbo.program_update WHERE id = 1;
            """);
        try
        {
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token);
            if (!await reader.ReadAsync(token)) return null;
            if (!Version.TryParse(reader.GetString(1), out var version))
                throw new InvalidDataException("Nieprawidłowa wersja instalatora w SQL.");
            return new(reader.GetGuid(0), version, reader.GetFieldValue<byte[]>(2));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException("Archiwum SQL nie ma tabeli aktualizacji. Uruchom nowy instalator na serwerze.", ex);
        }
    }

    internal async Task DownloadUpdate(PublishedUpdate release, string path, CancellationToken token)
    {
        using var db = CreateUpdateConnection();
        await db.OpenAsync(token);
        using var command = Command(db, """
            SELECT installer FROM dbo.program_update WHERE id = 1 AND release_id = @release;
            """);
        command.CommandTimeout = 120;
        command.Parameters.Add("@release", SqlDbType.UniqueIdentifier).Value = release.Id;
        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleRow, token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("W trakcie pobierania opublikowano nową wersję. Sprawdź aktualizację ponownie.");
        await using var source = reader.GetStream(0);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, token);
    }
}
