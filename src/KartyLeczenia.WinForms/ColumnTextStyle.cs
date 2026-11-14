using System.Runtime.CompilerServices;

namespace KartyLeczenia.Windows;

internal sealed record ColumnTextStyle(string Family, float Size, FontStyle FontStyle,
    int Argb, DataGridViewContentAlignment Alignment)
{
    sealed class Overrides
    {
        public ColumnTextStyle? Header;
        public ColumnTextStyle? Data;
    }

    static readonly ConditionalWeakTable<DataGridViewColumn, Overrides> Saved = new();

    internal static ColumnTextStyle Read(DataGridViewCellStyle style)
    {
        var font = style.Font ?? SystemFonts.DefaultFont;
        return new(font.Name, font.SizeInPoints, font.Style, style.ForeColor.ToArgb(), style.Alignment);
    }

    internal static ColumnTextStyle? Get(DataGridViewColumn column, bool header) =>
        Saved.TryGetValue(column, out var styles) ? (header ? styles.Header : styles.Data) : null;

    internal Font CreateFont()
    {
        float size = float.IsFinite(Size) ? Math.Clamp(Size, 1, 200) : SystemFonts.DefaultFont.SizeInPoints;
        try { return new Font(Family, size, FontStyle); }
        catch (ArgumentException) { return new Font(SystemFonts.DefaultFont.FontFamily, size); }
    }

    internal void Apply(DataGridView grid, DataGridViewColumn column, bool header)
    {
        var styles = Saved.GetValue(column, _ => new Overrides());
        var target = header ? column.HeaderCell.Style : column.DefaultCellStyle;
        var ownedFont = Get(column, header) is null ? null : target.Font;
        var font = CreateFont();
        target.Font = font;
        target.ForeColor = Color.FromArgb(Argb);
        if (Enum.IsDefined(Alignment)) target.Alignment = Alignment;
        if (header)
        {
            styles.Header = this;
            grid.EnableHeadersVisualStyles = false;
        }
        else styles.Data = this;
        ownedFont?.Dispose();
        grid.Disposed += (_, _) => font.Dispose();
        grid.Invalidate();
    }
}
