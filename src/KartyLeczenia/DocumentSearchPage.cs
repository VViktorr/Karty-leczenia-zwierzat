namespace KartyLeczenia.Views;

public sealed class DocumentSearchPage : ContentPage
{
    readonly ConnectionOptions options;
    readonly DocumentSearchKinds kinds;
    readonly Entry query = new() { Placeholder = "Numer dokumentu, nazwisko lub nazwa klienta", MaxLength = 100, WidthRequest = 450 };
    readonly Picker year = new() { Title = "Rok", WidthRequest = 180 };
    readonly DatePicker from = new() { Format = "dd.MM.yyyy", WidthRequest = 160 }, until = new() { Format = "dd.MM.yyyy", WidthRequest = 160 };
    readonly CollectionView results = new() { SelectionMode = SelectionMode.Single };
    readonly Label status = Ui.Note("Wpisz numer lub nazwisko i naciśnij Enter. Kliknij dokument, aby go pobrać.");
    readonly TaskCompletionSource<DocumentSuggestion?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly DocumentService service = new();
    CancellationTokenSource? work;
    bool initialized, closing;
    public Task<DocumentSuggestion?> Completion => completion.Task;
    public DocumentSearchPage(ConnectionOptions connection, string input, DocumentSearchKinds selectedKinds)
    {
        options = connection; kinds = selectedKinds; Title = "Wybierz dokument";
        query.Text = input;
        int thisYear = DateTime.Today.Year;
        year.Items.Add("Wszystkie lata");
        for (int y = thisYear; y >= thisYear - 15; y--) year.Items.Add(y.ToString());
        year.Items.Add("Zakres dat");
        year.SelectedIndex = 1;
        var documentYear = System.Text.RegularExpressions.Regex.Match(input, @"(?:/|\s)(20\d{2})$");
        if (documentYear.Success && year.Items.Contains(documentYear.Groups[1].Value)) year.SelectedItem = documentYear.Groups[1].Value;
        from.Date = new DateTime(thisYear, 1, 1); until.Date = new DateTime(thisYear, 12, 31);
        var dates = new HorizontalStackLayout { Spacing = 12, IsVisible = false, Children = { Ui.Field("Od", from), Ui.Field("Do (włącznie)", until) } };
        var filter = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.End, Children = { Ui.Field("Numer lub klient", query), Ui.Field("Rok / okres", year), Ui.PrimaryAction("Szukaj", Search) } };
        var top = new VerticalStackLayout { Spacing = 10, Children = { Ui.Heading("Wyszukaj i wybierz dokument"), filter, dates, status } };
        results.EmptyView = new Label { Text = "Brak wyników. Zmień nazwę, numer albo rok.", Margin = 16 };
        results.ItemTemplate = new DataTemplate(() =>
        {
            var number = new Label { FontAttributes = FontAttributes.Bold, FontSize = 15 };
            number.SetBinding(Label.TextProperty, nameof(DocumentSuggestion.Number));
            var detail = Ui.Note(""); detail.SetBinding(Label.TextProperty, nameof(DocumentSuggestion.Details));
            return new VerticalStackLayout { Padding = new Thickness(14, 10), Spacing = 4, Children = { number, detail } };
        });
        results.SelectionChanged += async (_, e) =>
        {
            if (closing || e.CurrentSelection.FirstOrDefault() is not DocumentSuggestion chosen) return;
            closing = true; work?.Cancel(); completion.TrySetResult(chosen); await Navigation.PopModalAsync();
        };
        var root = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        root.Add(Ui.Card(top), 0, 0); root.Add(Ui.Card(results), 0, 1);
        root.Add(Ui.Action("Anuluj", async () => { closing = true; work?.Cancel(); completion.TrySetResult(null); await Navigation.PopModalAsync(); }), 0, 2);
        Content = root;
        query.TextChanged += (_, _) => { work?.Cancel(); results.ItemsSource = null; };
        query.Completed += async (_, _) => await Search();
        year.SelectedIndexChanged += async (_, _) => { dates.IsVisible = year.SelectedItem as string == "Zakres dat"; if (initialized) await Search(); };
        Loaded += async (_, _) => { if (initialized) return; initialized = true; await Search(); };
    }
    async Task Search()
    {
        work?.Cancel();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)); work = cts;
        results.ItemsSource = null;
        if (string.IsNullOrWhiteSpace(query.Text)) { status.Text = "Wpisz numer, nazwisko lub nazwę klienta."; work = null; return; }
        DateTime? start = null, end = null;
        string selected = year.SelectedItem as string ?? "Wszystkie lata";
        if (int.TryParse(selected, out int y)) { start = new DateTime(y, 1, 1); end = start.Value.AddYears(1); }
        else if (selected == "Zakres dat")
        {
            start = from.Date is DateTime f ? f.Date : DateTime.Today;
            var last = until.Date is DateTime t ? t.Date : DateTime.Today;
            if (last < start.Value) { status.Text = "Data końcowa nie może poprzedzać początkowej."; work = null; return; }
            end = last.AddDays(1);
        }
        status.Text = "Wyszukiwanie…";
        try
        {
            var found = await service.SearchAsync(options, query.Text.Trim(), cts.Token, kinds, start, end, 101);
            if (cts.IsCancellationRequested || closing) return;
            results.ItemsSource = found.Take(100).ToList();
            status.Text = found.Count > 100 ? "Ponad 100 wyników. Zawęź nazwę, numer lub okres. Kliknij dokument, aby go pobrać." :
                found.Count == 0 ? "Brak dokumentów w wybranym okresie. Spróbuj opcji „Wszystkie lata”." : $"Dokumenty: {found.Count}. Kliknij właściwy dokument — dane zostaną pobrane automatycznie.";
        }
        catch (OperationCanceledException) { if (!closing && ReferenceEquals(work, cts)) status.Text = "Wyszukiwanie anulowane lub przekroczono czas oczekiwania."; }
        catch (Exception ex) { if (!closing && ReferenceEquals(work, cts)) status.Text = SubiektConnectionService.FriendlyError(ex); }
        finally { if (ReferenceEquals(work, cts)) work = null; }
    }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!Navigation.ModalStack.Contains(this)) { closing = true; work?.Cancel(); completion.TrySetResult(null); }
    }
}
