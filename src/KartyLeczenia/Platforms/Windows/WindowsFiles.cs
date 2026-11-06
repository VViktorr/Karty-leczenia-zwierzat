using Windows.Storage.Pickers;
namespace KartyLeczenia;
public static class WindowsFiles
{
    static nint Handle => WinRT.Interop.WindowNative.GetWindowHandle((Microsoft.UI.Xaml.Window)Application.Current!.Windows[0].Handler!.PlatformView!);
    public static async Task<string?> SaveAsync(string extension, string suggested)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggested };
        picker.FileTypeChoices.Add(extension == ".json" ? "Karta leczenia" : "Podgląd karty", new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        return (await picker.PickSaveFileAsync())?.Path;
    }
    public static async Task<string?> OpenAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
