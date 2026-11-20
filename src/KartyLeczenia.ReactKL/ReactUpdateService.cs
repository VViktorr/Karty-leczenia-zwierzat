using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record ReactUpdateInfo(Guid ReleaseId, string Version,
    string CurrentVersion, string Sha256, bool Newer, DateTime PublishedAtUtc);

/// <summary>
/// Uses the existing dbo.program_update SQL channel and integrity-checked
/// installer download; installation/publishing follows the previous SQL app.
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

    private static Version Normalize(Version value) => new(value.Major,value.Minor,
        Math.Max(0,value.Build),Math.Max(0,value.Revision));

    internal static bool IsNewer(string published, string installed)
    {
        if (!Version.TryParse(published, out var candidate))
            throw new InvalidDataException("Niepoprawna wersja instalatora opublikowanego w SQL.");
        var current = Version.TryParse(installed, out var parsed)
            ? parsed : new Version(0,0);
        return Normalize(candidate) > Normalize(current);
    }

    private SqlConnection Connection()
    {
        var options = store.Load().Options ??
            throw new InvalidOperationException("Skonfiguruj archiwum SQL przed sprawdzeniem aktualizacji.");
        return new(new SqlConnectionStringBuilder(options.BuildConnectionString("KartyLeczenia"))
        { ApplicationIntent = ApplicationIntent.ReadWrite }.ConnectionString);
    }

    internal async Task<ReactUpdateInfo?> Check(CancellationToken token)
    {
        using var db=Connection();
        await db.OpenAsync(token);
        using var query=new SqlCommand("""
            SELECT release_id,version,sha256,published_at_utc
            FROM dbo.program_update WHERE id=1;
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
            string installedVersion = ReactInstallUpdates.InstalledVersion;
            return new(reader.GetGuid(0),release.ToString(),installedVersion,
                Convert.ToHexString(hash),IsNewer(release.ToString(),installedVersion),reader.GetDateTime(3));
        }
        catch(SqlException ex) when(ex.Number==208)
        {
            throw new InvalidOperationException(
                "Brakuje tabeli aktualizacji SQL. Na serwerze uruchom scripts/Setup-SharedArchive.sql.",ex);
        }
    }

    internal async Task<string> Download(ReactUpdateInfo release, CancellationToken token)
    {
        Version current=Version.TryParse(ReactInstallUpdates.InstalledVersion,out var local)?local:new Version(0,0);
        if (!Version.TryParse(release.Version,out var advertised)||Normalize(advertised)<=Normalize(current))
            throw new InvalidOperationException("Brak nowszej wersji ReactKL do zainstalowania.");
        if(release.Sha256.Length!=64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Nieprawidłowy skrót SHA-256 publikacji.");
        string folder=Path.Combine(Path.GetTempPath(),"KartyLeczenia-Update",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string installer=Path.Combine(folder,"KartyLeczenia-Setup-win-x64-v"+advertised+".exe");
        bool valid=false;
        try
        {
            using var db=Connection();
            await db.OpenAsync(token);
            using var query=new SqlCommand("""
                SELECT installer FROM dbo.program_update
                WHERE id=1 AND release_id=@release AND sha256=@hash;
                """,db){CommandTimeout=300};
            query.Parameters.Add("@release",SqlDbType.UniqueIdentifier).Value=release.ReleaseId;
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
            var metadata = FileVersionInfo.GetVersionInfo(installer);
            var fileVersion = new Version(metadata.FileMajorPart, metadata.FileMinorPart,
                metadata.FileBuildPart, metadata.FilePrivatePart);
            if (fileVersion != Normalize(advertised))
                throw new InvalidDataException("Pobrany plik ma inną wersję niż opublikowana. Nie uruchomiono aktualizacji.");
            using(var stream=File.OpenRead(installer))
                if(stream.ReadByte()!='M' || stream.ReadByte()!='Z')
                    throw new InvalidDataException("Pobrany plik nie jest instalatorem Windows.");
            valid=true;
            return installer;
        }
        finally
        {
            if(!valid) DeleteDownloadFolder(folder);
        }
    }
    internal static void DeleteDownloadFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CrashLog.Write("ReactUpdate.Cleanup", ex); }
    }

    internal static void Start(string file)
    {
        if (!File.Exists(file) || !file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Brak zweryfikowanego instalatora aktualizacji.",file);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(file)!
            }) ?? throw new InvalidOperationException("Windows nie uruchomił instalatora aktualizacji.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Anulowano zgodę Windows na uruchomienie instalatora. Możesz ponowić próbę.", ex);
        }
    }
}
