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

        internal static readonly BindableProperty ShownProperty = BindableProperty.Create(nameof(Shown), typeof(bool), typeof(Slot), true);
        public bool Shown { get => (bool)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }
        internal readonly Label HeaderStyle = new();
        internal readonly Label DataStyle = new();
        internal void Initialize()
        {
            Index = index;
            Width = width;
        }
    }

    internal readonly CheckBox HorizontalLines = new() { IsChecked = true };
    internal readonly CheckBox VerticalLines = new() { IsChecked = true };
    readonly Dictionary<int, string> headers = new();
    readonly GridLength[] defaults;
    readonly Slot[] columns;
    readonly Slot[] positions;
    internal int Count => columns.Length;
    internal double FixedWidth => positions.Where(slot => slot.Width.IsAbsolute).Sum(slot => slot.Width.Value);
    internal event Action? WidthChanged;

    internal TableColumns(params GridLength[] widths)
    {
        defaults = widths;
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

    sealed record ColumnLayout(int Index, double Width, bool Star, bool Shown, int HeaderAlign = -1, int DataAlign = -1);
    sealed record Layout(ColumnLayout[] Columns, bool Horizontal, bool Vertical, double HeaderHeight, double RowHeight);
    string? preferenceKey;

    internal void SaveLayout()
    {
        if (preferenceKey is null) return;
        var layout = new Layout(columns.Select(column => new ColumnLayout(column.Index, column.Width.Value,
            column.Width.IsStar, column.Shown, AlignmentCode(column.HeaderStyle), AlignmentCode(column.DataStyle))).ToArray(), HorizontalLines.IsChecked, VerticalLines.IsChecked,
            CompactTable.GetRowHeight(this, true), CompactTable.GetRowHeight(this));
        Preferences.Default.Set(preferenceKey, System.Text.Json.JsonSerializer.Serialize(layout));
    }

    void RestoreLayout()
    {
        if (preferenceKey is null) return;
        var json = Preferences.Default.Get(preferenceKey, "");
        if (json.Length == 0) return;
        Layout? layout;
        try { layout = System.Text.Json.JsonSerializer.Deserialize<Layout>(json); }
        catch (System.Text.Json.JsonException) { return; }
        if (layout is null || layout.Columns is null || layout.Columns.Length != Count || !layout.Columns.Any(item => item.Shown) ||
            layout.Columns.Any(item => !double.IsFinite(item.Width) || item.Width <= 0) ||
            !layout.Columns.Select(item => item.Index).OrderBy(index => index).SequenceEqual(Enumerable.Range(0, Count))) return;
        for (int i = 0; i < Count; i++)
        {
            var item = layout.Columns[i];
            var column = columns[i];
            column.Index = item.Index;
            column.Width = new GridLength(item.Width, item.Star ? GridUnitType.Star : GridUnitType.Absolute);
            column.Shown = item.Shown;
            SetAlignment(column.HeaderStyle, item.HeaderAlign);
            SetAlignment(column.DataStyle, item.DataAlign);
            positions[item.Index].Width = item.Shown ? column.Width : new GridLength(0);
        }
        HorizontalLines.IsChecked = layout.Horizontal;
        VerticalLines.IsChecked = layout.Vertical;
        CompactTable.SetRowHeight(this, Math.Clamp(layout.HeaderHeight, 24, 100), true);
        CompactTable.SetRowHeight(this, Math.Clamp(layout.RowHeight, 18, 100));
        WidthChanged?.Invoke();
    }

    static int AlignmentCode(Label label) => label.ClassId is null ? -1 : (int)label.HorizontalTextAlignment;
    static void SetAlignment(Label label, int code)
    {
        if (code is >= 0 and <= 2) label.HorizontalTextAlignment = (TextAlignment)code;
        label.ClassId = code is >= 0 and <= 2 ? "Custom" : null;
    }

    bool parametersOpen;

    internal async Task ShowParameters()
    {
        if (parametersOpen) return;
        var shell = Shell.Current;
        var host = shell?.Navigation.ModalStack.LastOrDefault() ?? shell?.CurrentPage;
        if (host is null) return;
        var horizontal = new CheckBox { IsChecked = HorizontalLines.IsChecked };
        var vertical = new CheckBox { IsChecked = VerticalLines.IsChecked };
        var section = new Picker { ItemsSource = new[] { "Nagłówek", "Zawartość" }, SelectedIndex = 1 };
        var headerHeight = new Slider(24, 100, CompactTable.GetRowHeight(this, header: true));
        var height = new Slider(18, 100, CompactTable.GetRowHeight(this));
        var heightLabel = Ui.Note($"Minimalna wysokość wiersza: {height.Value:0}");
        height.ValueChanged += (_, e) => heightLabel.Text = $"Minimalna wysokość wiersza: {e.NewValue:0}";
        var headerLabel = Ui.Note($"Minimalna wysokość nagłówka: {headerHeight.Value:0}");
        headerHeight.ValueChanged += (_, e) => headerLabel.Text = $"Minimalna wysokość nagłówka: {e.NewValue:0}";
        var headerOptions = new VerticalStackLayout { IsVisible = false, Children = { headerLabel, headerHeight } };
        var bodyOptions = new VerticalStackLayout { Children = {
            Ui.Field("Linie poziome", horizontal), Ui.Field("Linie pionowe", vertical), heightLabel, height
        } };
        section.SelectedIndexChanged += (_, _) =>
        {
            headerOptions.IsVisible = section.SelectedIndex == 0;
            heightLabel.IsVisible = height.IsVisible = section.SelectedIndex == 1;
        };
        var fields = new VerticalStackLayout { Padding = 16, Spacing = 8, Children = {
            Ui.Heading("Parametry tabeli"), bodyOptions,
            Ui.Heading("Parametry kolumn"), Ui.Field("Sekcja", section), headerOptions
        } };
        var order = columns.OrderBy(column => column.Index).ToList();
        var sizes = columns.Select(column => new Entry
        {
            Text = column.Width.IsAbsolute ? column.Width.Value.ToString("0") : "",
            Placeholder = "Auto", Keyboard = Keyboard.Numeric, WidthRequest = 100
        }).ToArray();
        var alignment = columns.Select(column => new[] { AlignmentCode(column.HeaderStyle), AlignmentCode(column.DataStyle) }).ToArray();
        var visible = columns.Select(column => new CheckBox { IsChecked = column.Shown }).ToArray();
        var preview = new Grid { ColumnSpacing = 1, RowSpacing = 1 };
        var previewFrame = new Border { Padding = 6, StrokeThickness = 1,
            Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = preview } };
        fields.Children.Insert(0, previewFrame);
        string Name(int index) => headers.TryGetValue(index, out var title) && !string.IsNullOrWhiteSpace(title)
            ? title : $"Kolumna {index + 1}";
        Picker? columnPicker = null;
        void DrawPreview()
        {
            preview.Children.Clear();
            preview.ColumnDefinitions.Clear();
            preview.RowDefinitions.Clear();
            preview.ColumnSpacing = vertical.IsChecked ? 1 : 0;
            preview.RowSpacing = horizontal.IsChecked ? 1 : 0;
            preview.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#9BA6B1"), Color.FromArgb("#526174"));
            for (int row = 0; row < 4; row++)
                preview.RowDefinitions.Add(new RowDefinition { Height = row == 0 ? headerHeight.Value : height.Value });
            foreach (var column in order)
            {
                int index = Array.IndexOf(columns, column);
                if (!visible[index].IsChecked) continue;
                int position = preview.ColumnDefinitions.Count;
                double width = double.TryParse(sizes[index].Text, out var requested) ? Math.Clamp(requested, 32, 700) : 130;
                preview.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
                for (int row = 0; row < 4; row++)
                {
                    var cell = new Label { Text = row == 0 ? Name(index) : "Przykładowe dane",
                        Padding = new Thickness(5, 0), VerticalTextAlignment = TextAlignment.Center,
                        FontAttributes = row == 0 ? FontAttributes.Bold : FontAttributes.None,
                        LineBreakMode = LineBreakMode.TailTruncation };
                    cell.SetAppThemeColor(VisualElement.BackgroundColorProperty,
                        row == 0 ? Color.FromArgb("#DCE5EF") : Colors.White,
                        Color.FromArgb(row == 0 ? "#27374A" : "#20242B"));
                    int sectionIndex = row == 0 ? 0 : 1;
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (_, _) =>
                    {
                        if (columnPicker is not null) columnPicker.SelectedIndex = index;
                        section.SelectedIndex = sectionIndex;
                    };
                    cell.GestureRecognizers.Add(tap);
                    if (alignment[index][sectionIndex] >= 0) cell.HorizontalTextAlignment = (TextAlignment)alignment[index][sectionIndex];
                    preview.Add(cell, position, row);
                }
            }
        }
        foreach (var size in sizes) size.TextChanged += (_, _) => DrawPreview();
        foreach (var check in visible.Append(horizontal).Append(vertical)) check.CheckedChanged += (_, _) => DrawPreview();
        height.ValueChanged += (_, _) => DrawPreview();
        headerHeight.ValueChanged += (_, _) => DrawPreview();

        columnPicker = new Picker { ItemsSource = Enumerable.Range(0, Count).Select(Name).ToArray(), SelectedIndex = 0, WidthRequest = 220 };
        var list = new VerticalStackLayout { Spacing = 4, IsVisible = false };
        var selectedColumn = new ContentView();
        void ShowColumn()
        {
            var selected = columnPicker.SelectedIndex;
            if (selected < 0) return;
            var width = new Entry { Text = sizes[selected].Text, Placeholder = "Auto", Keyboard = Keyboard.Numeric, WidthRequest = 110 };
            width.TextChanged += (_, e) => sizes[selected].Text = e.NewTextValue;
            int part = Math.Max(0, section.SelectedIndex);
            var align = new Picker { ItemsSource = new[] { "Domyślne", "Do lewej", "Do środka", "Do prawej" },
                SelectedIndex = alignment[selected][part] + 1 };
            align.SelectedIndexChanged += (_, _) =>
            {
                alignment[selected][part] = align.SelectedIndex - 1;
                DrawPreview();
            };
            selectedColumn.Content = new HorizontalStackLayout { Spacing = 12, Children = {
                Ui.Field("Szerokość kolumny", width), Ui.Field("Wyrównanie", align)
            } };
        }
        section.SelectedIndexChanged += (_, _) => ShowColumn();
        columnPicker.SelectedIndexChanged += (_, _) => ShowColumn();
        fields.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = {
            Ui.Field("Kolumna", columnPicker),
            Ui.Action("Lista kolumn", () => { list.IsVisible = !list.IsVisible; return Task.CompletedTask; })
        } });
        fields.Children.Add(selectedColumn);
        void Draw()
        {
            foreach (var row in list.Children.OfType<HorizontalStackLayout>())
                foreach (var field in row.Children.OfType<VerticalStackLayout>()) field.Children.Clear();
            list.Children.Clear();
            DrawPreview();
            ShowColumn();
            for (int i = 0; i < order.Count; i++)
            {
                int position = i;
                int original = Array.IndexOf(columns, order[i]);
                void MoveDraft(int offset)
                {
                    int target = position + offset;
                    if (target < 0 || target >= order.Count) return;
                    (order[position], order[target]) = (order[target], order[position]);
                    Draw();
                }
                list.Add(new HorizontalStackLayout { Spacing = 8, Children = {
                    Ui.Field("Pokaż", visible[original]),
                    Ui.Field(headers.GetValueOrDefault(original, $"Kolumna {original + 1}"), sizes[original]),
                    Ui.Action("↑", () => { MoveDraft(-1); return Task.CompletedTask; }),
                    Ui.Action("↓", () => { MoveDraft(1); return Task.CompletedTask; })
                } });
            }
        }
        Draw();
        fields.Add(Ui.Note("Szerokość w pikselach logicznych. Puste pole przywraca szerokość domyślną."));
        fields.Add(list);
        ShowColumn();
        var page = new ContentPage { Title = "Parametry listy" };
        fields.Add(new HorizontalStackLayout { Children = {
            Ui.Action("Przywróć domyślne", () =>
            {
                order = columns.ToList();
                foreach (var size in sizes) size.Text = "";
                foreach (var pair in alignment) Array.Fill(pair, -1);
                foreach (var check in visible) check.IsChecked = true;
                horizontal.IsChecked = vertical.IsChecked = true;
                height.Value = 26;
                headerHeight.Value = 32;
                Draw();
                return Task.CompletedTask;
            }),
            Ui.PrimaryAction("OK", async () =>
            {
                if (!visible.Any(check => check.IsChecked))
                {
                    await page.DisplayAlertAsync("Lista kolumn", "Zostaw co najmniej jedną widoczną kolumnę.", "OK");
                    return;
                }
                var widths = new GridLength[Count];
                for (int i = 0; i < Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(sizes[i].Text)) widths[i] = defaults[i];
                    else if (int.TryParse(sizes[i].Text, out var width) && width is >= 32 and <= 700)
                        widths[i] = new GridLength(width);
                    else
                    {
                        await page.DisplayAlertAsync("Szerokość kolumny", "Wpisz liczbę od 32 do 700 albo zostaw puste pole.", "OK");
                        return;
                    }
                }
                for (int i = 0; i < order.Count; i++)
                {
                    var column = order[i];
                    column.Index = i;
                    int original = Array.IndexOf(columns, column);
                    column.Width = widths[original];
                    SetAlignment(column.HeaderStyle, alignment[original][0]);
                    SetAlignment(column.DataStyle, alignment[original][1]);
                    column.Shown = visible[original].IsChecked;
                    positions[i].Width = column.Shown ? column.Width : new GridLength(0);
                }
                CompactTable.SetRowHeight(this, height.Value);
                CompactTable.SetRowHeight(this, headerHeight.Value, header: true);
                HorizontalLines.IsChecked = horizontal.IsChecked;
                VerticalLines.IsChecked = vertical.IsChecked;
                WidthChanged?.Invoke();
                await page.Navigation.PopModalAsync();
                parametersOpen = false;
            }),
            Ui.Action("Anuluj", async () => await page.Navigation.PopModalAsync())
        } });
        page.Content = Ui.WithClose(page, new ScrollView { Content = fields }, async () => await page.Navigation.PopModalAsync());
        parametersOpen = true;
        page.Unloaded += (_, _) => parametersOpen = false;
        try { await host.Navigation.PushModalAsync(page); }
        catch { parametersOpen = false; throw; }
    }

    internal void AttachTableMenu(View table, string key)
    {
#if WINDOWS
        preferenceKey = "TableLayout." + key;
        RestoreLayout();
        if (table.BackgroundColor is null) table.BackgroundColor = Colors.Transparent;
        void Attach()
        {
            if (table.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
            var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
            var settings = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Parametry listy…" };
            settings.Click += async (_, _) => await ShowParameters();
            menu.Items.Add(settings);
            var shortcut = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.L,
                Modifiers = Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift
            };
            shortcut.Invoked += async (_, args) =>
            {
                if (parametersOpen) return;
                args.Handled = true;
                await ShowParameters();
            };
            element.KeyboardAccelerators.Add(shortcut);
            element.AddHandler(Microsoft.UI.Xaml.UIElement.RightTappedEvent,
                new Microsoft.UI.Xaml.Input.RightTappedEventHandler((_, args) =>
                {
                    args.Handled = true;
                    menu.ShowAt(element, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = args.GetPosition(element)
                    });
                }), true);
        }
        table.HandlerChanged += (_, _) => Attach();
        Attach();
#endif
    }

    internal void AttachHeader(Grid row, View cell, int index)
    {
#if WINDOWS
        headers[index] = cell switch { Label label => label.Text ?? "", Button button => button.Text ?? "", _ => "" };
        var column = columns[index];
        var grip = new BoxView
        {
            Color = Colors.Transparent, WidthRequest = 12, HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill, Margin = new Thickness(0, 0, -4, 0), ZIndex = 10
        };
        grip.SetBinding(Grid.ColumnProperty, new Binding(nameof(Slot.Index), source: column));
        grip.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(Slot.Shown), source: column));
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
                positions[resized.Index].Width = resized.Shown ? resized.Width : new GridLength(0);
                WidthChanged?.Invoke();
                args.Handled = true;
            };
            element.PointerReleased += (_, args) =>
            {
                if (pointerId != args.Pointer.PointerId) return;
                element.ReleasePointerCapture(args.Pointer);
                SaveLayout();
                args.Handled = true;
            };
            element.PointerCaptureLost += (_, _) => pointerId = null;
        }
        grip.HandlerChanged += (_, _) => AttachGrip();
        AttachGrip();
#endif
    }
}
