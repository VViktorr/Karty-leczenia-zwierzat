using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Data;
using Avalonia.Media;
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
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var location = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml");
        Styles.Add(new StyleInclude(location) { Source = location });
    }

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
                    },
                    CreateCatalogueLookup()
                }
            }
        };
    }

    private static Control CreateCatalogueLookup()
    {
        var server = new TextBox { Watermark = "Adres serwera lub nazwa instancji" };
        var symbol = new TextBox { Watermark = "Symbol kartoteki leku", MaxLength = 128 };
        var load = new Button { Content = "Pokaż warianty" };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var variants = new DataGrid
        {
            Height = 300,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            AutoGenerateColumns = false
        };
        foreach (var (header, property) in new[]
        {
            ("Gatunek", nameof(CatalogueVariant.Species)), ("Droga podania", nameof(CatalogueVariant.Route)),
            ("Dawkowanie", nameof(CatalogueVariant.Dose)), ("Tkanki jadalne", nameof(CatalogueVariant.Tissues)),
            ("Mleko", nameof(CatalogueVariant.Milk)), ("Jaja", nameof(CatalogueVariant.Eggs))
        })
        {
            variants.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property) });
        }

        var fonts = FontManager.Current.SystemFonts.Select(font => font.Name).Order().ToArray();
        var font = new ComboBox { ItemsSource = fonts, PlaceholderText = "Czcionka tabeli", MinWidth = 165 };
        font.SelectionChanged += (_, _) =>
        {
            if (font.SelectedItem is string family)
                variants.FontFamily = new FontFamily(family);
        };
        var fontSize = new NumericUpDown { Minimum = 8, Maximum = 32, Value = 14, Width = 85 };
        fontSize.ValueChanged += (_, change) =>
        {
            if (change.NewValue is decimal value) variants.FontSize = (double)value;
        };
        load.Click += async (_, _) =>
        {
            string address = server.Text?.Trim() ?? "";
            string code = symbol.Text?.Trim() ?? "";
            if (address.Length == 0 || code.Length == 0)
            {
                status.Text = "Podaj adres SQL Servera i symbol leku.";
                return;
            }

            load.IsEnabled = false;
            variants.ItemsSource = null;
            status.Text = "Wczytywanie wariantów…";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var result = await CatalogueLookup.Read(address, code, timeout.Token);
                variants.ItemsSource = result;
                status.Text = result.Count == 0 ? "Brak wariantów dla tego symbolu." : $"Warianty ({result.Count})";
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                status.Text = $"Odczyt SQL nie powiódł się (kod {ex.Number}). Sprawdź serwer, certyfikat i uprawnienia konta Windows.";
            }
            catch (OperationCanceledException)
            {
                status.Text = "Przekroczono czas odczytu z SQL.";
            }
            finally
            {
                load.IsEnabled = true;
            }
        };

        return new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = "Katalog leków — odczyt z SQL kontem Windows" },
                server, symbol, load, status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { font, new TextBlock { Text = "Rozmiar", VerticalAlignment = VerticalAlignment.Center }, fontSize }
                },
                variants }
        };

    }
}
