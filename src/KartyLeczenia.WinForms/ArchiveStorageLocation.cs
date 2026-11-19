using System.Text.Json;

namespace KartyLeczenia.Windows;

internal static class ArchiveStorageLocation
{
    private sealed record Selection(string Path, string? VolumeLabel, string? RelativeFolder);
    private static string SettingsPath => Path.Combine(SharedArchive.LocalDataFolder, "backup-destination.json");

    internal static string? Resolve()
    {
        if (!File.Exists(SettingsPath)) return null;
        Selection? selection;
        try { selection = JsonSerializer.Deserialize<Selection>(File.ReadAllText(SettingsPath)); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { CrashLog.Write("BackupDestination.Read", ex); return null; }
        if (selection is null) return null;
        if (!string.IsNullOrEmpty(selection.VolumeLabel) && selection.RelativeFolder is not null)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Removable &&
                        string.Equals(drive.VolumeLabel, selection.VolumeLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        string candidate = Path.GetFullPath(Path.Combine(drive.RootDirectory.FullName, selection.RelativeFolder));
                        if (Directory.Exists(candidate)) return candidate;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashLog.Write("BackupDestination.Drive", ex); }
            }
            return null;
        }
        return Directory.Exists(selection.Path) ? selection.Path : null;
    }

    private static string OptionsPath => Path.Combine(SharedArchive.LocalDataFolder, "backup-options.json");

    internal static BackupPreferences LoadPreferences()
    {
        try
        {
            if (File.Exists(OptionsPath))
                return JsonSerializer.Deserialize<BackupPreferences>(File.ReadAllText(OptionsPath))
                    ?? new BackupPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { CrashLog.Write("BackupOptions.Read", ex); }
        return new BackupPreferences { DestinationFolder = Resolve() ?? "" };
    }

    internal static BackupPreferences? ResolvePreferences()
    {
        var options = LoadPreferences();
        string? destination = Resolve();
        return destination is null ? null : options with { DestinationFolder = destination };
    }

    internal static void SavePreferences(BackupPreferences options)
    {
        if (string.IsNullOrWhiteSpace(options.DestinationFolder) ||
            !Directory.Exists(options.DestinationFolder))
            throw new DirectoryNotFoundException("Nie znaleziono folderu archiwum.");
        _ = options.FileName(); // Validate the configured filename before persisting.
        Save(options.DestinationFolder);
        Directory.CreateDirectory(SharedArchive.LocalDataFolder);
        string staging = OptionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(staging, JsonSerializer.Serialize(options));
            File.Move(staging, OptionsPath, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    internal static void Save(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        string? label = null;
        string? relative = null;
        string root = Path.GetPathRoot(folder)!;
        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.DriveType == DriveType.Removable)
        {
            label = drive.VolumeLabel;
            if (string.IsNullOrWhiteSpace(label))
                throw new InvalidOperationException("Nadaj pendrive'owi nazwę woluminu, aby program mógł go rozpoznać przy kolejnym uruchomieniu.");
            relative = Path.GetRelativePath(root, folder);
        }
        Directory.CreateDirectory(SharedArchive.LocalDataFolder);
        string staging = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(staging, JsonSerializer.Serialize(new Selection(folder, label, relative)));
            File.Move(staging, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}

internal sealed record BackupPreferences
{
    public string DestinationFolder { get; init; } = "";
    // Absolute path visible to the SQL Server service (not a client-only path).
    public string WorkingFolder { get; init; } = "";
    public string Naming { get; init; } = "Dzienna";
    public string CustomName { get; init; } = "KartyLeczenia";
    public bool Overwrite { get; init; } = true;
    public bool Compress { get; init; }
    public string FileName()
    {
        string stem = Naming switch
        {
            "Stała nazwa" => "KartyLeczenia",
            "Miesięczna" => "KartyLeczenia_" + DateTime.Now.ToString("yyyy-MM"),
            "Tygodniowa" => "KartyLeczenia_" +
                System.Globalization.ISOWeek.GetYear(DateTime.Now).ToString() + "-T" +
                System.Globalization.ISOWeek.GetWeekOfYear(DateTime.Now).ToString("00"),
            "Własna" => CustomName,
            _ => "KartyLeczenia_" + DateTime.Now.ToString("yyyy-MM-dd")
        };
        stem = Path.GetFileNameWithoutExtension(stem.Trim());
        if (string.IsNullOrWhiteSpace(stem) || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            stem is "." or "..")
            throw new InvalidOperationException("Podaj prawidłową nazwę archiwum.");
        return stem + ".bak";
    }
}
