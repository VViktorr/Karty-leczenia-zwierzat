using System.Security.Cryptography;

namespace KartyLeczenia.Windows;

internal sealed record CatalogueProduct(string Symbol, string Name, string RplId, string Description,
    byte[]? Revision, IReadOnlyList<CatalogueVariantEdit> Variants);

internal sealed record CatalogueVariantEdit(int? Id = null, string Species = "", string Indication = "",
    string Route = "", string Dose = "", string Tissues = "", string Milk = "", string Remarks = "",
    string Source = "", string Short = "", string Price = "", string Pdf = "");

internal sealed class CatalogueEditor : Form
{
    readonly CatalogueProduct product;
    readonly TextBox rpl = new() { Width = 160 };
    readonly TextBox description = new() { Width = 410 };
    CatalogueVariantsTable? variantView;
    readonly DataGridView variants = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false
    };
    readonly Dictionary<string, byte[]> pdfs = new(StringComparer.Ordinal);
    readonly Label message = new() { AutoSize = true, Padding = new Padding(8) };

    internal CatalogueEditor(CatalogueProduct product, Func<CatalogueProduct, IReadOnlyDictionary<string, byte[]>, Task> saveProduct)
    {
        this.product = product;
        Text = "Kartoteka w katalogu leków";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1150, 530);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(700, 360);
        rpl.Text = product.RplId;
        description.Text = product.Description;

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoScroll = true,
            WrapContents = true, Padding = new Padding(8) };
        header.Controls.Add(new Label { Text = $"{product.Symbol} · {product.Name}", AutoSize = true });
        header.SetFlowBreak(header.Controls[0], true);
        header.Controls.AddRange([
            new Label { Text = "ID wpisu RPL", AutoSize = true }, rpl,
            new Label { Text = "Opis", AutoSize = true }, description
        ]);

        (string Name, int Width)[] columns = [
            ("Gatunek", 110), ("Wskazanie", 200), ("Droga podania", 135), ("Dawkowanie", 220),
            ("Tkanki jadalne", 115), ("Mleko", 90), ("Uwagi", 150), ("Źródło danych", 160),
            ("Skrót karencji", 120), ("Karencja w cenniku", 150), ("PDF ChPL", 160)
        ];
        foreach (var (name, width) in columns)
            variants.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, Width = width });
        variants.Columns[10].ReadOnly = true;
        foreach (var variant in product.Variants) AddVariant(variant);
        variantView = new CatalogueVariantsTable(variants);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
            WrapContents = true, Padding = new Padding(8) };
        var add = new Button { Text = "Dodaj wariant", AutoSize = true };
        add.Click += (_, _) => AddVariant(new());
        var autoPdf = new Button { Text = "Wczytaj dane z ChPL (PDF)", AutoSize = true };
        autoPdf.Click += async (_, _) => await SuggestFromPdf(autoPdf);
        var pdf = new Button { Text = "Dołącz PDF do zaznaczonych wariantów", AutoSize = true };
        pdf.Click += (_, _) => AttachPdf();
        var save = new Button { Text = "Zapisz w SQL", AutoSize = true };
        save.Click += async (_, _) => await Finish(saveProduct);
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([add, autoPdf, pdf, save, cancel, message]);
        CancelButton = cancel;
        FormClosing += (_, e) => { if (!Enabled && DialogResult != DialogResult.OK) e.Cancel = true; };
        // AntdUI is the visible, editable catalogue table. The original grid
        // remains a data model for existing SQL/PDF draft processing.
        variants.Visible = false;
        Controls.Add(variants);
        Controls.Add(variantView);
        Controls.Add(header);
        Controls.Add(buttons);
        AppearanceThemes.ApplySurface(this, AppearanceThemes.Current);
    }

    void AddVariant(CatalogueVariantEdit variant)
    {
        var row = variants.Rows[variants.Rows.Add(variant.Species, variant.Indication, variant.Route, variant.Dose,
            variant.Tissues, variant.Milk, variant.Remarks, variant.Source, variant.Short, variant.Price, variant.Pdf)];
        row.Tag = variant;
        if (variant.Id is null) variants.CurrentCell = row.Cells[0];
        variantView?.RefreshFromSource();
    }

    async Task SuggestFromPdf(Button button)
    {
        if (!variants.EndEdit()) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Wybierz polską ChPL do automatycznego odczytu",
            Filter = "Charakterystyka produktu leczniczego (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        button.Enabled = false;
        message.Text = "Analiza treści ChPL…";
        try
        {
            var content = await File.ReadAllBytesAsync(dialog.FileName);
            if (!content.AsSpan().StartsWith("%PDF-"u8))
                throw new InvalidDataException("Wybrany plik nie jest poprawnym PDF.");

            // PDF is used only after user approval; nothing is persisted yet.
            var proposal = await Task.Run(() => ChplPdfAssistant.Read(dialog.FileName));
            if (IsDisposed) return;

            var detail = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill, WordWrap = true,
                Text = string.Join(Environment.NewLine, proposal.Warnings.Select(w => "UWAGA: " + w))
                    + Environment.NewLine + Environment.NewLine
                    + $"Zaproponowane warianty: {proposal.Variants.Count}"
                    + Environment.NewLine + Environment.NewLine + proposal.Evidence
            };
            using var preview = new Form
            {
                Text = "Podgląd odczytu ChPL – sprawdź przed użyciem",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(900, 650),
                MinimumSize = new Size(650, 420),
                AutoScaleMode = AutoScaleMode.Dpi
            };
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };
            var accept = new Button
            {
                Text = "Przenieś propozycje do edytora", AutoSize = true,
                DialogResult = DialogResult.OK,
                Enabled = proposal.Variants.Count > 0
            };
            var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
            footer.Controls.AddRange([accept, cancel]);
            preview.Controls.Add(detail);
            preview.Controls.Add(footer);
            preview.AcceptButton = accept;
            preview.CancelButton = cancel;

            if (preview.ShowDialog(this) != DialogResult.OK) { message.Text = "Anulowano odczyt PDF."; return; }

            var pdfKey = $"ChPL/{Convert.ToHexString(SHA256.HashData(content))}.pdf";
            pdfs[pdfKey] = content;
            int filled = 0, created = 0;
            foreach (var suggested in proposal.Variants)
            {
                DataGridViewRow? match = variants.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(row =>
                        string.Equals(row.Cells[0].Value?.ToString(), suggested.Species, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(row.Cells[2].Value?.ToString(), suggested.Route, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    // Never create a duplicate when the source lacks a route.
                    if (string.IsNullOrWhiteSpace(suggested.Route) &&
                        variants.Rows.Cast<DataGridViewRow>().Any(row =>
                            string.Equals(row.Cells[0].Value?.ToString(), suggested.Species, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    AddVariant(suggested with { Pdf = pdfKey });
                    created++;
                    continue;
                }

                // Preserve all existing manual edits. Fill only blank cells.
                void FillEmpty(int index, string? value)
                {
                    if (string.IsNullOrWhiteSpace(value) ||
                        !string.IsNullOrWhiteSpace(match.Cells[index].Value?.ToString())) return;
                    match.Cells[index].Value = value;
                    filled++;
                }
                FillEmpty(1, suggested.Indication);
                FillEmpty(3, suggested.Dose);
                FillEmpty(4, suggested.Tissues);
                FillEmpty(5, suggested.Milk);
                FillEmpty(7, suggested.Source);
                FillEmpty(10, pdfKey);
            }

            message.Text = $"ChPL: dodano {created} propozycji, uzupełniono {filled} pustych pól. Sprawdź wszystkie dane i kliknij „Zapisz w SQL”.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) message.Text = "Nie udało się odczytać PDF: " + ex.Message;
            CrashLog.Write("CatalogueEditor.ChplAutoRead", ex);
        }
        finally
        {
            if (!IsDisposed) button.Enabled = true;
        }
    }

    void AttachPdf()
    {
        if (!variants.EndEdit() || variantView?.SelectedSources().Count is not > 0)
        {
            message.Text = "Zaznacz warianty, do których chcesz dołączyć ChPL.";
            return;
        }

        using var dialog = new OpenFileDialog { Filter = "Dokument PDF (*.pdf)|*.pdf" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var content = File.ReadAllBytes(dialog.FileName);
            if (!content.AsSpan().StartsWith("%PDF-"u8))
            {
                message.Text = "Wybierz poprawny plik PDF.";
                return;
            }
            var key = $"ChPL/{Convert.ToHexString(SHA256.HashData(content))}.pdf";
            pdfs[key] = content;
            foreach (DataGridViewRow row in variantView!.SelectedSources()) row.Cells[10].Value = key;
            message.Text = $"Wybrano {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message.Text = ex.Message;
        }
    }

    async Task Finish(Func<CatalogueProduct, IReadOnlyDictionary<string, byte[]>, Task> saveProduct)
    {
        if (!variants.EndEdit()) return;
        var edits = new List<CatalogueVariantEdit>();
        foreach (DataGridViewRow row in variants.Rows)
        {
            var original = (CatalogueVariantEdit)row.Tag!;
            string Value(int i) => row.Cells[i].Value?.ToString() ?? "";
            var edit = new CatalogueVariantEdit(original.Id, Value(0), Value(1), Value(2), Value(3),
                Value(4), Value(5), Value(6), Value(7), Value(8), Value(9), Value(10));
            // For new variants store the canonical upper-case administration
            // route code (IV, IM, SC, PO...) instead of an arbitrary ChPL spelling.
            // Existing SQL variants keep their current source unless edited.
            if (edit.Id is null)
                edit = edit with { Route = AdministrationRouteLegend.Abbreviate(edit.Route) };
            // A new imported proposal has no SQL id, even when the operator
            // accepts it without editing. Persist it after validation.
            if (edit == original && edit.Id is not null) continue;
            if (edit.Tissues != original.Tissues || edit.Milk != original.Milk)
            {
                if (edit.Short == original.Short) edit = edit with { Short = "" };
                if (edit.Price == original.Price) edit = edit with { Price = "" };
            }
            if (edit.Id is null && (string.IsNullOrWhiteSpace(edit.Species) ||
                string.IsNullOrWhiteSpace(edit.Route) || string.IsNullOrWhiteSpace(edit.Dose)))
            {
                message.Text = "Podaj gatunek, drogę podania i dawkowanie nowego wariantu.";
                variants.CurrentCell = row.Cells[0];
                return;
            }
            edits.Add(edit);
        }

        var draft = product with { RplId = rpl.Text.Trim(), Description = description.Text.Trim(), Variants = edits };
        Enabled = false;
        try
        {
            await saveProduct(draft, pdfs);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { message.Text = ex.Message; }
        finally { Enabled = true; }
    }
}
