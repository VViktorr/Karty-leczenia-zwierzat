namespace KartyLeczenia.Views;

public sealed class RegistryPage : ContentPage
{
    readonly Entry id = new() { Keyboard = Keyboard.Numeric, WidthRequest = 130 };
    readonly Entry medicine = new();
    readonly Editor excerpt = new() { IsReadOnly = true, HeightRequest = 270 };
    readonly Editor result = new() { HeightRequest = 110 };
    readonly Entry species = new(), route = new(), food = new();
    readonly Label info = Ui.Note("Pobierz charakterystykę i sprawdź tożsamość leku.");
    readonly CheckBox confirmed = new();
    readonly Button apply = new() { Text = "Zastosuj do pozycji", IsEnabled = false };
    readonly TaskCompletionSource<RegistrySelection?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource lifetime = new();
    RegistryDocument? document;
    public Task<RegistrySelection?> Completion => completion.Task;

    public RegistryPage(string product, int? mapped)
    {
        Title = "Karencja z rejestru RPL";
        medicine.Text = product;
        id.Text = (mapped ?? (product.StartsWith("FLUNIVIS", StringComparison.OrdinalIgnoreCase) ? 50492 : product.StartsWith("FLORDOFEN 300", StringComparison.OrdinalIgnoreCase) ? 32709 : 0)).ToString();
        var panel = new VerticalStackLayout { Padding = 24, Spacing = 10, MaximumWidthRequest = 1050 };
        panel.Add(Ui.Heading("Karencja z oficjalnej charakterystyki"));
        panel.Add(Ui.Note("Porównaj nazwę, moc i postać leku z PDF. ID jest propozycją wymagającą sprawdzenia. Przy stosowaniu poza warunkami charakterystyki okres ustala lekarz."));
        panel.Add(Ui.Field("Nazwa produktu", medicine)); panel.Add(Ui.Field("ID produktu RPL (nie numer pozwolenia)", id));
        var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        buttons.Add(Ui.Action("Szukaj leku w RPL", () => Launcher.Default.OpenAsync("https://rejestrymedyczne.ezdrowie.gov.pl/rpl/search/public")));
        buttons.Add(Ui.Action("Otwórz źródło PDF", () => Launcher.Default.OpenAsync(RegistryService.Url(ProductId()))));
        buttons.Add(Ui.Action("Pobierz aktualną ChPL", Fetch)); panel.Add(buttons);
        panel.Add(info); panel.Add(excerpt);
        panel.Add(Context("Gatunek", species, ["Bydło", "Świnie", "Konie", "Owce", "Kozy", "Drób"]));
        panel.Add(Context("Droga podania", route, ["Domięśniowo", "Podskórnie", "Dożylnie", "Doustnie", "Dowymieniowo"]));
        panel.Add(Context("Produkt spożywczy", food, ["Tkanki jadalne", "Mleko", "Jaja", "Miód", "Tkanki jadalne i mleko"]));
        panel.Add(Ui.Action("Kopiuj zaznaczony fragment do karencji", () =>
        {
            string text = excerpt.Text ?? ""; int start = Math.Clamp(excerpt.CursorPosition, 0, text.Length);
            int length = Math.Clamp(excerpt.SelectionLength, 0, text.Length - start);
            if (length > 0) result.Text = text.Substring(start, length);
            else info.Text = "Zaznacz fragment sekcji karencji lub wpisz jego treść w polu poniżej.";
            return Task.CompletedTask;
        }));
        panel.Add(Ui.Field("Karencja do zapisania na karcie — sprawdź i popraw", result));
        panel.Add(new HorizontalStackLayout { Children = { confirmed, new Label { Text = "Potwierdzam produkt, gatunek, drogę podania i zgodność wybranej karencji z charakterystyką.", VerticalOptions = LayoutOptions.Center } } });
        panel.Add(apply); panel.Add(Ui.Action("Anuluj", Close));
        Content = new ScrollView { Content = panel };
        foreach (var entry in new[] { medicine, species, route, food }) entry.TextChanged += (_, _) => InvalidateChoice();
        result.TextChanged += (_, _) => InvalidateChoice();
        id.TextChanged += (_, _) => { document = null; excerpt.Text = ""; result.Text = ""; InvalidateChoice(); };
        confirmed.CheckedChanged += (_, e) => apply.IsEnabled = e.Value && document != null && new[] { medicine.Text, species.Text, route.Text, food.Text, result.Text }.All(s => !string.IsNullOrWhiteSpace(s));
        apply.Clicked += async (_, _) =>
        {
            if (document == null || !apply.IsEnabled) return;
            completion.TrySetResult(new(document, species.Text.Trim(), route.Text.Trim(), food.Text.Trim(), result.Text.Trim(), medicine.Text.Trim()));
            await Navigation.PopModalAsync();
        };
    }
    int ProductId() => int.TryParse(id.Text, out int value) && value > 0 ? value : throw new InvalidOperationException("Wpisz poprawne ID produktu RPL.");
    static VerticalStackLayout Context(string label, Entry entry, string[] options)
    {
        var picker = new Picker { Title = "Wybierz lub wpisz poniżej", ItemsSource = options };
        picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedItem is string value) entry.Text = value; };
        var field = Ui.Field(label, entry); field.Children.Insert(1, picker); return field;
    }
    void InvalidateChoice() { confirmed.IsChecked = false; apply.IsEnabled = false; }
    async Task Fetch()
    {
        int requested = ProductId(); document = null; InvalidateChoice(); excerpt.Text = ""; result.Text = ""; id.IsEnabled = false;
        info.Text = "Pobieranie PDF z rejestru…";
        try
        {
            RegistryDocument loaded;
            try { loaded = await RegistryService.Fetch(requested); }
            catch (Exception)
            {
                if (lifetime.IsCancellationRequested) return;
                var cached = RegistryService.Cached(requested);
                if (cached == null || !await DisplayAlertAsync("Brak aktualnego pobrania", $"Dostępna jest kopia pobrana {cached.DownloadedUtc.ToLocalTime():g}. Nie potwierdzono jej aktualności. Użyć do ręcznej weryfikacji?", "Użyj kopii", "Anuluj")) throw;
                loaded = cached;
            }
            if (lifetime.IsCancellationRequested) return;
            document = loaded;
            excerpt.Text = "IDENTYFIKACJA Z PDF:\n" + loaded.ProductIdentity + "\n\nOKRESY KARENCJI:\n" + loaded.WithdrawalSection;
            info.Text = $"RPL ID {loaded.Id} | pobrano {loaded.DownloadedUtc.ToLocalTime():g}\n{loaded.Url}\nData pobrania nie jest datą aktualizacji ChPL. Wybierz fragment odpowiedni do leczenia.";
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) info.Text = "Nie pobrano danych: " + ex.Message; }
        finally { id.IsEnabled = true; }
    }
    async Task Close() { completion.TrySetResult(null); await Navigation.PopModalAsync(); }
    protected override void OnDisappearing() { lifetime.Cancel(); completion.TrySetResult(null); base.OnDisappearing(); }
}
