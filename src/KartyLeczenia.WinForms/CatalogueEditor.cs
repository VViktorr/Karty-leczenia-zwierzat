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

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
            WrapContents = true, Padding = new Padding(8) };
        var add = new Button { Text = "Dodaj wariant", AutoSize = true };
        add.Click += (_, _) => AddVariant(new());
        var pdf = new Button { Text = "Dołącz PDF do zaznaczonych wariantów", AutoSize = true };
        pdf.Click += (_, _) => AttachPdf();
        var save = new Button { Text = "Zapisz w SQL", AutoSize = true };
        save.Click += async (_, _) => await Finish(saveProduct);
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([add, pdf, save, cancel, message]);
        CancelButton = cancel;
        FormClosing += (_, e) => { if (!Enabled && DialogResult != DialogResult.OK) e.Cancel = true; };
        Controls.Add(variants);
        Controls.Add(header);
        Controls.Add(buttons);
    }

    void AddVariant(CatalogueVariantEdit variant)
    {
        var row = variants.Rows[variants.Rows.Add(variant.Species, variant.Indication, variant.Route, variant.Dose,
            variant.Tissues, variant.Milk, variant.Remarks, variant.Source, variant.Short, variant.Price, variant.Pdf)];
        row.Tag = variant;
        if (variant.Id is null) variants.CurrentCell = row.Cells[0];
    }

    void AttachPdf()
    {
        if (!variants.EndEdit() || variants.SelectedRows.Count == 0)
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
            foreach (DataGridViewRow row in variants.SelectedRows) row.Cells[10].Value = key;
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
            if (edit == original) continue;
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
