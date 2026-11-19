namespace KartyLeczenia.Windows;

/// <summary>Reusable WinForms popup for customizing the visible AntdUI columns.</summary>
internal static class AntdTableColumnMenu
{
    internal static void Show(Control anchor, AntdUI.Table table, IReadOnlyList<AntdUI.Column> columns,
        Func<AntdUI.Column, bool>? canHide = null)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        int visible = columns.Count(column => column.Visible);

        foreach (var column in columns)
        {
            bool hideable = canHide?.Invoke(column) ?? true;
            var item = new ToolStripMenuItem(column.Title)
            {
                Checked = column.Visible,
                CheckOnClick = false,
                Enabled = hideable && (!column.Visible || visible > 1)
            };
            item.Click += (_, _) =>
            {
                column.Visible = !column.Visible;
                table.Invalidate();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Pokaż wszystkie kolumny", null, (_, _) =>
        {
            foreach (var column in columns) column.Visible = true;
            table.Invalidate();
        });
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(anchor, new Point(0, anchor.Height));
    }
}
