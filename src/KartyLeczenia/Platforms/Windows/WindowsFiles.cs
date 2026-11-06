using Windows.Storage.Pickers;
namespace KartyLeczenia;
public static class WindowsFiles
{
    static nint Handle => WinRT.Interop.WindowNative.GetWindowHandle((Microsoft.UI.Xaml.Window)Application.Current!.Windows[0].Handler!.PlatformView!);
    public static async Task<string?> SaveAsync(string extension, string suggested)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggested };
        picker.FileTypeChoices.Add(extension == ".json" ? "Karta leczenia" : extension == ".pdf" ? "Karta leczenia PDF" : "Podgląd karty", new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        return (await picker.PickSaveFileAsync())?.Path;
    }
    public static async Task<string?> OpenAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    static Microsoft.UI.Xaml.Controls.WebView2 Native(WebView view) => view.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.WebView2
        ?? throw new InvalidOperationException("Podgląd WebView2 nie jest jeszcze gotowy.");
    public static void ShowPrint(WebView view)
    {
        var core = Native(view).CoreWebView2 ?? throw new InvalidOperationException("Poczekaj na załadowanie dokumentu.");
        core.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
    }
    public static async Task ExportPdfAsync(WebView view, string destination)
    {
        var native = Native(view); await native.EnsureCoreWebView2Async();
        var core = native.CoreWebView2;
        var settings = core.Environment.CreatePrintSettings();
        settings.Orientation = Microsoft.Web.WebView2.Core.CoreWebView2PrintOrientation.Landscape;
        settings.PageWidth = 210d / 25.4d; settings.PageHeight = 297d / 25.4d;
        settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
        settings.ShouldPrintBackgrounds = true; settings.ShouldPrintHeaderAndFooter = false;
        if (!await core.PrintToPdfAsync(destination, settings)) throw new IOException("Nie udało się zapisać pliku PDF.");
    }
}
