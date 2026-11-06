namespace KartyLeczenia;
public sealed class App : Application
{
    public App()
    {
        Resources.Add(new Style(typeof(Button)) { Setters = { new Setter { Property = Button.CornerRadiusProperty, Value = 8 }, new Setter { Property = Button.PaddingProperty, Value = new Thickness(16, 10) } } });
        Resources.Add(new Style(typeof(Entry)) { Setters = { new Setter { Property = Entry.MinimumHeightRequestProperty, Value = 42 } } });
    }
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell()) { Title = "Karty leczenia zwierząt — MAUI", Width = 1400, Height = 920, MinimumWidth = 1050, MinimumHeight = 700 };
}
public sealed class AppState
{
    public ConnectionOptions? ValidatedConnection { get; set; }
}
public sealed class AppShell : Shell
{
    public AppShell()
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        var state = new AppState();
        var tabs = new TabBar();
        var connection = new Tab { Title = "Połączenie z Subiektem", Route = "connection" };
        connection.Items.Add(new ShellContent { Content = new Views.ConnectionPage(state) }); tabs.Items.Add(connection);
        var cards = new Tab { Title = "Karta leczenia", Route = "cards" };
        cards.Items.Add(new ShellContent { Content = new Views.CardPage(state) }); tabs.Items.Add(cards);
        Items.Add(tabs);
    }
}
