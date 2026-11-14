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
                    <Setter Property="FontFamily" Value="{DynamicResource AppFont}" />
                    <Setter Property="FontSize" Value="{DynamicResource AppTextSize}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                </Style>
                <Style TargetType="Button">
                    <Setter Property="FontFamily" Value="{DynamicResource AppFont}" />
                    <Setter Property="FontSize" Value="{DynamicResource AppTextSize}" />
                    <Setter Property="CornerRadius" Value="2" />
                    <Setter Property="Padding" Value="12,4" />
                    <Setter Property="MinimumHeightRequest" Value="30" />
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#E2E8F0, Dark=#2B3A50}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                </Style>
                <Style TargetType="Entry">
                    <Setter Property="FontFamily" Value="{DynamicResource AppFont}" />
                    <Setter Property="FontSize" Value="{DynamicResource AppTextSize}" />
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F8FAFC, Dark=#1E293B}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                    <Setter Property="PlaceholderColor" Value="{AppThemeBinding Light=#64748B, Dark=#94A3B8}" />
                    <Setter Property="MinimumHeightRequest" Value="32" />
                </Style>
                <Style TargetType="Editor">
                    <Setter Property="FontFamily" Value="{DynamicResource AppFont}" />
                    <Setter Property="FontSize" Value="{DynamicResource AppTextSize}" />
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F8FAFC, Dark=#1E293B}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                </Style>
                <Style TargetType="Picker">
                    <Setter Property="FontFamily" Value="{DynamicResource AppFont}" />
                    <Setter Property="FontSize" Value="{DynamicResource AppTextSize}" />
                    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F8FAFC, Dark=#1E293B}" />
                    <Setter Property="TextColor" Value="{AppThemeBinding Light=#0F172A, Dark=#F1F5F9}" />
                    <Setter Property="TitleColor" Value="{AppThemeBinding Light=#64748B, Dark=#94A3B8}" />
                </Style>
            </ResourceDictionary>
            """);
        Views.PersonalizationPage.Apply(Resources);
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
        this.SetAppThemeColor(Shell.TabBarTitleColorProperty, Color.FromArgb("#174EA6"), Color.FromArgb("#93C5FD"));
        this.SetAppThemeColor(Shell.TabBarUnselectedColorProperty, Color.FromArgb("#475569"), Color.FromArgb("#CBD5E1"));
        var state = new AppState();
        var tabs = new TabBar();
        var connection = new Tab { Title = "Ustawienia", Route = "connection" };
        var connectionPage = new Views.ConnectionPage(state);
        connection.Items.Add(new ShellContent { Title = "Połączenie i ustawienia", Content = connectionPage });
        connection.Items.Add(new ShellContent { Title = "Personalizacja", ContentTemplate = new DataTemplate(() => new Views.PersonalizationPage()) });
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
#if WINDOWS
        Loaded += (_, _) =>
        {
            if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement root)
                StyleMainTabs(root, tabs.Items.Select(tab => tab.Title).ToHashSet());
        };
#endif
        Loaded += async (_, _) => await connectionPage.InitializeAsync();
    }
#if WINDOWS
    static void StyleMainTabs(Microsoft.UI.Xaml.FrameworkElement root, HashSet<string> mainTitles)
    {
        void ApplyStyles(object? sender, object args) => Visit(root);

        void Visit(Microsoft.UI.Xaml.DependencyObject view)
        {
            if (view is Microsoft.UI.Xaml.Controls.TabViewItem tab && HasMainTitle(tab.Header))
            {
                double width = tab.IsSelected ? 172 : 156;
                double height = tab.IsSelected ? 42 : 37;
                if (tab.MinWidth != width) tab.MinWidth = width;
                if (tab.MinHeight != height) tab.MinHeight = height;
                var weight = tab.IsSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                if (tab.FontWeight.Weight != weight.Weight) tab.FontWeight = weight;
                return;
            }

            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(view);
            for (int i = 0; i < count; i++)
                Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(view, i));
        }

        bool HasMainTitle(object header) => header switch
        {
            string title => mainTitles.Contains(title),
            Microsoft.UI.Xaml.Controls.TextBlock text => mainTitles.Contains(text.Text),
            _ => false
        };

        root.LayoutUpdated += ApplyStyles;
        Visit(root);
    }
#endif
}
