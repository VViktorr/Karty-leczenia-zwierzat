namespace KartyLeczenia.Views;

public sealed class MedicineStockPage : ContentPage
{
    sealed record PriceRow(string Name, string Batch, string Price);
    readonly TableColumns priceWidths = new(GridLength.Star, 180, 140);
    readonly AppState state;
    readonly bool prices;
    readonly Picker priceBasis = new() { ItemsSource = new[] { "Brutto", "Netto" }, SelectedIndex = 0, WidthRequest = 105 };
    bool NetPrice => priceBasis.SelectedIndex == 1;
    readonly Picker filter = new() { WidthRequest = 235 };
    readonly Picker withdrawalSource = new() { Title = "Wybierz źródło karencji" };
    readonly DatePicker receivedFrom = new() { Format = "dd.MM.yyyy", Date = DateTime.Today.AddYears(-1) };
    readonly DatePicker receivedTo = new() { Format = "dd.MM.yyyy", Date = DateTime.Today };
    readonly Switch useReceivedRange = new();
    readonly string[] expiryColumns = ["Lek", "Seria", "Data przyjęcia", "Termin ważności", "Pozostało"];
    readonly List<Button> sortHeaders = [];
    readonly HashSet<string> expandedProducts = new(StringComparer.CurrentCultureIgnoreCase);
    readonly TableColumns expiryWidths = new(GridLength.Star, 125, 130, 175, 110);
    int sortColumn = 3;
    bool descending;
    readonly Entry search = new();
    readonly CollectionView list = new();
    readonly Label status = Ui.Note("");
    List<MedicinePrice> priceRows = [];
    List<MedicineExpiry> expiryRows = [];
    bool loading, loaded, openingPrint;
    ConnectionOptions? priceSelectionConnection;

    public MedicineStockPage(AppState state, bool prices)
    {
        this.state = state;
        this.prices = prices;
        Title = prices ? "Cennik leków" : "Kontrola terminów ważności";
        search.Placeholder = prices ? "Szukaj leku" : "Szukaj leku lub serii";
        filter.Title = prices ? "Rodzaj ceny" : "Termin ważności";
        if (!prices)
        {
            filter.ItemsSource = new[] { "Wszystkie dostawy", "Po terminie", "Do 30 dni", "Do 90 dni", "Bez daty" };
            filter.SelectedIndex = 0;
        }
        list.SelectionMode = SelectionMode.Single;
        list.ItemTemplate = prices ? new DataTemplate(() => CompactTable.Row(priceWidths, false,
            CompactTable.Cell(nameof(PriceRow.Name)), CompactTable.Cell(nameof(PriceRow.Batch)),
            CompactTable.Cell(nameof(PriceRow.Price), right: true))) : new DataTemplate(() => CreateExpiryRow());
        var actions = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        var refresh = Ui.Action("Odśwież", Refresh);
        if (prices)
        {
            filter.WidthRequest = 165;
            filter.Title = null;
            withdrawalSource.WidthRequest = 235;
            withdrawalSource.Title = null;
            foreach (var control in new View[] { search, filter, priceBasis, withdrawalSource, refresh })
            {
                control.HeightRequest = 32;
                control.VerticalOptions = LayoutOptions.End;
            }
            refresh.Padding = new Thickness(14, 0);
            actions.Add(Ui.Field("Wyszukiwanie", search), 0, 0);
            actions.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.End,
                Children = { Ui.Field("Rodzaj ceny", filter), Ui.Field("Netto / brutto", priceBasis) }
            }, 1, 0);
        }
        else
        {
            actions.Add(search, 0, 0);
            actions.Add(filter, 1, 0);
        }
        actions.Add(refresh, 2, 0);
        var header = new VerticalStackLayout { Spacing = 4, Children = { actions } };
        if (prices)
        {
            withdrawalSource.ItemsSource = new[] { "Katalog SQLite (ChPL)", "Opis leku" }
                .Concat(Enumerable.Range(1, 8).Select(index => $"Pole własne {index}")).ToList();
            withdrawalSource.SelectedIndex = 0;
            var print = Ui.Action("Wydrukuj cennik", PreviewPrices);
            print.HeightRequest = 32;
            print.Padding = new Thickness(14, 0);
            print.VerticalOptions = LayoutOptions.End;
            header.Children.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { Ui.Field("Źródło karencji na wydruku", withdrawalSource), print }
            });
        }
        if (!prices)
        {
            receivedFrom.IsEnabled = receivedTo.IsEnabled = false;
            header.Children.Add(new HorizontalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    Ui.Field("Filtruj datę przyjęcia", useReceivedRange),
                    Ui.Field("Od", receivedFrom),
                    Ui.Field("Do", receivedTo)
                }
            });
            useReceivedRange.Toggled += (_, e) =>
            {
                receivedFrom.IsEnabled = receivedTo.IsEnabled = e.Value;
                ShowRows();
            };
            receivedFrom.DateSelected += (_, _) => ShowRows();
            receivedTo.DateSelected += (_, _) => ShowRows();
        }
        header.Children.Add(status);
        var root = new Grid
        {
            Padding = prices ? 12 : 4, RowSpacing = 4,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            }
        };
        root.Add(Ui.Card(header), 0, 0);
        if (prices)
        {
            var priceHeading = CompactTable.Header(priceWidths, "Lek", "Najnowsza seria", "Cena brutto");
            var priceLabel = priceHeading.Children.OfType<Label>().ElementAt(2);
            priceLabel.HorizontalTextAlignment = TextAlignment.End;
            priceBasis.SelectedIndexChanged += (_, _) => priceLabel.Text = NetPrice ? "Cena netto" : "Cena brutto";
            var table = new Grid
            {
                RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } }
            };
            table.Add(priceHeading, 0, 0);
            table.Add(list, 0, 1);
            root.Add(Ui.Card(table), 0, 1);
        }
        else root.Add(new Border { Content = CreateExpiryTable(), Stroke = new SolidColorBrush(Color.FromArgb("#999999")), StrokeThickness = 1, Padding = 0 }, 0, 1);
        Content = root;
        search.TextChanged += (_, _) => ShowRows();
        priceBasis.SelectedIndexChanged += async (_, _) => { if (prices && !loading) await Refresh(); };
        filter.SelectedIndexChanged += async (_, _) =>
        {
            if (loading) return;
            if (prices) await Refresh();
            else ShowRows();
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Refresh();
    }

    async Task Refresh()
    {
        if (loading) return;
        loaded = false;
        list.ItemsSource = null;
        if (state.ValidatedConnection is not { } options)
        {
            status.Text = "Połącz się z bazą Subiekta w ustawieniach.";
            return;
        }

        loading = true;
        filter.IsEnabled = priceBasis.IsEnabled = false;
        status.Text = "Pobieranie danych z Subiekta…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (prices)
            {
                int? selected = priceSelectionConnection == options ? (filter.SelectedItem as MedicinePriceLevel)?.Number : null;
                var levels = await MedicineStockService.PriceLevels(options, timeout.Token);
                filter.ItemsSource = levels;
                if (levels.Length == 0)
                {
                    status.Text = "Nie znaleziono nazwanych poziomów cen w tej bazie.";
                    return;
                }
                var retail = levels.Where(item => item.IsRetail).Take(2).ToArray();
                var level = levels.FirstOrDefault(item => item.Number == selected)
                    ?? (retail.Length == 1 ? retail[0] : null);
                priceSelectionConnection = options;
                filter.SelectedItem = level;
                if (level is null)
                {
                    status.Text = "Wybierz rodzaj ceny. Nie rozpoznano jednoznacznie ceny detalicznej w Subiekcie.";
                    return;
                }
                priceRows = await MedicineStockService.Prices(options, level.Number, timeout.Token, net: NetPrice);
            }
            else expiryRows = await MedicineStockService.Expiries(options, timeout.Token);

            if (!ReferenceEquals(options, state.ValidatedConnection))
            {
                status.Text = "Zmieniono połączenie z bazą. Kliknij „Odśwież”.";
                return;
            }
            loaded = true;
            ShowRows();
        }
        catch (OperationCanceledException) { status.Text = "Przekroczono czas odczytu. Spróbuj odświeżyć listę."; }
        catch (Exception ex)
        {
            CrashLog.Write(prices ? "MedicinePrices.Read" : "MedicineExpiries.Read", ex);
            status.Text = "Nie udało się odczytać danych. " + SubiektConnectionService.FriendlyError(ex);
        }
        finally { filter.IsEnabled = priceBasis.IsEnabled = true; loading = false; }
    }

    async Task PreviewPrices()
    {
        if (openingPrint) return;
        if (!loaded || loading || state.ValidatedConnection is not { } options || filter.SelectedItem is not MedicinePriceLevel level)
            throw new InvalidOperationException("Najpierw wczytaj cennik.");
        if (withdrawalSource.SelectedIndex < 0)
            throw new InvalidOperationException("Wybierz źródło karencji.");
        bool net = NetPrice;
        string basis = net ? "netto" : "brutto";
        int source = withdrawalSource.SelectedIndex;
        string sourceName = source == 0 ? "Katalog SQLite (ChPL)" : $"Subiekt ({withdrawalSource.SelectedItem})";
        string query = (search.Text ?? "").Trim();

        openingPrint = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var data = await MedicineStockService.Prices(options, level.Number, timeout.Token, source, net);
            var catalogue = source == 0 ? await MedicineCatalogue.GetAsync() : null;
            if (state.ValidatedConnection != options || !loaded || loading || !Equals(filter.SelectedItem, level) || NetPrice != net || withdrawalSource.SelectedIndex != source || (search.Text ?? "").Trim() != query)
                throw new InvalidOperationException("Cennik zmienił się w trakcie przygotowania wydruku. Ponów operację.");

            var rows = data.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (rows.Count == 0) throw new InvalidOperationException("Brak pozycji do wydruku.");
            string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
            string body = string.Concat(rows.Select(row =>
            {
                string gross = row.Gross?.ToString("N2") ?? "—";
                string withdrawal = catalogue is null ? row.Withdrawal : CatalogueWithdrawal(catalogue, row);
                if (string.IsNullOrWhiteSpace(withdrawal)) withdrawal = "Brak danych";
                else withdrawal = string.Join(", ", withdrawal.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                return $"<tr><td>{Encode(row.Name)}</td><td>{Encode(BatchText(row))}</td><td class='price'>{Encode(gross)}</td><td>{Encode(withdrawal)}</td></tr>";
            }));
            string html = $$"""
                <!doctype html><html lang="pl"><head><meta charset="utf-8"><title>Cennik leków</title>
                <style>
                    @page { size: A4 landscape; margin: 12mm; }
                    body { font: 11pt Arial, sans-serif; color: #171717; }
                    h1 { font-size: 18pt; }
                    table { width: 100%; border-collapse: collapse; }
                    thead { display: table-header-group; }
                    tr { break-inside: avoid; }
                    th, td { border: 1px solid #999; padding: 6px; text-align: left; vertical-align: top; }
                    td { white-space: pre-wrap; overflow-wrap: anywhere; }
                    .price { text-align: right; white-space: nowrap; }
                </style></head><body><main class="sheet">
                <h1>Cennik leków</h1>
                <p>{{DateTime.Today:dd.MM.yyyy}} · {{Encode(level.ToString())}} · Źródło karencji: {{Encode(sourceName)}}</p>
                {{AdministrationRouteLegend.Html}}
                <table><thead><tr><th>Lek</th><th>Najnowsza seria</th><th>Cena {{basis}}</th><th>Karencja</th></tr></thead><tbody>{{body}}</tbody></table>
                </main></body></html>
                """;
            await Navigation.PushModalAsync(new PrintPreviewPage(html, "preview", "Podgląd cennika leków", "cennik-lekow"));
        }
        finally { openingPrint = false; }
    }

    static string CatalogueWithdrawal(ILookup<string, MedicineCatalogue.Entry> catalogue, MedicinePrice row)
    {
        var entry = MedicineCatalogue.Match(catalogue, row.Symbol, row.Name);
        if (entry is null || entry.Excluded) return "";
        return string.Join(" | ", entry.Variants
            .Select(v => (v.Species, v.Route, Text: FormatWithdrawal(v.Species, v.Meat, v.Milk)))
            .Where(item => item.Text.Length > 0)
            .Select(item => $"{item.Species} ({item.Route}): {item.Text}")
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    static string FormatWithdrawal(string species, string meat, string milk)
    {
        static bool Applies(string value)
        {
            var text = value.Trim();
            return text.Length > 0 && text is not ("—" or "-" or "–")
                && !text.StartsWith("Nie dotyczy", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("Brak", StringComparison.OrdinalIgnoreCase);
        }

        var parts = new List<string>();
        if (Applies(meat)) parts.Add("Tkanki: " + meat.Trim());
        bool companionAnimal = System.Text.RegularExpressions.Regex.IsMatch(species,
            @"\b(pies|psy|psów|kot|koty|kotów)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool milkWithdrawal = System.Text.RegularExpressions.Regex.IsMatch(milk,
            @"\b(?:dni|dzień|godzin\w*|h|tygodni\w*|miesi[eą]\w*|udoj\w*|niedopuszcz\w*)\b|nie stosować.*mlek|nie.*przeznaczon.*mlek",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!companionAnimal && Applies(milk) && milkWithdrawal && !milk.Contains("(jaja)", StringComparison.OrdinalIgnoreCase))
            parts.Add("Mleko: " + milk.Trim());
        return string.Join(", ", parts);
    }

    View CreateExpiryTable()
    {
        for (int i = 0; i < expiryColumns.Length; i++)
        {
            int column = i;
            var button = new Button { Padding = new Thickness(4, 0), MinimumHeightRequest = 22, HeightRequest = 22, BorderWidth = 0,
                CornerRadius = 0, FontSize = 12, BackgroundColor = Colors.Transparent };
            button.Clicked += (_, _) =>
            {
                descending = sortColumn == column && !descending;
                sortColumn = column;
                UpdateSortHeaders();
                ShowRows();
            };
            sortHeaders.Add(button);
        }
        UpdateSortHeaders();
        var heading = CompactTable.Row(expiryWidths, true, sortHeaders.ToArray());
        var table = new Grid
        {
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } }
        };
        table.Add(heading, 0, 0);
        table.Add(list, 0, 1);
        return table;
    }

    void UpdateSortHeaders()
    {
        for (int i = 0; i < sortHeaders.Count; i++)
            sortHeaders[i].Text = expiryColumns[i] + (i == sortColumn ? descending ? " ↓" : " ↑" : "");
    }

    View CreateExpiryRow()
    {
        var name = CompactTable.Cell(nameof(ExpiryRow.Name));
        var toggle = new Button { Padding = 0, MinimumHeightRequest = 18, HeightRequest = 18, WidthRequest = 18, BorderWidth = 0,
            CornerRadius = 0, FontSize = 12, HorizontalOptions = LayoutOptions.Start, BackgroundColor = Colors.Transparent };
        toggle.SetBinding(Button.TextProperty, nameof(ExpiryRow.ExpansionMark));
        toggle.SetBinding(IsVisibleProperty, nameof(ExpiryRow.IsProduct));
        toggle.SetBinding(IsEnabledProperty, nameof(ExpiryRow.CanExpand));
        toggle.Clicked += (_, _) =>
        {
            if (toggle.BindingContext is not ExpiryRow { CanExpand: true } row) return;
            if (!expandedProducts.Add(row.Product)) expandedProducts.Remove(row.Product);
            ShowRows();
            if (list.ItemsSource is List<ExpiryRow> rows)
            {
                var updated = rows.FirstOrDefault(item => item.IsProduct && item.Product == row.Product);
                if (updated != null) list.ScrollTo(updated, position: ScrollToPosition.MakeVisible, animate: false);
            }
        };
        SemanticProperties.SetDescription(toggle, "Rozwiń lub zwiń dostawy");
        var title = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = 20 }, new ColumnDefinition { Width = GridLength.Star } }
        };
        title.Add(toggle, 0, 0);
        title.Add(name, 1, 0);
        return CompactTable.Row(expiryWidths, false, title, CompactTable.Cell(nameof(ExpiryRow.Batch)),
            CompactTable.Cell(nameof(ExpiryRow.Received)), CompactTable.Cell(nameof(ExpiryRow.Expires)),
            CompactTable.Cell(nameof(ExpiryRow.Remaining), right: true));
    }

    sealed record ExpiryRow(string Product, string Name, string Batch, string Received, string Expires,
        string Remaining, bool IsProduct, bool CanExpand, string ExpansionMark = "")
    {

    }

    static string BatchText(MedicinePrice row) => string.IsNullOrWhiteSpace(row.LatestBatch) ? "Brak danych" : row.LatestBatch;

    void ShowRows()
    {
        if (!loaded) return;
        string query = (search.Text ?? "").Trim();
        bool Matches(string text) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        if (prices)
        {
            var priceList = priceRows.Where(row => Matches(row.Name)).Select(row =>
                new PriceRow(row.Name, BatchText(row), row.Gross?.ToString("N2") ?? "—")).ToList();
            list.ItemsSource = priceList;
            status.Text = priceList.Count == 0 ? "Brak pasujących pozycji." : $"Pozycje: {priceList.Count}";
            return;
        }

        DateTime today = DateTime.Today;
        var matching = expiryRows.Where(row => (Matches(row.Name) || Matches(row.Batch)) && MatchesExpiry(row.Expires?.Date, today));
        if (useReceivedRange.IsToggled)
        {
            if (receivedFrom.Date is not DateTime from || receivedTo.Date is not DateTime to || from.Date > to.Date)
            {
                list.ItemsSource = null;
                status.Text = "Wybierz poprawny zakres dat przyjęcia.";
                return;
            }
            matching = matching.Where(row => row.Received is DateTime date && date.Date >= from.Date && date.Date <= to.Date);
        }

        IOrderedEnumerable<MedicineExpiry> Order<T>(Func<MedicineExpiry, T> key, IComparer<T>? comparer = null) =>
            descending ? matching.OrderByDescending(key, comparer) : matching.OrderBy(key, comparer);
        var sorted = sortColumn switch
        {
            0 => Order(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            1 => Order(row => row.Batch, StringComparer.CurrentCultureIgnoreCase),
            2 => Order(row => row.Received ?? (descending ? DateTime.MinValue : DateTime.MaxValue)),
            4 => Order(row => row.Remaining),
            _ => Order(row => row.Expires ?? (descending ? DateTime.MinValue : DateTime.MaxValue))
        };
        var sortedRows = sorted.ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var groups = sortedRows.GroupBy(row => row.Name.Trim(), StringComparer.CurrentCultureIgnoreCase);
        if (sortColumn == 4)
            groups = descending
                ? groups.OrderByDescending(group => group.Sum(row => row.Remaining))
                : groups.OrderBy(group => group.Sum(row => row.Remaining));

        string ExpiryText(MedicineExpiry row)
        {
            string expiry = row.DeliveriesConfirmed ? row.Expires?.ToString("dd.MM.yyyy") ?? "—" : "Nie ustalono dostaw";
            return row.Expires?.Date < today ? expiry + " (po terminie)" : expiry;
        }
        string Batch(MedicineExpiry row) => string.IsNullOrWhiteSpace(row.Batch) ? "—" : row.Batch;
        var rows = new List<ExpiryRow>();
        int products = 0;
        foreach (var group in groups)
        {
            products++;
            var deliveries = group.ToArray();
            var first = deliveries[0];
            if (deliveries.Length == 1)
            {
                rows.Add(new(group.Key, group.Key, Batch(first), first.Received?.ToString("dd.MM.yyyy") ?? "—",
                    ExpiryText(first), first.Remaining.ToString("0.####"), false, false));
                continue;
            }

            bool expanded = expandedProducts.Contains(group.Key);
            var earliest = deliveries.Where(row => row.Expires.HasValue).MinBy(row => row.Expires);
            rows.Add(new(group.Key, group.Key, $"{deliveries.Length} pozycji", "—",
                earliest is null ? "—" : ExpiryText(earliest), deliveries.Sum(row => row.Remaining).ToString("0.####"), true, true, expanded ? "−" : "+"));
            if (!expanded) continue;
            foreach (var delivery in deliveries)
                rows.Add(new(group.Key, "↳ Dostawa", Batch(delivery), delivery.Received?.ToString("dd.MM.yyyy") ?? "—",
                    ExpiryText(delivery), delivery.Remaining.ToString("0.####"), false, false));
        }
        list.ItemsSource = rows;
        int unresolved = sortedRows.Where(row => !row.DeliveriesConfirmed).Select(row => row.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
        status.Text = products == 0 ? "Brak pasujących pozycji." : $"Produkty: {products} | Pozycje: {sortedRows.Count}";
        if (unresolved > 0) status.Text += $" | Nie ustalono dostaw dla {unresolved} leków. Pokazano ich łączny stan.";
    }

    bool MatchesExpiry(DateTime? expiry, DateTime today) => filter.SelectedIndex switch
    {
        1 => expiry < today,
        2 => expiry >= today && expiry <= today.AddDays(30),
        3 => expiry >= today && expiry <= today.AddDays(90),
        4 => expiry is null,
        _ => true
    };
}
