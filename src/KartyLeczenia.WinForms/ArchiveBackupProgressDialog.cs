namespace KartyLeczenia.Windows;

/// <summary>Modal progress while SQL is backed up and safely copied to USB.</summary>
internal sealed class ArchiveBackupProgressDialog : Form
{
    readonly Label stage = new() { Dock = DockStyle.Top, Height = 45 };
    readonly Label detail = new() { Dock = DockStyle.Top, Height = 36 };
    readonly ProgressBar progressBar = new() { Dock = DockStyle.Top, Height = 23, Style = ProgressBarStyle.Marquee };
    readonly Label percent = new() { Dock = DockStyle.Top, Height = 29, Text = "Trwa przygotowanie..." };
    readonly SharedArchive archive;
    readonly BackupPreferences options;
    internal DatabaseBackupResult? Result { get; private set; }
    internal Exception? Failure { get; private set; }
    bool running;

    internal ArchiveBackupProgressDialog(SharedArchive archive, BackupPreferences options)
    {
        this.archive = archive;
        this.options = options;
        Text = "Archiwizacja SQL KartyLeczenia — postęp";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(580, 225);
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22) };
        stage.Text = "Przygotowywanie pełnej kopii bazy...";
        stage.Font = new Font(Font, FontStyle.Bold);
        detail.Text = "Serwer: " + archive.Server + " · Baza: KartyLeczenia";
        body.Controls.Add(percent);
        body.Controls.Add(progressBar);
        body.Controls.Add(detail);
        body.Controls.Add(stage);
        Controls.Add(body);
        AppearanceThemes.ApplySurface(this, AppearanceThemes.Current);
        Shown += async (_, _) =>
        {
            if (running) return;
            running = true;
            IProgress<BackupProgressInfo> reporter = new Progress<BackupProgressInfo>(UpdateProgress);
            try
            {
                Result = await Task.Run(() => archive.BackupToLocalFile(options, reporter));
            }
            catch (Exception ex) { Failure = ex; }
            finally { DialogResult = Result is null ? DialogResult.Abort : DialogResult.OK; Close(); }
        };
    }

    void UpdateProgress(BackupProgressInfo value)
    {
        if (IsDisposed) return;
        stage.Text = value.Stage;
        detail.Text = value.Detail;
        if (value.Percent is int pct)
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = Math.Clamp(pct, 0, 100);
            percent.Text = pct + "%";
        }
        else
        {
            progressBar.Style = ProgressBarStyle.Marquee;
            percent.Text = "Trwa operacja...";
        }
    }
}
