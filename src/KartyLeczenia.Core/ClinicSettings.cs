using System.Text.Json;
namespace KartyLeczenia;

public sealed record ClinicSettings(string Name = "", string Address = "", string Doctor = "")
{
    public string CardText => string.Join("\n", new[] { Name.Trim(), Address.Trim() }.Where(s => s.Length > 0));
}
public static class ClinicSettingsStore
{
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "clinic.json");
    public static ClinicSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<ClinicSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }
    public static void Save(ClinicSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings)); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
