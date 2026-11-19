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

    internal ArchiveBackupDialog(bool restoring, string initialLocation)
    {
        Text = restoring ? "Odtwarzanie bazy z archiwum" : "Archiwizacja bazy SQL";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(480, 270);
        ClientSize = new Size(750, 290);
        MaximizeBox = true;
        MinimizeBox = false;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 5 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = restoring ? "Wskaż kopię zapasową .bak do odtworzenia:" :
                "Wybierz folder zapisu kopii zapasowej SQL:",
            AutoSize = true, Margin = new Padding(3, 3, 3, 10)
        };
        grid.Controls.Add(title, 0, 0);
        grid.SetColumnSpan(title, 2);
        location.Text = initialLocation;
        location.PlaceholderText = restoring ? @"np. D:\Kopie\KartyLeczenia.bak" : "Puste pole = domyślny folder kopii SQL Server";
        grid.Controls.Add(location, 0, 1);
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
        grid.Controls.Add(browse, 1, 1);

        if (!restoring)
        {
            grid.Controls.Add(compressed, 0, 2);
            grid.SetColumnSpan(compressed, 2);
        }
        else
        {
            grid.Controls.Add(new Label
            {
                Text = "Kompresja jest wykrywana automatycznie przez SQL Server przy odtwarzaniu.",
                AutoSize = true
            }, 0, 2);
        }

        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Ważne: folder lub plik musi być dostępny dla usługi SQL Server na wskazanym serwerze. " +
                "Przeglądaj pokazuje pliki tego komputera; wybrana ścieżka lokalna może być niedostępna " +
                "dla zdalnego serwera SQL. W takim przypadku wpisz ścieżkę serwera lub UNC."
        };
        grid.Controls.Add(note, 0, 3);
        grid.SetColumnSpan(note, 2);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel };
        var apply = new Button
        {
            Text = restoring ? "Przywróć bazę" : "Utwórz archiwum",
            AutoSize = true, DialogResult = DialogResult.OK
        };
        actions.Controls.AddRange([cancel, apply]);
        grid.Controls.Add(actions, 0, 4);
        grid.SetColumnSpan(actions, 2);
        Controls.Add(grid);
        AcceptButton = apply;
        CancelButton = cancel;
        AppearanceThemes.ApplySurface(this, AppearanceThemes.Current);
    }
}
