using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record SqlBackupSettings(
    string DestinationFolder = "",
    string WorkingFolder = "",
    string Naming = "daily",
    string CustomName = "KartyLeczenia",
    bool Overwrite = true,
    bool Compress = false)
{
    internal string FileName()
    {
        string name = Naming switch
        {
            "fixed" => "KartyLeczenia",
            "monthly" => "KartyLeczenia_" + DateTime.Now.ToString("yyyy-MM"),
            "custom" => CustomName,
            _ => "KartyLeczenia_" + DateTime.Now.ToString("yyyy-MM-dd")
        };
        name = Path.GetFileNameWithoutExtension(name.Trim());
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name is "." or "..")
            throw new ArgumentException("Niepoprawna nazwa pliku archiwum.");
        return name + ".bak";
    }
}

internal sealed record SqlBackupProgress(string Stage, int? Percent = null, string Detail = "");
internal sealed record SqlBackupResult(string Path, bool SqlVerified, string Message);

internal sealed class SqlBackupService(ConnectionSettingsStore connectionStore)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "react-backup-options.json");

    internal static SqlBackupSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<SqlBackupSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ReactBackup.Options", ex);
            return new();
        }
    }

    internal static void Save(SqlBackupSettings settings)
    {
        if (!Path.IsPathFullyQualified(settings.DestinationFolder) ||
            !Directory.Exists(settings.DestinationFolder))
            throw new DirectoryNotFoundException("Podaj istniejący folder archiwum na pendrivie.");
        _ = settings.FileName();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings));
            File.Move(temp, FilePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal SqlBackupResult Run(SqlBackupSettings config, IProgress<SqlBackupProgress> progress)
    {
        if (!Path.IsPathFullyQualified(config.DestinationFolder) ||
            !Directory.Exists(config.DestinationFolder))
            throw new DirectoryNotFoundException("Pendrive lub folder archiwum jest niedostępny.");
        var login = connectionStore.Load().Options ??
            throw new InvalidOperationException("Najpierw skonfiguruj połączenie do bazy programowej SQL.");
        string target = Path.Combine(config.DestinationFolder, config.FileName());
        if (!config.Overwrite && File.Exists(target))
        {
            var stem = Path.GetFileNameWithoutExtension(target);
            int suffix = 1;
            while (File.Exists(Path.Combine(config.DestinationFolder, $"{stem}_{suffix:000}.bak")))
                suffix++;
            target = Path.Combine(config.DestinationFolder, $"{stem}_{suffix:000}.bak");
        }
        string probe = Path.Combine(config.DestinationFolder,
            ".reactkl-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { using (File.Create(probe)) { } }
        finally { if (File.Exists(probe)) File.Delete(probe); }
        string staging = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? serverFile = null;
        bool verified = false;
        bool moved = false;
        try
        {
            progress.Report(new("Sprawdzanie SQL"));
            using var db = new SqlConnection(new SqlConnectionStringBuilder(
                login.BuildConnectionString("KartyLeczenia"))
            { ApplicationIntent = ApplicationIntent.ReadWrite, Pooling = false }.ConnectionString);
            db.Open();
            using (var cmd = Command(db, """
                IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
                  AND ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'BACKUP DATABASE'),0)<>1
                    THROW 50017,N'Konto SQL nie ma uprawnień BACKUP DATABASE.',1;
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'KartyLeczenia.BackupRestore',
                  @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
                IF @result<0 THROW 50011,N'Inne stanowisko wykonuje archiwizację.',1;
                """)) cmd.ExecuteNonQuery();
            string work = config.WorkingFolder;
            if (string.IsNullOrWhiteSpace(work))
            {
                using var ask = Command(db,
                    "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'));");
                work = ask.ExecuteScalar() as string ?? "";
            }
            if (work.Length == 0)
                throw new InvalidOperationException("SQL Server nie podał folderu roboczego. Wskaż go w ustawieniach.");
            string sep = work.Contains('/') && !work.Contains('\\') ? "/" : "\\";
            serverFile = work.TrimEnd('\\','/') + sep + "KartyLeczenia-robocza-"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
            progress.Report(new("Tworzenie pełnej kopii bazy SQL",null,"SQL Server"));
            string sql = "BACKUP DATABASE [KartyLeczenia] TO DISK=@path WITH COPY_ONLY,CHECKSUM,"
                + (config.Compress ? "COMPRESSION" : "NO_COMPRESSION") + ";";
            using (var backup = Command(db, sql, serverFile)) backup.ExecuteNonQuery();
            try
            {
                using var check = Command(db,
                    "RESTORE VERIFYONLY FROM DISK=@path WITH FILE=1,CHECKSUM;", serverFile);
                check.ExecuteNonQuery();
                verified = true;
            }
            catch (SqlException ex) when (ex.Errors.Cast<SqlError>()
                .Any(e => e.Number is 262 or 229 or 15247))
            {
                CrashLog.Write("ReactBackup.VerifyPermission", ex);
            }
            string source = Source(serverFile, login.Server) ??
                throw new IOException("Plik utworzono w folderze SQL, ale system Windows nie może go odczytać. " +
                    "Uruchom archiwizację na serwerze lub nadaj dostęp do folderu roboczego: " + serverFile);
            long length = new FileInfo(source).Length;
            if (length == 0) throw new IOException("Plik roboczy SQL ma zerowy rozmiar.");
            long copied = 0;
            byte[] firstHash;
            progress.Report(new("Przenoszenie archiwum na pendrive",0));
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = SHA256.Create())
            {
                byte[] buffer = new byte[1024*1024];
                int read;
                int last = -1;
                while ((read = input.Read(buffer,0,buffer.Length))>0)
                {
                    hash.TransformBlock(buffer,0,read,null,0);
                    output.Write(buffer,0,read);
                    copied+=read;
                    int pct = (int)(copied*100/length);
                    if (pct != last)
                    {
                        last = pct;
                        progress.Report(new("Przenoszenie archiwum na pendrive",pct,
                            $"{copied/1048576:N0} / {length/1048576:N0} MB"));
                    }
                }
                hash.TransformFinalBlock([],0,0);
                firstHash = hash.Hash!;
                output.Flush(flushToDisk:true);
            }
            progress.Report(new("Weryfikacja SHA-256"));
            using (var destination = File.OpenRead(staging))
                if (!CryptographicOperations.FixedTimeEquals(firstHash,SHA256.HashData(destination)))
                    throw new IOException("Sumy SHA-256 różnią się. Poprzednie archiwum pozostaje bez zmian.");
            File.Move(staging,target,config.Overwrite);
            moved = true;
            progress.Report(new("Usuwanie pliku roboczego"));
            File.Delete(source);
            progress.Report(new("Zakończono",100,target));
            return new(target,verified,verified
                ? "Pełna kopia SQL, RESTORE VERIFYONLY i SHA-256 zakończone poprawnie."
                : "Pełna kopia SQL CHECKSUM i SHA-256. RESTORE VERIFYONLY wymaga dodatkowych uprawnień.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException((moved
                ? "Gotowe archiwum znajduje się na pendrivie, ale czyszczenie folderu roboczego nie powiodło się. "
                : "Poprzednie archiwum nie zostało nadpisane. ") + ex.Message +
                (serverFile is null ? "" : " Plik roboczy: "+serverFile),ex);
        }
        finally
        {
            try { if (File.Exists(staging)) File.Delete(staging); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashLog.Write("ReactBackup.TempCleanup",ex); }
        }
    }

    private static SqlCommand Command(SqlConnection db, string sql, string? path = null)
    {
        var cmd = new SqlCommand(sql,db){CommandTimeout=0};
        if (path is not null) cmd.Parameters.Add("@path",SqlDbType.NVarChar,4000).Value=path;
        return cmd;
    }
    private static string? Source(string path,string server)
    {
        if (File.Exists(path)) return path;
        string host = server.Split('\\',',')[0].Trim();
        if (host.StartsWith("tcp:",StringComparison.OrdinalIgnoreCase))host=host[4..];
        if (host is "" or "." or "(local)" or "localhost")host=Environment.MachineName;
        if (path.Length>=3 && char.IsLetter(path[0]) && path[1]==':' && path[2]=='\\')
        {
            string remote=@"\\"+host+@"\"+char.ToUpperInvariant(path[0])+"$"+path[2..];
            return File.Exists(remote)?remote:null;
        }
        return null;
    }
}
