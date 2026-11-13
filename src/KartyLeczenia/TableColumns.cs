namespace KartyLeczenia.Views;

internal sealed class TableColumns
{
    internal sealed class Slot(int index, GridLength width) : BindableObject
    {
        internal static readonly BindableProperty IndexProperty = BindableProperty.Create(
            nameof(Index), typeof(int), typeof(Slot), 0);
        internal static readonly BindableProperty WidthProperty = BindableProperty.Create(
            nameof(Width), typeof(GridLength), typeof(Slot), GridLength.Star);

        public int Index { get => (int)GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
        public GridLength Width { get => (GridLength)GetValue(WidthProperty); set => SetValue(WidthProperty, value); }

        internal void Initialize()
        {
            Index = index;
            Width = width;
        }
    }

    readonly Slot[] columns;
    readonly Slot[] positions;
    internal int Count => columns.Length;
    internal double FixedWidth => positions.Where(slot => slot.Width.IsAbsolute).Sum(slot => slot.Width.Value);
    internal event Action? WidthChanged;

    internal TableColumns(params GridLength[] widths)
    {
        columns = CreateSlots(widths);
        positions = CreateSlots(widths);
    }

    static Slot[] CreateSlots(GridLength[] widths) => widths.Select((width, index) =>
    {
        var slot = new Slot(index, width);
        slot.Initialize();
        return slot;
    }).ToArray();

    internal Slot Column(int index) => columns[index];
    internal Slot Position(int index) => positions[index];

    void Move(Slot column, int direction)
    {
        int index = column.Index;
        int destination = index + direction;
        if (destination < 0 || destination >= Count) return;
        var neighbour = columns.Single(slot => slot.Index == destination);
        neighbour.Index = index;
        column.Index = destination;
        positions[index].Width = neighbour.Width;
        positions[destination].Width = column.Width;
    }

    internal void AttachHeader(Grid row, View cell, int index)
    {
#if WINDOWS
        var column = columns[index];
        void AttachMenu()
        {
            if (cell.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
            var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
            var left = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Przesuń w lewo" };
            var right = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Przesuń w prawo" };
            left.Click += (_, _) => Move(column, -1);
            right.Click += (_, _) => Move(column, 1);
            menu.Opening += (_, _) =>
            {
                left.IsEnabled = column.Index > 0;
                right.IsEnabled = column.Index < Count - 1;
            };
            menu.Items.Add(left);
            menu.Items.Add(right);
            element.ContextFlyout = menu;
        }
        cell.HandlerChanged += (_, _) => AttachMenu();
        AttachMenu();

        var grip = new BoxView
        {
            Color = Colors.Transparent, WidthRequest = 12, HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill, Margin = new Thickness(0, 0, -4, 0), ZIndex = 10
        };
        grip.SetBinding(Grid.ColumnProperty, new Binding(nameof(Slot.Index), source: column));
        row.Children.Add(grip);
        void AttachGrip()
        {
            if (grip.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
            double start = 0, width = 0;
            uint? pointerId = null;
            Slot resized = column;
            int direction = 1;
            element.PointerPressed += (_, args) =>
            {
                var point = args.GetCurrentPoint(null);
                if (!point.Properties.IsLeftButtonPressed) return;
                resized = column;
                direction = 1;
                if (column.Width.IsStar && columns.Count(slot => slot.Width.IsStar) == 1)
                {
                    int neighbour = column.Index == Count - 1 ? column.Index - 1 : column.Index + 1;
                    resized = columns.Single(slot => slot.Index == neighbour);
                    direction = -1;
                }
                width = resized.Width.IsAbsolute ? resized.Width.Value : cell.Width;
                if (width <= 0 || !element.CapturePointer(args.Pointer)) return;
                pointerId = args.Pointer.PointerId;
                start = point.Position.X;
                args.Handled = true;
            };
            element.PointerMoved += (_, args) =>
            {
                if (pointerId != args.Pointer.PointerId) return;
                var offset = args.GetCurrentPoint(null).Position.X - start;
                resized.Width = new GridLength(Math.Clamp(width + direction * offset, 32, 700));
                positions[resized.Index].Width = resized.Width;
                WidthChanged?.Invoke();
                args.Handled = true;
            };
            element.PointerReleased += (_, args) =>
            {
                if (pointerId != args.Pointer.PointerId) return;
                element.ReleasePointerCapture(args.Pointer);
                args.Handled = true;
            };
            element.PointerCaptureLost += (_, _) => pointerId = null;
        }
        grip.HandlerChanged += (_, _) => AttachGrip();
        AttachGrip();
#endif
    }
}
