namespace KartyLeczenia.Views;

public sealed class DocumentSearchPage : ContentPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        CrashLog.Write("DocumentSearchPage.Open");
    }

    readonly ConnectionOptions options;
    DocumentSearchKinds kinds;
    readonly DocumentSearchKinds initialKinds;
    readonly record struct SearchKey(DocumentSearchKinds Kinds, string Text, DateTime? From, DateTime? Until);
    sealed record CachedList(long SavedAt, List<DocumentSuggestion> Rows);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConnectionOptions,
        Dictionary<SearchKey, CachedList>> Cache = new();
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
        options = connection; initialKinds = kinds = selectedKinds; Title = "Wybierz dokument";
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
        var filter = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.End, Children = { Ui.Field("Numer lub klient", query), Ui.Field("Rok / okres", year), Ui.PrimaryAction("Szukaj", () => Search(force: true)) } };
        var tabs = new HorizontalStackLayout { Spacing = 4, Children = {
            Ui.Action("FS (Alt+1)", () => SelectKind(DocumentSearchKinds.Invoices)),
            Ui.Action("Paragony (Alt+2)", () => SelectKind(DocumentSearchKinds.Receipts)),
            Ui.Action("WZ", () => SelectKind(DocumentSearchKinds.WarehouseReleases)),
            Ui.Action("Wybrane typy", () => SelectKind(initialKinds))
        } };
        var top = new VerticalStackLayout { Spacing = 10, Children = { Ui.Heading("Wyszukaj i wybierz dokument"), tabs, filter, dates, status } };
        results.EmptyView = new Label { Text = "Brak wyników. Zmień nazwę, numer albo rok.", Margin = 16 };
        var columns = new TableColumns(100, 165, GridLength.Star, 145, new GridLength(0.65, GridUnitType.Star));
        results.ItemTemplate = new DataTemplate(() => CompactTable.Row(columns, false,
            CompactTable.Cell(nameof(DocumentSuggestion.Date), format: "{0:dd.MM.yyyy}"),
            CompactTable.Cell(nameof(DocumentSuggestion.Number)),
            CompactTable.Cell(nameof(DocumentSuggestion.Payer)),
            CompactTable.Cell(nameof(DocumentSuggestion.AmountText), right: true),
            CompactTable.Cell(nameof(DocumentSuggestion.RelatedText))));
        results.SelectionChanged += async (_, e) =>
        {
            if (closing || e.CurrentSelection.FirstOrDefault() is not DocumentSuggestion chosen) return;
            await CloseSearch(chosen);
        };
        var root = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = GridLength.Auto } } };
        root.Add(Ui.Card(top), 0, 0); var table = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } } };
        table.Add(CompactTable.Header(columns, "Data", "Numer", "Kontrahent", "Wartość", "Powiązane dokumenty"), 0, 0);
        table.Add(results, 0, 1);
        columns.AttachTableMenu(table);
        root.Add(Ui.Card(table), 0, 1);
        root.Add(Ui.Action("Anuluj", () => CloseSearch(null)), 0, 2);
        Content = Ui.WithClose(this, root, () => CloseSearch(null));
        query.TextChanged += (_, _) => { work?.Cancel(); status.Text = "Naciśnij Enter, aby wyszukać. Możesz nadal wybrać dokument z listy."; };
        query.Completed += async (_, _) => await Search();
        year.SelectedIndexChanged += async (_, _) => { dates.IsVisible = year.SelectedItem as string == "Zakres dat"; if (initialized) await Search(); };
#if WINDOWS
        HandlerChanged += (_, _) =>
        {
            if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement view) return;
            void AddShortcut(Windows.System.VirtualKey key, DocumentSearchKinds kind)
            {
                var shortcut = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
                {
                    Key = key, Modifiers = Windows.System.VirtualKeyModifiers.Menu
                };
                shortcut.Invoked += async (_, args) =>
                {
                    args.Handled = true;
                    await SelectKind(kind);
                };
                view.KeyboardAccelerators.Add(shortcut);
            }
            AddShortcut(Windows.System.VirtualKey.Number1, DocumentSearchKinds.Invoices);
            AddShortcut(Windows.System.VirtualKey.Number2, DocumentSearchKinds.Receipts);
        };
#endif
        Loaded += async (_, _) => { if (initialized) return; initialized = true; await Search(); };
    }
    Task SelectKind(DocumentSearchKinds kind)
    {
        kinds = kind;
        return Search();
    }

    async Task Search(bool force = false)
    {
        work?.Cancel();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)); work = cts;
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
        var key = new SearchKey(kinds, (query.Text ?? "").Trim(), start, end);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        CrashLog.Write($"DocumentSearch.Start kinds={key.Kinds} empty={key.Text.Length == 0} force={force}");
        var cache = Cache.GetOrCreateValue(options);
        foreach (var expired in cache.Where(item => Environment.TickCount64 - item.Value.SavedAt >= 300_000).Select(item => item.Key).ToArray())
            cache.Remove(expired);
        if (!force && cache.TryGetValue(key, out var saved))
        {
            ApplyResults(saved.Rows);
            CrashLog.Write($"DocumentSearch.CacheHit rows={saved.Rows.Count} elapsedMs={timer.ElapsedMilliseconds}");
            work = null;
            return;
        }
        CrashLog.Write("DocumentSearch.CacheMiss");
        status.Text = results.ItemsSource is null ? "Wczytywanie dokumentów…" :
            "Odświeżanie… Nadal możesz przewijać i wybierać dokumenty z poprzedniej listy.";
        try
        {
            var found = await service.SearchAsync(options, key.Text, cts.Token, key.Kinds, start, end, 501, allowEmpty: true);
            if (cts.IsCancellationRequested || closing) return;
            if (cache.Count >= 32) cache.Remove(cache.MinBy(item => item.Value.SavedAt).Key);
            cache[key] = new(Environment.TickCount64, found);
            var readMs = timer.ElapsedMilliseconds;
            ApplyResults(found);
            CrashLog.Write($"DocumentSearch.Success rows={found.Count} readMs={readMs} listMs={timer.ElapsedMilliseconds - readMs}");
        }
        catch (OperationCanceledException) { CrashLog.Write($"DocumentSearch.Cancelled elapsedMs={timer.ElapsedMilliseconds}"); if (!closing && ReferenceEquals(work, cts)) status.Text = "Wyszukiwanie anulowane lub przekroczono czas oczekiwania."; }
        catch (Exception ex) { CrashLog.Write($"DocumentSearch.Query.Failed type={ex.GetType().Name} elapsedMs={timer.ElapsedMilliseconds}"); if (!closing && ReferenceEquals(work, cts)) status.Text = SubiektConnectionService.FriendlyError(ex); }
        finally { if (ReferenceEquals(work, cts)) work = null; }
    }
    void ApplyResults(List<DocumentSuggestion> found)
    {
        results.ItemsSource = found.Take(500).ToList();
        status.Text = found.Count > 500 ? "Pokazano 500 dokumentów. Zawęź okres lub wpisz numer albo klienta." :
            found.Count == 0 ? "Brak dokumentów dla wybranych filtrów." : $"Dokumenty: {found.Count}. Kliknij dokument, aby go pobrać.";
    }

    async Task CloseSearch(DocumentSuggestion? chosen)
    {
        if (closing) return;
        closing = true;
        try
        {
            work?.Cancel();
            CrashLog.Write(chosen == null ? "DocumentSearch.Cancel" : "DocumentSearch.Select");
            // Let the native selection callback finish and fully dismiss the modal before updating the parent CollectionView.
            await Task.Yield();
            await Navigation.PopModalAsync();
            await Task.Yield();
            completion.TrySetResult(chosen);
        }
        catch (Exception ex)
        {
            CrashLog.Write("DocumentSearch.Close.Failed", ex);
            closing = false; status.Text = "Nie zamknięto okna wyboru: " + ex.Message;
        }
    }
    protected override bool OnBackButtonPressed() { _ = CloseSearch(null); return true; }
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!closing && !Navigation.ModalStack.Contains(this)) { closing = true; work?.Cancel(); completion.TrySetResult(null); }
    }
}
