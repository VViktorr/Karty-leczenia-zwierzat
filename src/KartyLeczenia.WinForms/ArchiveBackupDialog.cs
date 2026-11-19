namespace KartyLeczenia.Windows;

/// <summary>Choose an SQL Server-visible location and native SQL backup compression.</summary>
internal sealed class ArchiveBackupDialog : Form
{
    readonly TextBox location = new() { Dock = DockStyle.Fill };
    readonly CheckBox compressed = new()
    {
        Text = "Kompresuj kopię zapasową (jeśli obsługiwana przez edycję SQL Server)",
        AutoSize = true, Checked = false
    };
    internal string SelectedPath => location.Text.Trim();
    internal bool Compressed => compressed.Checked;

    internal ArchiveBackupDialog(bool restoring, string initialLocation, string server)
    {
        Text = restoring ? "Odtwarzanie SQL KartyLeczenia" : "Archiwizacja SQL KartyLeczenia";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(650, 300);
        ClientSize = new Size(800, 310);
        MaximizeBox = true;
        MinimizeBox = false;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = restoring ? "Odtworzenie bazy SQL KartyLeczenia" : "Utworzenie kopii zapasowej SQL KartyLeczenia",
            AutoSize = true, Margin = new Padding(3, 3, 3, 10),
            Font = new Font(Font, FontStyle.Bold)
        };
        grid.Controls.Add(title, 0, 0);
        grid.SetColumnSpan(title, 2);
        var subtitle = new Label
        {
            Text = $"Serwer: {server}   ·   Baza: KartyLeczenia",
            AutoSize = true, Margin = new Padding(3, 0, 3, 10)
        };
        grid.Controls.Add(subtitle, 0, 1);
        grid.SetColumnSpan(subtitle, 2);
        location.Text = initialLocation;
        location.PlaceholderText = restoring ? @"np. D:\Kopie\KartyLeczenia.bak" : "Puste pole = domyślny folder kopii SQL Server";
        grid.Controls.Add(location, 0, 2);
        var browse = new Button { Text = "Przeglądaj…", AutoSize = true, Anchor = AnchorStyles.Left };
        browse.Click += (_, _) =>
        {
            if (restoring)
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Wybierz kopię bazy SQL",
                    Filter = "Kopia SQL Server (*.bak)|*.bak|Wszystkie pliki (*.*)|*.*",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) == DialogResult.OK) location.Text = dialog.FileName;
            }
            else
            {
                using var dialog = new FolderBrowserDialog { Description = "Folder dostępny dla usługi SQL Server" };
                if (Directory.Exists(location.Text)) dialog.InitialDirectory = location.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) location.Text = dialog.SelectedPath;
            }
        };
        grid.Controls.Add(browse, 1, 2);

        if (!restoring)
        {
            grid.Controls.Add(compressed, 0, 3);
            grid.SetColumnSpan(compressed, 2);
        }
        else
        {
            grid.Controls.Add(new Label
            {
                Text = "Kompresja jest wykrywana automatycznie przez SQL Server przy odtwarzaniu.",
                AutoSize = true
            }, 0, 3);
        }

        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Plik .bak jest zapisywany i odczytywany przez usługę SQL Server, a nie przez aplikację na stanowisku. " +
                "Wskaż folder dostępny na serwerze lub ścieżkę UNC z odpowiednimi uprawnieniami. " +
                "Przycisk „Przeglądaj” pokazuje pliki bieżącego komputera."
        };
        grid.Controls.Add(note, 0, 4);
        grid.SetColumnSpan(note, 2);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Anuluj", AutoSize = false, Size = new Size(120, 38), DialogResult = DialogResult.Cancel };
        var apply = new Button
        {
            Text = restoring ? "Przywróć bazę" : "Utwórz archiwum",
            AutoSize = false, Size = new Size(175, 38), DialogResult = DialogResult.OK
        };
        actions.Controls.AddRange([cancel, apply]);
        grid.Controls.Add(actions, 0, 5);
        grid.SetColumnSpan(actions, 2);
        Controls.Add(grid);
        AcceptButton = apply;
        CancelButton = cancel;
        AppearanceThemes.ApplySurface(this, AppearanceThemes.Current);
    }
}
