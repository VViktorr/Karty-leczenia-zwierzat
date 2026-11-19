using System.Diagnostics;
using System.Security.Cryptography;

namespace KartyLeczenia.Windows;

internal static class ServerUpdateGate
{
    static string? Setting(string key)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "archive-installation.ini");
        if (!File.Exists(file)) return null;
        return File.ReadLines(file).Select(s => s.Trim())
            .FirstOrDefault(s => s.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim();
    }

    internal static bool IsServer => string.Equals(Setting("Role"), "server", StringComparison.OrdinalIgnoreCase);
    static string PendingInstaller => Path.Combine(AppContext.BaseDirectory, $"KartyLeczenia-Update-Pending-v{InstalledVersion}.exe");
    internal static bool HasPendingPublication => IsServer && File.Exists(PendingInstaller);

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

    internal static async Task<bool> OfferUpdate(Form owner, SharedArchive archive, PublishedUpdate release)
    {
        if (MessageBox.Show(owner,
            $"Dostępna wersja: {release.Version}. Obecna wersja: {InstalledVersion}.\n\nPobrać aktualizację i uruchomić instalator?",
            "Aktualizacja KartyLeczenia", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;

        string folder = Path.Combine(Path.GetTempPath(), "KartyLeczenia-Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string installer = Path.Combine(folder, "KartyLeczenia-Setup-win-x64.exe");
        bool started = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await archive.DownloadUpdate(release, installer, timeout.Token);
            byte[] hash;
            await using (var stream = File.OpenRead(installer))
                hash = await SHA256.HashDataAsync(stream, timeout.Token);
            if (!CryptographicOperations.FixedTimeEquals(hash, release.Sha256))
                throw new InvalidDataException("Suma SHA-256 pobranego instalatora jest niezgodna.");
            if (owner.IsDisposed || owner.Disposing) return false;

            using var process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" })
                ?? throw new InvalidOperationException("Nie udało się uruchomić instalatora.");
            started = true;
            return true;
        }
        finally
        {
            if (!started)
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashLog.Write("SqlUpdate.Cleanup", ex); }
            }
        }
    }

    internal static async Task PublishPending()
    {
        if (!HasPendingPublication)
            throw new InvalidOperationException("Brak instalatora oczekującego na publikację.");
        string server = SharedArchive.ReadInstalledServer();
        if (string.IsNullOrWhiteSpace(server) || !Version.TryParse(InstalledVersion, out _))
            throw new InvalidOperationException("Brak adresu archiwum lub poprawnego numeru zainstalowanej wersji.");

        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "Publish-KartyLeczeniaUpdate.ps1"),
            "-Server", server, "-Installer", PendingInstaller, "-Version", InstalledVersion, "-Pending" })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Nie udało się uruchomić publikacji.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Publikacja w SQL nie powiodła się. Szczegóły: " +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "KartyLeczenia", "update-publish.log"));
    }
}
