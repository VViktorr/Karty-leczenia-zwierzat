namespace KartyLeczenia.Windows;

internal sealed class CatalogueProductPicker : Form
{
    internal MedicinePrice? Selection { get; private set; }

    internal CatalogueProductPicker(ConnectionOptions options, string initialSymbol, MedicinePrice[]? pending = null)
    {
        Text = pending is null ? "Wybierz kartotekę Subiekta" : "Leki do uzupełnienia";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(670, 480);
        MinimumSize = new Size(400, 300);
        AutoScaleMode = AutoScaleMode.Dpi;
        var query = new TextBox { Width = 290, Text = initialSymbol, PlaceholderText = "Symbol lub nazwa z Subiekta" };
        var search = new Button { Text = "Szukaj", AutoSize = true };
        var accept = new Button { Text = "Wybierz", AutoSize = true, Enabled = false };
        var message = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
            WrapContents = true, Padding = new Padding(8) };
        toolbar.Controls.AddRange([query, search, accept]);
        Controls.Add(list);
        Controls.Add(toolbar);
        Controls.Add(message);

        async Task Search()
        {
            search.Enabled = false;
            accept.Enabled = false;
            list.Items.Clear();
            message.Text = "Wczytywanie kartotek…";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                string filter = query.Text.Trim();
                var results = pending is null
                    ? await MedicineStockService.FindCatalogueProducts(options, filter, timeout.Token)
                    : pending.Where(row => row.Symbol.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || row.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                if (IsDisposed) return;
                list.Items.AddRange(results.Select(row => new Item(row)).ToArray());
                if (results.Length == 1) list.SelectedIndex = 0;
                message.Text = results.Length == 0 ? "Brak pasujących kartotek." :
                    pending is not null ? $"Kartoteki wymagające uzupełnienia: {results.Length}." :
                    results.Length == 100 ? "Pokazano pierwsze 100 kartotek. Zawęź wyszukiwanie." : "";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) message.Text = SubiektConnectionService.FriendlyError(ex);
            }
            finally { if (!IsDisposed) search.Enabled = true; }
        }

        void Choose()
        {
            if (list.SelectedItem is not Item item) return;
            Selection = item.Product;
            DialogResult = DialogResult.OK;
        }

        search.Click += async (_, _) => await Search();
        accept.Click += (_, _) => Choose();
        list.DoubleClick += (_, _) => Choose();
        list.SelectedIndexChanged += (_, _) => accept.Enabled = list.SelectedIndex >= 0;
        AcceptButton = search;
        Shown += async (_, _) =>
        {
            query.SelectAll();
            await Search();
        };
        AppearanceThemes.ApplySurface(this, AppearanceThemes.Current);
    }

    sealed record Item(MedicinePrice Product)
    {
        public override string ToString() => $"{Product.Symbol} · {Product.Name}";
    }
}
