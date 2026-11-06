namespace KartyLeczenia;

sealed partial class MainForm
{
    readonly ComboBox database = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 560 };
    readonly ComboBox drugGroup = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 350, Text = "Leki" };
    readonly CheckBox rememberPassword = new() { Text = "Zapamiętaj hasło dla mojego konta Windows", AutoSize = true };
    readonly CheckBox showPassword = new() { Text = "Pokaż hasło", AutoSize = true };
    readonly Label connectionStatus = new() { AutoSize = true, MaximumSize = new Size(760, 0), Text = "Krok 1 z 3: wpisz serwer SQL. Adres znajdziesz w oknie logowania Subiekta." };
    readonly Button discoverCompanies = new() { Text = "Odśwież podmioty", AutoSize = true };
    readonly Button testConnection = new() { Text = "Test połączenia", AutoSize = true };
    readonly Button saveConnection = new() { Text = "Zapisz ustawienia", AutoSize = true, Enabled = false };
    readonly Button saveAndContinue = new() { Text = "Zapisz i przejdź do kart", AutoSize = true, Enabled = false };
    readonly Button cancelConnection = new() { Text = "Anuluj sprawdzanie", AutoSize = true, Enabled = false };
    readonly System.Windows.Forms.Timer discoveryTimer = new() { Interval = 1200 };
    readonly SubiektConnectionService connectionService = new();
    readonly ConnectionSettingsStore connectionStore = new();
    CancellationTokenSource? connectionWork;
    ConnectionOptions? validatedConnection;
    bool restoringConnection;
    bool connectionBusy;

    string DatabaseName => database.SelectedItem is CompanyDatabase company && database.Text == company.ToString()
        ? company.Database : database.Text.Trim();
    ConnectionOptions CurrentConnection() => new(server.Text.Trim(), DatabaseName, windows.Checked, login.Text.Trim(), password.Text, trust.Checked, drugGroup.Text.Trim());

    void BuildConnectionPage(TabPage page, TabControl tabs, TabPage card)
    {
        page.Padding = new Padding(24);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        page.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Połącz z Subiektem GT", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) });
        layout.Controls.Add(new Label { Text = "1. Wpisz serwer   →   2. Wybierz podmiot   →   3. Przetestuj i zapisz", AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
        AddInput(layout, "Serwer SQL", server);
        server.Width = 560; server.PlaceholderText = @"np. KOMPUTER\INSERTGT lub 192.168.1.10,1433";
        layout.Controls.Add(new Label { Text = @"Na tym komputerze może to być .\INSERTGT. W sieci wpisz nazwę/adres serwera i jego instancję lub port.", AutoSize = true, MaximumSize = new Size(760, 0) });
        layout.Controls.Add(windows);
        AddInput(layout, "Login SQL — nie login operatora Subiekta", login);
        AddInput(layout, "Hasło SQL", password);
        var passwordButtons = new FlowLayoutPanel { AutoSize = true };
        passwordButtons.Controls.AddRange([showPassword, rememberPassword]); layout.Controls.Add(passwordButtons);
        layout.Controls.Add(trust);
        layout.Controls.Add(new Label { Text = "Zaufanie certyfikatowi włączaj tylko dla znanego serwera w swojej sieci.", AutoSize = true });
        var dbButtons = new FlowLayoutPanel { AutoSize = true };
        dbButtons.Controls.Add(discoverCompanies); dbButtons.Controls.Add(cancelConnection); layout.Controls.Add(dbButtons);
        layout.Controls.Add(new Label { Text = "Podmiot Subiekta (baza firmy)", AutoSize = true });
        layout.Controls.Add(database);
        layout.Controls.Add(new Label { Text = "Lista pokazuje dostępne podmioty. Jeśli konto nie widzi listy baz, wpisz nazwę bazy ręcznie i użyj testu połączenia.", AutoSize = true, MaximumSize = new Size(760, 0) });
        layout.Controls.Add(new Label { Text = "Grupa towarów zawierająca leki", AutoSize = true });
        layout.Controls.Add(drugGroup);
        layout.Controls.Add(new Label { Text = "Domyślnie: Leki. Lista grup pojawi się po teście. Z dokumentów pobierane będą tylko towary z wybranej grupy.", AutoSize = true, MaximumSize = new Size(760, 0) });
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 12) };
        actions.Controls.AddRange([testConnection, saveConnection, saveAndContinue]); layout.Controls.Add(actions);
        layout.Controls.Add(connectionStatus);
        layout.Controls.Add(new Label { Text = "Potrzebne jest osobne konto tylko do odczytu. Test nie zmienia bazy. Ustawienia są zapisywane lokalnie; zapamiętane hasło jest szyfrowane dla bieżącego użytkownika Windows.", AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 16, 0, 0) });

        void Changed(object? sender, EventArgs e)
        {
            if (restoringConnection) return;
            validatedConnection = null; connectionWork?.Cancel(); discoveryTimer.Stop();
            connectionStatus.ForeColor = SystemColors.ControlText;
            connectionStatus.Text = "Ustawienia zmienione — wykonaj test przed zapisaniem i pobieraniem dokumentów.";
            UpdateConnectionActions();
        }
        foreach (Control control in new Control[] { server, login, password, database, drugGroup }) control.TextChanged += Changed;
        foreach (var control in new[] { windows, trust }) control.CheckedChanged += Changed;
        void InvalidateCompanies(object? sender, EventArgs e)
        {
            if (restoringConnection) return;
            restoringConnection = true;
            try { database.Items.Clear(); database.Text = ""; drugGroup.Items.Clear(); }
            finally { restoringConnection = false; }
            ScheduleDiscovery();
        }
        server.TextChanged += InvalidateCompanies;
        windows.CheckedChanged += (_, _) => { UpdateAuthenticationFields(); InvalidateCompanies(null, EventArgs.Empty); };
        trust.CheckedChanged += InvalidateCompanies;
        login.TextChanged += InvalidateCompanies;
        password.TextChanged += (_, _) =>
        {
            if (!restoringConnection)
            {
                restoringConnection = true;
                try { database.Items.Clear(); database.Text = ""; }
                finally { restoringConnection = false; }
            }
        };
        password.Leave += (_, _) => ScheduleDiscovery();
        server.Leave += (_, _) => ScheduleDiscovery();
        showPassword.CheckedChanged += (_, _) => password.UseSystemPasswordChar = !showPassword.Checked;
        discoveryTimer.Tick += async (_, _) => { discoveryTimer.Stop(); await DiscoverCompaniesAsync(); };
        discoverCompanies.Click += async (_, _) => { discoveryTimer.Stop(); await DiscoverCompaniesAsync(); };
        testConnection.Click += async (_, _) => { discoveryTimer.Stop(); await TestConnectionAsync(); };
        cancelConnection.Click += (_, _) => connectionWork?.Cancel();
        saveConnection.Click += (_, _) => SaveConnectionSettings();
        saveAndContinue.Click += (_, _) => { if (SaveConnectionSettings()) tabs.SelectedTab = card; };
        rememberPassword.CheckedChanged += (_, _) => UpdateConnectionActions();

        restoringConnection = true;
        var saved = connectionStore.Load();
        if (saved.Options is { } options)
        {
            server.Text = options.Server; database.Text = options.Database; windows.Checked = options.WindowsAuthentication;
            login.Text = options.UserName; password.Text = options.Password; trust.Checked = options.TrustCertificate;
            drugGroup.Text = options.DrugGroup; rememberPassword.Checked = saved.Remembered;
            connectionStatus.Text = saved.Warning ?? "Wczytano ustawienia. Sprawdź połączenie, aby rozpocząć pracę.";
        }
        else if (saved.Warning != null) connectionStatus.Text = saved.Warning;
        restoringConnection = false;
        UpdateAuthenticationFields(); UpdateConnectionActions();
        Shown += async (_, _) =>
        {
            if (saved.Options != null && (windows.Checked || password.Text.Length > 0)) await TestConnectionAsync();
            else server.Focus();
        };
        FormClosed += (_, _) => { discoveryTimer.Stop(); discoveryTimer.Dispose(); connectionWork?.Cancel(); };
    }

    void ScheduleDiscovery()
    {
        discoveryTimer.Stop();
        if (!restoringConnection && server.Text.Trim().Length > 0 && (windows.Checked || login.Text.Trim().Length > 0 && password.Text.Length > 0)) discoveryTimer.Start();
    }
    void UpdateAuthenticationFields()
    {
        login.Enabled = password.Enabled = showPassword.Enabled = rememberPassword.Enabled = !windows.Checked;
        if (windows.Checked) showPassword.Checked = false;
    }
    void UpdateConnectionActions()
    {
        bool valid = validatedConnection != null && validatedConnection == CurrentConnection() && !connectionBusy;
        saveConnection.Enabled = saveAndContinue.Enabled = valid;
        testConnection.Enabled = discoverCompanies.Enabled = !connectionBusy;
        cancelConnection.Enabled = connectionBusy;
        UseWaitCursor = connectionBusy;
    }
    CancellationTokenSource BeginConnectionWork(string message)
    {
        connectionWork?.Cancel();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        connectionWork = cts; connectionBusy = true;
        connectionStatus.ForeColor = SystemColors.ControlText; connectionStatus.Text = message;
        UpdateConnectionActions(); return cts;
    }
    void EndConnectionWork(CancellationTokenSource cts)
    {
        if (ReferenceEquals(connectionWork, cts))
        { connectionWork = null; connectionBusy = false; if (!IsDisposed) UpdateConnectionActions(); }
        cts.Dispose();
    }
    bool IsCurrent(ConnectionOptions options, CancellationTokenSource cts) => !IsDisposed && !cts.IsCancellationRequested && options == CurrentConnection();

    async Task DiscoverCompaniesAsync()
    {
        var options = CurrentConnection();
        if (string.IsNullOrWhiteSpace(options.Server)) { connectionStatus.Text = "Wpisz adres serwera SQL."; return; }
        using var cts = BeginConnectionWork("Łączenie z serwerem i wyszukiwanie dostępnych podmiotów…");
        try
        {
            var result = await connectionService.DiscoverAsync(options, cts.Token);
            if (!IsCurrent(options, cts)) return;
            restoringConnection = true;
            try
            {
                database.Items.Clear();
                foreach (var company in result.Companies) database.Items.Add(company);
                var previous = result.Companies.FirstOrDefault(c => c.Database.Equals(options.Database, StringComparison.OrdinalIgnoreCase));
                if (previous != null) database.SelectedItem = previous;
                else if (result.Companies.Count == 1 && string.IsNullOrWhiteSpace(options.Database)) database.SelectedIndex = 0;
                else database.Text = options.Database;
            }
            finally { restoringConnection = false; }
            validatedConnection = null;
            connectionStatus.Text = result.Companies.Count == 0
                ? "Serwer odpowiada, ale nie znaleziono widocznych podmiotów Subiekta. Wpisz nazwę bazy ręcznie i wykonaj test."
                : $"Krok 2 z 3: znaleziono podmioty: {result.Companies.Count}. Wybierz właściwy i kliknij Test połączenia.";
            if (result.Skipped > 0) connectionStatus.Text += $" Pominięto inne lub niedostępne bazy: {result.Skipped}.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed && options == CurrentConnection())
            { connectionStatus.ForeColor = Color.Firebrick; connectionStatus.Text = SubiektConnectionService.FriendlyError(ex) + " Możesz wpisać nazwę bazy ręcznie i wykonać test."; }
        }
        finally { EndConnectionWork(cts); }
    }
    async Task TestConnectionAsync()
    {
        var options = CurrentConnection(); validatedConnection = null;
        using var cts = BeginConnectionWork("Sprawdzanie dostępu do podmiotu, odczytu danych i uprawnień konta…");
        try
        {
            var result = await connectionService.TestAsync(options, cts.Token);
            if (!IsCurrent(options, cts)) return;
            SetDrugGroups(result.DrugGroups, options.DrugGroup);
            validatedConnection = CurrentConnection();
            connectionStatus.ForeColor = Color.DarkGreen;
            connectionStatus.Text = $"Krok 3 z 3: połączenie poprawne — {result.Company} [{options.Database}]. Odczyt wymaganych tabel działa; nie wykryto uprawnień do zapisu tabel ani wskazanych zmian struktury. Kliknij Zapisz i przejdź do kart.";
        }
        catch (DrugGroupNotFoundException ex)
        {
            if (IsCurrent(options, cts)) { SetDrugGroups(ex.Groups, options.DrugGroup); connectionStatus.ForeColor = Color.Firebrick; connectionStatus.Text = ex.Message; }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && options == CurrentConnection()) { connectionStatus.ForeColor = Color.Firebrick; connectionStatus.Text = SubiektConnectionService.FriendlyError(ex); }
        }
        finally { EndConnectionWork(cts); }
    }
    void SetDrugGroups(IReadOnlyList<string> groups, string current)
    {
        restoringConnection = true;
        try
        {
            drugGroup.Items.Clear(); foreach (string group in groups) drugGroup.Items.Add(group);
            drugGroup.Text = groups.FirstOrDefault(g => g.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? current;
        }
        finally { restoringConnection = false; }
    }
    bool SaveConnectionSettings()
    {
        if (validatedConnection is null || validatedConnection != CurrentConnection())
        { connectionStatus.Text = "Najpierw wykonaj test aktualnych ustawień."; return false; }
        try
        {
            connectionStore.Save(CurrentConnection(), rememberPassword.Checked);
            connectionStatus.ForeColor = Color.DarkGreen;
            connectionStatus.Text = "Zapisano ustawienia. Przy kolejnym uruchomieniu program je wczyta.";
            status.Text = "Połączenie gotowe. Wpisz numer faktury, paragonu lub WZ.";
            return true;
        }
        catch (Exception ex) { connectionStatus.ForeColor = Color.Firebrick; connectionStatus.Text = "Nie udało się zapisać ustawień: " + ex.Message; return false; }
    }
}
