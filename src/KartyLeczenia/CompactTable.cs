namespace KartyLeczenia.Views;

internal static class CompactTable
{
    sealed class RowHeight : BindableObject
    {
        internal static readonly BindableProperty ValueProperty =
            BindableProperty.Create(nameof(Value), typeof(double), typeof(RowHeight), 26d);

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TableColumns, RowHeight> RowHeights = new();

    internal static Label Cell(string path, bool right = false, string? format = null)
    {
        var label = new Label { LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = right ? TextAlignment.End : TextAlignment.Start };
        if (!right) label.SetDynamicResource(Label.HorizontalTextAlignmentProperty, "AppCellAlignment");
        label.SetBinding(Label.TextProperty, new Binding(path, stringFormat: format));
        return label;
    }

    internal static Grid Header(TableColumns widths, params string[] headings) =>
        Row(widths, true, headings.Select(text => new Label { Text = text, FontSize = 12,
            FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation }).ToArray());

    internal static Grid Row(TableColumns widths, bool header, params View[] cells)
    {
        var row = new Grid { Padding = new Thickness(3, 0), ColumnSpacing = 3,
            MinimumHeightRequest = header ? 32 : 26,
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = 1 } } };
        var height = RowHeights.GetValue(widths, _ => new RowHeight());
        if (!header)
            row.SetBinding(VisualElement.MinimumHeightRequestProperty, new Binding(nameof(RowHeight.Value), source: height));
        if (header)
            row.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#DCE5EF"), Color.FromArgb("#27374A"));
        if (!header)
        {
            var selected = new VisualState { Name = "Selected" };
            selected.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Color.FromArgb("#79A8D5") });
            var states = new VisualStateGroup { Name = "CommonStates" };
            states.States.Add(new VisualState { Name = "Normal" });
            states.States.Add(selected);
            VisualStateManager.SetVisualStateGroups(row, new VisualStateGroupList { states });
        }
        for (int i = 0; i < widths.Count; i++)
        {
            var column = new ColumnDefinition();
            column.SetBinding(ColumnDefinition.WidthProperty, new Binding(nameof(TableColumns.Slot.Width), source: widths.Position(i)));
            row.ColumnDefinitions.Add(column);
            var cell = cells[i];
            cell.SetBinding(Grid.ColumnProperty, new Binding(nameof(TableColumns.Slot.Index), source: widths.Column(i)));
            row.Children.Add(cell);
            if (header)
            {
                if (cell is Label heading)
                {
                    heading.SetDynamicResource(Label.FontSizeProperty, "AppHeadingSize");
                    heading.SetDynamicResource(Label.HorizontalTextAlignmentProperty, "AppHeadingAlignment");
                }
                else if (cell is Button button)
                    button.SetDynamicResource(Button.FontSizeProperty, "AppHeadingSize");
                widths.AttachHeader(row, cell, i);
            }
        }
        for (int i = 1; i < widths.Count; i++)
        {
            var divider = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.Start, InputTransparent = true };
            divider.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#8795A5"), Color.FromArgb("#455267"));
            divider.Margin = new Thickness(-2, 0, 0, 0);
            row.Add(divider, i, 0);
        }
        var line = new BoxView { HeightRequest = 1, InputTransparent = true };
        line.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#A0AAB6"), Color.FromArgb("#344457"));
        row.Add(line, 0, 1);
        Grid.SetColumnSpan(line, widths.Count);
        if (header) AttachHeightGrip(row, height, widths.Count);
        return row;
    }

    static void AttachHeightGrip(Grid row, RowHeight height, int columnCount)
    {
#if WINDOWS
        var grip = new BoxView { Color = Colors.Transparent, HeightRequest = 7,
            VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, -3), ZIndex = 11 };
        row.Add(grip, 0, 0);
        Grid.SetColumnSpan(grip, columnCount);
        Grid.SetRowSpan(grip, 2);

        void Attach()
        {
            if (grip.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
            uint? pointerId = null;
            double start = 0, initialHeight = 0;
            element.PointerPressed += (_, args) =>
            {
                var point = args.GetCurrentPoint(null);
                if (!point.Properties.IsLeftButtonPressed || !element.CapturePointer(args.Pointer)) return;
                pointerId = args.Pointer.PointerId;
                start = point.Position.Y;
                initialHeight = height.Value;
                args.Handled = true;
            };
            element.PointerMoved += (_, args) =>
            {
                if (pointerId != args.Pointer.PointerId) return;
                height.Value = Math.Clamp(initialHeight + args.GetCurrentPoint(null).Position.Y - start, 18, 100);
                args.Handled = true;
            };
            element.PointerReleased += (_, args) =>
            {
                if (pointerId != args.Pointer.PointerId) return;
                pointerId = null;
                element.ReleasePointerCapture(args.Pointer);
                args.Handled = true;
            };
            element.PointerCaptureLost += (_, _) => pointerId = null;
        }

        grip.HandlerChanged += (_, _) => Attach();
        Attach();
#endif
    }
}
