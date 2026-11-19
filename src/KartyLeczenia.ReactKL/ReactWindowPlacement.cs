using System.Text.Json;
using System.Drawing;
using System.Windows.Forms;

namespace KartyLeczenia.ReactKL;

/// <summary>
/// Local per-Windows-user placement, independent of shared SQL/clinic data.
/// An unplugged monitor must never leave the application off-screen.
/// </summary>
internal static class ReactWindowPlacement
{
    private sealed record Placement(int X, int Y, int Width, int Height, bool Maximized);
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KartyLeczenia", "reactkl-window.json");

    internal static Rectangle VisibleBounds(Rectangle saved, IReadOnlyList<Rectangle> workAreas,
        Rectangle fallback, Size minimum)
    {
        if (saved.Width < minimum.Width || saved.Height < minimum.Height ||
            saved.Width > 12000 || saved.Height > 12000 ||
            workAreas.Count == 0)
            return fallback;

        Rectangle? screen = null;
        long maxIntersection = 0;
        foreach (var area in workAreas)
        {
            var intersection = Rectangle.Intersect(saved, area);
            long size = (long)intersection.Width * intersection.Height;
            if (size > maxIntersection)
            {
                screen = area;
                maxIntersection = size;
            }
        }
        if (screen is null || maxIntersection < 10000) return fallback;
        var current = screen.Value;
        int width = Math.Min(saved.Width, current.Width);
        int height = Math.Min(saved.Height, current.Height);
        if (width < minimum.Width || height < minimum.Height) return fallback;
        return new Rectangle(
            Math.Clamp(saved.X, current.Left, current.Right - width),
            Math.Clamp(saved.Y, current.Top, current.Bottom - height),
            width, height);
    }

    internal static void Restore(Form window)
    {
        try
        {
            if (!File.Exists(PathName)) return;
            var saved = JsonSerializer.Deserialize<Placement>(File.ReadAllText(PathName));
            if (saved is null) return;
            Rectangle fallback = window.Bounds;
            var workAreas = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            var actual = VisibleBounds(new Rectangle(saved.X, saved.Y,
                saved.Width, saved.Height), workAreas, fallback, window.MinimumSize);
            window.StartPosition = FormStartPosition.Manual;
            window.Bounds = actual;
            if (saved.Maximized && actual != fallback)
                window.WindowState = FormWindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or OverflowException)
        {
            // Corrupt/old local state must not prevent launching a clinic workstation.
        }
    }

    internal static void Save(Form window)
    {
        try
        {
            if (window.WindowState == FormWindowState.Minimized) return;
            var bounds = window.WindowState == FormWindowState.Maximized
                ? window.RestoreBounds : window.Bounds;
            if (bounds.Width < window.MinimumSize.Width ||
                bounds.Height < window.MinimumSize.Height) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var value = new Placement(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                window.WindowState == FormWindowState.Maximized);
            string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(value));
                File.Move(temporary, PathName, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Placement is convenience only. Never interrupt closing or SQL operations.
        }
    }
}
