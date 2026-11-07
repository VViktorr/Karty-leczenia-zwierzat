namespace KartyLeczenia.Views;

public sealed class ConnectionPage : ContentPage
{
    readonly AppState state;
    readonly Entry clinicName = new();
    readonly Editor clinicAddress = new() { HeightRequest = 90 };
    readonly Entry clinicDoctor = new();
    readonly Entry server = new() { Placeholder = @"KOMPUTER\INSERTGT lub 192.168.1.10,1433" };
    readonly Entry database = new() { Placeholder = "Wybierz podmiot poniżej lub wpisz nazwę bazy" };
    readonly Entry login = new() { Placeholder = "Login SQL, nie operator Subiekta" };
    readonly Entry password = new() { IsPassword = true };
    readonly Entry group = new() { Text = "Leki" };
    readonly Switch windows = new() { IsToggled = true }, trust = new(), remember = new(), show = new();
    readonly Picker companies = new() { Title = "Podmioty na serwerze" }, groups = new() { Title = "Grupy leków" };
    readonly Picker warehouses = new() { Title = "Wybierz magazyn" };
    int? warehouseId;
    readonly Label status = Ui.Note("Krok 1 z 3: wpisz serwer SQL. Znajdziesz go w oknie logowania Subiekta.");
    readonly ActivityIndicator progress = new();
    readonly Button discover = new() { Text = "Odśwież podmioty" }, test = new() { Text = "Test połączenia" }, save = new() { Text = "Zapisz ustawienia", IsEnabled = false }, next = new() { Text = "Zapisz i przejdź do kart", IsEnabled = false }, cancel = new() { Text = "Anuluj", IsEnabled = false };
    readonly SubiektConnectionService service = new();
    readonly ConnectionSettingsStore store = new();
    CancellationTokenSource? work, debounce;
    bool changing, initialized;
    ConnectionOptions Options => new(server.Text?.Trim() ?? "", database.Text?.Trim() ?? "", windows.IsToggled, login.Text?.Trim() ?? "", password.Text ?? "", trust.IsToggled, group.Text?.Trim() ?? "Leki", warehouseId);

    public ConnectionPage(AppState appState)
    {
        state = appState; Title = "Ustawienia";
        var panel = new VerticalStackLayout { Spacing = 12, MaximumWidthRequest = 850, HorizontalOptions = LayoutOptions.Fill, Padding = 28 };
        clinicName.Text = state.Clinic.Name; clinicAddress.Text = state.Clinic.Address; clinicDoctor.Text = state.Clinic.Doctor;
        var clinic = new VerticalStackLayout { Spacing = 12 };
        clinic.Add(Ui.Heading("Dane lecznicy / podmiotu"));
        clinic.Add(Ui.Note("Stałe dane do nowych kart. Dane właściciela zwierząt nadal pochodzą z wybranego dokumentu."));
        clinic.Add(Ui.Field("Nazwa lecznicy / podmiotu", clinicName));
        clinic.Add(Ui.Field("Adres", clinicAddress));
        clinic.Add(Ui.Field("Lekarz i numer prawa wykonywania zawodu", clinicDoctor));
        clinic.Add(Ui.Action("Zapisz dane do kart", () =>
        {
            var value = new ClinicSettings(clinicName.Text?.Trim() ?? "", clinicAddress.Text?.Trim() ?? "", clinicDoctor.Text?.Trim() ?? "");
            ClinicSettingsStore.Save(value); state.Clinic = value; status.Text = "Zapisano dane lecznicy i lekarza do nowych kart.";
            return Task.CompletedTask;
        }));
        panel.Add(Ui.Card(clinic));
        var appearance = new VerticalStackLayout { Spacing = 10 };
        appearance.Add(Ui.Heading("Wygląd i diagnostyka"));
        var theme = new Picker { ItemsSource = new[] { "Zgodnie z systemem", "Jasny", "Ciemny" } };
        theme.SelectedIndex = (int)UiPreferencesStore.Load().Theme;
        appearance.Add(Ui.Field("Motyw aplikacji", theme));
        theme.SelectedIndexChanged += (_, _) =>
        {
            if (theme.SelectedIndex < 0) return;
            try
            {
                var mode = (ThemeMode)theme.SelectedIndex;
                UiPreferencesStore.Save(new(mode)); App.ApplyTheme(mode);
                status.Text = "Zapisano motyw aplikacji.";
            }
            catch (Exception ex) { CrashLog.Write("Appearance.Save", ex); status.Text = "Nie zapisano motywu: " + ex.Message; }
        };
        appearance.Add(Ui.Note("Lokalny dziennik błędów zapisuje wyjątki i przebieg pobierania dokumentu. Nie jest wysyłany automatycznie."));
        appearance.Add(Ui.Action("Otwórz folder crashlogów", () =>
        {
            Directory.CreateDirectory(CrashLog.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = CrashLog.Folder, UseShellExecute = true });
            return Task.CompletedTask;
        }));
        panel.Add(Ui.Card(appearance));
        panel.Add(Ui.Heading("Połączenie SQL — Subiekt GT"));
        panel.Add(Ui.Note("1. Serwer i logowanie   →   2. Podmiot, grupa leków i magazyn   →   3. Test i zapis"));
        panel.Add(Ui.Field("Serwer SQL", server));
        panel.Add(Ui.Note(@"Na tym komputerze np. .\INSERTGT. W sieci: nazwa lub adres komputera oraz instancja albo port."));
        panel.Add(Toggle("Uwierzytelnianie Windows", windows));
        panel.Add(Ui.Field("Login SQL", login)); panel.Add(Ui.Field("Hasło SQL", password));
        panel.Add(Toggle("Pokaż hasło", show)); panel.Add(Toggle("Zapamiętaj hasło dla mojego konta Windows", remember));
        panel.Add(Toggle("Zaufaj certyfikatowi znanego serwera w mojej sieci", trust));
        panel.Add(new HorizontalStackLayout { Spacing = 10, Children = { discover, cancel, progress } });
        panel.Add(Ui.Field("Wybierz podmiot", companies)); panel.Add(Ui.Field("Nazwa bazy podmiotu (można wpisać ręcznie)", database));
        panel.Add(Ui.Note("Niektóre konta nie mogą wyświetlać listy baz. Wtedy wpisz nazwę bazy i użyj testu."));
        panel.Add(Ui.Field("Grupa towarów — tylko leki", group)); panel.Add(groups);
        panel.Add(Ui.Field("Magazyn dla całej aplikacji", warehouses));
        panel.Add(Ui.Note("Wykonaj test połączenia, aby wczytać magazyny wybranego podmiotu."));
        panel.Add(new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { test, save, next } });
        panel.Add(status);
        panel.Add(Ui.Note("Połączenie wymaga konta tylko do odczytu. Test niczego nie zmienia w Subiekcie. Opcjonalnie zapamiętane hasło jest szyfrowane dla bieżącego konta Windows."));
        Content = new ScrollView { Content = panel };
        foreach (var entry in new[] { server, database, login, password, group }) entry.TextChanged += (_, _) => Changed();
        database.TextChanged += (_, _) => { if (!changing) ResetWarehouses(); };
        warehouses.SelectedIndexChanged += (_, _) =>
        {
            if (changing) return;
            warehouseId = (warehouses.SelectedItem as MedicineWarehouse)?.Id;
            Changed();
        };
        server.TextChanged += (_, _) => { if (!changing) { ResetCompanies(); ScheduleDiscovery(); } };
        login.TextChanged += (_, _) => { if (!changing) ResetCompanies(); };
        password.Unfocused += (_, _) => ScheduleDiscovery(); server.Unfocused += (_, _) => ScheduleDiscovery();
        windows.Toggled += (_, _) => { Authentication(); Changed(); if (!changing) { ResetCompanies(); ScheduleDiscovery(); } };
        trust.Toggled += (_, _) => { Changed(); if (!changing) ScheduleDiscovery(); };
        show.Toggled += (_, _) => password.IsPassword = !show.IsToggled;
        companies.SelectedIndexChanged += (_, _) => { if (companies.SelectedItem is CompanyDatabase company) database.Text = company.Database; };
        groups.SelectedIndexChanged += (_, _) => { if (groups.SelectedItem is string value) group.Text = value; };
        discover.Clicked += async (_, _) => await Discover(); test.Clicked += async (_, _) => await Test();
        save.Clicked += async (_, _) => await Save(false); next.Clicked += async (_, _) => await Save(true);
        cancel.Clicked += (_, _) => work?.Cancel();
        Loaded += async (_, _) => await InitializeAsync();
        Authentication(); UpdateButtons();
    }
    public async Task InitializeAsync()
    {
            if (initialized) return; initialized = true;
            changing = true;
            var loaded = store.Load();
            if (loaded.Options is { } value)
            {
                server.Text = value.Server; database.Text = value.Database; login.Text = value.UserName;
                password.Text = value.Password; CrashLog.RegisterSecret(value.Password); windows.IsToggled = value.WindowsAuthentication; trust.IsToggled = value.TrustCertificate;
                remember.IsToggled = loaded.Remembered; group.Text = value.DrugGroup; warehouseId = value.WarehouseId;
            }
            changing = false; Authentication();
            if (loaded.Warning != null) status.Text = loaded.Warning;
            if (loaded.Options != null && (windows.IsToggled || !string.IsNullOrEmpty(password.Text))) await Test();
        
    }
    static HorizontalStackLayout Toggle(string title, Switch toggle) => new() { Spacing = 12, Children = { toggle, new Label { Text = title, VerticalOptions = LayoutOptions.Center } } };
    void Authentication() { login.IsEnabled = password.IsEnabled = remember.IsEnabled = show.IsEnabled = !windows.IsToggled; if (windows.IsToggled) show.IsToggled = false; }
    void Changed()
    {
        if (changing) return;
        work?.Cancel(); debounce?.Cancel(); state.ValidatedConnection = null;
        status.Text = "Ustawienia zmienione — wykonaj test aktualnego połączenia."; UpdateButtons();
    }
    void ResetCompanies() { changing = true; companies.ItemsSource = null; database.Text = ""; ResetWarehouses(); changing = false; }
    void ResetWarehouses()
    {
        bool wasChanging = changing;
        changing = true;
        warehouseId = null;
        warehouses.ItemsSource = null;
        changing = wasChanging;
    }
    void UpdateButtons() { bool busy = work != null; progress.IsRunning = busy; discover.IsEnabled = test.IsEnabled = !busy; cancel.IsEnabled = busy; save.IsEnabled = next.IsEnabled = !busy && state.ValidatedConnection == Options; }
    async void ScheduleDiscovery()
    {
        debounce?.Cancel();
        if (changing || string.IsNullOrWhiteSpace(server.Text) || !windows.IsToggled && (string.IsNullOrWhiteSpace(login.Text) || string.IsNullOrEmpty(password.Text))) return;
        using var delay = new CancellationTokenSource(); debounce = delay;
        try { await Task.Delay(1200, delay.Token); await Discover(); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(debounce, delay)) debounce = null; }
    }
    async Task Run(string message, Func<ConnectionOptions, CancellationToken, Task> action)
    {
        debounce?.Cancel(); work?.Cancel();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); work = cts;
        var options = Options; CrashLog.RegisterSecret(options.Password); status.Text = message; UpdateButtons();
        try { await action(options, cts.Token); }
        catch (OperationCanceledException) { if (options == Options) status.Text = "Operacja anulowana lub przekroczono czas oczekiwania."; }
        catch (Exception ex) { if (options == Options) status.Text = SubiektConnectionService.FriendlyError(ex); }
        finally { if (ReferenceEquals(work, cts)) work = null; UpdateButtons(); }
    }
    Task Discover() => Run("Wyszukiwanie dostępnych podmiotów…", async (options, token) =>
    {
        var result = await service.DiscoverAsync(options, token);
        if (token.IsCancellationRequested || options != Options) return;
        changing = true;
        companies.ItemsSource = result.Companies.ToList();
        var selected = result.Companies.FirstOrDefault(x => x.Database == options.Database);
        if (selected != null) companies.SelectedItem = selected;
        else if (result.Companies.Count == 1 && options.Database == "") { ResetWarehouses(); companies.SelectedIndex = 0; }
        changing = false; state.ValidatedConnection = null;
        status.Text = result.Companies.Count == 0 ? "Nie znaleziono widocznych podmiotów. Wpisz nazwę bazy ręcznie i użyj testu." : $"Krok 2 z 3: znaleziono {result.Companies.Count} podmiotów. Wybierz firmę i wykonaj test.";
    });
    Task Test() => Run("Sprawdzanie odczytu, uprawnień i grupy leków…", async (options, token) =>
    {
        state.ValidatedConnection = null;
        try
        {
            var result = await service.TestAsync(options, token);
            if (token.IsCancellationRequested || options != Options) return;
            changing = true;
            groups.ItemsSource = result.DrugGroups.ToList();
            warehouses.ItemsSource = result.Warehouses.ToList();
            var selected = result.Warehouses.FirstOrDefault(item => item.Id == options.WarehouseId);
            selected ??= result.Warehouses.Count == 1 ? result.Warehouses[0] : null;
            warehouses.SelectedItem = selected;
            warehouseId = selected?.Id;
            changing = false;
            if (selected is null)
            {
                status.Text = result.Warehouses.Count == 0
                    ? "W tej bazie nie znaleziono magazynów."
                    : "Wybierz magazyn, ponów test i zapisz ustawienia.";
                return;
            }
            state.ValidatedConnection = Options;
            status.Text = $"Krok 3 z 3: połączenie poprawne — {result.Company} [{options.Database}], magazyn {selected.Name}. Odczyt działa; nie wykryto badanych uprawnień zapisu. Zapisz ustawienia.";
        }
        catch (DrugGroupNotFoundException ex)
        {
            if (options != Options) return;
            changing = true; groups.ItemsSource = ex.Groups.ToList(); changing = false; status.Text = ex.Message;
        }
    });
    async Task Save(bool proceed)
    {
        try
        {
            if (state.ValidatedConnection != Options) throw new InvalidOperationException("Najpierw wykonaj test aktualnego połączenia.");
            var clinic = new ClinicSettings(clinicName.Text?.Trim() ?? "", clinicAddress.Text?.Trim() ?? "", clinicDoctor.Text?.Trim() ?? "");
            ClinicSettingsStore.Save(clinic); state.Clinic = clinic;
            store.Save(Options, remember.IsToggled); status.Text = "Zapisano ustawienia połączenia.";
            if (proceed) await Shell.Current.GoToAsync("//cards");
        }
        catch (Exception ex) { status.Text = "Nie zapisano ustawień: " + ex.Message; }
    }
}
