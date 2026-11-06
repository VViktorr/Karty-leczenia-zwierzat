namespace KartyLeczenia.WinUI;
public partial class App : MauiWinUIApplication
{
    public App()
    {
        KartyLeczenia.CrashLog.Initialize();
        UnhandledException += (_, e) => KartyLeczenia.CrashLog.Write("WinUI.UnhandledException", e.Exception);
        try { InitializeComponent(); }
        catch (Exception ex) { KartyLeczenia.CrashLog.Write("WinUI.Initialize", ex); throw; }
    }
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
