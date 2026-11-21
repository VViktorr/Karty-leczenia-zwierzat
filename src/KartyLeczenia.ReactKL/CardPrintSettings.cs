using System.Text.Json;
using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

internal static class CardPrintSettings
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "card-print-react.json");

    internal static decimal? Load()
    {
        try
        {
            var height = JsonSerializer.Deserialize<decimal?>(File.ReadAllText(SettingsPath));
            return height is >= 1 and <= 150 ? height : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ReactKL.CardPrintSettings.Load", ex);
            return null;
        }
    }

    internal static void Save(decimal? height)
    {
        if (height is < 1 or > 150)
            throw new ArgumentException("Wysokość pieczątki musi wynosić od 1 do 150 mm.");

        string path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(height));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string Render(Card card) => CardHtmlRenderer.Render(card, Load());
}
