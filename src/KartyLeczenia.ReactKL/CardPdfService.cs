using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Print pipeline from the final WinForms app: render the core Card HTML with
/// WebView2 to a real PDF, not a renamed HTML file. Runs on the UI STA thread.
/// PDF export uses a hidden renderer; interactive printing owns a separate preview window.
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

    internal static Task<string> RenderAsync(
        Form owner, string html, CancellationToken token, bool landscape = true,
        double marginMm = 7, bool allowScripts = true)
        => RenderOrPrintAsync(owner, html, token, landscape, marginMm, allowScripts);

    internal static async Task<bool> PrintAsync(Form owner, string html, CancellationToken token, string mode)
    {
        if (mode == "externalPdf")
        {
            await OpenExternalPdfAsync(owner, html, token);
            return true;
        }
        var dialogKind = mode switch
        {
            "" or "browser" => CoreWebView2PrintDialogKind.Browser,
            "system" => CoreWebView2PrintDialogKind.System,
            _ => throw new ArgumentException("Nieznany tryb drukowania karty.")
        };
        if (owner.OwnedForms.FirstOrDefault(form => form.Name == "CardWebViewPrintPreview") is Form existing)
        {
            existing.Activate();
            existing.Controls.OfType<WebView2>().FirstOrDefault()?.CoreWebView2?.ShowPrintUI(dialogKind);
            return true;
        }
        await RenderOrPrintAsync(owner, html, token, true, 7, true, dialogKind: dialogKind);
        return true;
    }

    private static async Task<string> RenderOrPrintAsync(
        Form owner, string html, CancellationToken token, bool landscape,
        double marginMm, bool allowScripts, CoreWebView2PrintDialogKind? dialogKind = null)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new ArgumentException("Brak treści karty do wygenerowania PDF.");
        bool showPrintPreview = dialogKind.HasValue;
        var name = Guid.NewGuid().ToString("N");
        string source = Path.Combine(Path.GetTempPath(), $"reactkl-print-{name}.html");
        string output = Path.Combine(Path.GetTempPath(), $"reactkl-print-{name}.pdf");
        // Use the existing printer profile, but not the React application view
        // (which must never navigate away from the active editing session).
        string profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KartyLeczenia", "WebView2", "Print");
        var shell = new Form
        {
            Name = showPrintPreview ? "CardWebViewPrintPreview" : "",
            Text = "Podgląd wydruku karty leczenia",
            FormBorderStyle = showPrintPreview ? FormBorderStyle.Sizable : FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = showPrintPreview ? FormStartPosition.CenterParent : FormStartPosition.Manual,
            Location = owner.Location,
            Size = new Size(900, 640),
            Opacity = 0
        };
        var browser = new WebView2 { Dock = DockStyle.Fill };
        bool keepPreviewOpen = false;
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
            browser.CoreWebView2.Settings.IsScriptEnabled = allowScripts;
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
            if (showPrintPreview)
            {
                shell.FormClosed += (_, _) => DeleteTemporarySource(source);
                shell.Opacity = 1;
                shell.Activate();
                browser.Focus();
                browser.CoreWebView2.ShowPrintUI(dialogKind!.Value);
                keepPreviewOpen = true;
                return "";
            }

            var print = browser.CoreWebView2.Environment.CreatePrintSettings();
            print.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            print.PageWidth = 210 / 25.4;
            print.PageHeight = 297 / 25.4;
            print.MarginTop = print.MarginBottom = print.MarginLeft =
                print.MarginRight = marginMm / 25.4;
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
            if (!keepPreviewOpen)
            {
                shell.Dispose();
                DeleteTemporarySource(source);
            }
        }
    }

    private static async Task OpenExternalPdfAsync(Form owner, string html, CancellationToken token)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ReactKL-CardPrint");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.EnumerateFiles(folder, "karta-leczenia-*.pdf"))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                DeleteTemporarySource(file);
        }
        string path = await RenderAsync(owner, html, token);
        string target = Path.Combine(folder, $"karta-leczenia-{Guid.NewGuid():N}.pdf");
        try
        {
            token.ThrowIfCancellationRequested();
            File.Move(path, target);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            DeleteTemporarySource(target);
            throw;
        }
        finally { DeleteTemporarySource(path); }
    }

    private static void DeleteTemporarySource(string source)
    {
        try { File.Delete(source); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
