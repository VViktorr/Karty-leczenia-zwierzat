using System.Data;
using System.Security.Cryptography;
using KartyLeczenia;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.ReactKL;

internal sealed record SqlRestoreResult(
    string Database, string SourcePath, string? SafetyBackupPath, string Message);

/// <summary>
/// Full restore of the program-owned SQL database. The user must explicitly
/// choose a local .bak, type the database name and pass Windows Hello in the
/// host BEFORE this class can run. Never operates on the Subiekt database.
/// </summary>
internal static class SqlRestoreService
{
    private const string DatabaseName = "KartyLeczenia";

    internal static SqlRestoreResult Run(ConnectionOptions settings, string selectedFile,
        IProgress<SqlBackupProgress> progress)
    {
        if (string.IsNullOrWhiteSpace(selectedFile) ||
            !Path.IsPathFullyQualified(selectedFile) ||
            !string.Equals(Path.GetExtension(selectedFile), ".bak", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(selectedFile))
            throw new FileNotFoundException("Wybierz istniejący plik .bak z pełną kopią KartyLeczenia.", selectedFile);

        var info = new FileInfo(selectedFile);
        if (info.Length < 512)
            throw new InvalidDataException("Plik kopii jest zbyt mały, aby zawierać bazę danych.");

        using var db = ReactWindow.AdministratorDb(settings, "master");
        db.Open();
        using (var access = Sql(db, """
            IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
                THROW 50010,N'Odtwarzanie wymaga uprawnienia sysadmin SQL Server.',1;
            DECLARE @lock int;
            EXEC @lock = sys.sp_getapplock @Resource=N'KartyLeczenia.BackupRestore',
                @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
            IF @lock<0 THROW 50011,N'Na serwerze trwa inna operacja konserwacyjna.',1;
            """))
            access.ExecuteNonQuery();

        progress.Report(new("Weryfikacja dostępu do pliku archiwum"));
        string work = SqlBackupService.Load().WorkingFolder;
        if (string.IsNullOrWhiteSpace(work))
        {
            using var ask = Sql(db,
                "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'));");
            work = ask.ExecuteScalar() as string ?? "";
        }
        if (string.IsNullOrWhiteSpace(work))
            throw new InvalidOperationException(
                "Serwer SQL nie podał folderu kopii zapasowych. Ustaw folder roboczy w archiwizacji.");

        // A SQL Server service reads its own server-side path, never the
        // workstation's USB drive letter. Stage the user-selected .bak first.
        string separator = work.Contains('/') && !work.Contains('\\') ? "/" : "\\";
        string sqlStage = work.TrimEnd('\\','/') + separator
            + "ReactKL-restore-" + Guid.NewGuid().ToString("N") + ".bak";
        string? localStage = ResolveFileAccess(sqlStage,settings.Server);
        if (localStage is null)
            throw new IOException("Nie można zapisać pliku w folderze roboczym SQL. " +
                "Uruchom ReactKL na komputerze serwera lub skonfiguruj dostęp do folderu: " + work);
        if (!Directory.Exists(Path.GetDirectoryName(localStage)))
            throw new DirectoryNotFoundException(
                "Folder roboczy SQL nie jest dostępny z tego stanowiska: " + localStage);

        string? safetyCopy = null;
        bool restoring = false;
        bool success = false;
        try
        {
            progress.Report(new("Przenoszenie pliku na serwer SQL",0));
            CopyAndCheckHash(selectedFile, localStage, info.Length, progress);
            progress.Report(new("Sprawdzanie pełnej kopii SQL"));
            using (var header = Sql(db, "RESTORE HEADERONLY FROM DISK=@path WITH FILE=1;", sqlStage))
            using (var reader = header.ExecuteReader())
            {
                if (!reader.Read() ||
                    !string.Equals(reader["DatabaseName"] as string,DatabaseName,StringComparison.Ordinal) ||
                    Convert.ToInt32(reader["BackupType"]) != 1 ||
                    Convert.ToInt32(reader["Position"]) != 1)
                    throw new InvalidDataException(
                        "To nie jest pełna kopia bazy KartyLeczenia. Żadnych danych nie zmieniono.");
            }
            List<RestoreFile> files = ReadFiles(db,sqlStage);
            bool existed;
            using (var state = Sql(db, """
                SELECT state_desc,user_access_desc FROM sys.databases WHERE name=N'KartyLeczenia';
                """))
            using (var reader = state.ExecuteReader())
            {
                existed = reader.Read();
                if (existed && (reader.GetString(0)!="ONLINE" || reader.GetString(1)!="MULTI_USER"))
                    throw new InvalidOperationException(
                        "Baza musi być ONLINE i MULTI_USER. Sprawdź jej stan w SQL Server.");
            }
            string moves = FileMoves(db,files,existed);

            // VERIFYONLY is mandatory for a destructive restore; do not bypass
            // missing permissions or checksum corruption as we do for backups.
            using (var verify = Sql(db,
                "RESTORE VERIFYONLY FROM DISK=@path WITH FILE=1,CHECKSUM" + moves + ";", sqlStage))
            {
                progress.Report(new("RESTORE VERIFYONLY i CHECKSUM"));
                verify.ExecuteNonQuery();
            }

            if (existed)
            {
                safetyCopy = work.TrimEnd('\\','/') + separator
                    + "KartyLeczenia-przed-odtworzeniem-"
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-"
                    + Guid.NewGuid().ToString("N") + ".bak";
                progress.Report(new("Tworzenie kopii bezpieczeństwa aktualnej bazy"));
                using (var backup = Sql(db, """
                    BACKUP DATABASE [KartyLeczenia] TO DISK=@path
                    WITH COPY_ONLY,CHECKSUM,NO_COMPRESSION;
                    """,safetyCopy))
                    backup.ExecuteNonQuery();
                using (var check = Sql(db, """
                    RESTORE VERIFYONLY FROM DISK=@path WITH FILE=1,CHECKSUM;
                    """,safetyCopy))
                    check.ExecuteNonQuery();
            }

            progress.Report(new("Odtwarzanie bazy KartyLeczenia — nie zamykaj programu"));
            restoring = true;
            if (existed)
            {
                using var single = Sql(db,
                    "ALTER DATABASE [KartyLeczenia] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
                single.ExecuteNonQuery();
            }
            try
            {
                using var restore = Sql(db,
                    "RESTORE DATABASE [KartyLeczenia] FROM DISK=@path WITH FILE=1,"
                    + (existed ? "REPLACE," : "") + "RECOVERY" + moves + ";",sqlStage);
                restore.ExecuteNonQuery();
                using var multi = Sql(db,
                    "ALTER DATABASE [KartyLeczenia] SET MULTI_USER WITH ROLLBACK IMMEDIATE;");
                multi.ExecuteNonQuery();
                success = true;
            }
            finally
            {
                if (!success)
                {
                    try
                    {
                        using var recover = Sql(db, """
                            IF EXISTS(SELECT 1 FROM sys.databases
                                      WHERE name=N'KartyLeczenia' AND state_desc=N'ONLINE'
                                            AND user_access_desc=N'SINGLE_USER')
                               ALTER DATABASE [KartyLeczenia] SET MULTI_USER WITH ROLLBACK IMMEDIATE;
                            """);
                        recover.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write("ReactRestore.ResetMultiUser",ex);
                    }
                }
            }
            progress.Report(new("Odtwarzanie zakończone",100));
            return new(DatabaseName, selectedFile,safetyCopy,
                safetyCopy is null
                ? "Utworzono bazę KartyLeczenia z pełnej kopii."
                : "Odtworzono bazę KartyLeczenia. Kopia bezpieczeństwa wcześniejszego stanu pozostała na serwerze SQL.");
        }
        catch (Exception ex) when (restoring)
        {
            throw new InvalidOperationException(
                "Błąd podczas odtwarzania SQL. Stan bazy trzeba sprawdzić w SSMS. " +
                (safetyCopy is null ? "" : "Kopia bezpieczeństwa sprzed odtwarzania: " + safetyCopy + ". ") +
                ex.Message,ex);
        }
        finally
        {
            try { if (File.Exists(localStage)) File.Delete(localStage); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashLog.Write("ReactRestore.StagingCleanup",ex);
            }
        }
    }

    private sealed record RestoreFile(string Logical,string Physical,string Type);
    private static List<RestoreFile> ReadFiles(SqlConnection db,string path)
    {
        var result = new List<RestoreFile>();
        using var command = Sql(db,"RESTORE FILELISTONLY FROM DISK=@path WITH FILE=1;",path);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new((string)reader["LogicalName"],
                (string)reader["PhysicalName"],(string)reader["Type"]));
        if (result.Count==0 || result.Any(x=>x.Type is not ("D" or "L")))
            throw new InvalidDataException(
                "Kopia nie zawiera standardowych plików bazy SQL (D/L). Odtwarzanie wstrzymane.");
        return result;
    }
    private static string FileMoves(SqlConnection db,List<RestoreFile> files,bool exists)
    {
        var current=new List<(string Path,string Type)>();
        if (exists)
        {
            using var command=Sql(db, """
                SELECT physical_name,type_desc FROM sys.master_files
                WHERE database_id=DB_ID(N'KartyLeczenia') ORDER BY file_id;
                """);
            using var reader=command.ExecuteReader();
            while (reader.Read())
                current.Add((reader.GetString(0),reader.GetString(1)=="LOG"?"L":"D"));
        }
        var destinations=new List<string>();
        // Reuse the current database's existing file paths only when the
        // count and file types agree. Never map unknown backup files blindly.
        if (exists && current.Count==files.Count &&
            current.Count(x=>x.Type=="L")==files.Count(x=>x.Type=="L"))
        {
            var data=current.Where(x=>x.Type=="D").Select(x=>x.Path).ToArray();
            var logs=current.Where(x=>x.Type=="L").Select(x=>x.Path).ToArray();
            int d=0,l=0;
            foreach (var entry in files)
                destinations.Add(entry.Type=="D"?data[d++]:logs[l++]);
        }
        else
        {
            using var command=Sql(db, """
                SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')),
                       CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath'));
                """);
            using var reader=command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
                throw new InvalidOperationException(
                    "Serwer SQL nie podał folderów danych i dziennika.");
            string data=reader.GetString(0).TrimEnd('\\','/');
            string log=reader.GetString(1).TrimEnd('\\','/');
            string sep=data.Contains('/')&&!data.Contains('\\')?"/":"\\";
            string logSep=log.Contains('/')&&!log.Contains('\\')?"/":"\\";
            string unique=Guid.NewGuid().ToString("N");
            int d=0,l=0;
            foreach (var entry in files)
                destinations.Add(entry.Type=="D"
                    ?data+sep+"KartyLeczenia-"+unique+"-d"+(++d)+(d==1?".mdf":".ndf")
                    :log+logSep+"KartyLeczenia-"+unique+"-l"+(++l)+".ldf");
        }
        static string Quote(string text)=>"N'"+text.Replace("'","''",StringComparison.Ordinal)+"'";
        return string.Concat(files.Select((file,i)=>
            ", MOVE "+Quote(file.Logical)+" TO "+Quote(destinations[i])));
    }
    private static string? ResolveFileAccess(string serverFile,string sqlServer)
    {
        if (File.Exists(serverFile) || Directory.Exists(Path.GetDirectoryName(serverFile)))
            return serverFile;
        // For a remote SQL host, allow the existing Windows administrative
        // share only when it is already accessible to the signed-in operator.
        string host=sqlServer.Split('\\',',')[0].Trim();
        if (host.StartsWith("tcp:",StringComparison.OrdinalIgnoreCase))host=host[4..];
        if (host is "" or "." or "(local)" or "localhost")host=Environment.MachineName;
        if (serverFile.Length>=3 && char.IsLetter(serverFile[0]) &&
            serverFile[1]==':' && serverFile[2]=='\\')
        {
            string candidate=@"\\"+host+@"\"+char.ToUpperInvariant(serverFile[0])+"$"+serverFile[2..];
            if (Directory.Exists(Path.GetDirectoryName(candidate))) return candidate;
        }
        return null;
    }
    private static void CopyAndCheckHash(string source,string target,long length,
        IProgress<SqlBackupProgress> progress)
    {
        byte[] hash;
        using(var input=File.OpenRead(source))
        using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var sha=SHA256.Create())
        {
            byte[] buffer=new byte[1024*1024];
            long copied=0;
            int read,previous=-1;
            while ((read=input.Read(buffer,0,buffer.Length))!=0)
            {
                sha.TransformBlock(buffer,0,read,null,0);
                output.Write(buffer,0,read);
                copied+=read;
                int percent=(int)(copied*100/length);
                if(percent!=previous)
                {
                    previous=percent;
                    progress.Report(new("Przenoszenie pliku na serwer SQL",percent,
                        $"{copied/1048576:N0} / {length/1048576:N0} MB"));
                }
            }
            sha.TransformFinalBlock([],0,0);
            hash=sha.Hash!;
            output.Flush(flushToDisk:true);
        }
        using var copiedStream=File.OpenRead(target);
        if (!CryptographicOperations.FixedTimeEquals(hash,SHA256.HashData(copiedStream)))
            throw new IOException("Nieprawidłowy skrót SHA-256 skopiowanego pliku. Baza SQL pozostała bez zmian.");
    }
    private static SqlCommand Sql(SqlConnection db,string query,string? path=null)
    {
        var command=new SqlCommand(query,db){CommandTimeout=0};
        if(path is not null)command.Parameters.Add("@path",SqlDbType.NVarChar,4000).Value=path;
        return command;
    }
}
