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
