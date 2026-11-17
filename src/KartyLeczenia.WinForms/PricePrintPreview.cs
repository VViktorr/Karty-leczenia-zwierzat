using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KartyLeczenia.Windows;

internal sealed class PricePrintPreview : Form
{
    const int ResourceBusy = unchecked((int)0x800700AA);
    static readonly string PrintBrowserProfile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KartyLeczenia", "WebView2", "Print");

    static string CardAttachmentName(string? documentNumber)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string number = new string((documentNumber ?? "").Trim()
            .Select(character => invalid.Contains(character) ? '-' : character).ToArray())
            .Trim(' ', '.', '-');
        return number.Length == 0 ? "karta-leczenia.pdf" : $"karta-leczenia-{number}.pdf";
    }

    internal PricePrintPreview(string html, bool landscape, decimal margin, bool direct = false, bool treatmentCard = false,
        Func<Task<string?>>? findCardEmail = null, string? cardDocumentNumber = null)
    {
        Text = treatmentCard ? "Podgląd karty leczenia" : "Podgląd wydruku cennika";
        string baseName = treatmentCard ? "karta-leczenia" : "cennik-lekow";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 800);
        var browser = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var print = new Button { Text = "Drukuj", AutoSize = true, Enabled = false };
        var pdf = new Button { Text = "Zapisz jako PDF", AutoSize = true, Enabled = false };
        var close = new Button { Text = "Zamknij", AutoSize = true, DialogResult = DialogResult.Cancel };
        var email = new Button { Text = "Wyślij mailem", AutoSize = true, Enabled = false };
        bar.Controls.AddRange(treatmentCard ? [print, pdf, email, close] : [print, pdf, close]);
        Controls.Add(browser);
        Controls.Add(bar);
        CancelButton = close;
        if (direct)
        {
            Opacity = 0;
            ShowInTaskbar = false;
        }

        var file = Path.Combine(Path.GetTempPath(), $"{baseName}-{Guid.NewGuid():N}.html");
        var previewFile = Path.ChangeExtension(file, ".pdf");
        string? recoveryProfile = null;
        bool saving = false;
        FormClosing += (_, e) => e.Cancel = saving;
        Disposed += (_, _) =>
        {
            foreach (var path in new[] { file, previewFile })
            {
                try { File.Delete(path); }
                catch (IOException ex) { CrashLog.Write("PricePreview.Cleanup", ex); }
                catch (UnauthorizedAccessException ex) { CrashLog.Write("PricePreview.Cleanup", ex); }
            }
            if (recoveryProfile is not null && Directory.Exists(recoveryProfile))
            {
                try { Directory.Delete(recoveryProfile, recursive: true); }
                catch (IOException ex) { CrashLog.Write("PricePreview.ProfileCleanup", ex); }
                catch (UnauthorizedAccessException ex) { CrashLog.Write("PricePreview.ProfileCleanup", ex); }
            }
        };
        void Report(Exception ex)
        {
            CrashLog.Write("PricePreview.Failed", ex);
            if (!IsDisposed) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        CoreWebView2PrintSettings PrintSettings()
        {
            var settings = browser.CoreWebView2.Environment.CreatePrintSettings();
            settings.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = 210 / 25.4;
            settings.PageHeight = 297 / 25.4;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = (double)margin / 25.4;
            settings.ShouldPrintBackgrounds = true;
            return settings;
        }
        async Task InitializeBrowser()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: PrintBrowserProfile);
                if (!IsDisposed) await browser.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex) when (ex.HResult == ResourceBusy && !IsDisposed)
            {
                CrashLog.Write("PricePreview.ProfileBusy", ex);
                recoveryProfile = Path.Combine(Path.GetTempPath(), $"KartyLeczenia-WebView2-{Guid.NewGuid():N}");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: recoveryProfile);
                if (!IsDisposed) await browser.EnsureCoreWebView2Async(environment);
            }
        }

        Shown += async (_, _) =>
        {
            try
            {
                await InitializeBrowser();
                if (IsDisposed) return;
                async void PreparePreview(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    browser.CoreWebView2.NavigationCompleted -= PreparePreview;
                    if (!direct)
                    {
                        saving = true;
                        close.Enabled = false;
                        try
                        {
                            if (!e.IsSuccess || !await browser.CoreWebView2.PrintToPdfAsync(previewFile, PrintSettings()))
                                throw new IOException("Nie udało się przygotować podglądu stron.");
                            browser.CoreWebView2.NavigationCompleted += (_, loaded) =>
                            {
                                print.Enabled = pdf.Enabled = email.Enabled = loaded.IsSuccess;
                                if (!loaded.IsSuccess) Report(new IOException("Nie udało się otworzyć podglądu stron."));
                            };
                            browser.Source = new Uri(previewFile);
                            browser.Visible = true;
                        }
                        catch (Exception ex) { Report(ex); }
                        finally
                        {
                            saving = false;
                            close.Enabled = true;
                        }
                        return;
                    }
                    try
                    {
                        if (!e.IsSuccess) throw new IOException("Nie udało się przygotować wydruku cennika.");
                        var result = await browser.CoreWebView2.PrintAsync(PrintSettings());
                        if (result != CoreWebView2PrintStatus.Succeeded)
                            throw new IOException($"Nie udało się wysłać cennika na domyślną drukarkę ({result}).");
                    }
                    catch (Exception ex) { Report(ex); }
                    finally { Close(); }
                }
                browser.CoreWebView2.NavigationCompleted += PreparePreview;
                File.WriteAllText(file, html);
                browser.Source = new Uri(file);
            }
            catch (Exception ex) { Report(ex); if (direct) Close(); }
        };
        print.Click += (_, _) =>
        {
            try { browser.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
            catch (Exception ex) { Report(ex); }
        };
        if (treatmentCard)
        {
            email.Click += async (_, _) =>
            {
                saving = true;
                print.Enabled = pdf.Enabled = email.Enabled = close.Enabled = false;
                try
                {
                    var address = findCardEmail is null ? null : await findCardEmail();
                    PdfMail.Compose(Handle, previewFile, CardAttachmentName(cardDocumentNumber), address);
                }
                catch (Exception ex) { Report(ex); }
                finally
                {
                    saving = false;
                    print.Enabled = pdf.Enabled = email.Enabled = close.Enabled = true;
                }
            };
        }
        Disposed += (_, _) =>
        {
            if (!treatmentCard) email.Dispose();
        };
        pdf.Click += async (_, _) =>
        {
            using var target = new SaveFileDialog { Filter = "Dokument PDF (*.pdf)|*.pdf", FileName = baseName + ".pdf" };
            if (target.ShowDialog(this) != DialogResult.OK) return;
            saving = true;
            print.Enabled = pdf.Enabled = email.Enabled = close.Enabled = false;
            try
            {
                await Task.Run(() => File.Copy(previewFile, target.FileName, overwrite: true));
            }
            catch (Exception ex) { Report(ex); }
            finally
            {
                saving = false;
                print.Enabled = pdf.Enabled = email.Enabled = close.Enabled = true;
            }
        };
    }
}
