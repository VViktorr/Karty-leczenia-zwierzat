using Microsoft.Maui.Controls.Xaml;

namespace KartyLeczenia;
public sealed class App : Application
{
    public App()
    {
        Resources = new ResourceDictionary().LoadFromXaml("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
                <Style TargetType="ContentPage" ApplyToDerivedTypes="True">
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F1F5F9, Dark=#0F172A}" />
                </Style>
                <Style TargetType="Label">
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                </Style>
                <Style TargetType="Button">
                    <Setter Property="CornerRadius" Value="2" />
                    <Setter Property="Padding" Value="12,4" />
                    <Setter Property="MinimumHeightRequest" Value="30" />
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#E2E8F0, Dark=#2B3A50}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                </Style>
                <Style TargetType="Entry">
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F8FAFC, Dark=#1E293B}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                    <Setter Property="PlaceholderColor" Value="{AppThemeBinding Light=#64748B, Dark=#94A3B8}" />
                    <Setter Property="MinimumHeightRequest" Value="32" />
                </Style>
                <Style TargetType="Editor">
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F8FAFC, Dark=#1E293B}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                </Style>
                <Style TargetType="Picker">
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F8FAFC, Dark=#1E293B}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                    <Setter Property="TitleColor" Value="{AppThemeBinding Light=#64748B, Dark=#94A3B8}" />
                </Style>
            </ResourceDictionary>
            """);
        ApplyTheme(UiPreferencesStore.Load().Theme);
    }
    public static void ApplyTheme(ThemeMode mode)
    {
        if (Application.Current != null) Application.Current.UserAppTheme = mode switch { ThemeMode.Light => AppTheme.Light, ThemeMode.Dark => AppTheme.Dark, _ => AppTheme.Unspecified };
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
        var connectionPage = new Views.ConnectionPage(state);
        connection.Items.Add(new ShellContent { Content = connectionPage });
        var cards = new Tab { Title = "Karta leczenia", Route = "cards" };
        cards.Items.Add(new ShellContent { Content = new Views.CardPage(state) }); tabs.Items.Add(cards);
        var prices = new Tab { Title = "Cennik leków", Route = "medicine-prices" };
        prices.Items.Add(new ShellContent { Content = new Views.MedicineStockPage(state, prices: true) });
        tabs.Items.Add(prices);
        var expiry = new Tab { Title = "Kontrola terminów ważności", Route = "medicine-expiry" };
        expiry.Items.Add(new ShellContent { Content = new Views.MedicineStockPage(state, prices: false) });
        tabs.Items.Add(expiry);
        var registers = new Tab { Title = "Rejestry leków", Route = "medicine-registers" };
        registers.Items.Add(new ShellContent { ContentTemplate = new DataTemplate(() => new Views.MedicineRegistersPage()) });
        tabs.Items.Add(registers);
        tabs.Items.Add(connection);
        tabs.CurrentItem = cards;
        Items.Add(tabs);
        CurrentItem = tabs;
        Loaded += async (_, _) => await connectionPage.InitializeAsync();
    }
}
