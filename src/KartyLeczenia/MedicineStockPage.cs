namespace KartyLeczenia.Views;

public sealed class MedicineStockPage : ContentPage
{
    const string PrintPreferencePrefix = "PriceListPrint.";
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
    ConnectionOptions? loadedConnection;
    long lastRefreshTick;

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
            RememberPrintSettings();
            var formatField = Ui.Field("Format karencji z SQLite", printWithdrawalFormat);
            void UpdateFormatVisibility() => formatField.IsVisible = printWithdrawal.IsChecked && withdrawalSource.SelectedIndex == 0;
            printWithdrawal.CheckedChanged += (_, _) => UpdateFormatVisibility();
            withdrawalSource.SelectedIndexChanged += (_, _) => UpdateFormatVisibility();
            UpdateFormatVisibility();
            var print = Ui.Action("Wydrukuj cennik", () => PreviewPrices());
            print.HeightRequest = 32;
            print.Padding = new Thickness(14, 0);
            print.VerticalOptions = LayoutOptions.End;
            header.Children.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { Ui.Field("Źródło karencji na wydruku", withdrawalSource), formatField, print }
            });
            var printOptions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            printOptions.Add(Ui.Field("Czcionka wydruku", printFont));
            printOptions.Add(Ui.Field("Rozmiar (pt)", printSize));
            foreach (var (label, check) in new[] {
                ("Lp.", printNumber), ("Seria", printBatch), ("Cena", printPrice),
                ("Karencja", printWithdrawal), ("Legenda", printLegend),
                ("Drogi podania na każdej stronie", printLegendEveryPage) })
                printOptions.Add(Ui.Field(label, check));
            printLegendEveryPage.IsEnabled = printLegend.IsChecked;
            printLegend.CheckedChanged += (_, e) => printLegendEveryPage.IsEnabled = e.Value;
            var layoutOptions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            layoutOptions.Add(Ui.Field("Orientacja", printOrientation));
            layoutOptions.Add(PrintSliderField("Margines strony (mm)", printMargin));
            layoutOptions.Add(PrintSliderField("Min. wysokość wiersza (mm)", printRowHeight));
            layoutOptions.Add(PrintSliderField("Odstęp nad i pod tekstem (mm)", printRowPadding));
            string[] widthLabels = ["Lp.", "Nazwa leku", "Seria", "Cena", "Karencja"];
            CheckBox?[] visibilityOptions = [printNumber, null, printBatch, printPrice, printWithdrawal];
            for (int i = 0; i < widthLabels.Length; i++)
                {
                var widthField = PrintSliderField("Szerokość: " + widthLabels[i], printColumnWidths[i]);
                var visibility = visibilityOptions[i];
                if (visibility is not null)
                {
                    widthField.IsVisible = visibility.IsChecked;
                    visibility.CheckedChanged += (_, e) => widthField.IsVisible = e.Value;
                }
                layoutOptions.Add(widthField);
            }

            var settings = new VerticalStackLayout { Spacing = 6, Padding = 12, Children = {
                printOptions, Ui.Note("Szerokości są proporcjami widocznych kolumn. Wysokość wiersza rośnie, gdy tekst się zawija."),
                layoutOptions } };
            settings.Children.Add(TypographyFields("Wszystkie nagłówki kolumn", headerTypography));
            for (int i = 0; i < widthLabels.Length; i++)
                settings.Children.Add(TypographyFields("Komórki · " + widthLabels[i], columnTypography[i]));
            var settingsPage = new ContentPage { Title = "Ustawienia wydruku cennika" };
            var preview = new WebView();
            settings.Children.Add(Ui.Action("Odśwież próbkę PDF", () => PreviewPrices(preview)));
            var editor = new Grid { RowDefinitions = {
                new RowDefinition { Height = new GridLength(0.45, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(0.55, GridUnitType.Star) }
            } };
            editor.Add(new ScrollView { Content = settings }, 0, 0);
            editor.Add(preview, 0, 1);
            settingsPage.Content = Ui.WithClose(settingsPage, editor, async () => await settingsPage.Navigation.PopModalAsync());
            header.Children.Add(Ui.Action("Ustawienia wydruku", async () =>
            {
                await Navigation.PushModalAsync(settingsPage);
                await PreviewPrices(preview);
            }));
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
            priceBasis.SelectedIndexChanged += (_, _) => priceLabel.Text = NetPrice ? "Cena netto" : "Cena brutto";
            var table = new Grid
            {
                RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } }
            };
            table.Add(priceHeading, 0, 0);
            table.Add(list, 0, 1);
            priceWidths.AttachTableMenu(table, "MedicinePrices");
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
        CrashLog.Write("MedicineStockPage.Open");
        if (loaded && ReferenceEquals(loadedConnection, state.ValidatedConnection) &&
            Environment.TickCount64 - lastRefreshTick < 30_000)
        {
            CrashLog.Write(prices ? "MedicinePrices.CacheHit" : "MedicineExpiries.CacheHit");
            return;
        }
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
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
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
            var readMilliseconds = elapsed.ElapsedMilliseconds;
            loadedConnection = options;
            lastRefreshTick = Environment.TickCount64;
            loaded = true;
            ShowRows();
            CrashLog.Write($"{(prices ? "MedicinePrices" : "MedicineExpiries")}.Refresh readMs={readMilliseconds}, rowsMs={elapsed.ElapsedMilliseconds - readMilliseconds}");
        }
        catch (OperationCanceledException) { status.Text = "Przekroczono czas odczytu. Spróbuj odświeżyć listę."; }
        catch (Exception ex)
        {
            CrashLog.Write(prices ? "MedicinePrices.Read" : "MedicineExpiries.Read", ex);
            status.Text = "Nie udało się odczytać danych. " + SubiektConnectionService.FriendlyError(ex);
        }
        finally { filter.IsEnabled = priceBasis.IsEnabled = true; loading = false; }
    }

    static readonly string[] PrintFonts = ["Arial", "Calibri", "Tahoma", "Segoe UI", "Times New Roman"];
    readonly Picker printFont = new() { ItemsSource = PrintFonts, SelectedIndex = 0, WidthRequest = 145 };
    readonly Picker printSize = new() { ItemsSource = new[] { "8", "9", "10", "11", "12" }, SelectedIndex = 1, WidthRequest = 80 };
    readonly CheckBox printNumber = new() { IsChecked = true };
    readonly CheckBox printBatch = new() { IsChecked = true, IsEnabled = false };
    readonly CheckBox printPrice = new() { IsChecked = true };
    readonly CheckBox printWithdrawal = new() { IsChecked = true };
    readonly Picker printWithdrawalFormat = new() { ItemsSource = new[] { "Krótka karencja", "Pełna karencja" }, SelectedIndex = 0 };
    readonly CheckBox printLegend = new() { IsChecked = true };

    readonly CheckBox printLegendEveryPage = new();

    sealed class PrintTypography(int defaultAlignment)
    {
        public Picker Font { get; } = new()
        {
            ItemsSource = new[] { "Z ustawień wydruku" }.Concat(PrintFonts).ToArray(), SelectedIndex = 0
        };
        public Picker Size { get; } = new()
        {
            ItemsSource = new[] { "Z ustawień wydruku", "8", "9", "10", "11", "12" }, SelectedIndex = 0
        };
        public Picker Alignment { get; } = new()
        {
            ItemsSource = new[] { "Do lewej", "Do środka", "Do prawej" }, SelectedIndex = defaultAlignment
        };

        public string Css()
        {
            string font = Font.SelectedIndex > 0 ? $"font-family:\"{Font.SelectedItem}\", sans-serif;" : "";
            string size = Size.SelectedIndex > 0 ? $"font-size:{Size.SelectedItem}pt;" : "";
            string alignment = Alignment.SelectedIndex switch { 1 => "center", 2 => "right", _ => "left" };
            return $"{font}{size}text-align:{alignment};";
        }
    }

    readonly PrintTypography headerTypography = new(0);
    readonly PrintTypography[] columnTypography = [new(2), new(0), new(0), new(2), new(0)];

    static View TypographyFields(string title, PrintTypography typography) => new VerticalStackLayout
    {
        Spacing = 4,
        Children =
        {
            new Label { Text = title, FontAttributes = FontAttributes.Bold },
            new FlexLayout
            {
                Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                Children = { Ui.Field("Czcionka", typography.Font), Ui.Field("Rozmiar (pt)", typography.Size),
                    Ui.Field("Wyrównanie", typography.Alignment) }
            }
        }
    };

    readonly Picker printOrientation = new() { ItemsSource = new[] { "Pozioma", "Pionowa" }, SelectedIndex = 0 };
    readonly Slider printMargin = new(4, 25, 7);
    readonly Slider printRowHeight = new(0, 18, 0);
    readonly Slider printRowPadding = new(0, 5, 0.5);
    readonly Slider[] printColumnWidths = [new(2, 20, 4), new(10, 80, 43), new(5, 45, 14),
        new(5, 45, 13), new(10, 85, 40)];

    void RememberPrintSettings()
    {
        var preferences = Microsoft.Maui.Storage.Preferences.Default;
        foreach (var (name, picker) in new[]
        {
            ("Font", printFont), ("Size", printSize), ("Orientation", printOrientation),
            ("WithdrawalSource", withdrawalSource), ("WithdrawalFormat", printWithdrawalFormat)
        })
            RememberPicker(name, picker);

        RememberTypography("Header", headerTypography);
        for (int i = 0; i < columnTypography.Length; i++)
            RememberTypography($"Column{i}", columnTypography[i]);

        void RememberTypography(string name, PrintTypography typography)
        {
            RememberPicker(name + "Font", typography.Font);
            RememberPicker(name + "Size", typography.Size);
            RememberPicker(name + "Alignment", typography.Alignment);
        }

        void RememberPicker(string name, Picker picker)
        {
            string key = PrintPreferencePrefix + name;
            int index = preferences.Get(key, picker.SelectedIndex);
            if (index >= 0 && index < picker.Items.Count)
                picker.SelectedIndex = index;
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (picker.SelectedIndex >= 0)
                    preferences.Set(key, picker.SelectedIndex);
            };
        }

        foreach (var (name, check) in new[]
        {
            ("Number", printNumber), ("Price", printPrice),
            ("Withdrawal", printWithdrawal), ("Legend", printLegend), ("LegendEveryPage", printLegendEveryPage)
        })
        {
            string key = PrintPreferencePrefix + name;
            check.IsChecked = preferences.Get(key, check.IsChecked);
            check.CheckedChanged += (_, e) => preferences.Set(key, e.Value);
        }

        RememberSlider("Margin", printMargin);
        RememberSlider("RowHeight", printRowHeight);
        RememberSlider("RowPadding", printRowPadding);
        for (int i = 0; i < printColumnWidths.Length; i++)
            RememberSlider($"ColumnWidth{i}", printColumnWidths[i]);

        void RememberSlider(string name, Slider slider)
        {
            string key = PrintPreferencePrefix + name;
            slider.Value = Math.Clamp(preferences.Get(key, slider.Value), slider.Minimum, slider.Maximum);
            slider.ValueChanged += (_, e) => preferences.Set(key, e.NewValue);
        }
    }

    static View PrintSliderField(string title, Slider slider)
    {
        slider.WidthRequest = 155;
        var field = Ui.Field($"{title}: {slider.Value:0.#}", slider);
        var label = (Label)field.Children[0];
        slider.ValueChanged += (_, e) => label.Text = $"{title}: {e.NewValue:0.#}";
        field.Margin = new Thickness(0, 0, 12, 4);
        return field;
    }

    async Task PreviewPrices(WebView? sample = null)
    {
        if (openingPrint) return;
        if (!loaded || loading || state.ValidatedConnection is not { } options || filter.SelectedItem is not MedicinePriceLevel level)
            throw new InvalidOperationException("Najpierw wczytaj cennik.");
        if (printWithdrawal.IsChecked && withdrawalSource.SelectedIndex < 0)
            throw new InvalidOperationException("Wybierz źródło karencji.");
        bool net = NetPrice;
        string basis = net ? "netto" : "brutto";
        const bool showBatch = true;
        bool showNumber = printNumber.IsChecked, showPrice = printPrice.IsChecked, showWithdrawal = printWithdrawal.IsChecked, showLegend = printLegend.IsChecked;
        bool repeatLegend = showLegend && printLegendEveryPage.IsChecked;
        string headerCss = headerTypography.Css();
        var columnCss = columnTypography.Select(typography => typography.Css()).ToArray();
        int source = Math.Max(0, withdrawalSource.SelectedIndex);
        string font = printFont.SelectedItem as string ?? PrintFonts[0];
        string fontSize = printSize.SelectedItem as string ?? "9";
        string orientation = printOrientation.SelectedIndex == 1 ? "portrait" : "landscape";
        string margin = FormattableString.Invariant($"{printMargin.Value:0.#}");
        string rowHeight = FormattableString.Invariant($"{printRowHeight.Value:0.#}");
        string rowPadding = FormattableString.Invariant($"{printRowPadding.Value:0.#}");
        bool shortWithdrawal = printWithdrawalFormat.SelectedIndex == 0;
        var weights = printColumnWidths.Select(slider => slider.Value).ToArray();
        string sourceName = source == 0 ? "Katalog SQLite (ChPL)" : $"Subiekt ({withdrawalSource.SelectedItem})";
        string query = (search.Text ?? "").Trim();

        openingPrint = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var data = await MedicineStockService.Prices(options, level.Number, timeout.Token, showWithdrawal ? source : 0, net);
            var catalogue = showWithdrawal && source == 0 ? await MedicineCatalogue.GetAsync() : null;
            if (state.ValidatedConnection != options || !loaded || loading || !Equals(filter.SelectedItem, level) || NetPrice != net || (showWithdrawal && withdrawalSource.SelectedIndex != source) || (search.Text ?? "").Trim() != query)
                throw new InvalidOperationException("Cennik zmienił się w trakcie przygotowania wydruku. Ponów operację.");

            var rows = data.Where(row => row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (sample is not null) rows = rows.Take(12).ToList();
            if (rows.Count == 0) throw new InvalidOperationException("Brak pozycji do wydruku.");
            string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
            var columns = new List<(string Heading, int Index, double Weight, Func<MedicinePrice, int, string> Value, string Class)>();
            if (showNumber) columns.Add(("Lp.", 0, weights[0], (_, i) => (i + 1).ToString(), "price"));
            columns.Add(("Nazwa leku", 1, weights[1], (row, _) => row.Name, ""));
            if (showBatch) columns.Add(("Najnowsza seria", 2, weights[2], (row, _) => row.LatestBatch, ""));
            if (showPrice) columns.Add(($"Cena {basis}", 3, weights[3], (row, _) => row.Gross?.ToString("N2") ?? "—", "price"));
            if (showWithdrawal) columns.Add(("Karencja", 4, weights[4], (row, _) =>
            {
                string text = catalogue is null ? row.Withdrawal : CatalogueWithdrawal(catalogue, row, shortWithdrawal);
                return string.Join(", ", text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }, ""));

            double totalWeight = columns.Sum(column => column.Weight);
            string widths = string.Concat(columns.Select(column =>
                $"<col style='width:{FormattableString.Invariant($"{column.Weight * 100.0 / totalWeight:0.###}")}%'>"));
            string headings = string.Concat(columns.Select(column => $"<th>{Encode(column.Heading)}</th>"));
            string body = string.Concat(rows.Select((row, index) => "<tr>" +
                string.Concat(columns.Select(column => $"<td class='{column.Class}' style='{columnCss[column.Index]}'>{Encode(column.Value(row, index))}</td>"))
                + "</tr>"));
            string legend = showLegend ? AdministrationRouteLegend.Html : "";
            string legendRow = repeatLegend
                ? $"<tr><td class=\"route-legend\" colspan=\"{columns.Count}\">{legend}</td></tr>" : "";
            string html = $$"""
                <!doctype html><html lang="pl"><head><meta charset="utf-8"><title>Cennik leków</title>
                <style>
                    @page { size: A4 {{orientation}}; margin: {{margin}}mm; }
                    body { font: {{fontSize}}pt/1.15 "{{font}}", sans-serif; color: #171717; }
                    @media screen { body { margin: 0; } .sheet { box-sizing: border-box; width: {{(orientation == "portrait" ? 210 : 297)}}mm; padding: {{margin}}mm; } }
                    h1 { font-size: 13pt; margin: 0 0 1mm; }
                    p { margin: 0 0 1mm; }
                    table { width: 100%; table-layout: fixed; border-collapse: collapse; }
                    thead { display: table-header-group; }
                    tr { break-inside: avoid; }
                    th, td { border: 1px solid #999; padding: {{rowPadding}}mm 3px; text-align: left; vertical-align: top; }
                    th { background: #eee; {{headerCss}} }
                    .route-legend { border: 0; padding: 0; height: auto; }
                    td { height: {{rowHeight}}mm; overflow-wrap: anywhere; }
                    .price { text-align: right; white-space: nowrap; }
                </style></head><body><main class="sheet">
                <h1>Cennik leków</h1>
                <p>{{DateTime.Today:dd.MM.yyyy}} · {{Encode(level.ToString())}}{{(showWithdrawal ? " · Źródło karencji: " + Encode(sourceName) : "")}}</p>
                {{(repeatLegend ? "" : legend)}}
                <table><colgroup>{{widths}}</colgroup><thead>{{legendRow}}<tr>{{headings}}</tr></thead><tbody>{{body}}</tbody></table>
                </main></body></html>
                """;
            if (sample is not null) sample.Source = new HtmlWebViewSource { Html = html };
            else await Navigation.PushModalAsync(new PrintPreviewPage(html, "preview", "Podgląd cennika leków", "cennik-lekow"));
        }
        finally { openingPrint = false; }
    }

    static string CatalogueWithdrawal(ILookup<string, MedicineCatalogue.Entry> catalogue, MedicinePrice row, bool shortWithdrawal)
    {
        var entry = MedicineCatalogue.Match(catalogue, row.Symbol, row.Name);
        if (entry is null || entry.Excluded) return "";
        return string.Join(" | ", entry.Variants
            .Select(v => (v.Species, Route: shortWithdrawal ? ShortRoute(v.Route) : v.Route, Text: shortWithdrawal && v.WithdrawalShort.Length > 0
                ? v.WithdrawalShort : FullWithdrawal(v)))
            .Where(item => item.Text.Length > 0)
            .GroupBy(item => (item.Species, item.Text))
            .Select(group => $"{group.Key.Species} {string.Join("/", group.Select(item => item.Route).Distinct(StringComparer.OrdinalIgnoreCase))} · {group.Key.Text}"));
    }

    static string ShortRoute(string route) => route.Trim().ToLowerInvariant() switch
    {
        "domięśniowa" or "domięśniowo" => "IM",
        "dożylna" or "dożylnie" => "IV",
        "podskórna" or "podskórnie" => "SC",
        "domaciczna" or "domacicznie" => "IUT",
        "dowymieniowa" or "dowymieniowo" => "IMM",
        _ => route
    };

    static string FullWithdrawal(MedicineCatalogue.Variant variant) =>
        string.Join(", ", MedicineCatalogue.Withdrawal(variant).Split('\n', StringSplitOptions.RemoveEmptyEntries));

    View CreateExpiryTable()
    {
        for (int i = 0; i < expiryColumns.Length; i++)
        {
            int column = i;
            var button = new Button { Padding = new Thickness(4, 0), MinimumHeightRequest = 32, BorderWidth = 0,
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
        expiryWidths.AttachTableMenu(table, "MedicineExpiry");
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
