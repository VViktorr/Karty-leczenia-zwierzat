using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace KartyLeczenia.Views;

public sealed class CardPage : ContentPage
{
    readonly AppState state;
    readonly ObservableCollection<TreatmentRow> rows = new();
    readonly CollectionView list = new() { SelectionMode = SelectionMode.Single };
    readonly Dictionary<string, Editor> header = new(), editors = new();
    readonly Entry number = new() { Placeholder = "Numer lub fragment — podpowiedzi od 4 znaków", WidthRequest = 320, MaxLength = 100 };
    readonly Picker documentSuggestions = new() { Title = "Podpowiedzi dokumentów", WidthRequest = 320 };
    CancellationTokenSource? documentSearch;
    DocumentSuggestion? chosenDocument;
    ConnectionOptions? documentLookupConnection;
    bool choosingDocument;
    readonly Picker withdrawal = new() { Title = "Źródło karencji", WidthRequest = 260 };
    readonly Picker owner = new() { Title = "Dane właściciela", WidthRequest = 150 };
    readonly Label status = Ui.Note("Wybierz dokument lub otwórz zapisaną kartę.");
    readonly Label provenance = Ui.Note("Wybierz lek z listy, aby poprawić jego dane.");
    readonly CheckBox printed = new();
    readonly DocumentService documents = new();
    readonly VerticalStackLayout detail = new() { Spacing = 10, Padding = 12 };
    TreatmentRow? selected;
    bool changing;
    string scope = "", payer = "", recipient = "";
    CancellationTokenSource? automaticWork;
    int documentGeneration;
    string lastClinic = "", lastDoctor = "";

    public CardPage(AppState appState)
    {
        state = appState; Title = "Karta leczenia";
        withdrawal.Items.Add("Unijna baza UPD (domyślnie)"); withdrawal.Items.Add("Opis leku");
        for (int i = 1; i <= 8; i++) withdrawal.Items.Add("Pole własne " + i);
        withdrawal.SelectedIndex = 0; owner.Items.Add("Płatnik"); owner.Items.Add("Odbiorca"); owner.SelectedIndex = 0;
        owner.SelectedIndexChanged += (_, _) => { if (!changing && scope != "") header["Właściciel i adres"].Text = owner.SelectedIndex == 0 ? payer : recipient; };
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        actions.Add(Ui.Field("Faktura, paragon lub WZ", number));
        actions.Add(Ui.Field("Wybierz dokument", documentSuggestions));
        actions.Add(Ui.Field("Źródło karencji", withdrawal)); actions.Add(Ui.Field("Dane właściciela", owner));
        var documentActions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        documentActions.Add(Ui.PrimaryAction("Pobierz dokument", LoadDocument));
        documentActions.Add(Ui.Action("Otwórz kartę", OpenCard)); documentActions.Add(Ui.Action("Zapisz kartę", SaveCard));
        documentActions.Add(Ui.Action("Podgląd", () => Preview("preview")));
        documentActions.Add(Ui.Action("Drukuj", () => Preview("print")));
        documentActions.Add(Ui.PrimaryAction("Eksport do PDF", () => Preview("pdf")));
        var documentPanel = new VerticalStackLayout { Spacing = 8, Children = { Ui.Heading("1. Dokument źródłowy"), actions, documentActions } };
        var headerPanel = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (string field in CardSchema.Fields)
        {
            var input = new Editor { HeightRequest = 56, WidthRequest = 265 };
            header.Add(field, input); var panel = Ui.Field(field, input); panel.Margin = new Thickness(0, 0, 12, 8); headerPanel.Add(panel);
        }
        list.ItemsSource = rows;
        list.ItemTemplate = new DataTemplate(() =>
        {
            var check = new CheckBox { VerticalOptions = LayoutOptions.Center };
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(TreatmentRow.Printed), mode: BindingMode.TwoWay));
            var name = new Label { FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap };
            name.SetBinding(Label.TextProperty, nameof(TreatmentRow.Product));
            var summary = Ui.Note(""); summary.SetBinding(Label.TextProperty, nameof(TreatmentRow.Summary));
            var text = new VerticalStackLayout { Spacing = 3, Children = { name, summary } };
            var row = new Grid { Padding = 8, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Star } } };
            row.Add(check, 0, 0); row.Add(text, 1, 0); return row;
        });
        list.SelectionChanged += (_, e) => Select(e.CurrentSelection.FirstOrDefault() as TreatmentRow);
        printed.CheckedChanged += (_, e) => { if (!changing && selected != null) selected.Printed = e.Value; };
        detail.Add(Ui.Heading("4. Dane wybranego leku"));
        detail.Add(Ui.Note("Wybierz lek z listy po lewej. Każde pole możesz poprawić przed wydrukiem."));
        detail.Add(new HorizontalStackLayout { Children = { printed, new Label { Text = "Uwzględnij na wydruku", VerticalOptions = LayoutOptions.Center } } });
        detail.Add(Ui.Action("Karencja z UPD / RPL…", ApplyRegistry));
        foreach (string field in CardSchema.RowFields.Concat(CardSchema.ContextFields))
        {
            var editor = new Editor { HeightRequest = field is "Karencja" or "Dawkowanie i okres stosowania" or "Zalecenia / uwagi" ? 82 : 48 };
            string key = field;
            editor.TextChanged += (_, e) => { if (!changing && selected != null) { selected.Set(key, e.NewTextValue ?? ""); provenance.Text = selected.RegistryStatus + "\n" + selected.Get("Źródło karencji"); } };
            editors.Add(field, editor); detail.Add(Ui.Field(field, editor));
        }
        detail.Add(provenance); detail.IsEnabled = false;
        var split = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(0.36, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(0.64, GridUnitType.Star) } } };
        var drugList = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } }, RowSpacing = 8 };
        drugList.Add(new VerticalStackLayout { Children = { Ui.Heading("3. Leki i dostawy"), Ui.Note("Po pobraniu dokumentu wybierz pozycję do edycji.") } }, 0, 0);
        list.EmptyView = new Label { Text = "Brak pozycji. Wybierz dokument powyżej lub dodaj lek ręcznie.", Margin = 12, LineBreakMode = LineBreakMode.WordWrap };
        drugList.Add(list, 0, 1);
        split.Add(Ui.Card(drugList), 0, 0); split.Add(Ui.Card(new ScrollView { Content = detail }), 1, 0);
        var rowActions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = {
            Ui.Action("Uzupełnij brakujące karencje", () => FillWithdrawals(documentGeneration)),
            Ui.Action("Dodaj pozycję ręcznie", () => { var row = new TreatmentRow(); row.Set("Produkt", "Nowa pozycja"); rows.Add(row); list.SelectedItem = row; return Task.CompletedTask; }),
            Ui.Action("Usuń wybraną pozycję", async () => { if (selected != null && await DisplayAlertAsync("Usuń pozycję", "Usunąć zaznaczoną pozycję z tej karty?", "Usuń", "Anuluj")) rows.Remove(selected); })
        } };
        var root = new Grid { Padding = 18, RowSpacing = 8, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(0) }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        var cardDetails = Ui.Card(new ScrollView { Content = new VerticalStackLayout { Spacing = 8, Children = { Ui.Heading("2. Dane karty"), headerPanel } } });
        cardDetails.IsVisible = false;
        documentActions.Add(Ui.Action("Pokaż / ukryj dane karty", () => { cardDetails.IsVisible = !cardDetails.IsVisible; root.RowDefinitions[1].Height = new GridLength(cardDetails.IsVisible ? 225 : 0); return Task.CompletedTask; }));
        root.Add(Ui.Card(documentPanel), 0, 0); root.Add(cardDetails, 0, 1); root.Add(rowActions, 0, 2); root.Add(split, 0, 3); root.Add(status, 0, 4);
        Content = root;
        number.TextChanged += async (_, _) =>
        {
            if (choosingDocument) return;
            chosenDocument = null; documentSuggestions.ItemsSource = null;
            documentSearch?.Cancel();
            if ((number.Text?.Trim().Length ?? 0) < 4 || state.ValidatedConnection == null) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); documentSearch = cts;
            var options = state.ValidatedConnection; string input = number.Text!.Trim();
            try
            {
                await Task.Delay(400, cts.Token);
                var found = await documents.SearchAsync(options, input, cts.Token);
                if (cts.IsCancellationRequested || number.Text?.Trim() != input || state.ValidatedConnection != options) return;
                documentLookupConnection = options; documentSuggestions.ItemsSource = found;
                documentSuggestions.Title = found.Count >= 21 ? "Ponad 20 wyników — dopisz numer" : $"Dokumenty: {found.Count} — wybierz";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!cts.IsCancellationRequested && state.ValidatedConnection == options) status.Text = "Nie pobrano podpowiedzi: " + ex.Message; }
            finally { if (ReferenceEquals(documentSearch, cts)) documentSearch = null; }
        };
        documentSuggestions.SelectedIndexChanged += (_, _) =>
        {
            if (documentSuggestions.SelectedItem is not DocumentSuggestion suggestion) return;
            documentSearch?.Cancel(); choosingDocument = true; number.Text = suggestion.Number; choosingDocument = false; chosenDocument = suggestion;
        };
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (rows.Count > 0 || !string.IsNullOrWhiteSpace(header["Dokument źródłowy"].Text)) return;
        if (header["Lecznica i adres"].Text is null or "" || header["Lecznica i adres"].Text == lastClinic)
            header["Lecznica i adres"].Text = state.Clinic.CardText;
        if (header["Lekarz i nr prawa wykonywania zawodu"].Text is null or "" || header["Lekarz i nr prawa wykonywania zawodu"].Text == lastDoctor)
            header["Lekarz i nr prawa wykonywania zawodu"].Text = state.Clinic.Doctor;
        lastClinic = state.Clinic.CardText; lastDoctor = state.Clinic.Doctor;
    }
    void Select(TreatmentRow? row)
    {
        selected = row; changing = true;
        foreach (var pair in editors) pair.Value.Text = row?.Get(pair.Key) ?? "";
        printed.IsChecked = row?.Printed ?? false;
        provenance.Text = row is null ? "Wybierz pozycję." : row.RegistryStatus + "\n" + row.Get("Źródło karencji");
        detail.IsEnabled = row != null; changing = false;
    }
    Card Capture()
    {
        var fields = header.ToDictionary(p => p.Key, p => p.Value.Text ?? ""); fields["Zakres mapowania towarów"] = scope;
        return new(fields, rows.Select(r => new Dictionary<string, string>(r.Values)).ToList());
    }
    async Task LoadDocument()
    {
        var options = state.ValidatedConnection ?? throw new InvalidOperationException("Najpierw przetestuj połączenie z Subiektem.");
        if (documentLookupConnection != options) chosenDocument = null;
        if (chosenDocument == null && !string.IsNullOrWhiteSpace(number.Text) && number.Text.Trim().Length >= 4)
        {
            using var searchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string input = number.Text.Trim();
            var found = await documents.SearchAsync(options, input, searchTimeout.Token);
            if (state.ValidatedConnection != options || number.Text?.Trim() != input) return;
            documentLookupConnection = options;
            var exact = found.Where(d => d.Number.Equals(input, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) chosenDocument = exact[0];
            else if (found.Count == 1) chosenDocument = found[0];
            else { documentSuggestions.ItemsSource = found; status.Text = found.Count == 0 ? "Nie znaleziono dokumentu." : "Wybierz dokument z podpowiedzi; możesz dopisać dalszą część numeru."; return; }
            choosingDocument = true; number.Text = chosenDocument.Number; choosingDocument = false;
        }
        if (rows.Count > 0 && !await DisplayAlertAsync("Nowy dokument", "Pobranie zastąpi aktualnie edytowane pozycje. Kontynuować?", "Pobierz", "Anuluj")) return;
        automaticWork?.Cancel();
        int generation = ++documentGeneration;
        status.Text = "Pobieranie leków z dokumentu…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var data = await documents.ReadAsync(options, number.Text ?? "", withdrawal.SelectedIndex, timeout.Token, chosenDocument?.Id);
        if (generation != documentGeneration) return;
        if (state.ValidatedConnection != options) throw new InvalidOperationException("Zmieniono połączenie w trakcie pobierania. Ponów operację.");
        Select(null); rows.Clear(); foreach (var row in data.Rows) rows.Add(row);
        scope = data.Scope; payer = data.Payer; recipient = data.Recipient;
        header["Właściciel i adres"].Text = owner.SelectedIndex == 0 ? payer : recipient;
        header["Dokument źródłowy"].Text = data.Number;
        if (rows.Count > 0) list.SelectedItem = rows[0];
        status.Text = $"Pobrano {rows.Count} pozycji dostaw z grupy {options.DrugGroup}.";
        if (withdrawal.SelectedIndex == 0) await FillWithdrawals(generation);
    }
    async Task FillWithdrawals(int generation)
    {
        automaticWork?.Cancel();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); automaticWork = timeout;
        var targets = rows.ToList(); string sourceScope = scope;
        int filled = 0, missing = 0;
        try
        {
            if (generation != documentGeneration || timeout.IsCancellationRequested) return;
            status.Text = "Automatyczne pobieranie karencji z UPD…";
            using var gate = new SemaphoreSlim(3);
            var tasks = targets.GroupBy(r => AutomaticWithdrawalService.MappingKey(sourceScope, r)).Select(async group =>
            {
                string originalProduct = group.First().Product;
                AutomaticWithdrawal? resolved = null;
                string? error = null;
                await gate.WaitAsync(timeout.Token);
                try { resolved = await AutomaticWithdrawalService.ResolveAsync(sourceScope, group.First(), timeout.Token); }
                catch (Exception ex) { error = ex is OperationCanceledException ? "Przekroczono czas pobierania UPD." : ex.Message; }
                finally { gate.Release(); }
                if (generation != documentGeneration || timeout.IsCancellationRequested) return;
                foreach (var row in group)
                {
                    if (!rows.Contains(row) || row.Product != originalProduct || row.Get("Karencja") != "") continue;
                    if (resolved != null) { AutomaticWithdrawalService.Apply(row, resolved); filled++; }
                    else { row.Set("Status karencji", error == null ? "Brak zapamiętanego dopasowania UPD — wybierz lek" : "UPD: " + error, false); missing++; }
                }
            }).ToArray();
            await Task.WhenAll(tasks);
            if (generation == documentGeneration)
            {
                Select(selected);
                status.Text = $"Karencję uzupełniono automatycznie w {filled} pozycjach. Do dopasowania lub ponowienia: {missing}. Sprawdź źródło, produkt i leczenie przed wydrukiem.";
            }
        }
        catch (OperationCanceledException) { if (generation == documentGeneration) status.Text = "Przekroczono czas UPD. Już pobrane dane pozostają w karcie; brakujące pozycje ponów przez przycisk karencji."; }
        finally { if (ReferenceEquals(automaticWork, timeout)) automaticWork = null; }
    }

    async Task SaveCard()
    {
        var snapshot = Capture();
        var path = await WindowsFiles.SaveAsync(".json", "karta-" + Guid.NewGuid().ToString("N"));
        if (path == null) return;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        status.Text = "Zapisano kartę: " + path;
    }
    async Task OpenCard()
    {
        var path = await WindowsFiles.OpenAsync(); if (path == null) return;
        var card = JsonSerializer.Deserialize<Card>(await File.ReadAllTextAsync(path));
        if (card?.Fields is null || card.Rows is null || card.Rows.Any(r => r is null)) throw new InvalidDataException("Nieprawidłowy plik karty.");
        if (!await DisplayAlertAsync("Otwórz kartę", "Zastąpić aktualnie edytowaną kartę?", "Otwórz", "Anuluj")) return;
        automaticWork?.Cancel(); documentGeneration++;
        changing = true;
        foreach (var pair in header) pair.Value.Text = card.Fields.GetValueOrDefault(pair.Key, "");
        Select(null); rows.Clear(); foreach (var row in card.Rows) rows.Add(new(row));
        scope = card.Fields.GetValueOrDefault("Zakres mapowania towarów", "archiwum");
        payer = recipient = header["Właściciel i adres"].Text ?? ""; changing = false;
        if (rows.Count > 0) list.SelectedItem = rows[0]; status.Text = "Otworzono kartę. Zapisane dane źródłowe pozostają zachowane.";
    }
    async Task ApplyRegistry()
    {
        var row = selected ?? throw new InvalidOperationException("Wybierz lek.");
        string key = AutomaticWithdrawalService.MappingKey(scope, row);
        var page = new UpdRegistryPage(row.Product, UpdService.ReadMapping(key), RegistryService.ReadMapping(key));
        await Navigation.PushModalAsync(page);
        var result = await page.Completion;
        if (result == null || !rows.Contains(row)) return;
        if (result.Document.UpdId != null) UpdService.SaveMapping(key, result.Document.UpdId);
        else RegistryService.SaveMapping(key, result.Document.Id);
        row.ApplyRegistry(result);
        if (selected == row) Select(row); status.Text = "Zatwierdzono karencję dla wybranej pozycji.";
    }
    async Task Preview(string mode)
    {
        var snapshot = Capture();
        if (snapshot.Rows.Where(r => string.Equals(r.GetValueOrDefault("Drukuj"), "True", StringComparison.OrdinalIgnoreCase)).Any(CardSchema.NeedsReview) &&
            !await DisplayAlertAsync("Weryfikacja karencji", "Nie wszystkie karencje i dopasowania leków zostały zatwierdzone. Sprawdź uzupełnione dane i ewentualne braki. Czy potwierdzasz ich sprawdzenie przed podglądem?", "Potwierdzam", "Wróć")) return;
        string html = CardHtmlRenderer.Render(snapshot);
        await Navigation.PushModalAsync(new PrintPreviewPage(html, mode));
        status.Text = "Otwarto podgląd formularza A4 poziomo.";
    }
}
