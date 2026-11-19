namespace KartyLeczenia.Windows;

/// <summary>Subiekt-style backup preferences. Only paths and presentation are stored locally.</summary>
internal sealed class ArchiveBackupSettingsDialog : Form
{
    readonly TextBox destination = new() { Dock = DockStyle.Fill };
    readonly TextBox working = new() { Dock = DockStyle.Fill };
    readonly ComboBox naming = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly TextBox custom = new() { Width = 220 };
    readonly CheckBox overwrite = new() { Text = "Nadpisuj archiwa o tej samej nazwie bez pytania", AutoSize = true };
    readonly CheckBox compress = new() { Text = "Kompresuj kopię SQL Server", AutoSize = true };
    readonly Label preview = new() { AutoSize = true };

    internal BackupPreferences? Selected { get; private set; }

    internal ArchiveBackupSettingsDialog(BackupPreferences current)
    {
        Text = "Ustawienia archiwizacji SQL KartyLeczenia";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(710, 425);
        ClientSize = new Size(800, 445);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var location = new TabPage("Lokalizacja") { Padding = new Padding(14) };
        var compression = new TabPage("Kompresja") { Padding = new Padding(14) };
        var security = new TabPage("Zabezpieczenia") { Padding = new Padding(14) };
        tabs.TabPages.AddRange([location, compression, security]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        void Line(int row, string label, Control input, Button? browse = null)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 6, 10) }, 0, row);
            input.Margin = new Padding(3, 5, 3, 5);
            layout.Controls.Add(input, 1, row);
            if (browse is not null) layout.Controls.Add(browse, 2, row);
        }

        destination.Text = current.DestinationFolder;
        working.Text = current.WorkingFolder;
        var chooseDestination = new Button { Text = "Przeglądaj…", AutoSize = true };
        chooseDestination.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Wybierz folder docelowy archiwów na pendrivie" };
            if (Directory.Exists(destination.Text)) dialog.InitialDirectory = destination.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
        };
        var chooseWorking = new Button { Text = "Przeglądaj…", AutoSize = true };
        chooseWorking.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Folder roboczy widoczny dla usługi SQL Server" };
            if (Directory.Exists(working.Text)) dialog.InitialDirectory = working.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) working.Text = dialog.SelectedPath;
        };
        Line(0, "Folder archiwum:", destination, chooseDestination);
        Line(1, "Folder roboczy SQL:", working, chooseWorking);

        naming.Items.AddRange(["Dzienna", "Stała nazwa", "Miesięczna", "Tygodniowa", "Własna"]);
        naming.SelectedItem = naming.Items.Contains(current.Naming) ? current.Naming : "Dzienna";
        custom.Text = current.CustomName;
        Line(2, "Nazwa pliku .bak:", naming);
        Line(3, "Własna nazwa:", custom);
        overwrite.Checked = current.Overwrite;
        layout.Controls.Add(overwrite, 1, 4);
        layout.Controls.Add(preview, 1, 5);
        naming.SelectedIndexChanged += (_, _) => UpdatePreview();
        custom.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        location.Controls.Add(layout);

        compression.Controls.Add(new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            Controls =
            {
                compress,
                new Label { AutoSize = true, MaximumSize = new Size(700, 0),
                    Text = "Kompresja jest wykonywana przez SQL Server. Każda kopia pozostaje pełna i samodzielna." }
            }
        });
        compress.Checked = current.Compress;
        security.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Przed archiwizacją wymagane jest potwierdzenie Windows Hello.\n\n" +
                   "SQL wykonuje pełną kopię z CHECKSUM w folderze roboczym. " +
                   "Następnie program przenosi ją na pendrive, weryfikuje SHA-256 " +
                   "i usuwa plik roboczy dopiero po pomyślnym zapisie. " +
                   "W razie błędu poprzednie poprawne archiwum nie jest nadpisywane."
        });

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 58,
            Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft
        };
        var save = new Button { Text = "Zapisz ustawienia", AutoSize = true, Height = 36 };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) =>
        {
            try
            {
                var options = new BackupPreferences
                {
                    DestinationFolder = destination.Text.Trim(),
                    WorkingFolder = working.Text.Trim(),
                    Naming = naming.Text,
                    CustomName = custom.Text.Trim(),
                    Overwrite = overwrite.Checked,
                    Compress = compress.Checked
                };
                ArchiveStorageLocation.SavePreferences(options);
                Selected = options;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                MessageBox.Show(this, ex.Message, "Błędne ustawienia archiwizacji",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(tabs);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
        AppearanceThemes.ApplySurface(this, AppearanceThemes.Current);
    }

    void UpdatePreview()
    {
        try
        {
            preview.Text = "Przykładowy plik: " + new BackupPreferences
            { Naming = naming.Text, CustomName = custom.Text }.FileName();
        }
        catch (InvalidOperationException) { preview.Text = "Nieprawidłowa nazwa pliku"; }
        custom.Enabled = naming.Text == "Własna";
    }
}
