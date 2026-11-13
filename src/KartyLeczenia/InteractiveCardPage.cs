namespace KartyLeczenia.Views;

internal sealed class InteractiveCardPage : ContentPage
{
    protected override void OnAppearing()
    {
        base.OnAppearing();
        CrashLog.Write("InteractiveCardPage.Open");
    }

    readonly TableColumns columns = new(52, 110, 180, 125, 155, 112, 215, 185, 116);

    internal InteractiveCardPage(IReadOnlyDictionary<string, Editor> header, IEnumerable<TreatmentRow> rows,
        Func<TreatmentRow, MedicineCatalogue.Entry?> match, Func<ContentPage, string, Task> output,
        int withdrawalFormatIndex, Action<int> changeWithdrawalFormat)
    {
        Title = "Karta leczenia";
        var fields = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var pair in header.Where(pair => pair.Key != "Dokument źródłowy"))
        {
            var target = pair.Value;
            var input = new Editor { Text = target.Text, WidthRequest = 240, HeightRequest = 58 };
            input.TextChanged += (_, e) => target.Text = e.NewTextValue;
            fields.Add(Ui.Field(pair.Key, input));
        }
        var details = new VerticalStackLayout { Children = { fields }, IsVisible = false };
        var tableHeader = CompactTable.Header(columns, "Druk", "Zwierzęta", "Lek", "Ilość / seria",
            "Droga podania", "Rozpoznanie", "Dawkowanie", "Karencja", "Uwagi");
        tableHeader.MinimumHeightRequest = 32;
        foreach (var label in tableHeader.Children.OfType<Label>())
        {
            label.Margin = new Thickness(3, 3);
        }
        var table = new VerticalStackLayout { Spacing = 0, Children = { tableHeader } };
        var extraFields = new List<View>();
        foreach (var row in rows) table.Add(EditRow(row, match(row), extraFields));
        var withdrawalFormat = new Picker
        {
            ItemsSource = new[] { "Pełna karencja", "Skrót karencji" },
            SelectedIndex = withdrawalFormatIndex,
            WidthRequest = 175
        };
        withdrawalFormat.SelectedIndexChanged += (_, _) => changeWithdrawalFormat(withdrawalFormat.SelectedIndex);
        bool showDetails = false;
        var editing = new HorizontalStackLayout { Spacing = 4, Children = {
            Ui.Action("Szczegóły pozycji", () => {
                showDetails = !showDetails;
                foreach (var field in extraFields) field.IsVisible = showDetails;
                return Task.CompletedTask;
            }),
            Ui.Action("Dane karty", () => { details.IsVisible = !details.IsVisible; return Task.CompletedTask; })
        } };
        var outputActions = new HorizontalStackLayout { Spacing = 4, Children = {
            Ui.Action("Podgląd wydruku", () => output(this, "preview")),
            Ui.Action("Drukuj", () => output(this, "print")),
            Ui.PrimaryAction("Eksport do PDF", () => output(this, "pdf"))
        } };
        var format = Ui.Field("Karencja na wydruku", withdrawalFormat);
        editing.Margin = outputActions.Margin = new Thickness(0, 0, 18, 4);
        var toolbar = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.End,
            Children = { editing, outputActions, format }
        };
        var root = new Grid { Padding = 12, RowSpacing = 8, RowDefinitions = {
            new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }
        } };
        root.Add(toolbar, 0, 0);
        var content = new VerticalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Start, Children = { details, table } };
        root.Add(new ScrollView { Orientation = ScrollOrientation.Both, Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Always }, 0, 1);
        void UpdateWidth() => content.WidthRequest = columns.FixedWidth + 30;
        columns.WidthChanged += UpdateWidth;
        UpdateWidth();
        var routeLegend = Ui.Note("Drogi podania · " + AdministrationRouteLegend.Description);
        routeLegend.VerticalOptions = LayoutOptions.Center;
        Content = Ui.WithClose(this, root, async () => { await Navigation.PopModalAsync(); }, routeLegend);
    }

    Grid EditRow(TreatmentRow row, MedicineCatalogue.Entry? entry, List<View> extraFields)
    {
        var editors = new Dictionary<string, Editor>();
        bool refreshing = false;
        Editor Field(string key, string hint)
        {
            var input = new Editor { Text = row.Get(key), Placeholder = hint, AutoSize = EditorAutoSizeOption.TextChanges, VerticalOptions = LayoutOptions.Start, MinimumHeightRequest = 32 };
            editors.Add(key, input);
            input.TextChanged += (_, e) => { if (!refreshing) row.Set(key, e.NewTextValue ?? ""); };
            return input;
        }
        var printed = new CheckBox { IsChecked = row.Printed, VerticalOptions = LayoutOptions.Start };
        printed.CheckedChanged += (_, e) => row.Printed = e.Value;
        var species = Field("Gatunek", "Gatunek");
        var route = Field("Droga podania", "Droga podania");
        var picker = new Picker { Title = "Wybierz wariant", IsVisible = false };
        var variants = entry is { Excluded: false } ? MedicineCatalogue.DistinctVariants(entry) : [];
        MedicineCatalogue.Variant[] choices = [];
        var pdf = new Button { Text = "Otwórz ChPL", IsVisible = false };
        string SelectedDocument()
        {
            var selected = picker.SelectedIndex;
            if (selected >= 0 && selected < choices.Length)
                return choices[selected].PdfPath;
            var documents = variants.Select(v => v.PdfPath).Where(path => path.Length > 0)
                .Distinct().Take(2).ToArray();
            return documents.Length == 1 ? documents[0] : "";
        }

        pdf.Clicked += async (_, _) =>
        {
            var path = SelectedDocument();
            if (path.Length == 0) return;
            try { await MedicineCatalogue.OpenPdfAsync(path); }
            catch (Exception ex) { await Shell.Current.DisplayAlertAsync("ChPL", ex.Message, "OK"); }
        };
        void RefreshChoices()
        {
            var value = row.Get("Gatunek").Trim();
            choices = variants.Where(v => value.Length == 0 ||
                MedicineCatalogue.SameSpecies(v.Species, value) ||
                (string.Equals(value, "Bydło", StringComparison.OrdinalIgnoreCase) && MedicineCatalogue.IsCattle(v))).ToArray();
            refreshing = true;
            var labels = choices.Select(v => string.Join(" · ", new[] { v.Route, v.Species, v.Indication }
                .Where(text => text.Length > 0))).ToArray();
            var repeatedLabels = labels.GroupBy(label => label).Where(group => group.Count() > 1)
                .Select(group => group.Key).ToHashSet();
            picker.ItemsSource = choices.Select((v, index) => repeatedLabels.Contains(labels[index])
                ? string.Join(" · ", new[] { labels[index], v.Dosing,
                    MedicineCatalogue.Withdrawal(v).Replace("\n", "; "), v.Restrictions }.Where(text => text.Length > 0))
                : labels[index]).ToArray();
            picker.SelectedIndex = Array.FindIndex(choices, v =>
                v.Route == row.Get("Droga podania") && v.Dosing == row.Get("Dawkowanie i okres stosowania") &&
                MedicineCatalogue.Withdrawal(v) == row.Get("Karencja"));
            picker.IsVisible = choices.Length > 1 || (choices.Length == 1 && picker.SelectedIndex < 0);
            route.IsVisible = !picker.IsVisible || picker.SelectedIndex < 0;
            pdf.IsVisible = SelectedDocument().Length > 0;
            refreshing = false;
        }
        void ApplyChoice(MedicineCatalogue.Variant variant)
        {
            if (entry is null) return;
            MedicineCatalogue.ApplySelected(row, entry, variant);
            refreshing = true;
            foreach (var field in editors)
            {
                var value = row.Get(field.Key);
                field.Value.Text = field.Key == "Opis zwierząt / numer siedziby stada" && value == row.Get("Gatunek")
                    ? "" : value;
            }
            refreshing = false;
        }
        species.TextChanged += (_, _) => { if (!refreshing) RefreshChoices(); };
        species.Unfocused += (_, _) => { if (choices.Length == 1 && row.Get("Droga podania").Length == 0) ApplyChoice(choices[0]); };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            pdf.IsVisible = SelectedDocument().Length > 0;
            int index = picker.SelectedIndex;
            if (index >= 0 && index < choices.Length) ApplyChoice(choices[index]);
            route.IsVisible = !picker.IsVisible || index < 0;
        };
        var description = Field("Opis zwierząt / numer siedziby stada", "Opis / nr stada");
        if (string.Equals(description.Text, species.Text, StringComparison.OrdinalIgnoreCase))
        {
            refreshing = true;
            description.Text = "";
            refreshing = false;
        }
        var count = Field("Liczba zwierząt", "Liczba");
        var expiry = Field("Ważność", "Ważność");
        foreach (var field in new[] { description, count, expiry })
        {
            field.IsVisible = false;
            extraFields.Add(field);
        }
        var line = CompactTable.Row(columns, false, printed,
            new VerticalStackLayout { Children = { species, description, count } },
            new VerticalStackLayout { Children = { Field("Produkt", "Nazwa leku"), pdf } },
            new VerticalStackLayout { Children = { Field("Ilość wydana", "Ilość"), Field("Seria", "Seria"), expiry } },
            new VerticalStackLayout { Children = { picker, route } },
            Field("Rozpoznanie", "Rozpoznanie"),
            Field("Dawkowanie i okres stosowania", "Dawkowanie"),
            Field("Karencja", "Karencja"),
            Field("Zalecenia / uwagi", "Uwagi"));
        RefreshChoices();
        return line;
    }
}
