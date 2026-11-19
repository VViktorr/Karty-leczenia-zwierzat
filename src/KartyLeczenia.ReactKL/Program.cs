using System.Globalization;
using KartyLeczenia;

namespace KartyLeczenia.ReactKL;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => CrashLog.Write("ReactKL.UI", args.Exception);
        CrashLog.Initialize();
        try { Application.Run(new ReactWindow()); }
        catch (Exception error)
        {
            CrashLog.Write("ReactKL.Fatal", error);
            MessageBox.Show("Nie udało się uruchomić ReactKL:\n" + error.Message,
                "ReactKL", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
