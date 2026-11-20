using System.Diagnostics;
using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// The installer keeps the existing installation directory, [Archive] role
/// and server address. A server publishes the exact setup EXE copied by Inno;
/// other workstations query the same dbo.program_update table as before.
/// </summary>
internal static class ReactInstallUpdates
{
    private static string InstallFile => Path.Combine(AppContext.BaseDirectory, "archive-installation.ini");

    internal static string? Read(string key) => ReadValue(InstallFile, key);

    internal static string? ReadValue(string path, string key)
    {
        if (!File.Exists(path)) return null;
        bool inArchive = false;
        foreach (string line in File.ReadLines(path))
        {
            string part = line.Trim();
            if (part.StartsWith("[") && part.EndsWith("]"))
            {
                inArchive = string.Equals(part, "[Archive]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inArchive || part.StartsWith(';') || part.StartsWith('#')) continue;
            int pos = part.IndexOf('=');
            if (pos >= 0 && string.Equals(part[..pos].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return part[(pos + 1)..].Trim();
        }
        return null;
    }

    internal static bool IsInstalled => File.Exists(InstallFile) &&
        Version.TryParse(Read("AppVersion"), out _);
    internal static bool IsServer => string.Equals(Read("Role"), "server", StringComparison.OrdinalIgnoreCase);
    internal static string? Server => Read("Server");

    internal static string InstalledVersion =>
        Version.TryParse(Read("AppVersion"), out _) ? Read("AppVersion")! : ReactUpdateService.CurrentVersion;

    internal static string PendingInstaller => Path.Combine(AppContext.BaseDirectory,
        $"KartyLeczenia-Update-Pending-v{InstalledVersion}.exe");

    internal static bool HasPendingPublication => IsServer && File.Exists(PendingInstaller);

    private static int publishing;

    internal static async Task PublishPending(CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref publishing, 1) != 0)
            throw new InvalidOperationException("Publikacja instalatora już trwa.");
        try { await PublishPendingCore(token); }
        finally { Interlocked.Exchange(ref publishing, 0); }
    }

    private static async Task PublishPendingCore(CancellationToken token)
    {
        if (!HasPendingPublication)
            throw new InvalidOperationException("Brak instalatora oczekującego na publikację na komputerze serwera SQL.");
        string server = Server ?? "";
        if (string.IsNullOrWhiteSpace(server) || !Version.TryParse(InstalledVersion, out _))
            throw new InvalidOperationException("Brak serwera archiwum lub wersji w archive-installation.ini.");

        string script = Path.Combine(AppContext.BaseDirectory, "Publish-ReactKLUpdate.ps1");
        if (!File.Exists(script))
            throw new FileNotFoundException("Brak skryptu publikacji aktualizacji ReactKL.", script);

        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(powershell)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (string argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            script, "-Server", server, "-Installer", PendingInstaller, "-Version",
            InstalledVersion, "-Pending", "-TrustServerCertificate"
        })
            info.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Windows nie uruchomił publikacji aktualizacji.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Nie udało się opublikować instalatora w SQL. Szczegóły: " +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "KartyLeczenia", "update-publish.log"));
    }
}
