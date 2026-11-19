namespace KartyLeczenia.Windows;

/// <summary>
/// Unified main tab painting for the live preview and the production UI.
/// Matches AntdUI's Line/Card concepts without changing WinForms TabPage
/// identity or its saved selected indexes during the transition.
/// </summary>
internal static class MainTabPainter
{
    internal static void Draw(TabControl tabs, DrawItemEventArgs e, AppearanceOptions appearance)
    {
        if (e.Index < 0 || e.Index >= tabs.TabCount) return;
        bool selected = e.Index == tabs.SelectedIndex;
        // Tab styling is independent of the application color preset.
        // Even users of the Classic palette may choose the Line/Card header.
        bool classic = appearance.MainTabStyle == 3;
        bool dark = AppearanceThemes.IsDark(appearance);
        int style = appearance.MainTabStyle;
        var rect = e.Bounds;
        rect.Inflate(-2, -2);
        if (rect.Width <= 4 || rect.Height <= 4) return;

        Color canvas = AppearanceThemes.Canvas(appearance);
        Color ink = AppearanceThemes.Ink(appearance);
        Color accent = AppearanceThemes.Accent(appearance);
        Color activeText = selected ? (style == 2 ? accent : ink) : ink;
        Color subdued = AppearanceThemes.Blend(canvas, ink, dark ? .08f : .055f);
        Color selectedFill = style switch
        {
            1 => dark ? AppearanceThemes.Blend(canvas, Color.White, .12f) : Color.White,
            2 => AppearanceThemes.Blend(canvas, accent, dark ? .26f : .14f),
            _ => canvas
        };

        using (var baseBrush = new SolidBrush(style == 3 ? SystemColors.Control : canvas))
            e.Graphics.FillRectangle(baseBrush, e.Bounds);

        if (style == 1 || style == 2)
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            int radius = Math.Min(Math.Max(4, appearance.CornerRadius), Math.Min(rect.Width, rect.Height) / 2);
            int diameter = 2 * radius;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var back = new SolidBrush(selected ? selectedFill : subdued);
            e.Graphics.FillPath(back, path);
            if (style == 1)
            {
                using var border = new Pen(selected ? accent
                    : AppearanceThemes.Blend(canvas, ink, .13f), selected ? 1.5f : 1f);
                e.Graphics.DrawPath(border, path);
            }
        }
        else if (style == 0 && selected)
        {
            // AntdUI Tabs "Line" model: only the indicator is saturated.
            // Avoid painting an active full-area block behind the text.
            bool atBottom = tabs.Alignment == TabAlignment.Bottom;
            int thickness = Math.Max(2, (int)Math.Round(tabs.DeviceDpi / 96f * 3f));
            var indicator = new Rectangle(rect.Left + 6,
                atBottom ? rect.Top : rect.Bottom - thickness,
                Math.Max(1, rect.Width - 12), thickness);
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, indicator);
            activeText = accent;
        }
        else if (style == 3)
        {
            using var brush = new SolidBrush(selected ? SystemColors.ControlLightLight : SystemColors.Control);
            e.Graphics.FillRectangle(brush, rect);
            using var edge = new Pen(SystemColors.ControlDark);
            e.Graphics.DrawRectangle(edge, rect);
            activeText = SystemColors.ControlText;
        }

        using var selectedFont = selected
            ? new Font(tabs.Font, tabs.Font.Style | FontStyle.Bold) : null;
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
            selectedFont ?? tabs.Font, rect, activeText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
        if (selected && tabs.Focused)
        {
            var focus = rect;
            focus.Inflate(-5, -5);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus);
        }
    }


}
