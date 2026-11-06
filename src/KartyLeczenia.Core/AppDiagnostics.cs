using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KartyLeczenia;

public enum ThemeMode { System, Light, Dark }
public sealed record UiPreferences(ThemeMode Theme = ThemeMode.System);
public static class UiPreferencesStore
{
    static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "appearance.json");
    public static UiPreferences Load()
    {
        try
        {
            var value = File.Exists(PathName) ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(PathName)) : null;
            return value != null && Enum.IsDefined(value.Theme) ? value : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { CrashLog.Write("Appearance.Load", ex); return new(); }
    }
    public static void Save(UiPreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        string temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, PathName, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class CrashLog
{
    static readonly object Gate = new();
    static readonly HashSet<string> Secrets = new(StringComparer.Ordinal);
    static bool initialized;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "logs");
    public static void RegisterSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (Gate) Secrets.Add(value);
    }
    public static void Initialize()
    {
        lock (Gate)
        {
            if (initialized) return;
            initialized = true;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain.UnhandledException", e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception"));
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Task.UnobservedTaskException", e.Exception);
        Write("Application.Start");
    }
    static string Sanitize(string value)
    {
        value = Regex.Replace(value, @"(?i)\b(password|pwd|user id|uid|data source|server|initial catalog|database)\s*=\s*(?:""[^""]*""|'[^']*'|[^;\r\n]*)", "$1=[redacted]");
        foreach (string secret in Secrets.OrderByDescending(s => s.Length)) value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return value;
    }
    public static void Write(string operation, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "app-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Move(path, Path.Combine(Folder, "app-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmssfff") + ".log"), true);
                var text = new StringBuilder();
                text.AppendLine(DateTimeOffset.Now.ToString("O") + " | " + operation);
                text.AppendLine("App: " + Assembly.GetEntryAssembly()?.GetName().Version + " | " + RuntimeInformation.FrameworkDescription + " | " + RuntimeInformation.OSDescription);
                for (var current = error; current != null; current = current.InnerException)
                {
                    text.AppendLine(current.GetType().FullName + " (0x" + current.HResult.ToString("X8") + "): " + current.Message);
                    text.AppendLine(current.StackTrace);
                }
                File.AppendAllText(path, Sanitize(text.ToString()) + Environment.NewLine, Encoding.UTF8);
                foreach (var old in new DirectoryInfo(Folder).GetFiles("app-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(10)) old.Delete();
            }
        }
        catch { /* Logging must never cause another application failure. */ }
    }
}
