namespace KartyLeczenia.Views;

public sealed class RegistryPage : ContentPage
{
    readonly Entry id = new() { WidthRequest = 850 };
    readonly Entry medicine = new();
    readonly Editor excerpt = new() { IsReadOnly = true, HeightRequest = 270 };
    readonly Editor result = new() { HeightRequest = 110 };
    readonly Entry species = new(), route = new(), food = new();
    readonly Label info = Ui.Note("Pobierz charakterystykę i sprawdź tożsamość leku.");
    readonly Picker candidates = new() { Title = "Wyniki wyszukiwania leków" };
    readonly CheckBox confirmed = new();
    readonly Button apply = new() { Text = "Zastosuj do pozycji", IsEnabled = false };
    readonly TaskCompletionSource<RegistrySelection?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource lifetime = new();
    RegistryDocument? document;
    int fetchGeneration;
    public Task<RegistrySelection?> Completion => completion.Task;

    public RegistryPage(string product, int? mapped)
    {
        Title = "Karencja z rejestru RPL";
        medicine.Text = product;
        var matches = RegistryCatalog.ExactCandidates(product);
        id.Text = (mapped ?? (matches.Count == 1 ? matches[0].Id : 0)).ToString();
        var panel = new VerticalStackLayout { Padding = 24, Spacing = 10, MaximumWidthRequest = 1050 };
        panel.Add(Ui.Heading("Karencja z oficjalnej charakterystyki"));
        panel.Add(Ui.Note("Porównaj nazwę, moc i postać leku z PDF. Karencja jest wpisywana automatycznie z pełnej sekcji PDF. Dopasowanie produktu wymaga sprawdzenia. Przy stosowaniu poza warunkami charakterystyki okres ustala lekarz."));
        panel.Add(Ui.Field("Nazwa produktu", medicine)); panel.Add(candidates);
        panel.Add(Ui.Field("ID RPL lub oficjalny link do charakterystyki", id));
        var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        buttons.Add(Ui.Action("Znajdź lek w katalogu", Search));
        buttons.Add(Ui.Action("Aktualizuj katalog RPL", async () => { await RegistryCatalog.UpdateAsync(true, lifetime.Token); ShowCandidates(); }));
        buttons.Add(Ui.Action("Otwórz źródło PDF", () => Launcher.Default.OpenAsync(RegistryService.Url(ProductId()))));
        buttons.Add(Ui.Action("Pobierz aktualną ChPL", Fetch)); panel.Add(buttons);
        panel.Add(info); panel.Add(excerpt);
        panel.Add(Context("Gatunek", species, ["Bydło", "Świnie", "Konie", "Owce", "Kozy", "Drób"]));
        panel.Add(Context("Droga podania", route, ["Domięśniowo", "Podskórnie", "Dożylnie", "Doustnie", "Dowymieniowo"]));
        panel.Add(Context("Produkt spożywczy", food, ["Tkanki jadalne", "Mleko", "Jaja", "Miód", "Tkanki jadalne i mleko"]));
        panel.Add(Ui.Action("Opcjonalnie zawęź karencję do zaznaczonego fragmentu", () =>
        {
            string text = excerpt.Text ?? ""; int start = Math.Clamp(excerpt.CursorPosition, 0, text.Length);
            int length = Math.Clamp(excerpt.SelectionLength, 0, text.Length - start);
            if (length > 0) result.Text = text.Substring(start, length);
            else info.Text = "Pełna karencja jest już wpisana poniżej. Zaznacz fragment tylko wtedy, gdy chcesz ją zawęzić.";
            return Task.CompletedTask;
        }));
        panel.Add(Ui.Field("Karencja uzupełniona automatycznie — sprawdź i ewentualnie popraw", result));
        panel.Add(new HorizontalStackLayout { Children = { confirmed, new Label { Text = "Potwierdzam dopasowanie produktu i zgodność karencji z charakterystyką oraz leczeniem.", VerticalOptions = LayoutOptions.Center } } });
        panel.Add(apply); panel.Add(Ui.Action("Anuluj", Close));
        Content = new ScrollView { Content = panel };
        foreach (var entry in new[] { medicine, species, route, food }) entry.TextChanged += (_, _) => InvalidateChoice();
        result.TextChanged += (_, _) => InvalidateChoice();
        id.TextChanged += (_, _) => { fetchGeneration++; document = null; excerpt.Text = ""; result.Text = ""; InvalidateChoice(); };
        confirmed.CheckedChanged += (_, e) => apply.IsEnabled = e.Value && document != null && new[] { medicine.Text, result.Text }.All(s => !string.IsNullOrWhiteSpace(s));
        candidates.SelectedIndexChanged += async (_, _) =>
        {
            if (candidates.SelectedItem is RegistryProduct candidate)
            { id.Text = candidate.Id.ToString(); await Fetch(); }
        };
        bool firstLoad = true;
        Loaded += async (_, _) => { if (!firstLoad) return; firstLoad = false; if (id.Text != "0") await Fetch(); else await Search(); };
        apply.Clicked += async (_, _) =>
        {
            if (document == null || !apply.IsEnabled) return;
            completion.TrySetResult(new(document, species.Text?.Trim() ?? "", route.Text?.Trim() ?? "", food.Text?.Trim() ?? "", result.Text.Trim(), medicine.Text.Trim()));
            await Navigation.PopModalAsync();
        };
    }
    int ProductId() => RegistryService.ParseProductId(id.Text ?? "");
    void ShowCandidates()
    {
        var matches = RegistryCatalog.Search(medicine.Text ?? "");
        candidates.ItemsSource = matches;
        info.Text = matches.Count == 0 ? "Nie znaleziono dopasowania w dostępnym katalogu. Wklej oficjalny link do charakterystyki; program zapamięta powiązanie po zatwierdzeniu." : $"Znaleziono {matches.Count} produktów. Wybierz właściwą moc i postać.";
    }
    async Task Search()
    {
        string? catalogFailure = null;
        try
        {
            if (RegistryCatalog.Search(medicine.Text ?? "").Count == 0)
            {
                try { await RegistryCatalog.UpdateAsync(token: lifetime.Token); }
                catch (Exception ex) { catalogFailure = "Katalog RPL jest niedostępny: " + ex.Message; }
            }
            if (lifetime.IsCancellationRequested) return;
            ShowCandidates();
            if (catalogFailure != null) info.Text += "\n" + catalogFailure;
            var exact = RegistryCatalog.ExactCandidates(medicine.Text ?? "");
            if (exact.Count == 1 && candidates.ItemsSource is List<RegistryProduct> items)
                candidates.SelectedItem = items.FirstOrDefault(p => p.Id == exact[0].Id);
        }
        catch (Exception ex) { info.Text = "Wyszukiwanie nie powiodło się: " + ex.Message; }
    }
    static VerticalStackLayout Context(string label, Entry entry, string[] options)
    {
        var picker = new Picker { Title = "Wybierz lub wpisz poniżej", ItemsSource = options };
        picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedItem is string value) entry.Text = value; };
        var field = Ui.Field(label, entry); field.Children.Insert(1, picker); return field;
    }
    void InvalidateChoice() { confirmed.IsChecked = false; apply.IsEnabled = false; }
    async Task Fetch()
    {
        int requested = ProductId(); int generation = ++fetchGeneration; document = null; InvalidateChoice(); excerpt.Text = ""; result.Text = ""; id.IsEnabled = false;
        info.Text = "Pobieranie PDF z rejestru…";
        try
        {
            RegistryDocument loaded;
            try { loaded = await RegistryService.Fetch(requested, lifetime.Token); }
            catch (Exception)
            {
                if (lifetime.IsCancellationRequested) return;
                var cached = RegistryService.Cached(requested);
                if (cached == null || !await DisplayAlertAsync("Brak aktualnego pobrania", $"Dostępna jest kopia pobrana {cached.DownloadedUtc.ToLocalTime():g}. Nie potwierdzono jej aktualności. Użyć do ręcznej weryfikacji?", "Użyj kopii", "Anuluj")) throw;
                loaded = cached;
            }
            if (lifetime.IsCancellationRequested || generation != fetchGeneration) return;
            document = loaded;
            result.Text = loaded.WithdrawalSection;
            excerpt.Text = "IDENTYFIKACJA Z PDF:\n" + loaded.ProductIdentity + "\n\nOKRESY KARENCJI:\n" + loaded.WithdrawalSection;
            info.Text = $"RPL ID {loaded.Id} | pobrano {loaded.DownloadedUtc.ToLocalTime():g}\n{loaded.Url}\nKarencję wpisano automatycznie. Data pobrania nie jest datą aktualizacji ChPL. Sprawdź produkt i leczenie.";
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) info.Text = "Nie pobrano danych: " + ex.Message; }
        finally { if (generation == fetchGeneration) id.IsEnabled = true; }
    }
    async Task Close() { completion.TrySetResult(null); await Navigation.PopModalAsync(); }
    protected override void OnDisappearing() { lifetime.Cancel(); completion.TrySetResult(null); base.OnDisappearing(); }
}
