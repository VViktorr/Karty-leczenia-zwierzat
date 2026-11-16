using System.Net;
using System.Drawing.Text;

namespace KartyLeczenia.Windows;

internal sealed class PricePrintSettings
{
    public string Font = "Arial";
    public int FontWeight = 400;
    // Store font family names rather than indices (installed fonts can change or reorder).
    public string[] FontNames = new string[6];
    // Zero means inherit the global size; positive values are points.
    public decimal[] FontSizes = new decimal[6];
    public int[] FontWeights = new int[6];
    public decimal Size = 9, Margin = 7, RowHeight = 0, Padding = 0.5m;
    public bool Landscape = true, Number = true, Batch = true, Price = true, Withdrawal = true, Legend = true;
    public int WithdrawalFormat = 1;
    public bool RouteCodes = true, LegendEveryPage;
    public int[] Fonts = new int[6], Sizes = new int[6], Alignments = new int[6];

    internal static readonly string[] LegacyFonts = ["Arial", "Calibri", "Tahoma", "Segoe UI", "Times New Roman"];

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "price-print-winforms.json");
    static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    internal static PricePrintSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return System.Text.Json.JsonSerializer.Deserialize<PricePrintSettings>(File.ReadAllText(FilePath), JsonOptions)
                    is { } loaded ? Migrate(loaded) : new();
        }
        catch (Exception ex) { CrashLog.Write("WinForms.PricePrint.Load", ex); }
        return new();
    }

    static PricePrintSettings Migrate(PricePrintSettings settings)
    {
        settings.FontNames = Resize(settings.FontNames);
        settings.FontSizes = Resize(settings.FontSizes);
        settings.FontWeights = Resize(settings.FontWeights);
        settings.Fonts = Resize(settings.Fonts);
        settings.Sizes = Resize(settings.Sizes);
        settings.Alignments = Resize(settings.Alignments);
        settings.Widths = settings.Widths?.Length == 5 ? settings.Widths : [4, 43, 14, 13, 40];
        settings.Font = string.IsNullOrWhiteSpace(settings.Font) ? "Arial" : settings.Font;
        settings.FontWeight = NormalizeWeight(settings.FontWeight);
        for (int i = 0; i < 6; i++)
        {
            // Previous versions serialized a font index in the five-item hard-coded list.
            if (string.IsNullOrWhiteSpace(settings.FontNames[i]) && settings.Fonts[i] is >= 1 and <= 5)
                settings.FontNames[i] = LegacyFonts[settings.Fonts[i] - 1];
            // Previous size values were indices: 1=8, 2=9 ... 5=12.
            if (settings.FontSizes[i] == 0 && settings.Sizes[i] is >= 1 and <= 5)
                settings.FontSizes[i] = 7 + settings.Sizes[i];
            settings.FontSizes[i] = Math.Clamp(settings.FontSizes[i], 0, 48);
            settings.FontWeights[i] = settings.FontWeights[i] == 0 ? 0 : NormalizeWeight(settings.FontWeights[i]);
        }
        return settings;
    }

    static T[] Resize<T>(T[]? values)
    {
        var result = new T[6];
        if (values is not null) Array.Copy(values, result, Math.Min(6, values.Length));
        return result;
    }

    internal static int NormalizeWeight(int value) => value is 400 or 500 or 600 or 700 or 800 or 900 ? value : 400;

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
        Size = new Size(1120, 760);
        MinimumSize = new Size(660, 480);
        AutoScaleMode = AutoScaleMode.Dpi;
        var options = new TabControl { Dock = DockStyle.Top, Height = 155, Padding = new Point(12, 4) };
        var sample = new WebBrowser { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, ScriptErrorsSuppressed = true };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        var paper = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 3, 0, 0) };
        FlowLayoutPanel section = null!;

        void Section(string title)
        {
            var page = new TabPage(title);
            section = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(5) };
            page.Controls.Add(section);
            options.TabPages.Add(page);
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
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = label == "Czcionka" ? 200 : 125 };
            box.Items.AddRange(items);
            box.SelectedIndex = Math.Clamp(index, 0, items.Length - 1);
            Field(label, box);
            return box;
        }
        NumericUpDown Measure(string label, decimal value, decimal min, decimal max, int decimals = 0)
        {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1 : 0.5m, Width = 60 };
            Field(label, box);
            return box;
        }
        CheckBox Toggle(string label, bool value)
        {
            var box = new CheckBox { Text = label, Checked = value, AutoSize = true };
            section.Controls.Add(box);
            return box;
        }

        Section("Strona");
        string[] fonts;
        try
        {
            using var installed = new InstalledFontCollection();
            fonts = installed.Families.Select(f => f.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Append(settings.Font)
                .Concat(settings.FontNames?.Where(name => !string.IsNullOrWhiteSpace(name)) ?? [])
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            CrashLog.Write("WinForms.PricePrint.SystemFonts", ex);
            fonts = PricePrintSettings.LegacyFonts.Append(settings.Font)
                .Concat(settings.FontNames?.Where(name => !string.IsNullOrWhiteSpace(name)) ?? [])
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        string[] weights = ["Domyślna", "Normalna (400)", "Średnia (500)", "Półgruba (600)", "Gruba (700)", "Bardzo gruba (800)", "Najgrubsza (900)"];
        int[] weightValues = [0, 400, 500, 600, 700, 800, 900];
        int WeightIndex(int value) => Math.Max(0, Array.IndexOf(weightValues, value));
        var font = Select("Czcionka", fonts, Math.Max(0, Array.FindIndex(fonts, name => string.Equals(name, settings.Font, StringComparison.OrdinalIgnoreCase))));
        var size = Measure("Rozmiar (pt)", settings.Size, 6, 48, 1);
        var weight = Select("Grubość", weights[1..], Math.Max(0, WeightIndex(settings.FontWeight) - 1));
        weight.Width = 155;
        var orientation = Select("Orientacja A4", ["Pozioma", "Pionowa"], settings.Landscape ? 0 : 1);
        var margin = Measure("Margines (mm)", settings.Margin, 4, 25, 1);
        var height = Measure("Min. wysokość (mm)", settings.RowHeight, 0, 18, 1);
        var padding = Measure("Odstęp pionowy (mm)", settings.Padding, 0, 5, 1);
        Section("Treść i legenda");
        var format = Select("Treść karencji", ["Pełny zapis", "Zwięźle według wariantów", "Zwięźle według gatunków"], settings.WithdrawalFormat);
        format.Width = 185;
        var routes = Select("Oznaczenie drogi podania", ["Nazwy (domięśniowo)", "Kody (IM)"], settings.RouteCodes ? 1 : 0);
        section.SetFlowBreak(section.Controls[section.Controls.Count - 1], true);
        var number = Toggle("Lp.", settings.Number);
        var batch = Toggle("Seria", settings.Batch);
        var price = Toggle("Cena", settings.Price);
        var withdrawal = Toggle("Karencja", settings.Withdrawal);
        var legend = Toggle("Legenda", settings.Legend);
        var repeatLegend = Toggle("Drogi podania na każdej stronie", settings.LegendEveryPage);
        repeatLegend.Enabled = legend.Checked;
        legend.CheckedChanged += (_, _) => repeatLegend.Enabled = legend.Checked;
        string[] headings = ["Lp.", "Nazwa leku", "Najnowsza seria", priceHeading, "Karencja"];
        Section("Tabela");
        var target = Select("Edytuj", ["Nagłówki kolumn", .. headings], 0);
        var widths = new NumericUpDown[headings.Length];
        var typographyFields = new Control[headings.Length + 1][];

        var typography = new (ComboBox Font, NumericUpDown Size, ComboBox Weight, ComboBox Alignment)[6];
        for (int i = 0; i < typography.Length; i++)
        {
            int start = section.Controls.Count;
            if (i > 0)
                widths[i - 1] = Measure("Proporcja szerokości", settings.Widths[i - 1], 1, 100);
            string currentFont = settings.FontNames?.ElementAtOrDefault(i) ?? "";
            var familyItems = new[] { "Domyślna", .. fonts };
            int familyIndex = string.IsNullOrWhiteSpace(currentFont) ? 0
                : Array.FindIndex(familyItems, name => string.Equals(name, currentFont, StringComparison.OrdinalIgnoreCase));
            int fontWeight = settings.FontWeights?.ElementAtOrDefault(i) ?? 0;
            decimal points = settings.FontSizes?.ElementAtOrDefault(i) ?? 0;
            typography[i] = (
                Select("Czcionka", familyItems, Math.Max(0, familyIndex)),
                Measure("Rozmiar (pt; 0 = domyślny)", points, 0, 48, 1),
                Select("Grubość", weights, WeightIndex(fontWeight)),
                Select("Wyrównanie", ["Do lewej", "Do środka", "Do prawej"], settings.Alignments.ElementAtOrDefault(i)));
            typography[i].Weight.Width = 155;
            typographyFields[i] = section.Controls.Cast<Control>().Skip(start).ToArray();
        }
        void ShowTypography()
        {
            section.SuspendLayout();
            for (int i = 0; i < typographyFields.Length; i++)
                foreach (var field in typographyFields[i])
                    field.Visible = i == target.SelectedIndex;
            section.ResumeLayout();
        }
        target.SelectedIndexChanged += (_, _) => ShowTypography();
        ShowTypography();

        // CSS-escape quotes and backslashes in a Windows font family name.
        static string CssFont(string family) => "\"" + family.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "").Replace("\n", "") + "\",sans-serif";

        string TypographyCss(int index)
        {
            var (family, points, selectedWeight, align) = typography[index];
            string fontCss = family.SelectedIndex > 0 ? $"font-family:{CssFont((string)family.SelectedItem!)};" : "";
            string sizeCss = points.Value > 0 ? FormattableString.Invariant($"font-size:{points.Value:0.#}pt;") : "";
            string weightCss = selectedWeight.SelectedIndex > 0 ? $"font-weight:{weightValues[selectedWeight.SelectedIndex]};" : "";
            string alignment = align.SelectedIndex switch { 1 => "center", 2 => "right", _ => "left" };
            return $"{fontCss}{sizeCss}{weightCss}text-align:{alignment};";
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
                body {font-family: {{CssFont((string)font.SelectedItem!)}}; font-size: {{size.Value}}pt; font-weight: {{weightValues[weight.SelectedIndex + 1]}} }
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
        foreach (var box in new[] { font, weight, orientation, format, routes }
            .Concat(typography.SelectMany(style => new[] { style.Font, style.Weight, style.Alignment })))
            box.SelectedIndexChanged += (_, _) => QueueSample();
        foreach (var box in new[] { size, margin, height, padding }
            .Concat(widths).Concat(typography.Select(style => style.Size)))
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
            settings.FontWeight = weightValues[weight.SelectedIndex + 1];
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
            settings.FontNames = typography.Select(style => style.Font.SelectedIndex == 0 ? "" : (string)style.Font.SelectedItem!).ToArray();
            settings.FontSizes = typography.Select(style => style.Size.Value).ToArray();
            settings.FontWeights = typography.Select(style => weightValues[style.Weight.SelectedIndex]).ToArray();
            // Compatibility placeholders. Legacy indices are cleared so changing installed
            // Windows fonts does not restore a stale font on the next launch.
            settings.Fonts = new int[6];
            settings.Sizes = new int[6];
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
