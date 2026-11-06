using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KartyLeczenia;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed record Card(Dictionary<string, string> Fields, List<Dictionary<string, string>> Rows);

sealed partial class MainForm : Form
{
    readonly TextBox server = new(), login = new(), password = new() { UseSystemPasswordChar = true };
    readonly CheckBox windows = new() { Text = "Logowanie Windows", Checked = true, AutoSize = true };
    readonly CheckBox trust = new() { Text = "Zaufaj certyfikatowi serwera", AutoSize = true };
    readonly TextBox document = new();
    readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox owner = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Dictionary<string, TextBox> fields = new();
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, AllowUserToAddRows = true };
    readonly Label status = new() { AutoSize = true, Text = "Podaj połączenie i numer dokumentu." };
    readonly string[] columns = ["Opis zwierząt / numer siedziby stada", "Liczba zwierząt", "Rozpoznanie", "Produkt", "Seria", "Ważność", "Ilość wydana", "Dawkowanie i okres stosowania", "Karencja", "Zalecenia / uwagi", "Sekcja (II lub III)"];
    string[] addresses = ["", ""];
    bool changingRegistry;
    string documentScope = "";
    const string RegistryState = "Status karencji";
    const string RegistrySource = "Źródło karencji";
    const string RegistryHash = "SHA256 źródła";
    const string RegistryExcerpt = "Pełna sekcja karencji źródła";


    public MainForm()
    {
        Text = "Karty leczenia zwierząt";
        Width = 1400; Height = 900;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var connection = new TabPage("Połączenie z Subiektem");
        var card = new TabPage("Karta leczenia");
        tabs.TabPages.AddRange([connection, card]);
        Controls.Add(tabs);
        BuildConnectionPage(connection, tabs, card);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        document.Width = 180; document.PlaceholderText = "Pełny numer FS / PA / WZ";
        source.Items.Add("Rejestr RPL (podstawowe źródło)");
        source.Items.Add("Opis leku");
        for (int i = 1; i <= 8; i++) source.Items.Add("Pole własne " + i);
        source.SelectedIndex = 0;
        owner.Items.AddRange(["Płatnik", "Odbiorca"]); owner.SelectedIndex = 0;
        owner.SelectedIndexChanged += (_, _) => fields["Właściciel i adres"].Text = addresses[owner.SelectedIndex];
        top.Controls.AddRange([document, source, owner]);
        Button load = new() { Text = "Pobierz dokument", AutoSize = true };
        load.Click += async (_, _) =>
        {
            load.Enabled = false;
            try { await LoadDocument(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Nie udało się pobrać dokumentu"); }
            finally { load.Enabled = true; }
        };
        top.Controls.Add(load);
        AddButton(top, "Karencja z rejestru…", ApplyRegistry);
        AddButton(top, "Zapisz kartę", SaveCard);
        AddButton(top, "Otwórz kartę", OpenCard);
        AddButton(top, "Podgląd / druk", Preview);
        var editor = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        foreach (string name in new[] { "Nr karty", "Lecznica i adres", "Lekarz i nr prawa wykonywania zawodu", "Właściciel i adres", "Zgłoszenie — data i godzina", "Wykonanie — data i godzina", "Wyniki badań", "Dokument źródłowy" })
        {
            var box = new TextBox { Width = 285, Multiline = true, Height = 44 };
            fields.Add(name, box);
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            panel.Controls.Add(new Label { Text = name, AutoSize = true }); panel.Controls.Add(box); editor.Controls.Add(panel);
        }
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Drukuj", HeaderText = "Drukuj" });
        foreach (string name in columns) grid.Columns.Add(name, name);
        foreach (string name in new[] { "Gatunek", "Droga podania", "Produkt spożywczy", RegistryState, RegistrySource, RegistryHash, RegistryExcerpt, "Produkt rejestru", "ID towaru" })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = name, ReadOnly = name is RegistryState or RegistryHash or RegistryExcerpt or RegistrySource or "ID towaru" });
        grid.Columns[RegistryHash].Visible = false;
        grid.Columns[RegistryExcerpt].Visible = false;
        grid.Columns["ID towaru"].Visible = false;
        grid.CellValueChanged += (_, e) =>
        {
            if (changingRegistry || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string name = grid.Columns[e.ColumnIndex].Name;
            var row = grid.Rows[e.RowIndex];
            if (name is "Karencja" or "Produkt" or "Gatunek" or "Droga podania" or "Produkt spożywczy" or "Produkt rejestru" or "Dawkowanie i okres stosowania" or "Opis zwierząt / numer siedziby stada")
                row.Cells[RegistryState].Value = "Zmieniono ręcznie — sprawdź przed wydrukiem";
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        bottom.Controls.Add(status);
        card.Controls.Add(grid); card.Controls.Add(editor); card.Controls.Add(top); card.Controls.Add(bottom);
    }

    static void AddInput(FlowLayoutPanel panel, string label, TextBox input)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true });
        input.Width = 400; panel.Controls.Add(input);
    }
    static void AddButton(FlowLayoutPanel panel, string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message); } };
        panel.Controls.Add(button);
    }
    string ConnectionString()
    {
        if (validatedConnection is null || validatedConnection != CurrentConnection())
            throw new InvalidOperationException("Przejdź do połączenia z Subiektem i wykonaj test aktualnych ustawień.");
        return CurrentConnection().BuildConnectionString();
    }

    async Task LoadDocument()
    {
        if (string.IsNullOrWhiteSpace(document.Text)) throw new InvalidOperationException("Podaj pełny numer dokumentu.");
        if (grid.Rows.Cast<DataGridViewRow>().Any(r => !r.IsNewRow) &&
            MessageBox.Show("Pobranie zastąpi aktualnie edytowane pozycje. Kontynuować?", "Nowy dokument", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        string connectionString = ConnectionString();
        var documentConnection = CurrentConnection();
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync();
        using var head = new SqlCommand("""
            SELECT d.dok_Id, d.dok_NrPelny,
              pl.adrh_NazwaPelna AS PN, pl.adrh_Nazwa AS PS, pl.adrh_Adres AS PA,
              pl.adrh_Kod AS PK, pl.adrh_Miejscowosc AS PM,
              od.adrh_NazwaPelna AS ONa, od.adrh_Nazwa AS OS, od.adrh_Adres AS OA,
              od.adrh_Kod AS OKo, od.adrh_Miejscowosc AS OM
            FROM dbo.dok__Dokument d
            LEFT JOIN dbo.adr_Historia pl ON pl.adrh_Id=d.dok_PlatnikAdreshId
            LEFT JOIN dbo.adr_Historia od ON od.adrh_Id=d.dok_OdbiorcaAdreshId
            WHERE d.dok_NrPelny=@number
            """, con);
        head.Parameters.Add("@number", SqlDbType.NVarChar, 100).Value = document.Text.Trim();
        int id; string number; string[] newAddresses;
        await using (var reader = await head.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Nie znaleziono dokumentu.");
            string V(string n) => reader[n]?.ToString() ?? "";
            id = Convert.ToInt32(reader["dok_Id"]); number = V("dok_NrPelny");
            string Name(string full, string shortName) => string.IsNullOrWhiteSpace(V(full)) ? V(shortName) : V(full);
            newAddresses = [Name("PN", "PS") + "\n" + V("PA") + "\n" + V("PK") + " " + V("PM"),
                            Name("ONa", "OS") + "\n" + V("OA") + "\n" + V("OKo") + " " + V("OM")];
            if (await reader.ReadAsync()) throw new InvalidOperationException("Numer nie jest jednoznaczny. Wybór po identyfikatorze wymaga rozszerzenia.");
        }
        int selectedSource = source.SelectedIndex;
        string withdrawal = selectedSource == 0 ? "CAST(NULL AS nvarchar(4000))" : selectedSource == 1 ? "t.tw_Opis" : "t.tw_Pole" + (selectedSource - 1);
        using var cmd = new SqlCommand($"""
            SELECT p.ob_Id, p.ob_TowId, t.tw_Nazwa, p.ob_Opis, p.ob_Ilosc, p.ob_Jm,
              {withdrawal} AS Karencja,
              r.mr_Id, r.mr_Ilosc, r.mr_DoId,
              zp.ob_NumerSeryjny AS Seria,
              zp.ob_Termin AS Waznosc, z.mr_Termin AS TerminRuchu
            FROM dbo.dok_Pozycja p
            LEFT JOIN dbo.tw__Towar t ON t.tw_Id=p.ob_TowId
            LEFT JOIN dbo.dok_MagRuch r ON r.mr_PozId=p.ob_Id
            LEFT JOIN dbo.dok_MagRuch z ON z.mr_Id=r.mr_DoId AND z.mr_TowId=r.mr_TowId
            LEFT JOIN dbo.dok_Pozycja zp ON zp.ob_Id=z.mr_PozId
            WHERE (p.ob_DokHanId=@id OR p.ob_DokMagId=@id)
              AND t.tw_IdGrupa IN (SELECT grt_Id FROM dbo.sl_GrupaTw WHERE grt_Nazwa=@drugGroup)
            ORDER BY p.ob_Id, r.mr_Id
            """, con);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@drugGroup", SqlDbType.NVarChar, 50).Value = documentConnection.DrugGroup;
        var rows = new List<object[]>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string V(string n) => reader[n] is DBNull ? "" : reader[n].ToString() ?? "";
                object date = reader["Waznosc"] is DBNull ? reader["TerminRuchu"] : reader["Waznosc"];
                string expiry = date is DateTime dt ? dt.ToString("dd.MM.yyyy") : "";
                string product = string.IsNullOrWhiteSpace(V("tw_Nazwa")) ? V("ob_Opis") : V("tw_Nazwa");
                string quantity = V("mr_Ilosc");
                if (quantity == "") quantity = V("ob_Ilosc");
                string note = V("Seria") == "" || expiry == "" ? "Sprawdź / uzupełnij serię i ważność." : "";
                if (reader["mr_DoId"] is DBNull) note += " Brak powiązania z dostawą źródłową — sprawdź ręcznie.";
                rows.Add([true, "", "", "", product, V("Seria"), expiry, quantity + " " + V("ob_Jm"), "", V("Karencja"), note, "II",
                    "", "", "", selectedSource == 0 ? "Brak karencji — wybierz produkt w RPL" : "Dane Subiekta — do weryfikacji",
                    selectedSource == 0 ? "" : "Subiekt: " + withdrawal, "", "", "", V("ob_TowId")]);
            }
        }
        if (documentConnection != CurrentConnection())
            throw new InvalidOperationException("W trakcie pobierania zmieniono połączenie. Ponów pobieranie po teście nowych ustawień.");
        changingRegistry = true;
        try { grid.Rows.Clear(); foreach (var row in rows) grid.Rows.Add(row); }
        finally { changingRegistry = false; }
        documentScope = documentConnection.Server.ToUpperInvariant() + "|" + documentConnection.Database.ToUpperInvariant();
        addresses = newAddresses;
        fields["Właściciel i adres"].Text = addresses[owner.SelectedIndex];
        fields["Dokument źródłowy"].Text = number;
        status.Text = $"Pobrano {rows.Count} wierszy. Sprawdź produkty, jednostki, dawki i brakujące dane przed wydrukiem.";
    }

    void ApplyRegistry()
    {
        grid.EndEdit();
        var row = grid.CurrentRow;
        if (row is null || row.IsNewRow) throw new InvalidOperationException("Wybierz wiersz produktu na karcie.");
        string product = row.Cells["Produkt"].Value?.ToString() ?? "";
        string key = documentScope + "|" + (row.Cells["ID towaru"].Value?.ToString() ?? "") + "|" + product;
        using var dialog = new RegistryDialog(product, RegistryService.ReadMapping(key));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selection is not { } selection) return;
        // Persist only medicine identity. Species, route and withdrawal must be confirmed for every use.
        RegistryService.SaveMapping(key, selection.Document.Id);
        changingRegistry = true;
        try
        {
            row.Cells["Karencja"].Value = selection.Text;
            row.Cells["Gatunek"].Value = selection.Species;
            row.Cells["Droga podania"].Value = selection.Route;
            row.Cells["Produkt spożywczy"].Value = selection.Food;
            row.Cells["Produkt rejestru"].Value = selection.Medicine;
            row.Cells[RegistrySource].Value = selection.Document.Url + " | pobrano " + selection.Document.DownloadedUtc.ToString("O") + " | zatwierdzono " + DateTimeOffset.UtcNow.ToString("O");
            row.Cells[RegistryHash].Value = selection.Document.Sha256;
            row.Cells[RegistryExcerpt].Value = selection.Document.WithdrawalSection;
            row.Cells[RegistryState].Value = "RPL — sprawdzono i zatwierdzono";
        }
        finally { changingRegistry = false; }
        status.Text = "Zastosowano karencję wyłącznie do wybranego wiersza. Inne zwierzęta lub drogi podania wymagają osobnego wyboru.";
    }

    Card CaptureCard()
    {
        grid.EndEdit();
        var rows = new List<Dictionary<string, string>>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var values = new Dictionary<string, string>();
            foreach (DataGridViewColumn c in grid.Columns) values[c.Name] = row.Cells[c.Index].Value?.ToString() ?? "";
            rows.Add(values);
        }
        var snapshot = fields.ToDictionary(p => p.Key, p => p.Value.Text);
        snapshot["Zakres mapowania towarów"] = documentScope;
        return new(snapshot, rows);
    }
    void SaveCard()
    {
        using var dialog = new SaveFileDialog { Filter = "Karta JSON|*.json", FileName = "karta-" + Guid.NewGuid().ToString("N") + ".json" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(CaptureCard(), new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        status.Text = "Zapisano kartę: " + dialog.FileName;
    }
    void OpenCard()
    {
        using var dialog = new OpenFileDialog { Filter = "Karta JSON|*.json" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var card = JsonSerializer.Deserialize<Card>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Nieprawidłowy plik.");
        if (MessageBox.Show("Otwarcie zastąpi aktualnie edytowaną kartę. Kontynuować?", "Otwórz", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        foreach (var p in fields) p.Value.Text = card.Fields.GetValueOrDefault(p.Key, "");
        changingRegistry = true;
        try
        {
        grid.Rows.Clear();
        foreach (var row in card.Rows)
        {
            object[] values = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.Index == 0
                ? (object)string.Equals(row.GetValueOrDefault(c.Name), "True", StringComparison.OrdinalIgnoreCase)
                : row.GetValueOrDefault(c.Name, "")).ToArray();
            grid.Rows.Add(values);
        }
        }
        finally { changingRegistry = false; }
        documentScope = card.Fields.GetValueOrDefault("Zakres mapowania towarów", "archiwum");
        addresses = [fields["Właściciel i adres"].Text, fields["Właściciel i adres"].Text];
        status.Text = "Otworzono kartę.";
    }
    void Preview()
    {
        var card = CaptureCard();
        var selected = card.Rows.Where(r => string.Equals(r.GetValueOrDefault("Drukuj"), "True", StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Zaznacz co najmniej jedną pozycję do wydruku.");
        if (selected.Any(r => r.GetValueOrDefault("Sekcja (II lub III)") is not ("II" or "III")))
            throw new InvalidOperationException("Każda zaznaczona pozycja musi mieć sekcję II lub III.");
        if (selected.Any(r => string.IsNullOrWhiteSpace(r.GetValueOrDefault("Karencja")) || r.GetValueOrDefault(RegistryState) != "RPL — sprawdzono i zatwierdzono"))
        {
            if (MessageBox.Show(this, "Część pozycji ma brak karencji, dane spoza RPL lub ręczne zmiany. Sprawdź każdą z nich. Czy potwierdzasz dane i chcesz otworzyć podgląd?", "Weryfikacja karencji", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        }
        static string E(string? v) => WebUtility.HtmlEncode(v ?? "").Replace("\r\n", "\n").Replace("\n", "<br>");
        string Field(string k) => E(card.Fields.GetValueOrDefault(k));
        var html = new StringBuilder("""
            <!doctype html><html lang="pl"><meta charset="utf-8">
            <title>Karta leczenia zwierząt</title>
            <style>
            @page { size:A4 landscape; margin:10mm; }
            body {font:10px Arial,sans-serif;color:#000;background:#fff;}
            h1 {text-align:center;font-size:15px;} h2 {font-size:12px;}
            .header {display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;margin:14px 0;}
            table {width:100%;border-collapse:collapse;table-layout:fixed;}
            th,td {border:1px solid #000;padding:5px;overflow-wrap:anywhere;vertical-align:top;}
            thead {display:table-header-group;} tr {break-inside:avoid;}
            th {font-weight:normal;} .sign {display:flex;justify-content:space-around;margin:35px 0 18px;}
            .tools {margin:10px;} @media print {.tools{display:none;}}
            </style><body>
            <div class="tools"><button onclick="window.print()">Drukuj / zapisz PDF</button>
            Sprawdź A4 poziomo i wyłącz nagłówki oraz stopki przeglądarki.</div>
            <h1>KSIĄŻKA LECZENIA ZWIERZĄT GOSPODARSKICH ORAZ ZWIERZĄT,<br>
            Z KTÓRYCH POZYSKANE TKANKI LUB PRODUKTY SĄ PRZEZNACZONE DO SPOŻYCIA PRZEZ LUDZI / EWIDENCJA LECZENIA ZWIERZĄT</h1>
            """);
        html.Append($"<div class='header'><div>I. Zakład leczniczy:<br>{Field("Lecznica i adres")}</div><div>Posiadacz zwierzęcia:<br>{Field("Właściciel i adres")}</div><div>Nr dokumentu: {Field("Nr karty")}<br>Dokument źródłowy: {Field("Dokument źródłowy")}</div></div>");
        html.Append($"<p>Zgłoszenie: {Field("Zgłoszenie — data i godzina")}<br>Wykonanie czynności: {Field("Wykonanie — data i godzina")}</p>");
        foreach (string section in new[] { "II", "III" })
        {
            html.Append(section == "II" ? "<h2>II. Leczenie zwierząt</h2>" : "<h2>III. Potwierdzenie nabycia produktu leczniczego weterynaryjnego / paszy leczniczej</h2>");
            html.Append("<table><thead><tr><th style='width:3%'>Lp.</th><th>Zwierzęta / siedziba stada</th><th style='width:5%'>Liczba</th><th>Rozpoznanie</th><th>Produkt</th><th>Seria / ważność</th><th>Ilość, dawkowanie i okres stosowania</th><th>Karencja</th><th>Zalecenia / uwagi</th></tr></thead><tbody>");
            int index = 0;
            foreach (var row in selected.Where(r => r.GetValueOrDefault("Sekcja (II lub III)") == section))
            {
                string V(string k) => E(row.GetValueOrDefault(k));
                html.Append($"<tr><td>{++index}</td><td>{V(columns[0])}</td><td>{V(columns[1])}</td><td>{V(columns[2])}</td><td>{V(columns[3])}</td><td>{V(columns[4])}<br>{V(columns[5])}</td><td>Ilość wydana: {V(columns[6])}<br>{V(columns[7])}</td><td>{V(columns[8])}<br><small>{V("Gatunek")} / {V("Droga podania")} / {V("Produkt spożywczy")}</small></td><td>{V(columns[9])}</td></tr>");
            }
            if (index == 0) html.Append("<tr><td colspan='9' style='height:25px'></td></tr>");
            html.Append("</tbody></table>");
        }
        html.Append("<details class='tools'><summary>Źródła karencji (nie drukowane)</summary>");
        foreach (var row in selected) html.Append("<p>" + E(row.GetValueOrDefault("Produkt")) + "<br>" + E(row.GetValueOrDefault(RegistrySource)) + "<br>" + E(row.GetValueOrDefault(RegistryState)) + "</p>");
        html.Append("</details>");
        html.Append("<p>Oświadczam, że nabyte produkty lecznicze weterynaryjne / pasze lecznicze zostaną zastosowane zgodnie z zaleceniami lekarza weterynarii.</p>");
        html.Append($"<div class='sign'><div>{Field("Lekarz i nr prawa wykonywania zawodu")}<br><br>................................................<br>podpis i pieczęć lekarza weterynarii</div><div>................................................<br>podpis posiadacza zwierzęcia</div></div><p>IV. Wyniki badań uzupełniających: {Field("Wyniki badań")}</p></body></html>");
        using var dialog = new SaveFileDialog { Filter = "Podgląd HTML|*.html", FileName = "karta-" + Guid.NewGuid().ToString("N") + ".html" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, html.ToString(), Encoding.UTF8);
        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
    }
}
