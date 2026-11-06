namespace KartyLeczenia.Views;

public sealed class UpdRegistryPage : ContentPage
{
    readonly Entry query = new(), link = new(), species = new(), route = new();
    readonly Picker products = new() { Title = "Wybierz właściwy produkt, moc i postać" };
    readonly Editor identity = new() { IsReadOnly = true, HeightRequest = 100 };
    readonly Editor text = new() { HeightRequest = 260 };
    readonly Label info = Ui.Note("Wyszukaj produkt w UPD. Karencja zostanie wpisana po pobraniu źródła.");
    readonly CheckBox confirm = new();
    readonly Button apply = new() { Text = "Zastosuj do pozycji", IsEnabled = false };
    readonly TaskCompletionSource<RegistrySelection?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource lifetime = new();
    RegistryDocument? document;
    int generation;
    public Task<RegistrySelection?> Completion => completion.Task;
    public UpdRegistryPage(string product, string? mapped, int? rplMapped)
    {
        Title = "Karencja — UPD"; query.Text = product; link.Text = mapped ?? "";
        var panel = new VerticalStackLayout { Padding = 24, Spacing = 10, MaximumWidthRequest = 1050 };
        panel.Add(Ui.Heading("Karencja z unijnej bazy UPD"));
        panel.Add(Ui.Note("Źródło główne: UPD. Wybierz produkt dopuszczony w Polsce i sprawdź nazwę, moc, postać oraz kontekst leczenia. Pierwsze dopasowanie zapamiętamy; kolejne karty pobiorą aktualną karencję automatycznie. Znane terminy są tłumaczone słownikowo, oryginał pozostaje obok. Każde pole karencji możesz poprawić przed wydrukiem."));
        panel.Add(Ui.Field("Nazwa leku", query));
        panel.Add(Ui.Action("Wyszukaj w UPD", Search));
        panel.Add(Ui.Action("Otwórz wyszukiwarkę UPD w przeglądarce", () => Launcher.Default.OpenAsync("https://medicines.health.europa.eu/veterinary/pl/search-medicines")));
        panel.Add(products);
        panel.Add(Ui.Field("Link do produktu UPD lub 12-cyfrowe ID", link));
        panel.Add(Ui.Action("Pobierz karencję", Fetch));
        panel.Add(Ui.Action("Otwórz produkt w UPD", () => Launcher.Default.OpenAsync(UpdService.Url(link.Text ?? ""))));
        panel.Add(info); panel.Add(identity);
        panel.Add(Ui.Field("Gatunek — automatycznie przy jednej możliwości", species));
        panel.Add(Ui.Field("Droga podania — automatycznie przy jednej możliwości", route)); panel.Add(Ui.Field("Karencja — sprawdź i ewentualnie popraw", text));
        panel.Add(new HorizontalStackLayout { Children = { confirm, new Label { Text = "Potwierdzam produkt, moc, postać i zgodność karencji z leczeniem.", VerticalOptions = LayoutOptions.Center } } });
        panel.Add(apply);
        panel.Add(Ui.Action("Użyj zapasowego źródła RPL…", async () =>
        {
            var page = new RegistryPage(product, rplMapped); await Navigation.PushModalAsync(page);
            var result = await page.Completion;
            if (result != null) { completion.TrySetResult(result); await Navigation.PopModalAsync(); }
        }));
        panel.Add(Ui.Action("Anuluj", async () => { completion.TrySetResult(null); await Navigation.PopModalAsync(); }));
        Content = new ScrollView { Content = panel };
        link.TextChanged += (_, _) => { generation++; document = null; identity.Text = ""; text.Text = ""; species.Text = ""; route.Text = ""; Invalidate(); };
        species.TextChanged += (_, _) => Invalidate(); route.TextChanged += (_, _) => Invalidate();
        query.TextChanged += (_, _) => Invalidate(); text.TextChanged += (_, _) => Invalidate();
        confirm.CheckedChanged += (_, e) => apply.IsEnabled = e.Value && document != null && !string.IsNullOrWhiteSpace(text.Text);
        products.SelectedIndexChanged += async (_, _) => { if (products.SelectedItem is UpdProduct item) { link.Text = item.Id; await Fetch(); } };
        apply.Clicked += async (_, _) =>
        {
            if (document == null || !apply.IsEnabled) return;
            completion.TrySetResult(new(document, species.Text?.Trim() ?? "", route.Text?.Trim() ?? "", "", text.Text.Trim(), document.ProductIdentity));
            await Navigation.PopModalAsync();
        };
        bool loaded = false;
        Loaded += async (_, _) => { if (loaded) return; loaded = true; if (mapped != null) await Fetch(); else await Search(); };
    }
    void Invalidate() { confirm.IsChecked = false; apply.IsEnabled = false; }
    async Task Search()
    {
        info.Text = "Wyszukiwanie produktów w UPD…";
        try
        {
            var found = await UpdService.Search(query.Text ?? "", lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            products.ItemsSource = found;
            info.Text = found.Count == 0 ? "Brak wyników. Spróbuj krótszej nazwy lub wklej link UPD. Możesz jawnie wybrać zapasowy RPL." : $"Wyniki: {found.Count}. Wybierz właściwy produkt — nazwa sama nie potwierdza zgodności.";
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) info.Text = "Nie udało się wyszukać w UPD: " + ex.Message; }
    }
    async Task Fetch()
    {
        int requested = ++generation; document = null; Invalidate(); text.Text = ""; identity.Text = "";
        info.Text = "Pobieranie aktualnej karencji z publicznej strony UPD…";
        try
        {
            var result = await UpdService.Fetch(link.Text ?? "", lifetime.Token);
            if (lifetime.IsCancellationRequested || requested != generation) return;
            document = result; species.Text = result.SuggestedSpecies; route.Text = result.SuggestedRoute; identity.Text = result.ProductIdentity; text.Text = result.WithdrawalSection;
            info.Text = $"{result.Url}\nPobrano {result.DownloadedUtc.ToLocalTime():g}. Sprawdź dopasowanie i leczenie. Data pobrania nie jest datą aktualizacji źródła.";
        }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested && requested == generation) info.Text = "Nie pobrano karencji: " + ex.Message; }
    }
    protected override void OnDisappearing()
    {
        // A secondary modal (RPL) also hides this page; cancel only when this page is removed.
        base.OnDisappearing();
        if (!Navigation.ModalStack.Contains(this)) { lifetime.Cancel(); completion.TrySetResult(null); }
    }
}
