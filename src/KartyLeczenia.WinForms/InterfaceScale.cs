namespace KartyLeczenia.Windows;

internal static class InterfaceScale
{
    internal static IReadOnlyList<int> Percentages { get; } = Array.AsReadOnly(Enumerable.Range(0, 25).Select(step => 80 + step * 5).ToArray());
    static readonly string FilePath = Path.Combine(SharedArchive.LocalDataFolder, "winforms-interface-scale.txt");

    internal static int Load()
    {
        try
        {
            return int.TryParse(File.ReadAllText(FilePath), out int value) && Percentages.Contains(value) ? value : 100;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 100;
        }
    }

    internal static void Save(int percentage)
    {
        Directory.CreateDirectory(SharedArchive.LocalDataFolder);
        string temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, percentage.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally { File.Delete(temporaryPath); }
    }
}
