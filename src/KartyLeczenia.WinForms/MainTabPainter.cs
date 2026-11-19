using System.Drawing.Drawing2D;

namespace KartyLeczenia.Windows;

/// <summary>
/// Modern tab-header renderer used both in the application and theme preview.
/// The backing WinForms TabControl still owns keyboard navigation and page content.
/// </summary>
internal static class MainTabPainter
{
    internal static void Draw(TabControl tabs, DrawItemEventArgs e, AppearanceOptions appearance)
    {
        if (e.Index < 0 || e.Index >= tabs.TabCount) return;

        bool selected = e.Index == tabs.SelectedIndex;
        bool dark = AppearanceThemes.IsDark(appearance);
        int style = appearance.MainTabStyle;
        Color canvas = AppearanceThemes.Canvas(appearance);
        Color text = AppearanceThemes.Ink(appearance);
        Color accent = AppearanceThemes.Accent(appearance);

        // Avoid the native dotted Win98-style focus rectangle. The active
        // tab has a prominent visual selection, and native keyboard tab
        // navigation remains intact.
        var rect = e.Bounds;
        rect.Inflate(-3, -2);
        if (selected)
        {
            // Restore the earlier prominent/larger selected card.
            rect.Y -= 1;
            rect.Height += 1;
        }
        if (rect.Width < 10 || rect.Height < 10) return;

        // Explicitly clear the whole owner-drawn item. No stale native selection
        // strips, no overlapping normal/selected background regions.
        using (var baseFill = new SolidBrush(canvas))
            e.Graphics.FillRectangle(baseFill, e.Bounds);

        // The selected tab is always visually distinctive: high-contrast fill
        // for cards, tonal fill for Line/Material, and stronger text on all.
        Color selectedFill = style switch
        {
            0 => AppearanceThemes.Blend(canvas, accent, dark ? .27f : .13f),
            1 => accent,
            2 => AppearanceThemes.Blend(canvas, accent, dark ? .37f : .22f),
            _ => accent
        };
        Color inactiveFill = style switch
        {
            1 or 2 => AppearanceThemes.Blend(canvas, text, dark ? .045f : .035f),
            _ => canvas
        };
        Color fill = selected ? selectedFill : inactiveFill;
        Color foreground = selected && (style is 1 or 3)
            ? AppearanceThemes.ContrastingText(accent)
            : selected ? accent : text;

        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = Math.Clamp(appearance.CornerRadius, 5, 16);
            using var path = Rounded(rect, radius);
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillPath(brush, path);

            if (selected && (style is 1 or 2 or 3))
            {
                // Light, consistent outline rather than a native 3D tab frame.
                using var border = new Pen(style == 1 ? accent :
                    AppearanceThemes.Blend(accent, canvas, .24f), 1.2f);
                e.Graphics.DrawPath(border, path);
            }

            if (selected && style == 0)
            {
                bool bottom = tabs.Alignment == TabAlignment.Bottom;
                int thickness = Math.Clamp((int)Math.Round(tabs.DeviceDpi / 96.0 * 3), 3, 6);
                int lineY = bottom ? rect.Top + 2 : rect.Bottom - thickness - 2;
                var indicator = new Rectangle(rect.Left + radius, lineY,
                    Math.Max(1, rect.Width - 2 * radius), thickness);
                using var brush = new SolidBrush(accent);
                e.Graphics.FillRectangle(brush, indicator);
            }
        }
        finally { e.Graphics.Restore(state); }

        using var activeFont = selected ? new Font(tabs.Font.FontFamily,
            tabs.Font.SizeInPoints + 1.5f, tabs.Font.Style | FontStyle.Bold,
            GraphicsUnit.Point) : null;
        var caption = rect;
        caption.Inflate(-6, -3);
        if (selected && style == 0)
        {
            caption.Height -= 3;
            if (tabs.Alignment == TabAlignment.Bottom) caption.Y += 3;
        }
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
            activeFont ?? tabs.Font, caption, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
    }

    static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        int r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        int size = Math.Max(2, r * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }
}
