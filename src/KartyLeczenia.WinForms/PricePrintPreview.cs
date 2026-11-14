using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KartyLeczenia.Windows;

internal sealed class PricePrintPreview : Form
{
    internal PricePrintPreview(string html, bool landscape, decimal margin)
    {
        Text = "Podgląd wydruku cennika";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 800);
        var browser = new WebView2 { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var print = new Button { Text = "Drukuj", AutoSize = true, Enabled = false };
        var pdf = new Button { Text = "Zapisz jako PDF", AutoSize = true, Enabled = false };
        var close = new Button { Text = "Zamknij", AutoSize = true, DialogResult = DialogResult.Cancel };
        bar.Controls.AddRange([print, pdf, close]);
        Controls.Add(browser);
        Controls.Add(bar);
        CancelButton = close;

        var file = Path.Combine(Path.GetTempPath(), $"cennik-lekow-{Guid.NewGuid():N}.html");
        bool saving = false;
        FormClosing += (_, e) => e.Cancel = saving;
        Disposed += (_, _) =>
        {
            try { File.Delete(file); }
            catch (IOException ex) { CrashLog.Write("PricePreview.Cleanup", ex); }
            catch (UnauthorizedAccessException ex) { CrashLog.Write("PricePreview.Cleanup", ex); }
        };
        void Report(Exception ex)
        {
            CrashLog.Write("PricePreview.Failed", ex);
            if (!IsDisposed) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Shown += async (_, _) =>
        {
            try
            {
                await browser.EnsureCoreWebView2Async();
                if (IsDisposed) return;
                browser.CoreWebView2.NavigationCompleted += (_, e) => print.Enabled = pdf.Enabled = e.IsSuccess;
                File.WriteAllText(file, html);
                browser.Source = new Uri(file);
            }
            catch (Exception ex) { Report(ex); }
        };
        print.Click += (_, _) =>
        {
            try { browser.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
            catch (Exception ex) { Report(ex); }
        };
        pdf.Click += async (_, _) =>
        {
            using var target = new SaveFileDialog { Filter = "Dokument PDF (*.pdf)|*.pdf", FileName = "cennik-lekow.pdf" };
            if (target.ShowDialog(this) != DialogResult.OK) return;
            saving = true;
            print.Enabled = pdf.Enabled = close.Enabled = false;
            try
            {
                var settings = browser.CoreWebView2.Environment.CreatePrintSettings();
                settings.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
                settings.PageWidth = 210 / 25.4;
                settings.PageHeight = 297 / 25.4;
                settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = (double)margin / 25.4;
                settings.ShouldPrintBackgrounds = true;
                if (!await browser.CoreWebView2.PrintToPdfAsync(target.FileName, settings))
                    throw new IOException("Nie udało się zapisać cennika jako PDF.");
            }
            catch (Exception ex) { Report(ex); }
            finally
            {
                saving = false;
                print.Enabled = pdf.Enabled = close.Enabled = true;
            }
        };
    }
}
