using Microsoft.Maui.Hosting;
namespace KartyLeczenia;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}
