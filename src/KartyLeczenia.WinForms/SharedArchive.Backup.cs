using System.Data;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia.Windows;

internal sealed partial class SharedArchive
{
    internal string BackupDatabase(ConnectionOptions administrator, string folder)
    {
        using var db = OpenMaintenance(administrator);
        return CreateBackup(db, folder, "kopia");
    }

    internal string RestoreDatabase(ConnectionOptions administrator, string path)
    {
        using var db = OpenMaintenance(administrator);
        using (var header = MaintenanceCommand(db, "RESTORE HEADERONLY FROM DISK = @path WITH FILE = 1;", path))
        using (var reader = header.ExecuteReader())
        {
            if (!reader.Read() || !string.Equals(reader["DatabaseName"] as string, DatabaseName, StringComparison.Ordinal)
                || Convert.ToInt32(reader["BackupType"]) != 1 || Convert.ToInt32(reader["Position"]) != 1)
                throw new InvalidOperationException("Wskaż plik z pełną kopią bazy KartyLeczenia.");
        }
        using (var verify = MaintenanceCommand(db, "RESTORE VERIFYONLY FROM DISK = @path WITH FILE = 1;", path))
            verify.ExecuteNonQuery();

        // Keep RESTORE WITH REPLACE inside the existing archive's own database files.
        var targetFiles = new HashSet<string>(StringComparer.Ordinal);
        using (var files = MaintenanceCommand(db, """
            SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(N'KartyLeczenia');
            """))
        using (var reader = files.ExecuteReader())
            while (reader.Read()) targetFiles.Add(reader.GetString(0));

        using (var files = MaintenanceCommand(db, "RESTORE FILELISTONLY FROM DISK = @path WITH FILE = 1;", path))
        using (var reader = files.ExecuteReader())
        {
            var backupFiles = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read()) backupFiles.Add((string)reader["PhysicalName"]);
            if (targetFiles.Count == 0 || !targetFiles.SetEquals(backupFiles))
                throw new InvalidOperationException("Odtwarzanie w programie wymaga istniejącej bazy KartyLeczenia z tymi samymi ścieżkami plików danych i dziennika co w kopii.");
        }

        using (var state = MaintenanceCommand(db, """
            SELECT COUNT(*) FROM sys.databases
            WHERE name = N'KartyLeczenia' AND state_desc = N'ONLINE' AND user_access_desc = N'MULTI_USER';
            """))
            if ((int)state.ExecuteScalar()! != 1)
                throw new InvalidOperationException("Baza KartyLeczenia musi być dostępna w trybie wieloużytkownikowym.");

        string safetyCopy = CreateBackup(db, "", "przed-odtworzeniem");
        try
        {
            using var restore = MaintenanceCommand(db, """
                ALTER DATABASE [KartyLeczenia] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                BEGIN TRY
                    RESTORE DATABASE [KartyLeczenia] FROM DISK = @path WITH FILE = 1, REPLACE, RECOVERY;
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

    SqlConnection OpenMaintenance(ConnectionOptions administrator)
    {
        var connectionString = new SqlConnectionStringBuilder((administrator with
        {
            Server = Server,
            TrustCertificate = options.TrustCertificate
        }).BuildConnectionString("master"))
        {
            ApplicationIntent = ApplicationIntent.ReadWrite,
            Pooling = false
        };
        var db = new SqlConnection(connectionString.ConnectionString);
        try
        {
            db.Open();
            using var access = MaintenanceCommand(db, """
                IF ISNULL(IS_SRVROLEMEMBER(N'sysadmin'), 0) <> 1
                    THROW 50010, N'Zaloguj się kontem administratora SQL Server (sysadmin).', 1;
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

    static string CreateBackup(SqlConnection db, string folder, string label)
    {
        if (folder.Length == 0)
        {
            using var location = MaintenanceCommand(db, "SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'));");
            folder = location.ExecuteScalar() as string ?? "";
        }
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException("Serwer SQL nie zwrócił domyślnego folderu kopii zapasowych. Wskaż folder dostępny dla SQL Servera.");

        char separator = folder.Contains('/') && !folder.Contains('\\') ? '/' : '\\';
        string path = $"{folder.TrimEnd('\\', '/')}{separator}KartyLeczenia-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
        using (var backup = MaintenanceCommand(db, """
            BACKUP DATABASE [KartyLeczenia] TO DISK = @path WITH COPY_ONLY, CHECKSUM;
            """, path))
            backup.ExecuteNonQuery();
        using (var verify = MaintenanceCommand(db, "RESTORE VERIFYONLY FROM DISK = @path WITH FILE = 1, CHECKSUM;", path))
            verify.ExecuteNonQuery();
        return path;
    }

    static SqlCommand MaintenanceCommand(SqlConnection db, string sql, string? path = null)
    {
        var command = new SqlCommand(sql, db) { CommandTimeout = 0 };
        if (path is not null) command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = path;
        return command;
    }
}
