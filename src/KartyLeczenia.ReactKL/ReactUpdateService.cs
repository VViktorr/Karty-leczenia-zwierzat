using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record ReactUpdateInfo(Guid ReleaseId, string Version,
    string CurrentVersion, string Sha256, bool Newer, DateTime PublishedAtUtc);

/// <summary>
/// Read-only update discovery and integrity-checked installer download.
/// Publication is deliberately a separate administrator-run SQL script.
/// </summary>
internal sealed class ReactUpdateService(ConnectionSettingsStore store)
{
    internal static string CurrentVersion
    {
        get
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return "0.0.0";
            string? version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
            return Version.TryParse(version, out var parsed) ? parsed.ToString() : "0.0.0";
        }
    }

    private SqlConnection Connection()
    {
        var options = store.Load().Options ??
            throw new InvalidOperationException("Skonfiguruj archiwum SQL przed sprawdzeniem aktualizacji.");
        return new(new SqlConnectionStringBuilder(options.BuildConnectionString("KartyLeczenia"))
        { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString);
    }

    internal async Task<ReactUpdateInfo?> Check(CancellationToken token)
    {
        using var db=Connection();
        await db.OpenAsync(token);
        using var query=new SqlCommand("""
            SELECT release_id,version,sha256,published_at_utc
            FROM dbo.react_program_update WHERE id=1;
            """,db){CommandTimeout=30};
        try
        {
            using var reader=await query.ExecuteReaderAsync(CommandBehavior.SingleRow,token);
            if (!await reader.ReadAsync(token)) return null;
            string text=reader.GetString(1);
            if (!Version.TryParse(text,out var release))
                throw new InvalidDataException("Niepoprawny numer wersji publikacji w SQL.");
            var hash=reader.GetFieldValue<byte[]>(2);
            if(hash.Length!=32)throw new InvalidDataException("Błędna suma SHA-256 aktualizacji SQL.");
            Version current=Version.TryParse(CurrentVersion,out var local)?local:new Version(0,0);
            return new(reader.GetGuid(0),release.ToString(),CurrentVersion,
                Convert.ToHexString(hash),release>current,reader.GetDateTime(3));
        }
        catch(SqlException ex) when(ex.Number==208)
        {
            throw new InvalidOperationException(
                "W SQL nie ma jeszcze kanału ReactKL. Administrator powinien uruchomić scripts/Setup-ReactKLUpdates.sql.",ex);
        }
    }

    internal async Task<string> Download(ReactUpdateInfo release, CancellationToken token)
    {
        Version current=Version.TryParse(CurrentVersion,out var local)?local:new Version(0,0);
        if (!Version.TryParse(release.Version,out var advertised)||advertised<=current)
            throw new InvalidOperationException("Brak nowszej wersji ReactKL do zainstalowania.");
        if(release.Sha256.Length!=64 || !Convert.TryFromHexString(release.Sha256,new byte[32],out int parsed) || parsed!=32)
            throw new InvalidDataException("Nieprawidłowy skrót SHA-256 publikacji.");
        string folder=Path.Combine(Path.GetTempPath(),"KartyLeczenia.ReactKL-Updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string installer=Path.Combine(folder,"ReactKL-Setup-win-x64-v"+advertised+".exe");
        bool valid=false;
        try
        {
            using var db=Connection();
            await db.OpenAsync(token);
            using var query=new SqlCommand("""
                SELECT installer FROM dbo.react_program_update
                WHERE id=1 AND release_id=@release AND version=@version AND sha256=@hash;
                """,db){CommandTimeout=300};
            query.Parameters.Add("@release",SqlDbType.UniqueIdentifier).Value=release.ReleaseId;
            query.Parameters.Add("@version",SqlDbType.NVarChar,32).Value=release.Version;
            query.Parameters.Add("@hash",SqlDbType.Binary,32).Value=Convert.FromHexString(release.Sha256);
            using var reader=await query.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess|CommandBehavior.SingleRow,token);
            if(!await reader.ReadAsync(token))
                throw new InvalidOperationException("Opublikowano nowszy instalator. Sprawdź aktualizację ponownie.");
            await using(var source=reader.GetStream(0))
            await using(var target=new FileStream(installer,FileMode.CreateNew,FileAccess.Write,
                FileShare.None,81920,useAsync:true))
            {
                await source.CopyToAsync(target,token);
                await target.FlushAsync(token);
            }
            byte[] digest;
            await using(var file=File.OpenRead(installer))
                digest=await SHA256.HashDataAsync(file,token);
            if(!CryptographicOperations.FixedTimeEquals(digest,Convert.FromHexString(release.Sha256)))
                throw new InvalidDataException("Błędna suma SHA-256 pobranego instalatora. Nie uruchomiono aktualizacji.");
            var actualFileVersion=FileVersionInfo.GetVersionInfo(installer).FileVersion;
            if (!Version.TryParse(actualFileVersion,out var fileVersion) || fileVersion!=advertised)
                throw new InvalidDataException("Pobrany plik ma inną wersję niż opublikowana. Nie uruchomiono aktualizacji.");
            using(var stream=File.OpenRead(installer))
                if(stream.ReadByte()!='M' || stream.ReadByte()!='Z')
                    throw new InvalidDataException("Pobrany plik nie jest instalatorem Windows.");
            valid=true;
            return installer;
        }
        finally
        {
            if(!valid)
            {
                try{Directory.Delete(folder,true);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {CrashLog.Write("ReactUpdate.Cleanup",ex);}
            }
        }
    }
    internal static void Start(string file)
    {
        if (!File.Exists(file) || !file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Brak zweryfikowanego instalatora aktualizacji.",file);
        using var process=Process.Start(new ProcessStartInfo(file)
        {
            UseShellExecute=true,
            WorkingDirectory=Path.GetDirectoryName(file)!
        }) ?? throw new InvalidOperationException("Windows nie uruchomił instalatora aktualizacji.");
    }
}
