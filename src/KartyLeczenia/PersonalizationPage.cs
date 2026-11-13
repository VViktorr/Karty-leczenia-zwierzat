namespace KartyLeczenia.Views;

internal sealed class PersonalizationPage : ContentPage
{
    static readonly string[] Fonts = ["Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana"];
    static readonly string[] TextSizes = ["12", "13", "14", "15", "16", "18"];
    static readonly string[] HeadingSizes = ["13", "14", "16", "18", "20", "22"];
    static readonly string[] Alignments = ["Do lewej", "Do środka", "Do prawej"];
    static readonly TextAlignment[] AlignmentValues = [TextAlignment.Start, TextAlignment.Center, TextAlignment.End];

    internal PersonalizationPage()
    {
        Title = "Personalizacja";
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 16, Spacing = 10, MaximumWidthRequest = 440, HorizontalOptions = LayoutOptions.Start,
            Children =
            {
                Ui.Heading("Personalizacja"),
                Field("Czcionka aplikacji", "AppFont", Fonts, 0),
                Field("Rozmiar tekstu", "AppTextSize", TextSizes, 1),
                Field("Rozmiar nagłówków", "AppHeadingSize", HeadingSizes, 2),
                Field("Wyrównanie nagłówków", "AppHeadingAlignment", Alignments, 0),
                Field("Wyrównanie tekstu w tabelach", "AppCellAlignment", Alignments, 0)
            }
        } };
    }

    static int Choice(string key, string[] values, int fallback)
    {
        int index = Preferences.Default.Get(key, fallback);
        return index >= 0 && index < values.Length ? index : fallback;
    }

    static View Field(string title, string key, string[] values, int fallback)
    {
        var picker = new Picker { ItemsSource = values, SelectedIndex = Choice(key, values, fallback) };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0) return;
            Preferences.Default.Set(key, picker.SelectedIndex);
            if (Application.Current is { } app) Apply(app.Resources);
        };
        return Ui.Field(title, picker);
    }

    internal static void Apply(ResourceDictionary resources)
    {
        resources["AppFont"] = Fonts[Choice("AppFont", Fonts, 0)];
        resources["AppTextSize"] = double.Parse(TextSizes[Choice("AppTextSize", TextSizes, 1)]);
        resources["AppHeadingSize"] = double.Parse(HeadingSizes[Choice("AppHeadingSize", HeadingSizes, 2)]);
        resources["AppHeadingAlignment"] = AlignmentValues[Choice("AppHeadingAlignment", Alignments, 0)];
        resources["AppCellAlignment"] = AlignmentValues[Choice("AppCellAlignment", Alignments, 0)];
    }
}
