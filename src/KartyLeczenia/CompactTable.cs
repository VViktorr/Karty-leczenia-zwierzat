namespace KartyLeczenia.Views;

internal static class CompactTable
{
    internal static Label Cell(string path, bool right = false, string? format = null)
    {
        var label = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = right ? TextAlignment.End : TextAlignment.Start };
        label.SetBinding(Label.TextProperty, new Binding(path, stringFormat: format));
        return label;
    }

    internal static Grid Header(GridLength[] widths, params string[] headings) =>
        Row(widths, true, headings.Select(text => new Label { Text = text, FontSize = 12,
            FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation }).ToArray());

    internal static Grid Row(GridLength[] widths, bool header, params View[] cells)
    {
        var row = new Grid { Padding = new Thickness(3, 0), ColumnSpacing = 3,
            MinimumHeightRequest = header ? 22 : 20,
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = 1 } } };
        if (header)
            row.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EAEAEA"), Color.FromArgb("#27374A"));
        if (!header)
        {
            var selected = new VisualState { Name = "Selected" };
            selected.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Color.FromArgb("#79A8D5") });
            var states = new VisualStateGroup { Name = "CommonStates" };
            states.States.Add(new VisualState { Name = "Normal" });
            states.States.Add(selected);
            VisualStateManager.SetVisualStateGroups(row, new VisualStateGroupList { states });
        }
        foreach (var width in widths) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        for (int i = 0; i < cells.Length; i++) row.Add(cells[i], i, 0);
        for (int i = 1; i < widths.Length; i++)
        {
            var divider = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.Start, InputTransparent = true };
            divider.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#AAAAAA"), Color.FromArgb("#455267"));
            divider.Margin = new Thickness(-2, 0, 0, 0);
            row.Add(divider, i, 0);
        }
        var line = new BoxView { HeightRequest = 1, InputTransparent = true };
        line.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#B8B8B8"), Color.FromArgb("#344457"));
        row.Add(line, 0, 1);
        Grid.SetColumnSpan(line, widths.Length);
        return row;
    }
}
