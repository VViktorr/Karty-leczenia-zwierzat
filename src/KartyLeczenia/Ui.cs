namespace KartyLeczenia.Views;
static class Ui
{
    public static Label Heading(string text) => new() { Text = text, FontSize = 26, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 8) };
    public static Label Note(string text) => new() { Text = text, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };
    public static VerticalStackLayout Field(string title, View control) => new() { Spacing = 3, Children = { new Label { Text = title, FontSize = 13, FontAttributes = FontAttributes.Bold }, control } };
    public static Button Action(string text, Func<Task> run)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await run(); }
            catch (Exception ex) { await Shell.Current.DisplayAlertAsync("Nie udało się wykonać operacji", SubiektConnectionService.FriendlyError(ex), "OK"); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
}
