using System.Drawing.Drawing2D;

global using Button = AntdUI.Button;
global using CheckBox = AntdUI.Checkbox;

namespace KartyLeczenia.Windows;

/// <summary>
/// Consistent solid, high-contrast selection for segmented document and expiry
/// filters. Paint the entire surface rather than relying on AntdUI's separate
/// ToggleBack/DefaultBack layers, which may leave washed-out foregrounds in
/// the Classic preset and on changing appearance at runtime.
/// </summary>
internal sealed class ThemeSelector : Button
{
    AppearanceOptions look = AppearanceOptions.ForPreset("Antd Jasny");
    bool hovering;
    bool pressing;

    internal ThemeSelector()
    {
        AutoSize = true;
        TabStop = true;
        WaveSize = 0;
        AccessibleRole = AccessibleRole.RadioButton;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        MouseEnter += (_, _) => { hovering = true; Invalidate(); };
        MouseLeave += (_, _) => { hovering = false; pressing = false; Invalidate(); };
        MouseDown += (_, _) => { pressing = true; Invalidate(); };
        MouseUp += (_, _) => { pressing = false; Invalidate(); };
    }

    internal void SetAppearance(AppearanceOptions value)
    {
        look = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;

        Color baseColor = AppearanceThemes.Canvas(look);
        Color ink = AppearanceThemes.Ink(look);
        Color accent = AppearanceThemes.Accent(look);
        bool dark = AppearanceThemes.IsDark(look);
        bool selected = Toggle;
        bool outline = look.SwitchStyle == 2;
        Color surface = selected
            ? outline ? AppearanceThemes.Blend(baseColor, accent, dark ? .21f : .10f) : accent
            : AppearanceThemes.Blend(baseColor, ink, dark ? .11f : .035f);
        if (hovering)
            surface = AppearanceThemes.Blend(surface,
                selected ? Color.White : accent, selected ? .08f : (dark ? .17f : .08f));
        if (pressing) surface = AppearanceThemes.Blend(surface, Color.Black, .09f);
        Color fore = !Enabled ? AppearanceThemes.Blend(ink, baseColor, .55f)
            : selected ? outline ? accent : AppearanceThemes.ContrastingText(surface) : ink;

        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(baseColor);
            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            if (rect.Width < 2 || rect.Height < 2) return;
            int radius = look.SwitchStyle == 4
                ? rect.Height / 2 : Math.Clamp(look.CornerRadius, 5, 16);
            using var path = Rounded(rect, radius);
            using (var brush = new SolidBrush(surface))
                e.Graphics.FillPath(brush, path);
            Color borderColor = selected && outline ? accent
                : selected ? AppearanceThemes.Blend(surface, baseColor, .16f)
                : AppearanceThemes.Blend(baseColor, ink, dark ? .32f : .15f);
            using (var border = new Pen(borderColor, selected && outline ? 1.8f : 1f))
                e.Graphics.DrawPath(border, path);
        }
        finally { e.Graphics.Restore(state); }

        var label = new Rectangle(8, 2, Math.Max(0, Width - 16), Math.Max(0, Height - 4));
        TextRenderer.DrawText(e.Graphics, Text, Font, label, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix);
        // No Windows focus rectangle: selection itself has a visible solid fill.
    }

    static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        int r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        int diameter = Math.Max(2, r * 2);
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
