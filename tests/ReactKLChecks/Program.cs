using KartyLeczenia.ReactKL;

static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("ReactKL regression check failed: " + description);
    }

    private static void Main()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "reactkl-ini-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, """
                [Other]
                Role=client
                Server=not-the-archive
                [Archive]
                Role=server
                Server=SQLHOST,51433
                AppVersion=2.2.2
                [AnotherSection]
                Role=client
                """);
            Check(ReactInstallUpdates.ReadValue(path, "Role") == "server",
                "server role must come only from [Archive] section");
            Check(ReactInstallUpdates.ReadValue(path, "Server") == "SQLHOST,51433",
                "archive server address must be preserved");
            Check(ReactInstallUpdates.ReadValue(path, "AppVersion") == "2.2.2",
                "manually assigned version must be preserved");
            Check(ReactInstallUpdates.ReadValue(path, "Missing") is null,
                "unknown key must not be substituted");
            Check(ReactInstallUpdates.ReadValue(path + ".missing", "Role") is null,
                "missing config must be treated as not installed");

            File.WriteAllText(path, """
                [Archive]
                Role=client
                Server=RemoteHost\KLARCHIVE
                AppVersion=2.2.4
                """);
            Check(ReactInstallUpdates.ReadValue(path, "Role") == "client",
                "client role must survive an in-place upgrade");
            Check(ReactInstallUpdates.ReadValue(path, "Server") == @"RemoteHost\KLARCHIVE",
                "named SQL instance must survive an in-place upgrade");

            Check(ReactUpdateService.IsNewer("2.2.3", "2.2.2"),
                "higher manually assigned version offered");
            Check(ReactUpdateService.IsNewer("2.3.0", "2.2.99"),
                "version semantics, not alphabetic comparison");
            Check(!ReactUpdateService.IsNewer("2.2.2.0", "2.2.2"),
                "three/four segment versions must be equivalent");
            Check(!ReactUpdateService.IsNewer("2.2.2", "2.2.3"),
                "downgrade must never be offered");
            Check(!ReactUpdateService.IsNewer("2.2", "2.2.0"),
                "two/three segment versions must be equivalent");
            bool bad = false;
            try { _ = ReactUpdateService.IsNewer("not-a-version", "2.2.2"); }
            catch (InvalidDataException) { bad = true; }
            Check(bad, "malformed published SQL version must be rejected");

            Console.WriteLine($"ReactKL regression checks passed: {checks}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
