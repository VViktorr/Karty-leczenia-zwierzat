using System.Text.Json;

namespace KartyLeczenia.Avalon;

internal sealed record ColumnPreference(string Key, int Order, double? Width);
internal sealed record DesktopPreferences(int Theme = 0, string? Font = null,
    decimal FontSize = 14, decimal RowHeight = 30, ColumnPreference[]? Columns = null,
    double WindowWidth = 1000, double WindowHeight = 680, bool Maximized = false, int Layout = 0)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "Avalon", "appearance.json");

    internal static DesktopPreferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    internal void Save()
    {
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
