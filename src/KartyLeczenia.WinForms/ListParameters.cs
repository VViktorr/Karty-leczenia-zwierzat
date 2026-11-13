namespace KartyLeczenia.Windows;

internal static class ListParameters
{
    internal static void Show(Form owner, DataGridView grid)
    {
        using var dialog = new Form { Text = "Parametry listy", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(620, 420), MinimizeBox = false, MaximizeBox = false };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, DisplayMember = "HeaderText" };
        foreach (var column in grid.Columns.Cast<DataGridViewColumn>().OrderBy(column => column.DisplayIndex))
            list.Items.Add(column, column.Visible);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        void AddMove(string text, int offset)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) =>
            {
                int index = list.SelectedIndex, next = index + offset;
                if (index < 0 || next < 0 || next >= list.Items.Count) return;
                var item = list.Items[index]!;
                bool shown = list.GetItemChecked(index);
                list.Items.RemoveAt(index);
                list.Items.Insert(next, item);
                list.SetItemChecked(next, shown);
                list.SelectedIndex = next;
            };
            bar.Controls.Add(button);
        }
        AddMove("W górę", -1);
        AddMove("W dół", 1);
        var multi = new CheckBox { Text = "Multiwybór", Checked = grid.MultiSelect, AutoSize = true };
        var headers = new CheckBox { Text = "Nagłówki wierszy", Checked = grid.RowHeadersVisible, AutoSize = true };
        var lines = new CheckBox { Text = "Linie siatki", Checked = grid.CellBorderStyle != DataGridViewCellBorderStyle.None, AutoSize = true };
        bar.Controls.AddRange([multi, headers, lines]);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        var ok = new Button { Text = "OK" };
        ok.Click += (_, _) =>
        {
            if (list.CheckedItems.Count == 0) { MessageBox.Show(dialog, "Zostaw przynajmniej jedną kolumnę."); return; }
            dialog.DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([ok, cancel]);
        dialog.Controls.Add(list);
        dialog.Controls.Add(actions);
        dialog.Controls.Add(bar);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        for (int i = 0; i < list.Items.Count; i++)
        {
            var column = (DataGridViewColumn)list.Items[i]!;
            column.DisplayIndex = i;
            column.Visible = list.GetItemChecked(i);
        }
        grid.MultiSelect = multi.Checked;
        grid.RowHeadersVisible = headers.Checked;
        grid.CellBorderStyle = lines.Checked ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.None;
    }
}
