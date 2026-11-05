using System.Diagnostics;

namespace KartyLeczenia;

sealed class RegistryDialog : Form
{
    readonly NumericUpDown id = new() { Minimum = 1, Maximum = 999999999, Width = 120 };
    readonly TextBox medicine = new() { Width = 550 };
    readonly ComboBox species = new() { Width = 180 }, route = new() { Width = 180 }, food = new() { Width = 180 };
    readonly TextBox excerpt = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    readonly TextBox result = new() { Multiline = true, Height = 100, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    readonly Label info = new() { AutoSize = true, MaximumSize = new Size(950, 0) };
    readonly CheckBox confirmed = new() { AutoSize = true, Text = "Potwierdzam zgodność produktu, gatunku, drogi podania i wybranej karencji z charakterystyką." };
    readonly Button apply = new() { Text = "Zastosuj do pozycji", AutoSize = true, Enabled = false };
    RegistryDocument? document;
    public RegistrySelection? Selection { get; private set; }

    public RegistryDialog(string product, int? mapped)
    {
        Text = "Karencja z RPL — weryfikacja przed zastosowaniem"; Width = 1050; Height = 800;
        medicine.Text = product;
        id.Value = mapped ?? (product.StartsWith("FLUNIVIS", StringComparison.OrdinalIgnoreCase) ? 50492 : product.StartsWith("FLORDOFEN 300", StringComparison.OrdinalIgnoreCase) ? 32709 : 1);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(10) };
        for (int i = 0; i < 9; i++) layout.RowStyles.Add(new RowStyle(i is 3 or 6 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 65 : i == 6 ? 35 : 0));
        Controls.Add(layout);
        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        top.Controls.Add(new Label { Text = "ID RPL:", AutoSize = true }); top.Controls.Add(id); top.Controls.Add(medicine);
        var fetch = new Button { Text = "Pobierz aktualną ChPL", AutoSize = true };
        top.Controls.Add(fetch);
        var open = new Button { Text = "Otwórz źródło PDF", AutoSize = true };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(RegistryService.Url((int)id.Value)) { UseShellExecute = true });
        top.Controls.Add(open);
        var search = new Button { Text = "Szukaj leku w RPL", AutoSize = true };
        search.Click += (_, _) => Process.Start(new ProcessStartInfo("https://rejestrymedyczne.ezdrowie.gov.pl/rpl/search/public") { UseShellExecute = true });
        top.Controls.Add(search);
        layout.Controls.Add(top);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(950, 0), Text = "Sprawdź nazwę, moc i postać w źródłowym PDF. Dla innego produktu podaj jego ID RPL. Numer jest tylko propozycją, nie automatycznym dopasowaniem. Karencja dotyczy stosowania zgodnego z ChPL; inne stosowanie wymaga ustalenia przez lekarza." });
        layout.Controls.Add(info);
        layout.Controls.Add(excerpt);
        var choices = new FlowLayoutPanel { AutoSize = true };
        species.Items.AddRange(["Bydło", "Świnie", "Konie", "Owce", "Kozy", "Drób"]);
        route.Items.AddRange(["Domięśniowo", "Podskórnie", "Dożylnie", "Doustnie", "Dowymieniowo"]);
        food.Items.AddRange(["Tkanki jadalne", "Mleko", "Jaja", "Miód", "Tkanki jadalne i mleko"]);
        foreach (var entry in new[] { ("Gatunek", species), ("Droga podania", route), ("Produkt spożywczy", food) })
        { choices.Controls.Add(new Label { AutoSize = true, Text = entry.Item1 }); choices.Controls.Add(entry.Item2); }
        layout.Controls.Add(choices);
        var copy = new Button { Text = "Kopiuj zaznaczony fragment do karencji", AutoSize = true };
        copy.Click += (_, _) => { if (excerpt.SelectionLength > 0) result.Text = excerpt.SelectedText; };
        layout.Controls.Add(copy);
        layout.Controls.Add(result);
        layout.Controls.Add(confirmed);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(apply); var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel }; buttons.Controls.Add(cancel); layout.Controls.Add(buttons);
        CancelButton = cancel;
        void InvalidateChoice() { confirmed.Checked = false; apply.Enabled = false; }
        foreach (var control in new Control[] { medicine, species, route, food, result }) control.TextChanged += (_, _) => InvalidateChoice();
        id.ValueChanged += (_, _) => { document = null; excerpt.Clear(); result.Clear(); info.Text = "Pobierz charakterystykę dla wybranego ID."; InvalidateChoice(); };
        confirmed.CheckedChanged += (_, _) => apply.Enabled = confirmed.Checked && document != null && !string.IsNullOrWhiteSpace(medicine.Text) && !string.IsNullOrWhiteSpace(species.Text) && !string.IsNullOrWhiteSpace(route.Text) && !string.IsNullOrWhiteSpace(food.Text) && !string.IsNullOrWhiteSpace(result.Text);
        fetch.Click += async (_, _) =>
        {
            fetch.Enabled = false; id.Enabled = false; document = null; InvalidateChoice(); excerpt.Clear(); result.Clear();
            int requested = (int)id.Value;
            try
            {
                RegistryDocument loaded;
                try { loaded = await RegistryService.Fetch(requested); }
                catch (Exception ex)
                {
                    var cached = RegistryService.Cached(requested);
                    if (cached is null || MessageBox.Show(this, "Pobranie nie powiodło się: " + ex.Message + "\n\nDostępna kopia z " + cached.DownloadedUtc.ToLocalTime().ToString("g") + ". Nie potwierdzono jej aktualności. Użyć kopii do ręcznej weryfikacji?", "Brak połączenia z rejestrem", MessageBoxButtons.YesNo) != DialogResult.Yes) throw;
                    loaded = cached;
                }
                document = loaded; excerpt.Text = "IDENTYFIKACJA Z PDF — SPRAWDŹ NAZWĘ, MOC I POSTAĆ:\r\n" + loaded.ProductIdentity + "\r\n\r\nOKRESY KARENCJI:\r\n" + loaded.WithdrawalSection;
                info.Text = "RPL, ID " + loaded.Id + " | pobrano: " + loaded.DownloadedUtc.ToLocalTime().ToString("g") + "\n" + loaded.Url + "\nZaznacz odpowiedni fragment lub wpisz karencję na jego podstawie. Data pobrania nie jest datą aktualizacji ChPL.";
            }
            catch (Exception ex) { info.Text = "Nie pobrano danych. " + ex.Message; }
            finally { fetch.Enabled = true; id.Enabled = true; }
        };
        apply.Click += (_, _) =>
        {
            if (document is null || !apply.Enabled) return;
            Selection = new(document, species.Text.Trim(), route.Text.Trim(), food.Text.Trim(), result.Text.Trim(), medicine.Text.Trim());
            DialogResult = DialogResult.OK;
        };
    }
}
