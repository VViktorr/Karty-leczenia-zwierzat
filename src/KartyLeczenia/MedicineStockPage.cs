namespace KartyLeczenia.Views;

public sealed class MedicineStockPage : ContentPage
{
    sealed record Row(string Name, string Detail);
    readonly AppState state;
    readonly bool prices;
    readonly Picker filter = new() { WidthRequest = 235 };
    readonly Entry search = new();
    readonly CollectionView list = new();
    readonly Label status = Ui.Note("");
    List<MedicinePrice> priceRows = [];
    List<MedicineExpiry> expiryRows = [];
    bool loading, loaded;

    public MedicineStockPage(AppState state, bool prices)
    {
        this.state = state;
        this.prices = prices;
        Title = prices ? "Cennik leków" : "Kontrola terminów ważności";
        search.Placeholder = prices ? "Szukaj leku" : "Szukaj leku lub serii";
        filter.Title = prices ? "Poziom ceny brutto" : "Termin ważności";
        if (!prices)
        {
            filter.ItemsSource = new[] { "Wszystkie dostawy", "Po terminie", "Do 30 dni", "Do 90 dni", "Bez daty" };
            filter.SelectedIndex = 0;
        }
        list.SelectionMode = SelectionMode.None;
        list.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { FontAttributes = FontAttributes.Bold };
            name.SetBinding(Label.TextProperty, nameof(Row.Name));
            var detail = Ui.Note("");
            detail.SetBinding(Label.TextProperty, nameof(Row.Detail));
            return new VerticalStackLayout { Padding = new Thickness(12, 8), Spacing = 3, Children = { name, detail } };
        });
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
        actions.Add(search, 0, 0);
        actions.Add(filter, 1, 0);
        actions.Add(Ui.Action("Odśwież", Refresh), 2, 0);
        var header = new VerticalStackLayout { Spacing = 8, Children = { Ui.Heading(Title), actions } };
        header.Children.Add(status);
        var root = new Grid
        {
            Padding = 18, RowSpacing = 10,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            }
        };
        root.Add(Ui.Card(header), 0, 0);
        root.Add(Ui.Card(list), 0, 1);
        Content = root;
        search.TextChanged += (_, _) => ShowRows();
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
        filter.IsEnabled = false;
        status.Text = "Pobieranie danych z Subiekta…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (prices)
            {
                int? selected = filter.SelectedItem is int currentLevel ? currentLevel : null;
                int[] levels = await MedicineStockService.PriceLevels(options, timeout.Token);
                filter.ItemsSource = levels;
                if (levels.Length == 0)
                {
                    status.Text = "Nie znaleziono obsługiwanych poziomów ceny brutto w tej bazie.";
                    return;
                }
                int level = selected.HasValue && levels.Contains(selected.Value) ? selected.Value : levels[0];
                filter.SelectedItem = level;
                priceRows = await MedicineStockService.Prices(options, level, timeout.Token);
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
        finally { filter.IsEnabled = true; loading = false; }
    }

    void ShowRows()
    {
        if (!loaded) return;
        string query = (search.Text ?? "").Trim();
        bool Matches(string text) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        DateTime today = DateTime.Today;
        var rows = prices
            ? priceRows.Where(row => Matches(row.Name)).Select(row =>
                new Row(row.Name, row.Gross is decimal gross ? $"Brutto: {gross:N2}" : "Brak ceny brutto")).ToList()
            : expiryRows.Where(row => Matches(row.Name) || Matches(row.Batch))
                .Where(row => MatchesExpiry(row.Expires?.Date, today))
                .OrderBy(row => row.Expires ?? DateTime.MaxValue)
                .Select(row =>
                {
                    string date = row.Expires?.ToString("dd.MM.yyyy") ?? "brak daty";
                    string batch = string.IsNullOrWhiteSpace(row.Batch) ? "brak numeru" : row.Batch;
                    string expired = row.Expires?.Date < today ? " | po terminie" : "";
                    return new Row(row.Name, $"Seria: {batch} | Magazyn: {row.WarehouseName} | Ważność: {date}{expired} | Pozostało: {row.Remaining:0.####} (jedn. mag.)");
                }).ToList();
        list.ItemsSource = rows;
        status.Text = rows.Count == 0 ? "Brak pasujących pozycji." : $"Pozycje: {rows.Count}";
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
