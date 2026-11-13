namespace KartyLeczenia.Views;
static class Ui
{
    public static Label Heading(string text)
    {
        var label = new Label { Text = text, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) };
        label.SetDynamicResource(Label.FontSizeProperty, "AppHeadingSize");
        label.SetDynamicResource(Label.HorizontalTextAlignmentProperty, "AppHeadingAlignment");
        return label;
    }
    public static Label Note(string text)
    {
        var label = new Label { Text = text, LineBreakMode = LineBreakMode.WordWrap };
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#475569"), Color.FromArgb("#CBD5E1"));
        return label;
    }
    public static VerticalStackLayout Field(string title, View control) => new() { Spacing = 4, Children = { new Label { Text = title, FontAttributes = FontAttributes.Bold }, control } };
    public static Border Card(View content)
    {
        var border = new Border { Content = content, Padding = 10, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(2) } };
        border.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#172334"));
        border.Stroke = new SolidColorBrush(Color.FromArgb("#64748B"));
        return border;
    }
    public static View WithClose(ContentPage page, View content, Func<Task> close, View? titleDetail = null)
    {
        var button = Action("✕", close);
        button.WidthRequest = button.MinimumWidthRequest = 36;
        button.HeightRequest = button.MinimumHeightRequest = 34;
        button.Padding = 0; button.Margin = 0; button.FontSize = 17;
        SemanticProperties.SetDescription(button, "Zamknij okno");
        var bar = new Grid { Padding = new Thickness(16, 8), ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } } };
        bar.Add(new Label { Text = page.Title, FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }, 0, 0);
        if (titleDetail is null) bar.Add(button, 1, 0);
        else
        {
            bar.ColumnDefinitions[0].Width = GridLength.Auto;
            bar.ColumnDefinitions[1].Width = GridLength.Star;
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnSpacing = 12;
            bar.Add(titleDetail, 1, 0);
            bar.Add(button, 2, 0);
        }
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star } } };
        root.Add(bar, 0, 0); root.Add(content, 0, 1);
        return root;
    }
    public static Button Action(string text, Func<Task> run)
    {
        var button = new Button { Text = text, Margin = new Thickness(0, 0, 8, 6) };
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            CrashLog.Write("Action.Start " + text);
            try { await run(); }
            catch (Exception ex) { CrashLog.Write("Action." + text, ex); await Shell.Current.DisplayAlertAsync("Nie udało się wykonać operacji", SubiektConnectionService.FriendlyError(ex), "OK"); }
            finally
            {
                button.IsEnabled = true;
                CrashLog.Write($"Action.End {text} elapsedMs={timer.ElapsedMilliseconds}");
            }
        };
        return button;
    }
    public static Button PrimaryAction(string text, Func<Task> run)
    {
        var button = Action(text, run);
        button.SetAppThemeColor(Button.BackgroundColorProperty, Color.FromArgb("#0F766E"), Color.FromArgb("#0F766E"));
        button.TextColor = Colors.White;
        return button;
    }
}
