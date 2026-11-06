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
    readonly Entry number = new() { Placeholder = "Numer lub nazwisko — Enter otwiera wybór dokumentu", HeightRequest = 44, HorizontalOptions = LayoutOptions.Fill, MaxLength = 100 };
    readonly Picker documentSuggestions = new() { Title = "", HeightRequest = 44, HorizontalOptions = LayoutOptions.Fill };
    DocumentSuggestion? chosenDocument;
    ConnectionOptions? documentLookupConnection;
    bool choosingDocument, loadingDocument;
    readonly CheckBox searchInvoices = new() { IsChecked = true, WidthRequest = 24, HeightRequest = 24, MinimumWidthRequest = 24, MinimumHeightRequest = 24 }, searchWarehouse = new() { IsChecked = true, WidthRequest = 24, HeightRequest = 24, MinimumWidthRequest = 24, MinimumHeightRequest = 24 }, searchReceipts = new() { IsChecked = true, WidthRequest = 24, HeightRequest = 24, MinimumWidthRequest = 24, MinimumHeightRequest = 24 };
    DocumentSearchKinds SearchKinds => (searchInvoices.IsChecked ? DocumentSearchKinds.Invoices : DocumentSearchKinds.None)
        | (searchWarehouse.IsChecked ? DocumentSearchKinds.WarehouseReleases : DocumentSearchKinds.None)
        | (searchReceipts.IsChecked ? DocumentSearchKinds.Receipts : DocumentSearchKinds.None);
    readonly Picker withdrawal = new() { Title = "", HeightRequest = 44, HorizontalOptions = LayoutOptions.Fill };
    readonly Picker owner = new() { Title = "", HeightRequest = 44, HorizontalOptions = LayoutOptions.Fill };
    readonly Label searchHint = Ui.Note("Wpisz numer lub nazwisko i naciśnij Enter. W oknie wyboru możesz ustawić rok lub zakres dat.");
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
        withdrawal.Items.Add("Rejestr RPL (domyślnie)"); withdrawal.Items.Add("Opis leku");
        for (int i = 1; i <= 8; i++) withdrawal.Items.Add("Pole własne " + i);
        withdrawal.SelectedIndex = 0; owner.Items.Add("Płatnik"); owner.Items.Add("Odbiorca"); owner.SelectedIndex = 0;
        owner.SelectedIndexChanged += (_, _) => { if (!changing && scope != "") header["Właściciel i adres"].Text = owner.SelectedIndex == 0 ? payer : recipient; };
        var searchTypes = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        foreach (var item in new[] { (Toggle: searchInvoices, Title: "FS"), (Toggle: searchWarehouse, Title: "WZ"), (Toggle: searchReceipts, Title: "PA") })
        {
            item.Toggle.WidthRequest = item.Toggle.MinimumWidthRequest = 28;
            item.Toggle.HeightRequest = item.Toggle.MinimumHeightRequest = 28;
            item.Toggle.Margin = 0; item.Toggle.HorizontalOptions = LayoutOptions.End; item.Toggle.VerticalOptions = LayoutOptions.Center;
            item.Toggle.Color = Color.FromArgb("#14B8A6");
            SemanticProperties.SetDescription(item.Toggle, "Uwzględnij dokumenty " + item.Title);
            var label = new Label { Text = item.Title, FontSize = 12, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center, VerticalTextAlignment = TextAlignment.Center };
            var pair = new Grid { ColumnSpacing = 2, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = new GridLength(28) } } };
            pair.Add(label, 0, 0); pair.Add(item.Toggle, 1, 0);
            var frame = new Border { Content = pair, WidthRequest = 58, HeightRequest = 30, Padding = new Thickness(5, 0, 1, 0), StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(5) } };
            void UpdateFilterAppearance()
            {
                frame.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb(item.Toggle.IsChecked ? "#CCFBF1" : "#F8FAFC"), Color.FromArgb(item.Toggle.IsChecked ? "#173C40" : "#1E293B"));
                frame.Stroke = new SolidColorBrush(Color.FromArgb(item.Toggle.IsChecked ? "#14B8A6" : "#64748B"));
                label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb(item.Toggle.IsChecked ? "#115E59" : "#475569"), Color.FromArgb(item.Toggle.IsChecked ? "#CCFBF1" : "#CBD5E1"));
            }
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => item.Toggle.IsChecked = !item.Toggle.IsChecked;
            label.GestureRecognizers.Add(tap);
            item.Toggle.CheckedChanged += async (_, _) => { UpdateFilterAppearance(); await RefreshDocumentSuggestions(); };
            UpdateFilterAppearance(); searchTypes.Add(frame);
        }
        var actions = new Grid { ColumnSpacing = 16, RowSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) } },
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } } };
        var searchFields = new[] { Ui.Field("Numer dokumentu lub nazwisko", number), Ui.Field("Źródło karencji", withdrawal), Ui.Field("Dane właściciela", owner) };
        for (int i = 0; i < searchFields.Length; i++) { ((Label)searchFields[i].Children[0]).HeightRequest = 32; ((Label)searchFields[i].Children[0]).VerticalTextAlignment = TextAlignment.Center; actions.Add(searchFields[i], i, 0); }
        var searchLabel = (Label)searchFields[0].Children[0];
        searchFields[0].Children.RemoveAt(0);
        var searchHeader = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };
        searchHeader.Add(searchLabel, 0, 0); searchHeader.Add(searchTypes, 1, 0);
        searchFields[0].Children.Insert(0, searchHeader);
        actions.SizeChanged += (_, _) =>
        {
            bool narrow = actions.Width > 0 && actions.Width < 1000;
            for (int i = 0; i < searchFields.Length; i++)
            {
                Grid.SetColumn(searchFields[i], narrow ? i == 0 ? 0 : i - 1 : i);
                Grid.SetRow(searchFields[i], narrow && i > 0 ? 1 : 0);
                Grid.SetColumnSpan(searchFields[i], narrow && i == 0 ? 3 : narrow && i == 2 ? 2 : 1);
            }
        };
        var documentActions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        documentActions.Add(Ui.PrimaryAction("Wyszukaj dokument", OpenDocumentSearch));
        documentActions.Add(Ui.Action("Otwórz kartę", OpenCard)); documentActions.Add(Ui.Action("Zapisz kartę", SaveCard));
        documentActions.Add(Ui.Action("Podgląd", () => Preview("preview")));
        documentActions.Add(Ui.Action("Drukuj", () => Preview("print")));
        documentActions.Add(Ui.PrimaryAction("Eksport do PDF", () => Preview("pdf")));
        number.Completed += async (_, _) =>
        {
            try { await OpenDocumentSearch(); }
            catch (Exception ex) { CrashLog.Write("DocumentSearch.Enter.Failed", ex); await DisplayAlertAsync("Nie pobrano dokumentu", SubiektConnectionService.FriendlyError(ex), "OK"); }
        };
        var documentPanel = new VerticalStackLayout { Spacing = 8, Children = { Ui.Heading("1. Dokument źródłowy"), actions, searchHint, documentActions } };
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
            var remove = new Button { Text = "×", FontSize = 20, WidthRequest = 30, HeightRequest = 30,
                MinimumWidthRequest = 30, MinimumHeightRequest = 30, Padding = 0, VerticalOptions = LayoutOptions.Center };
            SemanticProperties.SetDescription(remove, "Usuń ten lek z karty");
            ToolTipProperties.SetText(remove, "Usuń pozycję");
            remove.Clicked += async (_, _) => { if (remove.BindingContext is TreatmentRow item) await RemoveRow(item); };
            var row = new Grid { Padding = 8, ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };
            row.Add(check, 0, 0); row.Add(text, 1, 0); row.Add(remove, 2, 0); return row;
        });
        list.SelectionChanged += (_, e) => Select(e.CurrentSelection.FirstOrDefault() as TreatmentRow);
        printed.CheckedChanged += (_, e) => { if (!changing && selected != null) selected.Printed = e.Value; };
        detail.Add(Ui.Heading("4. Dane wybranego leku"));
        detail.Add(Ui.Note("Wybierz lek z listy po lewej. Każde pole możesz poprawić przed wydrukiem."));
        detail.Add(new HorizontalStackLayout { Children = { printed, new Label { Text = "Uwzględnij na wydruku", VerticalOptions = LayoutOptions.Center } } });
        detail.Add(Ui.Action("Karencja z RPL…", ApplyRegistry));
        detail.Add(Ui.Action("UPD — źródło zapasowe…", () => ApplyRegistrySource(true)));
        foreach (string field in CardSchema.RowFields.Concat(CardSchema.ContextFields))
        {
            var editor = new Editor { HeightRequest = field is "Karencja" or "Dawkowanie i okres stosowania" or "Zalecenia / uwagi" ? 82 : 48 };
            string key = field;
            editor.TextChanged += (_, e) => { if (!changing && selected != null) { selected.Set(key, e.NewTextValue ?? "");
                if (key is "Gatunek" or "Droga podania")
                {
                    changing = true;
                    foreach (string updated in new[] { "Karencja", "Dawkowanie i okres stosowania", "Droga podania", "Opis zwierząt / numer siedziby stada" }) editors[updated].Text = selected.Get(updated);
                    changing = false;
                }
                provenance.Text = selected.RegistryStatus + "\n" + selected.Get("Źródło karencji"); } };
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
            Ui.Action("Usuń wybraną pozycję", async () => { if (selected is TreatmentRow item) await RemoveRow(item); })
        } };
        var root = new Grid { Padding = 18, RowSpacing = 8, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(0) }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        var cardDetails = Ui.Card(new ScrollView { Content = new VerticalStackLayout { Spacing = 8, Children = { Ui.Heading("2. Dane karty"), headerPanel } } });
        cardDetails.IsVisible = false;
        documentActions.Add(Ui.Action("Pokaż / ukryj dane karty", () => { cardDetails.IsVisible = !cardDetails.IsVisible; root.RowDefinitions[1].Height = new GridLength(cardDetails.IsVisible ? 225 : 0); return Task.CompletedTask; }));
        root.Add(Ui.Card(documentPanel), 0, 0); root.Add(cardDetails, 0, 1); root.Add(rowActions, 0, 2); root.Add(split, 0, 3); root.Add(status, 0, 4);
        Content = root;
        number.TextChanged += async (_, _) => await RefreshDocumentSuggestions();
        documentSuggestions.SelectedIndexChanged += (_, _) =>
        {
            if (documentSuggestions.SelectedItem is not DocumentSuggestion suggestion) return;
            searchHint.Text = "Wybrano: " + suggestion + " | " + suggestion.AmountText;
            choosingDocument = true; number.Text = suggestion.Number; choosingDocument = false; chosenDocument = suggestion;
        };
    }
    async Task RemoveRow(TreatmentRow item)
    {
        if (!rows.Contains(item)) return;
        int generation = documentGeneration;
        if (!await DisplayAlertAsync("Usuń pozycję", $"Usunąć „{item.Product}” z tej karty?", "Usuń", "Anuluj")) return;
        if (generation != documentGeneration || !rows.Contains(item)) return;
        bool wasSelected = ReferenceEquals(selected, item);
        rows.Remove(item);
        if (wasSelected) { list.SelectedItem = null; Select(null); }
    }
    Task RefreshDocumentSuggestions()
    {
        if (choosingDocument) return Task.CompletedTask;
        chosenDocument = null; documentSuggestions.ItemsSource = null;
        searchHint.Text = "Naciśnij Enter lub „Wyszukaj dokument”, aby wybrać dokument i okres.";
        return Task.CompletedTask;
    }
    bool searchOpen;
    async Task OpenDocumentSearch()
    {
        if (searchOpen || loadingDocument) return;
        var options = state.ValidatedConnection ?? throw new InvalidOperationException("Najpierw przetestuj połączenie w Ustawieniach.");
        if (SearchKinds == DocumentSearchKinds.None) throw new InvalidOperationException("Zaznacz przynajmniej jeden rodzaj dokumentu.");
        searchOpen = true;
        try
        {
            var page = new DocumentSearchPage(options, number.Text ?? "", SearchKinds);
            await Navigation.PushModalAsync(page);
            var picked = await page.Completion;
            if (picked == null || state.ValidatedConnection != options) return;
            choosingDocument = true; number.Text = picked.Number; choosingDocument = false;
            chosenDocument = picked; documentLookupConnection = options;
            searchHint.Text = "Wybrano: " + picked + " | " + picked.AmountText;
            await LoadDocument();
        }
        finally { searchOpen = false; }
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
        if (loadingDocument) return;
        loadingDocument = true;
        try { CrashLog.Write("Document.Load.Start"); await LoadDocumentCore(); CrashLog.Write("Document.Load.Finished"); }
        catch (Exception ex) { CrashLog.Write("Document.Load.Failed", ex); throw; }
        finally { loadingDocument = false; }
    }
    async Task LoadDocumentCore()
    {
        var requestedKinds = SearchKinds;
        var options = state.ValidatedConnection ?? throw new InvalidOperationException("Najpierw przetestuj połączenie z Subiektem.");
        if (SearchKinds == DocumentSearchKinds.None) throw new InvalidOperationException("Zaznacz co najmniej jeden rodzaj dokumentu: faktury, WZ lub paragony.");
        if (documentLookupConnection != options) chosenDocument = null;
        if (chosenDocument == null && !string.IsNullOrWhiteSpace(number.Text) && number.Text.Trim().Length >= 4)
        {
            using var searchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string input = number.Text.Trim(); var kinds = SearchKinds;
            var found = await documents.SearchAsync(options, input, searchTimeout.Token, kinds);
            if (state.ValidatedConnection != options || number.Text?.Trim() != input || SearchKinds != kinds) return;
            documentLookupConnection = options;
            var exact = found.Where(d => d.Number.Equals(input, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) chosenDocument = exact[0];
            else if (found.Count == 1) chosenDocument = found[0];
            else { documentSuggestions.ItemsSource = found; status.Text = found.Count == 0 ? "Nie znaleziono dokumentu." : "Wybierz dokument z podpowiedzi; możesz dopisać numer lub nazwisko."; return; }
            choosingDocument = true; number.Text = chosenDocument.Number; choosingDocument = false;
        }
        if (rows.Count > 0 && !await DisplayAlertAsync("Nowy dokument", "Pobranie zastąpi aktualnie edytowane pozycje. Kontynuować?", "Pobierz", "Anuluj")) return;
        automaticWork?.Cancel();
        int generation = ++documentGeneration;
        status.Text = "Pobieranie leków z dokumentu…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        string requestedNumber = number.Text ?? "";
        CrashLog.Write("Document.SQL.Read");
        var data = await documents.ReadAsync(options, requestedNumber, withdrawal.SelectedIndex, timeout.Token, chosenDocument?.Id);
        if (generation != documentGeneration || SearchKinds != requestedKinds || number.Text != requestedNumber) return;
        if (state.ValidatedConnection != options) throw new InvalidOperationException("Zmieniono połączenie w trakcie pobierania. Ponów operację.");
        CrashLog.Write("Document.UI.Apply rows=" + data.Rows.Count);
        Select(null); list.SelectedItem = null; rows.Clear(); foreach (var row in data.Rows) rows.Add(row);
        scope = data.Scope; payer = data.Payer; recipient = data.Recipient;
        header["Właściciel i adres"].Text = owner.SelectedIndex == 0 ? payer : recipient;
        header["Dokument źródłowy"].Text = data.Number + (chosenDocument?.HasRelatedText == true && chosenDocument.Related.Count > 0 ? "\n" + chosenDocument.RelatedText : "");
        if (rows.Count > 0) list.SelectedItem = rows[0];
        status.Text = $"Pobrano {rows.Count} pozycji dostaw z grupy {options.DrugGroup}.";
        CrashLog.Write("Document.UI.Applied");
        if (withdrawal.SelectedIndex == 0) { CrashLog.Write("Document.RPL.Start"); await FillWithdrawals(generation); CrashLog.Write("Document.RPL.Finished"); }
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
            if (targets.Any(r => RegistryService.ReadMapping(AutomaticWithdrawalService.MappingKey(sourceScope, r)) == null && RegistryCatalog.ExactCandidates(r.Product).Count != 1))
            {
                status.Text = "Aktualizowanie katalogu RPL…";
                try { await RegistryCatalog.UpdateAsync(token: timeout.Token); }
                catch (Exception) when (!timeout.IsCancellationRequested) { }
            }
            if (generation != documentGeneration || timeout.IsCancellationRequested) return;
            status.Text = "Automatyczne pobieranie karencji z RPL…";
            using var gate = new SemaphoreSlim(3);
            var tasks = targets.GroupBy(r => AutomaticWithdrawalService.MappingKey(sourceScope, r)).Select(async group =>
            {
                string originalProduct = group.First().Product;
                AutomaticWithdrawal? resolved = null;
                string? error = null;
                await gate.WaitAsync(timeout.Token);
                try { resolved = await AutomaticWithdrawalService.ResolveAsync(sourceScope, group.First(), timeout.Token); }
                catch (Exception ex) { error = ex is OperationCanceledException ? "Przekroczono czas pobierania RPL." : ex.Message; }
                finally { gate.Release(); }
                if (generation != documentGeneration || timeout.IsCancellationRequested) return;
                foreach (var row in group)
                {
                    if (!rows.Contains(row) || row.Product != originalProduct || row.Get("Karencja") != "") continue;
                    if (resolved != null) { AutomaticWithdrawalService.Apply(row, resolved); if (string.IsNullOrWhiteSpace(row.Get("Karencja"))) missing++; else filled++; }
                    else { row.Set("Status karencji", error == null ? "Brak jednoznacznego dopasowania RPL — wybierz lek" : "RPL: " + error, false); missing++; }
                }
            }).ToArray();
            await Task.WhenAll(tasks);
            if (generation == documentGeneration)
            {
                Select(selected);
                status.Text = $"Karencję uzupełniono automatycznie w {filled} pozycjach. Do dopasowania lub ponowienia: {missing}. Sprawdź źródło, produkt i leczenie przed wydrukiem.";
            }
        }
        catch (OperationCanceledException) { if (generation == documentGeneration) status.Text = "Przekroczono czas RPL. Już pobrane dane pozostają w karcie; brakujące pozycje ponów przez przycisk karencji."; }
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
    Task ApplyRegistry() => ApplyRegistrySource(false);
    async Task ApplyRegistrySource(bool useUpd)
    {
        var row = selected ?? throw new InvalidOperationException("Wybierz lek.");
        string key = AutomaticWithdrawalService.MappingKey(scope, row);
        RegistrySelection? result;
        if (useUpd)
        {
            var page = new UpdRegistryPage(row.Product, UpdService.ReadMapping(key), RegistryService.ReadMapping(key));
            await Navigation.PushModalAsync(page); result = await page.Completion;
        }
        else
        {
            var page = new RegistryPage(row.Product, RegistryService.ReadMapping(key), row.Get("Domyślny gatunek") == "True" ? "" : row.Get("Gatunek"), row.Get("Droga podania"));
            await Navigation.PushModalAsync(page); result = await page.Completion;
        }
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
