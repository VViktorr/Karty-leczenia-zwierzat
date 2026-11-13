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
        var row = new Grid { Padding = new Thickness(6, 0), ColumnSpacing = 4,
            MinimumHeightRequest = header ? 26 : 24,
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = 1 } } };
        if (header)
            row.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#E7EDF4"), Color.FromArgb("#27374A"));
        foreach (var width in widths) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        for (int i = 0; i < cells.Length; i++) row.Add(cells[i], i, 0);
        for (int i = 1; i < widths.Length; i++)
        {
            var divider = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.Start, InputTransparent = true };
            divider.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#C4CBD3"), Color.FromArgb("#455267"));
            divider.Margin = new Thickness(-2, 0, 0, 0);
            row.Add(divider, i, 0);
        }
        var line = new BoxView { HeightRequest = 1, InputTransparent = true };
        line.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#D9E0E8"), Color.FromArgb("#344457"));
        row.Add(line, 0, 1);
        Grid.SetColumnSpan(line, widths.Length);
        return row;
    }
}
