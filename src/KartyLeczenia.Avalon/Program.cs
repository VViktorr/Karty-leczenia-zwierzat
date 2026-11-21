using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace KartyLeczenia.Avalon;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<AvalonApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
}

public sealed class AvalonApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = CreateMainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    private Window CreateMainWindow()
    {
        var themeSelector = new ComboBox
        {
            ItemsSource = new[] { "Systemowy", "Jasny", "Ciemny" },
            SelectedIndex = 0,
            MinWidth = 135
        };
        themeSelector.SelectionChanged += (_, _) =>
            RequestedThemeVariant = themeSelector.SelectedIndex switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

        return new Window
        {
            Title = "Karty leczenia zwierząt — Avalon",
            Width = 1000,
            Height = 680,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Karty leczenia zwierząt", FontSize = 24 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = "Motyw", VerticalAlignment = VerticalAlignment.Center },
                            themeSelector
                        }
                    }
                }
            }
        };
    }
}
