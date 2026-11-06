namespace KartyLeczenia.Views;

public sealed class RegistryPage : ContentPage
{
    readonly Entry id = new() { WidthRequest = 850 };
    readonly Entry medicine = new();
    readonly Editor excerpt = new() { IsReadOnly = true, HeightRequest = 270 };
    readonly Editor result = new() { HeightRequest = 110 };
    readonly Editor dosing = new() { HeightRequest = 170 };
    readonly Picker sourceRoutes = new() { Title = "Drogi podania rozpoznane w charakterystyce" };
    readonly Label contextNotice = Ui.Note("");
    bool updatingContext;
    string lastAutoWithdrawal = "", lastAutoDosing = "";
    readonly Entry species = new(), route = new(), food = new();
    readonly Label info = Ui.Note("Pobierz charakterystykę i sprawdź tożsamość leku.");
    readonly Picker candidates = new() { Title = "Wyniki wyszukiwania leków" };
    readonly CheckBox confirmed = new();
    readonly Button apply = new() { Text = "Zastosuj do pozycji", IsEnabled = false };
    readonly TaskCompletionSource<RegistrySelection?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource lifetime = new();
    RegistryDocument? document;
    readonly string initialSpecies, initialRoute;
    int fetchGeneration;
    CancellationTokenSource? lookupDelay;
    public Task<RegistrySelection?> Completion => completion.Task;

    public RegistryPage(string product, int? mapped, string currentSpecies = "", string currentRoute = "")
    {
        Title = "Karencja z rejestru RPL";
        initialSpecies = currentSpecies; initialRoute = currentRoute;
        medicine.Text = product;
        species.Text = string.IsNullOrWhiteSpace(currentSpecies) ? "Bydło" : currentSpecies;
        route.Text = currentRoute;
        var matches = RegistryCatalog.ExactCandidates(product);
        id.Text = (mapped ?? (matches.Count == 1 ? matches[0].Id : 0)).ToString();
        var panel = new VerticalStackLayout { Padding = 24, Spacing = 10, MaximumWidthRequest = 1050 };
        panel.Add(Ui.Heading("Karencja z oficjalnej charakterystyki"));
        panel.Add(Ui.Note("Porównaj nazwę, moc i postać leku z PDF. Karencja jest wpisywana automatycznie z pełnej sekcji PDF. Dopasowanie produktu wymaga sprawdzenia. Przy stosowaniu poza warunkami charakterystyki okres ustala lekarz."));
        panel.Add(Ui.Field("Nazwa produktu", medicine)); panel.Add(candidates);
        panel.Add(Ui.Field("ID RPL lub oficjalny link do charakterystyki", id));
        var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        buttons.Add(Ui.Action("Znajdź lek w katalogu", Search));
        buttons.Add(Ui.Action("Aktualizuj katalog RPL", async () => { await RegistryCatalog.UpdateAsync(true, lifetime.Token); await Search(); }));
        buttons.Add(Ui.Action("Otwórz źródło PDF", () => Launcher.Default.OpenAsync(RegistryService.Url(ProductId()))));
        buttons.Add(Ui.Action("Pobierz aktualną ChPL", Fetch)); panel.Add(buttons);
        panel.Add(info);
        excerpt.IsVisible = false;
        panel.Add(Ui.Action("Pokaż / ukryj pełne sekcje ChPL", () => { excerpt.IsVisible = !excerpt.IsVisible; return Task.CompletedTask; }));
        panel.Add(excerpt);
        panel.Add(Context("Gatunek", species, ["Bydło", "Świnie", "Konie", "Owce", "Kozy", "Drób"]));
        panel.Add(sourceRoutes);
        panel.Add(Ui.Field("Wybrana droga podania", route));
        panel.Add(contextNotice);
        panel.Add(Ui.Field("Dawkowanie, częstość i sposób podania z ChPL — sprawdź i ewentualnie popraw", dosing));
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
        Content = Ui.WithClose(this, new ScrollView { Content = panel }, Close);
        foreach (var entry in new[] { medicine, species, route, food }) entry.TextChanged += (_, _) => InvalidateChoice();
        result.TextChanged += (_, _) => InvalidateChoice();
        dosing.TextChanged += (_, _) => InvalidateChoice();
        species.TextChanged += (_, _) => RecomputeContext();
        route.TextChanged += (_, _) => RecomputeContext();
        sourceRoutes.SelectedIndexChanged += (_, _) => { if (sourceRoutes.SelectedItem is string value) route.Text = value; };
        id.TextChanged += (_, _) => { fetchGeneration++; document = null; excerpt.Text = ""; result.Text = ""; dosing.Text = ""; species.Text = string.IsNullOrWhiteSpace(currentSpecies) ? "Bydło" : currentSpecies; route.Text = currentRoute; InvalidateChoice(); };
        confirmed.CheckedChanged += (_, e) => apply.IsEnabled = e.Value && document != null && new[] { medicine.Text, result.Text }.All(s => !string.IsNullOrWhiteSpace(s));
        candidates.SelectedIndexChanged += async (_, _) =>
        {
            if (candidates.SelectedItem is RegistryProduct candidate)
            { id.Text = candidate.Id.ToString(); await Fetch(); }
        };
        medicine.TextChanged += async (_, _) =>
        {
            lookupDelay?.Cancel(); fetchGeneration++; document = null; excerpt.Text = ""; result.Text = ""; id.IsEnabled = true; id.Text = "0"; InvalidateChoice();
            using var delay = new CancellationTokenSource(); lookupDelay = delay;
            try { await Task.Delay(400, delay.Token); if (!lifetime.IsCancellationRequested) ShowCandidates(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!lifetime.IsCancellationRequested) info.Text = "Nie odczytano katalogu: " + ex.Message; }
            finally { if (ReferenceEquals(lookupDelay, delay)) lookupDelay = null; }
        };
        medicine.Completed += async (_, _) => await Search();
        bool firstLoad = true;
        Loaded += async (_, _) => { if (!firstLoad) return; firstLoad = false; if (id.Text != "0") await Fetch(); else await Search(); };
        apply.Clicked += async (_, _) =>
        {
            if (document == null || !apply.IsEnabled) return;
            completion.TrySetResult(new(document, species.Text?.Trim() ?? "", route.Text?.Trim() ?? "", food.Text?.Trim() ?? "", result.Text.Trim(), medicine.Text.Trim()) { Dosing = dosing.Text?.Trim() ?? "" });
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
            if (exact.Count == 0 && !System.Text.RegularExpressions.Regex.IsMatch(medicine.Text ?? "", @"\d+(?:[.,]\d+)?\s*(?:mg|g|µg|mcg|IU)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var named = RegistryCatalog.NameCandidates(medicine.Text ?? "");
                if (named.Count == 1) exact = named;
            }
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
            updatingContext = true;
            document = loaded;
            species.Text = !string.IsNullOrWhiteSpace(initialSpecies) ? initialSpecies : !string.IsNullOrWhiteSpace(loaded.SuggestedSpecies) ? loaded.SuggestedSpecies : "Bydło";
            route.Text = !string.IsNullOrWhiteSpace(initialRoute) ? initialRoute : loaded.SuggestedRoute;
            result.Text = ""; dosing.Text = ""; lastAutoWithdrawal = lastAutoDosing = "";
            updatingContext = false;
            RecomputeContext();
            excerpt.Text = "IDENTYFIKACJA Z PDF:\n" + loaded.ProductIdentity + "\n\nOKRESY KARENCJI:\n" + loaded.WithdrawalSection + "\n\nPEŁNE DAWKOWANIE I SPOSÓB PODANIA:\n" + loaded.DosingSection + "\n\nGATUNKI I OGRANICZENIA ŹRÓDŁA:\n" + loaded.TargetSpeciesSection;
            info.Text = $"RPL ID {loaded.Id} | pobrano {loaded.DownloadedUtc.ToLocalTime():g}\n{loaded.Url}\nDane są dobierane do gatunku i drogi podania. Data pobrania nie jest datą aktualizacji ChPL. Sprawdź produkt i leczenie.";
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) info.Text = "Nie pobrano danych: " + ex.Message; }
        finally { if (generation == fetchGeneration) id.IsEnabled = true; }
    }
    void RecomputeContext()
    {
        if (updatingContext || document == null) return;
        updatingContext = true;
        try
        {
            var context = TreatmentContextService.Select(document, species.Text ?? "", route.Text ?? "");
            sourceRoutes.ItemsSource = context.Routes;
            if (string.IsNullOrWhiteSpace(route.Text) && context.Route.Length > 0) route.Text = context.Route;
            bool manualWithdrawal = !string.IsNullOrWhiteSpace(result.Text) && result.Text != lastAutoWithdrawal;
            bool manualDosing = !string.IsNullOrWhiteSpace(dosing.Text) && dosing.Text != lastAutoDosing;
            if (!manualWithdrawal) { result.Text = context.Withdrawal; lastAutoWithdrawal = context.Withdrawal; }
            if (!manualDosing) { dosing.Text = context.Dosing; lastAutoDosing = context.Dosing; }
            contextNotice.Text = context.Notice + (manualWithdrawal || manualDosing ? " Zachowano ręcznie zmienione pola — sprawdź ich zgodność z nowym kontekstem." : "");
            InvalidateChoice();
        }
        finally { updatingContext = false; }
    }
    async Task Close() { completion.TrySetResult(null); await Navigation.PopModalAsync(); }
    protected override void OnDisappearing() { lookupDelay?.Cancel(); lifetime.Cancel(); completion.TrySetResult(null); base.OnDisappearing(); }
}
