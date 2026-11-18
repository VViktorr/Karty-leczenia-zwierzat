using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KartyLeczenia.Windows;

/// <summary>Offers an administrator-published update without preventing normal work.</summary>
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

    internal static string? UpdateShare => Setting("UpdateShare");

    internal static string InstalledVersion
    {
        get
        {
            // The setup version is saved by Inno Setup. Standalone builds use EXE metadata.
            string? version = Setting("AppVersion");
            if (!string.IsNullOrWhiteSpace(version) && Version.TryParse(version, out _))
                return version;
            string? executableVersion = FileVersionInfo.GetVersionInfo(
                Environment.ProcessPath ?? Application.ExecutablePath).ProductVersion;
            return !string.IsNullOrWhiteSpace(executableVersion) && Version.TryParse(executableVersion, out _)
                ? executableVersion : "nieznana";
        }
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
            {
                // A new deployment may not have published a release yet. Do not
                // lock users out simply because the update share is uninitialized.
                // An actual announced update remains mandatory below.
                CrashLog.Write("ServerUpdateGate.NoManifest",
                    new FileNotFoundException("Nie opublikowano jeszcze instalatora na serwerze.", manifest));
                return true;
            }
            var release = JsonSerializer.Deserialize<Release>(File.ReadAllText(manifest),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Pusty manifest aktualizacji.");
            if (!Version.TryParse(release.Version, out var required) || required is null)
                throw new InvalidDataException("Błędny numer wersji aktualizacji.");
            // The installer records the installed release version. Fall back to the
            // executable metadata for portable builds without installer settings.
            string installedVersion = InstalledVersion;
            if (!Version.TryParse(installedVersion, out var current) || current is null)
            {
                CrashLog.Write("ServerUpdateGate.UnknownVersion",
                    new InvalidDataException("Nie można ustalić wersji zainstalowanego programu."));
                return true;
            }
            if (current >= required) return true;
            if (string.IsNullOrWhiteSpace(release.Installer)
                || release.Installer != Path.GetFileName(release.Installer)
                || !release.Installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || release.Sha256 is null || release.Sha256.Length != 64
                || !release.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Manifest zawiera nieprawidłowe informacje o instalatorze.");

            string installer = Path.Combine(share, release.Installer);
            var choice = MessageBox.Show(
                $"Znaleziono nowszą wersję programu KartyLeczenia ({required}).\n" +
                $"Obecna wersja: {current}.\n\n" +
                "Aktualizować teraz?\n\n" +
                "Tak — pobierz aktualizację i uruchom instalator.\n" +
                "Nie — uruchom program bez aktualizacji.",
                "Dostępna aktualizacja KartyLeczenia",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes) return true;

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
                "Nie udało się sprawdzić lub uruchomić aktualizacji.\n\n" +
                ex.Message + "\n\nProgram zostanie uruchomiony bez aktualizacji.",
                "Aktualizacja KartyLeczenia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
    }
}
