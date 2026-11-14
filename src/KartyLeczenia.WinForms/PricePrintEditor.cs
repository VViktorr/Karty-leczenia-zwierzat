using System.Net;

namespace KartyLeczenia.Windows;

internal sealed class PricePrintSettings
{
    public string Font = "Arial";
    public decimal Size = 9, Margin = 7, RowHeight = 0, Padding = 0.5m;
    public bool Landscape = true, Number = true, Batch, Price = true, Withdrawal = true, Legend = true;
    public int WithdrawalFormat = 1;
    public bool RouteCodes = true, LegendEveryPage;
    public int[] Fonts = new int[6], Sizes = new int[6], Alignments = new int[6];

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "price-print-winforms.json");
    static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    internal static PricePrintSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return System.Text.Json.JsonSerializer.Deserialize<PricePrintSettings>(File.ReadAllText(FilePath), JsonOptions)
                    ?? new();
        }
        catch (Exception ex) { CrashLog.Write("WinForms.PricePrint.Load", ex); }
        return new();
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception ex) { CrashLog.Write("WinForms.PricePrint.Save", ex); }
    }
    public decimal[] Widths = [4, 43, 14, 13, 40];
}

internal sealed class PricePrintEditor : Form
{
    readonly Func<IEnumerable<MedicinePrice>, string> render;

    internal PricePrintEditor(PricePrintSettings settings, MedicinePrice[] rows, string priceHeading, LocalCatalogue catalogue)
    {
        Text = "Ustawienia wydruku cennika";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 730);
        MinimumSize = new Size(660, 480);
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 224, AutoScroll = true, Padding = new Padding(8) };
        var sample = new WebBrowser { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, ScriptErrorsSuppressed = true };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };

        var paper = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 3, 0, 0) };
        var section = options;
        void Section(string title)
        {
            section = new FlowLayoutPanel { AutoSize = true, Width = 600, Padding = new Padding(0, 2, 0, 2) };
            section.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(3, 8, 12, 3) });
            options.Controls.Add(section);
            options.SetFlowBreak(section, true);
            void ResizeSection()
            {
                foreach (FlowLayoutPanel row in options.Controls.OfType<FlowLayoutPanel>())
                    row.MaximumSize = new Size(Math.Max(100, options.ClientSize.Width - 32), 0);
            }
            options.SizeChanged += (_, _) => ResizeSection();
            ResizeSection();
        }
        void Field(string label, Control control)
        {
            var field = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
            field.Controls.Add(new Label { Text = label, AutoSize = true });
            field.Controls.Add(control);
            section.Controls.Add(field);
        }
        ComboBox Select(string label, string[] items, int index)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            box.Items.AddRange(items);
            box.SelectedIndex = Math.Clamp(index, 0, items.Length - 1);
            Field(label, box);
            return box;
        }
        NumericUpDown Measure(string label, decimal value, decimal min, decimal max, int decimals = 0)
        {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1 : 0.5m, Width = 80 };
            Field(label, box);
            return box;
        }
        CheckBox Toggle(string label, bool value)
        {
            var box = new CheckBox { Text = label, Checked = value, AutoSize = true };
            section.Controls.Add(box);
            return box;
        }

        Section("Strona i tekst");
        string[] fonts = ["Arial", "Calibri", "Tahoma", "Segoe UI", "Times New Roman"];
        var font = Select("Czcionka", fonts, Array.IndexOf(fonts, settings.Font));
        var size = Measure("Rozmiar (pt)", settings.Size, 8, 12);
        var orientation = Select("Orientacja A4", ["Pozioma", "Pionowa"], settings.Landscape ? 0 : 1);
        var margin = Measure("Margines (mm)", settings.Margin, 4, 25, 1);
        var height = Measure("Min. wysokość (mm)", settings.RowHeight, 0, 18, 1);
        var padding = Measure("Odstęp pionowy (mm)", settings.Padding, 0, 5, 1);
        var format = Select("Treść karencji", ["Pełny zapis", "Zwięźle według wariantów", "Zwięźle według gatunków"], settings.WithdrawalFormat);
        format.Width = 210;
        var routes = Select("Oznaczenie drogi podania", ["Nazwy (domięśniowo)", "Kody (IM)"], settings.RouteCodes ? 1 : 0);
        Section("Widoczne kolumny i legenda");
        var number = Toggle("Lp.", settings.Number);
        var batch = Toggle("Seria", true);
        batch.Enabled = false;
        var price = Toggle("Cena", settings.Price);
        var withdrawal = Toggle("Karencja", settings.Withdrawal);
        var legend = Toggle("Legenda", settings.Legend);
        var repeatLegend = Toggle("Drogi podania na każdej stronie", settings.LegendEveryPage);
        repeatLegend.Enabled = legend.Checked;
        legend.CheckedChanged += (_, _) => repeatLegend.Enabled = legend.Checked;
        string[] headings = ["Lp.", "Nazwa leku", "Najnowsza seria", priceHeading, "Karencja"];
        Section("Proporcje kolumn");
        var widths = headings.Select((heading, index) => Measure("Proporcja: " + heading, settings.Widths[index], 1, 100)).ToArray();

        var typography = new (ComboBox Font, ComboBox Size, ComboBox Alignment)[6];
        for (int i = 0; i < typography.Length; i++)
        {
            Section(i == 0 ? "Wszystkie nagłówki kolumn" : "Komórki · " + headings[i - 1]);
            typography[i] = (
                Select("Czcionka", ["Domyślna", .. fonts], settings.Fonts.ElementAtOrDefault(i)),
                Select("Rozmiar (pt)", ["Domyślny", "8", "9", "10", "11", "12"], settings.Sizes.ElementAtOrDefault(i)),
                Select("Wyrównanie", ["Do lewej", "Do środka", "Do prawej"], settings.Alignments.ElementAtOrDefault(i)));
        }

        string TypographyCss(int index)
        {
            var (family, points, align) = typography[index];
            string fontCss = family.SelectedIndex > 0 ? $"font-family:\"{family.SelectedItem}\",sans-serif;" : "";
            string sizeCss = points.SelectedIndex > 0 ? $"font-size:{points.SelectedItem}pt;" : "";
            string alignment = align.SelectedIndex switch { 1 => "center", 2 => "right", _ => "left" };
            return $"{fontCss}{sizeCss}text-align:{alignment};";
        }

        render = data =>
        {
            bool[] shown = [number.Checked, true, batch.Checked, price.Checked, withdrawal.Checked];
            var columns = Enumerable.Range(0, shown.Length).Where(i => shown[i]).ToArray();
            decimal totalWidth = columns.Sum(i => widths[i].Value);
            var html = new System.Text.StringBuilder(FormattableString.Invariant($$"""
                <!doctype html><html lang="pl"><head><meta charset="utf-8"><meta http-equiv="X-UA-Compatible" content="IE=edge"><title>Cennik leków</title>
                <style>
                @page { size: A4 {{(orientation.SelectedIndex == 0 ? "landscape" : "portrait")}}; margin: {{margin.Value}}mm }
                body {font: {{size.Value}}pt "{{font.SelectedItem}}", sans-serif }
                @media screen {
                    html {background: #ddd}
                    body {box-sizing: border-box; width: {{(orientation.SelectedIndex == 0 ? 297 : 210)}}mm;
                          min-height: {{(orientation.SelectedIndex == 0 ? 210 : 297)}}mm;
                          margin: 12px auto; padding: {{margin.Value}}mm; background: white; border: 1px solid #999 }
                }
                table {width: 100%; table-layout: fixed; border-collapse: collapse}
                thead {display: table-header-group}
                tr {break-inside: avoid; page-break-inside: avoid}
                td, th {border: 1px solid #999; padding: {{padding.Value}}mm 3px; text-align: left; vertical-align: top; word-wrap: break-word}
                td {height: {{height.Value}}mm}
                th {background: #eee; {{TypographyCss(0)}}}
                .route-legend {border: 0; padding: 0; height: auto}
                </style></head><body><h2>Cennik leków</h2><p>{{DateTime.Today:dd.MM.yyyy}}</p>
                """));
            if (legend.Checked && !repeatLegend.Checked) html.Append(AdministrationRouteLegend.Html);
            html.Append("<table><colgroup>");
            foreach (int i in columns)
                html.Append(FormattableString.Invariant($"<col style='width:{100 * widths[i].Value / totalWidth:0.###}%'>"));
            html.Append("</colgroup><thead>");
            if (legend.Checked && repeatLegend.Checked)
                html.Append($"<tr><td class='route-legend' colspan='{columns.Length}'>")
                    .Append(AdministrationRouteLegend.Html).Append("</td></tr>");
            html.Append("<tr>");
            foreach (int i in columns) html.Append("<th>").Append(WebUtility.HtmlEncode(headings[i])).Append("</th>");
            html.Append("</tr></thead><tbody>");
            int index = 0;
            foreach (var row in data)
            {
                string[] values = [(++index).ToString(), row.Name, row.LatestBatch, row.Gross?.ToString("N2") ?? "",
                    withdrawal.Checked ? catalogue.PriceWithdrawal(row.Symbol, row.Name, format.SelectedIndex, routes.SelectedIndex == 1) : ""];
                html.Append("<tr>");
                foreach (int i in columns)
                    html.Append($"<td style='{TypographyCss(i + 1)}'>").Append(WebUtility.HtmlEncode(values[i]).ReplaceLineEndings("<br>")).Append("</td>");
                html.Append("</tr>");
            }
            return html.Append("</tbody></table></body></html>").ToString();
        };

        void UpdateSample()
        {
            bool landscape = orientation.SelectedIndex == 0;
            decimal available = (landscape ? 297 : 210) - 2 * margin.Value;
            bool[] visible = [number.Checked, true, batch.Checked, price.Checked, withdrawal.Checked];
            decimal total = widths.Where((_, i) => visible[i]).Sum(box => box.Value);
            paper.Text = $"A4 {(landscape ? "297 × 210" : "210 × 297")} mm · na kolumny {available:0.#} mm · "
                + string.Join("  |  ", headings.Where((_, i) => visible[i]).Zip(
                    widths.Where((_, i) => visible[i]), (heading, box) => $"{heading} {available * box.Value / total:0.#} mm"));
            sample.DocumentText = Render(rows.Take(12));
        }
        var refreshTimer = new System.Windows.Forms.Timer { Interval = 150 };
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            SaveSettings();
            UpdateSample();
        };
        void QueueSample()
        {
            refreshTimer.Stop();
            refreshTimer.Start();
        }
        foreach (var box in new[] { font, orientation, format, routes }
            .Concat(typography.SelectMany(style => new[] { style.Font, style.Size, style.Alignment })))
            box.SelectedIndexChanged += (_, _) => QueueSample();
        foreach (var box in new[] { size, margin, height, padding }.Concat(widths))
            box.ValueChanged += (_, _) => QueueSample();
        foreach (var box in new[] { number, batch, price, withdrawal, legend, repeatLegend })
            box.CheckedChanged += (_, _) => QueueSample();
        FormClosed += (_, _) => refreshTimer.Stop();
        Disposed += (_, _) => refreshTimer.Dispose();
        var print = new Button { Text = "Otwórz podgląd całego cennika", AutoSize = true };
        void SaveSettings()
        {
            settings.Font = (string)font.SelectedItem!;
            settings.Size = size.Value;
            settings.Landscape = orientation.SelectedIndex == 0;
            settings.Margin = margin.Value;
            settings.RowHeight = height.Value;
            settings.Padding = padding.Value;
            settings.WithdrawalFormat = format.SelectedIndex;
            settings.RouteCodes = routes.SelectedIndex == 1;
            settings.Number = number.Checked;
            settings.Batch = batch.Checked;
            settings.Price = price.Checked;
            settings.Withdrawal = withdrawal.Checked;
            settings.Legend = legend.Checked;
            settings.Widths = widths.Select(box => box.Value).ToArray();
            settings.LegendEveryPage = repeatLegend.Checked;
            settings.Fonts = typography.Select(style => style.Font.SelectedIndex).ToArray();
            settings.Sizes = typography.Select(style => style.Size.SelectedIndex).ToArray();
            settings.Alignments = typography.Select(style => style.Alignment.SelectedIndex).ToArray();
            settings.Save();
        }
        FormClosed += (_, _) => SaveSettings();
        print.Click += (_, _) =>
        {
            SaveSettings();
            using var preview = new PricePrintPreview(Render(rows), settings.Landscape, settings.Margin);
            DialogPlacement.Show(preview, this, "price-print-preview");
        };
        var close = new Button { Text = "Zamknij", AutoSize = true, DialogResult = DialogResult.Cancel };
        var printNow = new Button { Text = "Drukuj", AutoSize = true };
        printNow.Click += (_, _) => print.PerformClick();
        actions.Controls.AddRange([print, printNow, close]);
        Controls.Add(sample);
        Controls.Add(paper);
        Controls.Add(options);
        Controls.Add(actions);
        CancelButton = close;
        Shown += (_, _) => UpdateSample();
    }

    internal string Render(IEnumerable<MedicinePrice> rows) => render(rows);
}
