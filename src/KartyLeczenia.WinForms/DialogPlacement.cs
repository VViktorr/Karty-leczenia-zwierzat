using System.Text.Json;

namespace KartyLeczenia.Windows;

internal static class DialogPlacement
{
    sealed record Placement(Rectangle Bounds, bool Maximized);

    internal static DialogResult Show(Form dialog, Form owner, string key)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KartyLeczenia", "windows", key + ".json");
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Placement>(File.ReadAllText(path)) is { } saved
                && saved.Bounds.Width > 0 && saved.Bounds.Height > 0)
            {
                var screen = Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.IntersectsWith(saved.Bounds))
                    ?? Screen.FromControl(owner);
                var area = screen.WorkingArea;
                bool resizable = dialog.FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
                int width = Math.Clamp(resizable ? saved.Bounds.Width : dialog.Width, 1, area.Width);
                int height = Math.Clamp(resizable ? saved.Bounds.Height : dialog.Height, 1, area.Height);
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Bounds = new Rectangle(Math.Clamp(saved.Bounds.X, area.Left, area.Right - width),
                    Math.Clamp(saved.Bounds.Y, area.Top, area.Bottom - height), width, height);
                if (saved.Maximized && dialog.MaximizeBox) dialog.WindowState = FormWindowState.Maximized;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLog.Write("DialogPlacement.Restore." + key, ex);
        }

        var result = dialog.ShowDialog(owner);
        try
        {
            var bounds = dialog.WindowState == FormWindowState.Normal ? dialog.Bounds : dialog.RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, JsonSerializer.Serialize(new Placement(bounds,
                        dialog.WindowState == FormWindowState.Maximized)));
                    File.Move(temp, path, overwrite: true);
                }
                finally { File.Delete(temp); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("DialogPlacement.Save." + key, ex);
        }
        return result;
    }
}
