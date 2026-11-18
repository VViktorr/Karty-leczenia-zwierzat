using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KartyLeczenia.Windows;

/// <summary>Checks the administrator-published installer before the client opens its archive.</summary>
internal static class ServerUpdateGate
{
    sealed record Release(string Version, string Installer, string Sha256);
    static string? Setting(string key)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "archive-installation.ini");
        if (!File.Exists(file)) return null;
        return File.ReadLines(file).Select(s => s.Trim())
            .FirstOrDefault(s => s.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim();
    }

    internal static bool Check(bool settingsOnly)
    {
        // Server controls publication. Settings can still be opened to repair a broken connection.
        if (settingsOnly || string.Equals(Setting("Role"), "server", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            string? share = Setting("UpdateShare");
            if (string.IsNullOrWhiteSpace(share)) return true;
            if (!share.StartsWith(@"\\", StringComparison.Ordinal) || share.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("Nieprawidłowy folder aktualizacji.");

            string manifest = Path.Combine(share, "release.json");
            if (!File.Exists(manifest))
                throw new IOException("Brak dostępu do manifestu aktualizacji na serwerze: " + manifest);
            var release = JsonSerializer.Deserialize<Release>(File.ReadAllText(manifest),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Pusty manifest aktualizacji.");
            if (!Version.TryParse(release.Version, out var required) || required is null)
                throw new InvalidDataException("Błędny numer wersji aktualizacji.");
            if (!Version.TryParse(Setting("AppVersion"), out var current) || current is null)
                throw new InvalidDataException("Brak wersji aktualnie zainstalowanego programu.");
            if (current >= required) return true;
            if (string.IsNullOrWhiteSpace(release.Installer)
                || release.Installer != Path.GetFileName(release.Installer)
                || !release.Installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || release.Sha256 is null || release.Sha256.Length != 64
                || !release.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Manifest zawiera nieprawidłowe informacje o instalatorze.");

            string installer = Path.Combine(share, release.Installer);
            var choice = MessageBox.Show(
                $"Serwer wymaga wersji {required}, a na tym komputerze jest {current}.\n\n" +
                "Przed dalszą pracą trzeba zainstalować aktualizację.\n" +
                "Tak — pobierz i uruchom instalator.\nNie — zamknij program.",
                "Wymagana aktualizacja KartyLeczenia",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (choice != DialogResult.Yes) return false;

            // Copy to local temp: the installer can restart even when the SMB share disconnects.
            string tempDir = Path.Combine(Path.GetTempPath(), "KartyLeczenia-Update", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string local = Path.Combine(tempDir, release.Installer);
            using (var source = File.OpenRead(installer))
            using (var target = File.Create(local))
                source.CopyTo(target);
            using var stream = File.OpenRead(local);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualHash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Suma SHA-256 instalatora nie zgadza się z wersją opublikowaną na serwerze.");
            Process.Start(new ProcessStartInfo(local) { UseShellExecute = true, Verb = "runas" });
            return false; // The new installer replaces this application after it exits.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or InvalidDataException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            CrashLog.Write("ServerUpdateGate.Check", ex);
            MessageBox.Show(
                "Nie można sprawdzić lub uruchomić wymaganej aktualizacji z serwera.\n\n" +
                ex.Message + "\n\nPraca została wstrzymana. Skontaktuj się z administratorem serwera.",
                "Kontrola aktualizacji", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
