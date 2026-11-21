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
        var preferences = DesktopPreferences.Load();
        var themeSelector = new ComboBox
        {
            ItemsSource = new[] { "Systemowy", "Jasny", "Ciemny" },
            SelectedIndex = Math.Clamp(preferences.Theme, 0, 2),
            MinWidth = 135
        };
        void ApplyTheme() => RequestedThemeVariant = themeSelector.SelectedIndex switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

        themeSelector.SelectionChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        var catalogue = CreateCatalogueLookup(preferences, out var readTablePreferences);
        var window = new Window
        {
            Title = "Karty leczenia zwierząt — Avalon",
            Width = Math.Clamp(preferences.WindowWidth, 520, 2400),
            Height = Math.Clamp(preferences.WindowHeight, 360, 1600),
            WindowState = preferences.Maximized ? WindowState.Maximized : WindowState.Normal,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Content = new StackPanel
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
                    catalogue
                }
            } }
        };
        window.Closing += (_, _) =>
        {
            try
            {
                var table = readTablePreferences();
                (table with
                {
                    Theme = themeSelector.SelectedIndex,
                    WindowWidth = window.WindowState == WindowState.Normal ? window.Bounds.Width : window.Width,
                    WindowHeight = window.WindowState == WindowState.Normal ? window.Bounds.Height : window.Height,
                    Maximized = window.WindowState == WindowState.Maximized
                }).Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Nie można zapisać ustawień wyglądu: " + ex.Message);
            }
        };
        return window;
    }

    private static Control CreateCatalogueLookup(DesktopPreferences preferences,
        out Func<DesktopPreferences> readPreferences)
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
        var columnDefinitions = new[]
        {
            ("Gatunek", nameof(CatalogueVariant.Species)), ("Droga podania", nameof(CatalogueVariant.Route)),
            ("Dawkowanie", nameof(CatalogueVariant.Dose)), ("Tkanki jadalne", nameof(CatalogueVariant.Tissues)),
            ("Mleko", nameof(CatalogueVariant.Milk)), ("Jaja", nameof(CatalogueVariant.Eggs))
        };
        var saved = preferences.Columns ?? [];
        var columns = new Dictionary<string, DataGridTextColumn>();
        foreach (var (header, property) in columnDefinitions.OrderBy(column =>
            saved.FirstOrDefault(item => item.Key == column.Item2)?.Order ?? int.MaxValue))
        {
            var column = new DataGridTextColumn { Header = header, Binding = new Binding(property) };
            if (saved.FirstOrDefault(item => item.Key == property)?.Width is >= 20 and <= 4000 and var width)
                column.Width = new DataGridLength(width);
            columns.Add(property, column);
            variants.Columns.Add(column);
        }

        var fonts = FontManager.Current.SystemFonts.Select(font => font.Name).Order().ToArray();
        var font = new ComboBox { ItemsSource = fonts, PlaceholderText = "Czcionka tabeli", MinWidth = 165,
            SelectedItem = fonts.FirstOrDefault(name => name == preferences.Font) };
        if (font.SelectedItem is string savedFont) variants.FontFamily = new FontFamily(savedFont);
        font.SelectionChanged += (_, _) =>
        {
            if (font.SelectedItem is string family)
                variants.FontFamily = new FontFamily(family);
        };
        var fontSize = new NumericUpDown { Minimum = 8, Maximum = 32, Value = Math.Clamp(preferences.FontSize, 8, 32), Width = 85 };
        fontSize.ValueChanged += (_, change) =>
        {
            if (change.NewValue is decimal value) variants.FontSize = (double)value;
        };
        var rowHeight = new NumericUpDown { Minimum = 18, Maximum = 120,
            Value = Math.Clamp(preferences.RowHeight, 18, 120), Width = 85 };
        variants.FontSize = (double)fontSize.Value!.Value;
        variants.RowHeight = (double)rowHeight.Value!.Value;
        rowHeight.ValueChanged += (_, change) =>
        {
            if (change.NewValue is decimal value) variants.RowHeight = (double)value;
        };
        readPreferences = () => preferences with
        {
            Font = font.SelectedItem as string,
            FontSize = fontSize.Value ?? 14,
            RowHeight = rowHeight.Value ?? 30,
            Columns = columns.Select(item => new ColumnPreference(item.Key, item.Value.DisplayIndex,
                item.Value.Width.IsAbsolute ? item.Value.Width.Value : null)).ToArray()
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
                    Children = { font, new TextBlock { Text = "Rozmiar", VerticalAlignment = VerticalAlignment.Center }, fontSize,
                        new TextBlock { Text = "Wysokość wiersza", VerticalAlignment = VerticalAlignment.Center }, rowHeight }
                },
                variants }
        };

    }
}
