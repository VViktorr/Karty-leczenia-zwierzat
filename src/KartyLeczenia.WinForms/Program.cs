namespace KartyLeczenia.Windows;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        CrashLog.Initialize();
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                CrashLog.Write("WinForms.ThreadException", e.Exception);
                MessageBox.Show($"Wystąpił błąd. Szczegóły zapisano w folderze:\n{CrashLog.Folder}",
                    "Karty leczenia zwierząt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            CrashLog.Write("WinForms.Application.Exit");
        }
        catch (Exception ex)
        {
            CrashLog.Write("WinForms.Application.Fatal", ex);
            MessageBox.Show($"Program został zakończony. Szczegóły zapisano w folderze:\n{CrashLog.Folder}",
                "Karty leczenia zwierząt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
