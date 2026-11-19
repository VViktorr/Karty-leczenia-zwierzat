using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed record DatabaseBackupResult(string Path, bool Verified, string VerificationNote);

internal sealed partial class SharedArchive
{
    // An ordinary KartyLeczenia SQL login may create a verified backup
    // when granted BACKUP DATABASE. No sysadmin server role is required.
    internal DatabaseBackupResult BackupDatabase(string folder, bool compress = false)
        => BackupDatabase(options, folder, compress);

    internal DatabaseBackupResult BackupDatabase(ConnectionOptions credentials, string folder, bool compress = false)
    {
        using var db = OpenMaintenance(credentials, restoring: false);
        return CreateBackup(db, folder, "kopia", compress);
    }

    internal string? RestoreDatabase(string path)
    {
        // Reuse the same authenticated connection settings as daily work.
        // SQL Server still enforces sysadmin privilege for destructive restore.
        using var db = OpenMaintenance(options, restoring: true);
        using (var header = MaintenanceCommand(db, "RESTORE HEADERONLY FROM DISK = @path WITH FILE = 1;", path))
        using (var reader = header.ExecuteReader())
        {
            if (!reader.Read() || !string.Equals(reader["DatabaseName"] as string, DatabaseName, StringComparison.Ordinal)
                || Convert.ToInt32(reader["BackupType"]) != 1 || Convert.ToInt32(reader["Position"]) != 1)
                throw new InvalidOperationException("Wskaż plik z pełną kopią bazy KartyLeczenia.");
        }
        bool exists;
        using (var state = MaintenanceCommand(db, """
            SELECT CASE WHEN state_desc = N'ONLINE' AND user_access_desc = N'MULTI_USER' THEN 1 ELSE 0 END
            FROM sys.databases WHERE name = N'KartyLeczenia';
            """))
        {
            var value = state.ExecuteScalar();
            exists = value is not null;
            if (exists && value is not 1)
                throw new InvalidOperationException("Istniejąca baza KartyLeczenia musi być dostępna w trybie wieloużytkownikowym.");
        }

        string moves = RestoreFileMoves(db, path, exists);
        using (var verify = MaintenanceCommand(db, $"RESTORE VERIFYONLY FROM DISK = @path WITH FILE = 1{moves};", path))
            verify.ExecuteNonQuery();

        if (!exists)
        {
            using var restoreNew = MaintenanceCommand(db,
                $"RESTORE DATABASE [KartyLeczenia] FROM DISK = @path WITH FILE = 1, RECOVERY{moves};", path);
            restoreNew.ExecuteNonQuery();
            return null;
        }

        string safetyCopy = CreateBackup(db, "", "przed-odtworzeniem").Path;
        try
        {
            using var restore = MaintenanceCommand(db, $"""
                ALTER DATABASE [KartyLeczenia] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                BEGIN TRY
                    RESTORE DATABASE [KartyLeczenia] FROM DISK = @path WITH FILE = 1, REPLACE, RECOVERY{moves};
                    ALTER DATABASE [KartyLeczenia] SET MULTI_USER WITH ROLLBACK IMMEDIATE;
                END TRY
                BEGIN CATCH
                    IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'KartyLeczenia'
                               AND state_desc = N'ONLINE' AND user_access_desc = N'SINGLE_USER')
                        ALTER DATABASE [KartyLeczenia] SET MULTI_USER WITH ROLLBACK IMMEDIATE;
                    THROW;
                END CATCH;
                """, path);
            restore.ExecuteNonQuery();
            return safetyCopy;
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException($"Odtwarzanie przerwane. Kopia sprzed odtworzenia: {safetyCopy}. {ex.Message}", ex);
        }
    }

    static string RestoreFileMoves(SqlConnection db, string path, bool exists)
    {
        var backupFiles = new List<(string Logical, string Physical, string Type)>();
        using (var files = MaintenanceCommand(db, "RESTORE FILELISTONLY FROM DISK = @path WITH FILE = 1;", path))
        using (var reader = files.ExecuteReader())
            while (reader.Read())
                backupFiles.Add(((string)reader["LogicalName"], (string)reader["PhysicalName"], (string)reader["Type"]));
        if (backupFiles.Count == 0)
            throw new InvalidOperationException("Kopia nie zawiera plików bazy.");

        if (exists)
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            using var files = MaintenanceCommand(db,
                "SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(N'KartyLeczenia');");
            using var reader = files.ExecuteReader();
            while (reader.Read()) targets.Add(reader.GetString(0));
            if (targets.SetEquals(backupFiles.Select(file => file.Physical))) return "";
        }

        if (backupFiles.Any(file => file.Type is not ("D" or "L")))
            throw new InvalidOperationException("Przeniesienie obsługuje standardowe pliki danych i dziennika SQL Server.");

        using var folders = MaintenanceCommand(db, """
            SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath')),
                   CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath'));
            """);
        using var defaults = folders.ExecuteReader();
        if (!defaults.Read() || defaults.IsDBNull(0) || defaults.IsDBNull(1))
            throw new InvalidOperationException("Serwer docelowy nie podał folderów danych i dziennika.");

        var prefix = $"KartyLeczenia-{Guid.NewGuid():N}";
        int dataIndex = 0, logIndex = 0;
        static string Quote(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        return string.Concat(backupFiles.Select(file =>
        {
            string target = file.Type == "D"
                ? ServerPath(defaults.GetString(0), $"{prefix}-d{++dataIndex}.{(dataIndex == 1 ? "mdf" : "ndf")}")
                : ServerPath(defaults.GetString(1), $"{prefix}-l{++logIndex}.ldf");
            return $", MOVE {Quote(file.Logical)} TO {Quote(target)}";
        }));
    }

    SqlConnection OpenMaintenance(ConnectionOptions credentials, bool restoring)
    {
        // BACKUP DATABASE permission is checked in the target database, not
        // master. RESTORE stays restricted to a sysadmin session using stored credentials.
        var connectionString = new SqlConnectionStringBuilder((credentials with
        {
            Server = Server,
            TrustCertificate = options.TrustCertificate
        }).BuildConnectionString(restoring ? "master" : DatabaseName))
        {
            ApplicationIntent = ApplicationIntent.ReadWrite,
            Pooling = false
        };
        var db = new SqlConnection(connectionString.ConnectionString);
        try
        {
            db.Open();
            using var access = MaintenanceCommand(db, restoring ? """
                IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) <> 1
                    THROW 50010, N'Aktualnie zalogowane konto SQL nie ma uprawnień do odtwarzania bazy. Wymagana rola sysadmin; archiwizacji i odtwarzania nie można wykonać z pominięciem uprawnień SQL Server.', 1;
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock @Resource = N'KartyLeczenia.BackupRestore',
                    @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0;
                IF @lockResult < 0
                    THROW 50011, N'Kopia zapasowa lub odtwarzanie już trwa na tym serwerze.', 1;
                """ : """
                IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) <> 1
                   AND ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'BACKUP DATABASE'), 0) <> 1
                    THROW 50017, N'Konto SQL nie ma uprawnienia BACKUP DATABASE. Uruchom instalator w trybie Serwer, aby nadać uprawnienie operatorowi.', 1;
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock @Resource = N'KartyLeczenia.BackupRestore',
                    @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0;
                IF @lockResult < 0
                    THROW 50011, N'Kopia zapasowa lub odtwarzanie już trwa na tym serwerze.', 1;
                """);
            access.ExecuteNonQuery();
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    static DatabaseBackupResult CreateBackup(SqlConnection db, string folder, string label, bool compress = false)
    {
        if (folder.Length == 0)
        {
            using var location = MaintenanceCommand(db, "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));");
            folder = location.ExecuteScalar() as string ?? "";
        }
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException("Serwer SQL nie zwrócił domyślnego folderu kopii zapasowych. Wskaż folder dostępny dla SQL Servera.");

        string path = ServerPath(folder, $"KartyLeczenia-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak");
        string backupSql = compress
            ? "BACKUP DATABASE [KartyLeczenia] TO DISK = @path WITH COPY_ONLY, CHECKSUM, COMPRESSION;"
            : "BACKUP DATABASE [KartyLeczenia] TO DISK = @path WITH COPY_ONLY, CHECKSUM, NO_COMPRESSION;";
        using (var backup = MaintenanceCommand(db, backupSql, path))
            backup.ExecuteNonQuery();
        // A database backup operator can create a valid CHECKSUM backup, but
        // RESTORE VERIFYONLY additionally requires CREATE DATABASE permission
        // on the instance. Do not elevate the operator just for verification.
        try
        {
            using var verify = MaintenanceCommand(db,
                "RESTORE VERIFYONLY FROM DISK = @path WITH FILE = 1, CHECKSUM;", path);
            verify.ExecuteNonQuery();
            return new(path, true, "");
        }
        catch (SqlException ex) when (ex.Number is 262 or 229 or 15247)
        {
            CrashLog.Write("SqlBackup.VerifyPermission", ex);
            return new(path, false,
                "Kopia została utworzona z CHECKSUM, ale konto nie ma uprawnienia CREATE DATABASE do wykonania RESTORE VERIFYONLY. " +
                "Kopię należy okresowo przetestować poprzez odtworzenie na osobnej instancji testowej.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Plik kopii powstał na serwerze: {path}. Weryfikacja RESTORE VERIFYONLY nie powiodła się: {ex.Message}", ex);
        }
    }

    static string ServerPath(string folder, string filename)
    {
        char separator = folder.Contains('/') && !folder.Contains('\\') ? '/' : '\\';
        return $"{folder.TrimEnd('\\', '/')}{separator}{filename}";
    }

    static SqlCommand MaintenanceCommand(SqlConnection db, string sql, string? path = null)
    {
        var command = new SqlCommand(sql, db) { CommandTimeout = 0 };
        if (path is not null) command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = path;
        return command;
    }
}
