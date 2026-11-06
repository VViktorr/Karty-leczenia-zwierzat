namespace KartyLeczenia;
public sealed class App : Application
{
    public App()
    {
        static AppThemeBinding Theme(string light, string dark) => new() { Light = Color.FromArgb(light), Dark = Color.FromArgb(dark) };
        Resources.Add(new Style(typeof(ContentPage)) { ApplyToDerivedTypes = true, Setters = { new Setter { Property = ContentPage.BackgroundColorProperty, Value = Theme("#F1F5F9", "#0F172A") } } });
        Resources.Add(new Style(typeof(Label)) { Setters = { new Setter { Property = Label.TextColorProperty, Value = Theme("#0F172A", "#F1F5F9") } } });
        Resources.Add(new Style(typeof(Button)) { Setters = {
            new Setter { Property = Button.CornerRadiusProperty, Value = 8 },
            new Setter { Property = Button.PaddingProperty, Value = new Thickness(16, 10) },
            new Setter { Property = Button.MinimumHeightRequestProperty, Value = 42d },
            new Setter { Property = Button.BackgroundColorProperty, Value = Theme("#E2E8F0", "#2B3A50") },
            new Setter { Property = Button.TextColorProperty, Value = Theme("#0F172A", "#F1F5F9") } } });
        foreach (var type in new[] { typeof(Entry), typeof(Editor), typeof(Picker) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Theme("#F8FAFC", "#1E293B") });
            if (type == typeof(Entry)) { style.Setters.Add(new Setter { Property = Entry.TextColorProperty, Value = Theme("#0F172A", "#F1F5F9") }); style.Setters.Add(new Setter { Property = Entry.PlaceholderColorProperty, Value = Theme("#64748B", "#94A3B8") }); style.Setters.Add(new Setter { Property = Entry.MinimumHeightRequestProperty, Value = 44d }); }
            if (type == typeof(Editor)) style.Setters.Add(new Setter { Property = Editor.TextColorProperty, Value = Theme("#0F172A", "#F1F5F9") });
            if (type == typeof(Picker)) { style.Setters.Add(new Setter { Property = Picker.TextColorProperty, Value = Theme("#0F172A", "#F1F5F9") }); style.Setters.Add(new Setter { Property = Picker.TitleColorProperty, Value = Theme("#64748B", "#94A3B8") }); }
            Resources.Add(style);
        }
    }
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell()) { Title = "Karty leczenia zwierząt", Width = 1400, Height = 920, MinimumWidth = 1050, MinimumHeight = 700 };
}
public sealed class AppState
{
    public ClinicSettings Clinic { get; set; } = ClinicSettingsStore.Load();
    public ConnectionOptions? ValidatedConnection { get; set; }
}
public sealed class AppShell : Shell
{
    public AppShell()
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        this.SetAppThemeColor(Shell.BackgroundColorProperty, Color.FromArgb("#FFFFFF"), Color.FromArgb("#172334"));
        this.SetAppThemeColor(Shell.ForegroundColorProperty, Color.FromArgb("#0F172A"), Color.FromArgb("#F1F5F9"));
        this.SetAppThemeColor(Shell.TitleColorProperty, Color.FromArgb("#0F172A"), Color.FromArgb("#F1F5F9"));
        var state = new AppState();
        var tabs = new TabBar();
        var connection = new Tab { Title = "Ustawienia", Route = "connection" };
        connection.Items.Add(new ShellContent { Content = new Views.ConnectionPage(state) }); tabs.Items.Add(connection);
        var cards = new Tab { Title = "Karta leczenia", Route = "cards" };
        cards.Items.Add(new ShellContent { Content = new Views.CardPage(state) }); tabs.Items.Add(cards);
        Items.Add(tabs);
    }
}
