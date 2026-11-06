using Microsoft.Maui.Hosting;
namespace KartyLeczenia;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CrashLog.Initialize();
        try { return MauiApp.CreateBuilder().UseMauiApp<App>().Build(); }
        catch (Exception ex) { CrashLog.Write("Maui.Startup", ex); throw; }
    }
}
