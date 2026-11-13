namespace KartyLeczenia.Views;

public sealed class PrintPreviewPage : ContentPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        CrashLog.Write("PrintPreviewPage.Open");
    }

    readonly WebView browser = new();
    readonly Label status = Ui.Note("Przygotowywanie formularza A4 poziomo…");
    readonly Button print, pdf;
    readonly string pdfName;
    bool ready, firstAction, closed;
    public PrintPreviewPage(string html, string firstMode, string title = "Podgląd karty leczenia", string pdfName = "karta-leczenia")
    {
        Title = title;
        this.pdfName = pdfName;
        print = Ui.Action("Drukuj", Print);
        pdf = Ui.PrimaryAction("Eksport do PDF", Export);
        print.IsEnabled = pdf.IsEnabled = false;
        var toolbar = new FlexLayout { Padding = 12, Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { print, pdf,
            Ui.Action("Zamknij podgląd", async () => { closed = true; await Navigation.PopModalAsync(); }) } };
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } } };
        root.Add(toolbar, 0, 0); root.Add(status, 0, 1); root.Add(browser, 0, 2); Content = Ui.WithClose(this, root, async () => { closed = true; await Navigation.PopModalAsync(); });
        browser.Navigated += async (_, e) =>
        {
            if (closed) return;
            ready = e.Result == WebNavigationResult.Success;
            if (ready)
            {
                try { ready = await browser.EvaluateJavaScriptAsync("document.querySelector('.sheet') !== null") == "true"; }
                catch (Exception) { ready = false; }
            }
            if (closed) return;
            print.IsEnabled = pdf.IsEnabled = ready;
            status.Text = ready ? "A4 poziomo. Podgląd, wydruk i PDF korzystają z tego samego formularza." : "Nie udało się przygotować podglądu.";
            if (!ready || firstAction) return; firstAction = true;
            try { if (firstMode == "print") await Print(); else if (firstMode == "pdf") await Export(); }
            catch (Exception ex) { status.Text = "Nie wykonano operacji: " + ex.Message; }
        };
        browser.Source = new HtmlWebViewSource { Html = html };
    }
    Task Print()
    {
        if (!ready) throw new InvalidOperationException("Poczekaj na załadowanie podglądu.");
        WindowsFiles.ShowPrint(browser);
        status.Text = "W oknie drukowania wybierz drukarkę, A4 i orientację poziomą. Wyłącz nagłówki i stopki.";
        return Task.CompletedTask;
    }
    async Task Export()
    {
        if (!ready) throw new InvalidOperationException("Poczekaj na załadowanie podglądu.");
        var path = await WindowsFiles.SaveAsync(".pdf", pdfName);
        if (path == null || closed) return;
        print.IsEnabled = pdf.IsEnabled = false;
        try { await WindowsFiles.ExportPdfAsync(browser, path); status.Text = "Zapisano PDF: " + path; }
        finally { if (!closed) print.IsEnabled = pdf.IsEnabled = true; }
    }
    protected override void OnDisappearing() { if (!Navigation.ModalStack.Contains(this)) closed = true; base.OnDisappearing(); }
}
