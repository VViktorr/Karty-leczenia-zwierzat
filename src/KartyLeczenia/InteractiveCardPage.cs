namespace KartyLeczenia.Views;

internal sealed class InteractiveCardPage : ContentPage
{
    static readonly GridLength[] Columns = [
        54, new(1.15, GridUnitType.Star), new(1.25, GridUnitType.Star), new(0.85, GridUnitType.Star),
        new(1.25, GridUnitType.Star), new(1.25, GridUnitType.Star), new(1.4, GridUnitType.Star),
        new(0.9, GridUnitType.Star), new(0.9, GridUnitType.Star)
    ];

    internal InteractiveCardPage(IReadOnlyDictionary<string, Editor> header, IEnumerable<TreatmentRow> rows,
        Func<TreatmentRow, MedicineCatalogue.Entry?> match, Func<ContentPage, string, Task> output)
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
        var table = new VerticalStackLayout { Spacing = 4, Children = {
            CompactTable.Header(Columns, "Druk", "Zwierzęta", "Lek", "Ilość / seria", "Droga podania",
                "Rozpoznanie", "Dawkowanie", "Karencja", "Uwagi") } };
        foreach (var row in rows) table.Add(EditRow(row, match(row)));
        var toolbar = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = {
            Ui.Action("Dane karty", () => { details.IsVisible = !details.IsVisible; return Task.CompletedTask; }),
            Ui.Action("Podgląd wydruku", () => output(this, "preview")),
            Ui.Action("Drukuj", () => output(this, "print")),
            Ui.PrimaryAction("Eksport do PDF", () => output(this, "pdf"))
        } };
        var root = new Grid { Padding = 12, RowSpacing = 8, RowDefinitions = {
            new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star }
        } };
        root.Add(toolbar, 0, 0);
        root.Add(new ScrollView { Orientation = ScrollOrientation.Both, Content = new VerticalStackLayout {
            MinimumWidthRequest = 1100, Spacing = 8, Children = { details, table }
        } }, 0, 1);
        Content = Ui.WithClose(this, root, async () => { await Navigation.PopModalAsync(); });
    }

    static Grid EditRow(TreatmentRow row, MedicineCatalogue.Entry? entry)
    {
        var editors = new Dictionary<string, Editor>();
        bool refreshing = false;
        Editor Field(string key, string hint)
        {
            var input = new Editor { Text = row.Get(key), Placeholder = hint, AutoSize = EditorAutoSizeOption.TextChanges };
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
        void RefreshChoices()
        {
            var value = row.Get("Gatunek").Trim();
            choices = variants.Where(v => value.Length == 0 ||
                string.Equals(v.Species, value, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(value, "Bydło", StringComparison.OrdinalIgnoreCase) && MedicineCatalogue.IsCattle(v))).ToArray();
            picker.ItemsSource = choices.Select(v => string.Join(" · ", new[] { v.Route, v.Species, v.Indication, v.Dosing }
                .Where(text => text.Length > 0))).ToArray();
            picker.SelectedIndex = -1;
            picker.IsVisible = choices.Length > 0;
        }
        void ApplyChoice(MedicineCatalogue.Variant variant)
        {
            if (entry is null) return;
            MedicineCatalogue.ApplySelected(row, entry, variant);
            refreshing = true;
            foreach (var field in editors) field.Value.Text = row.Get(field.Key);
            refreshing = false;
        }
        species.TextChanged += (_, _) => { if (!refreshing) RefreshChoices(); };
        species.Unfocused += (_, _) => { if (choices.Length == 1 && row.Get("Droga podania").Length == 0) ApplyChoice(choices[0]); };
        picker.SelectedIndexChanged += (_, _) =>
        {
            int index = picker.SelectedIndex;
            if (index >= 0 && index < choices.Length) ApplyChoice(choices[index]);
        };
        var line = CompactTable.Row(Columns, false, printed,
            new VerticalStackLayout { Children = { species, Field("Opis zwierząt / numer siedziby stada", "Opis / nr stada"), Field("Liczba zwierząt", "Liczba") } },
            Field("Produkt", "Nazwa leku"),
            new VerticalStackLayout { Children = { Field("Ilość wydana", "Ilość"), Field("Seria", "Seria"), Field("Ważność", "Ważność") } },
            new VerticalStackLayout { Children = { picker, route } },
            Field("Rozpoznanie", "Rozpoznanie"),
            Field("Dawkowanie i okres stosowania", "Dawkowanie"),
            Field("Karencja", "Karencja"),
            Field("Zalecenia / uwagi", "Uwagi"));
        RefreshChoices();
        return line;
    }
}
