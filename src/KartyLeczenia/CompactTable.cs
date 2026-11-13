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

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TableColumns, RowHeight> HeaderHeights = new();

    static RowHeight Height(TableColumns columns, bool header) => header
        ? HeaderHeights.GetValue(columns, _ => new RowHeight { Value = 32 })
        : RowHeights.GetValue(columns, _ => new RowHeight());

    internal static double GetRowHeight(TableColumns columns, bool header = false) => Height(columns, header).Value;
    internal static void SetRowHeight(TableColumns columns, double value, bool header = false) =>
        Height(columns, header).Value = value;

    sealed class SingleLineConverter : IValueConverter
    {
        internal static readonly SingleLineConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is string text ? string.Join(" ", text.Split(['\r', '\n'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) : value;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    internal static Label Cell(string path, bool right = false, string? format = null, bool singleLine = false)
    {
        var label = new Label { LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = right ? TextAlignment.End : TextAlignment.Start };
        if (singleLine) label.MaxLines = 1;
        if (!right) label.SetDynamicResource(Label.HorizontalTextAlignmentProperty, "AppCellAlignment");
        label.SetBinding(Label.TextProperty, new Binding(path, converter: singleLine ? SingleLineConverter.Instance : null, stringFormat: format));
        return label;
    }

    internal static Grid Header(TableColumns widths, params string[] headings) =>
        Row(widths, true, headings.Select(text => new Label { Text = text, FontSize = 12,
            FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation }).ToArray());

    internal static Grid Row(TableColumns widths, bool header, params View[] cells)
    {
        var row = new Grid { Padding = new Thickness(3, 0), ColumnSpacing = 0,
            MinimumHeightRequest = header ? 32 : 26,
            RowDefinitions = { new RowDefinition { Height = GridLength.Star }, new RowDefinition { Height = 1 } } };
        var height = RowHeights.GetValue(widths, _ => new RowHeight());
        row.SetBinding(VisualElement.MinimumHeightRequestProperty, new Binding(nameof(RowHeight.Value), source: Height(widths, header)));
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
            cell.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(TableColumns.Slot.Shown), source: widths.Column(i)));
            if (cell is Label text)
            {
                var style = header ? widths.Column(i).HeaderStyle : widths.Column(i).DataStyle;
                var original = text.HorizontalTextAlignment;
                void ApplyAlignment()
                {
                    if (style.ClassId is not null) text.HorizontalTextAlignment = style.HorizontalTextAlignment;
                    else if (!header && original == TextAlignment.End) text.HorizontalTextAlignment = original;
                    else text.SetDynamicResource(Label.HorizontalTextAlignmentProperty, header ? "AppHeadingAlignment" : "AppCellAlignment");
                }
                var reference = new WeakReference<Label>(text);
                style.PropertyChanged += Changed;
                void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
                {
                    if (!reference.TryGetTarget(out var target)) { style.PropertyChanged -= Changed; return; }
                    if (style.ClassId is not null) target.HorizontalTextAlignment = style.HorizontalTextAlignment;
                    else if (!header && original == TextAlignment.End) target.HorizontalTextAlignment = original;
                    else target.SetDynamicResource(Label.HorizontalTextAlignmentProperty, header ? "AppHeadingAlignment" : "AppCellAlignment");
                }
                ApplyAlignment();
            }
            if (cell is Label) cell.Margin = new Thickness(5, 0);
            row.Children.Add(cell);
            if (header)
            {
                if (cell is Label heading)
                {
                    heading.SetDynamicResource(Label.FontSizeProperty, "AppHeadingSize");

                }
                else if (cell is Button button)
                    button.SetDynamicResource(Button.FontSizeProperty, "AppHeadingSize");
                widths.AttachHeader(row, cell, i);
            }
        }
        for (int i = 1; i < widths.Count; i++)
        {
            var divider = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.Start, InputTransparent = true };
            divider.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#687786"), Color.FromArgb("#69788A"));
            divider.VerticalOptions = LayoutOptions.Fill;
            divider.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(CheckBox.IsChecked), source: widths.VerticalLines));
            row.Add(divider, i, 0);
            Grid.SetRowSpan(divider, 2);
        }
        var line = new BoxView { HeightRequest = 1, InputTransparent = true };
        line.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#748292"), Color.FromArgb("#657588"));
        line.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(CheckBox.IsChecked), source: widths.HorizontalLines));
        row.Add(line, 0, 1);
        Grid.SetColumnSpan(line, widths.Count);
        if (header) AttachHeightGrip(row, height, widths);
        return row;
    }

    static void AttachHeightGrip(Grid row, RowHeight height, TableColumns columns)
    {
#if WINDOWS
        var grip = new BoxView { Color = Colors.Transparent, HeightRequest = 7,
            VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, -3), ZIndex = 11 };
        row.Add(grip, 0, 0);
        Grid.SetColumnSpan(grip, columns.Count);
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
                columns.SaveLayout();
                args.Handled = true;
            };
            element.PointerCaptureLost += (_, _) => pointerId = null;
        }

        grip.HandlerChanged += (_, _) => Attach();
        Attach();
#endif
    }
}
