using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Print pipeline from the final WinForms app: render the core Card HTML with
/// WebView2 to a real PDF, not a renamed HTML file. Runs on the UI STA thread.
/// The temporary renderer is never a user-facing WinForms screen.
/// </summary>
internal static class CardPdfService
{
    internal static string AttachmentName(string? documentNumber)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var number = new string((documentNumber ?? "").Trim()
            .Select(character => invalid.Contains(character) ? '-' : character).ToArray())
            .Trim(' ', '.', '-');
        return number.Length == 0 ? "karta-leczenia.pdf"
            : $"karta-leczenia-{number}.pdf";
    }

    internal static async Task<string> RenderAsync(
        Form owner, string html, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new ArgumentException("Brak treści karty do wygenerowania PDF.");
        var name = Guid.NewGuid().ToString("N");
        string source = Path.Combine(Path.GetTempPath(), $"reactkl-print-{name}.html");
        string output = Path.Combine(Path.GetTempPath(), $"reactkl-print-{name}.pdf");
        // Use the existing printer profile, but not the React application view
        // (which must never navigate away from the active editing session).
        string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KartyLeczenia", "WebView2", "Print");
        using var shell = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = owner.Location,
            Size = new Size(900, 640),
            Opacity = 0
        };
        using var browser = new WebView2 { Dock = DockStyle.Fill };
        shell.Controls.Add(browser);
        try
        {
            File.WriteAllText(source, html);
            token.ThrowIfCancellationRequested();
            shell.Show(owner);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: profile);
            token.ThrowIfCancellationRequested();
            await browser.EnsureCoreWebView2Async(environment);
            var ready = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2NavigationCompletedEventArgs> navigation =
                (_, args) =>
                {
                    if (args.IsSuccess) ready.TrySetResult(true);
                    else ready.TrySetException(
                        new IOException("Nie udało się wczytać karty do druku PDF."));
                };
            browser.CoreWebView2.NavigationCompleted += navigation;
            try
            {
                browser.CoreWebView2.Navigate(new Uri(source).AbsoluteUri);
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            }
            finally { browser.CoreWebView2.NavigationCompleted -= navigation; }
            token.ThrowIfCancellationRequested();
            var print = browser.CoreWebView2.Environment.CreatePrintSettings();
            print.Orientation = CoreWebView2PrintOrientation.Landscape;
            print.PageWidth = 210 / 25.4;
            print.PageHeight = 297 / 25.4;
            print.MarginTop = print.MarginBottom = print.MarginLeft =
                print.MarginRight = 7 / 25.4;
            print.ShouldPrintBackgrounds = true;
            if (!await browser.CoreWebView2.PrintToPdfAsync(output, print) ||
                !File.Exists(output) || new FileInfo(output).Length < 100)
                throw new IOException("Nie udało się utworzyć dokumentu PDF.");
            await using var verify = File.OpenRead(output);
            var header = new byte[5];
            if (await verify.ReadAsync(header, token) != header.Length ||
                System.Text.Encoding.ASCII.GetString(header) != "%PDF-")
                throw new InvalidDataException("Wygenerowany plik nie jest dokumentem PDF.");
            return output;
        }
        catch
        {
            try { if (File.Exists(output)) File.Delete(output); }
            catch (IOException) { }
            throw;
        }
        finally
        {
            if (!shell.IsDisposed) shell.Close();
            try { File.Delete(source); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void MoveToDestination(string source, string target)
    {
        string stage = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, stage);
            File.Move(stage, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(stage)) File.Delete(stage); }
            catch (IOException) { }
        }
    }
}
