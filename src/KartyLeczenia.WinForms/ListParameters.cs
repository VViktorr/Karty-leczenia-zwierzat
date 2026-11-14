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
        var styles = new Dictionary<(DataGridViewColumn Column, bool Header), ColumnTextStyle>();
        void EditStyle(bool header)
        {
            if (list.SelectedItem is not DataGridViewColumn column) return;
            var key = (column, header);
            var inherited = header ? column.HeaderCell.InheritedStyle : column.InheritedStyle;
            var style = styles.TryGetValue(key, out var pending) ? pending : ColumnTextStyle.Read(inherited);
            using var editor = new Form
            {
                Text = $"{column.HeaderText} – {(header ? "Nagłówek" : "Dane")}",
                StartPosition = FormStartPosition.CenterParent,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
            };
            var columns = new ListBox { SelectionMode = SelectionMode.MultiExtended, Width = 300,
                Height = 140, DisplayMember = "HeaderText" };
            foreach (var item in list.Items) columns.Items.Add(item);
            columns.SetSelected(list.SelectedIndex, true);
            var size = new NumericUpDown { Minimum = 1, Maximum = 200, DecimalPlaces = 1,
                Increment = 0.5m, Value = Math.Clamp((decimal)style.Size, 1, 200) };
            bool sizeChanged = false, fontChanged = false, colorChanged = false, alignmentChanged = false;
            size.ValueChanged += (_, _) => sizeChanged = true;
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            var fontButton = new Button { AutoSize = true, Text = $"{style.Family}, {style.Size:g} pkt" };
            fontButton.Click += (_, _) =>
            {
                using var initial = style.CreateFont();
                using var picker = new FontDialog { Font = initial, FontMustExist = true };
                if (picker.ShowDialog(editor) != DialogResult.OK) return;
                using var chosen = picker.Font;
                style = style with { Family = chosen.Name, Size = chosen.SizeInPoints, FontStyle = chosen.Style };
                fontChanged = true;
                size.Value = Math.Clamp((decimal)style.Size, 1, 200);
                fontButton.Text = $"{style.Family}, {style.Size:g} pkt";
            };
            var colorButton = new Button { AutoSize = true, Text = "Kolor tekstu", ForeColor = Color.FromArgb(style.Argb) };
            colorButton.Click += (_, _) =>
            {
                using var picker = new ColorDialog { Color = colorButton.ForeColor, FullOpen = true };
                if (picker.ShowDialog(editor) != DialogResult.OK) return;
                style = style with { Argb = picker.Color.ToArgb() };
                colorButton.ForeColor = picker.Color;
                colorChanged = true;
            };
            var alignment = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            alignment.Items.AddRange(["Do lewej", "Do środka", "Do prawej"]);
            alignment.SelectedIndex = style.Alignment.ToString().EndsWith("Right") ? 2
                : style.Alignment.ToString().EndsWith("Center") ? 1 : 0;
            alignment.SelectedIndexChanged += (_, _) => alignmentChanged = true;
            var accept = new Button { Text = "OK", DialogResult = DialogResult.OK };
            var dismiss = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel };
            panel.Controls.AddRange([new Label { Text = "Kolumny (Ctrl+klik lub Shift+klik)", AutoSize = true },
                columns, fontButton, new Label { Text = "Rozmiar czcionki (pkt)", AutoSize = true },
                size, colorButton, alignment, accept, dismiss]);
            editor.Controls.Add(panel);
            editor.AcceptButton = accept;
            editor.CancelButton = dismiss;
            if (DialogPlacement.Show(editor, dialog, "column-text-style") != DialogResult.OK) return;
            if (!sizeChanged && !fontChanged && !colorChanged && !alignmentChanged) return;
            var selectedAlignment = alignment.SelectedIndex switch
            {
                1 => DataGridViewContentAlignment.MiddleCenter,
                2 => DataGridViewContentAlignment.MiddleRight,
                _ => DataGridViewContentAlignment.MiddleLeft
            };
            foreach (DataGridViewColumn selected in columns.SelectedItems)
            {
                var selectedKey = (selected, header);
                var current = styles.TryGetValue(selectedKey, out var edited) ? edited
                    : ColumnTextStyle.Read(header ? selected.HeaderCell.InheritedStyle : selected.InheritedStyle);
                styles[selectedKey] = current with
                {
                    Family = fontChanged ? style.Family : current.Family,
                    FontStyle = fontChanged ? style.FontStyle : current.FontStyle,
                    Size = sizeChanged || fontChanged ? (float)size.Value : current.Size,
                    Argb = colorChanged ? style.Argb : current.Argb,
                    Alignment = alignmentChanged ? selectedAlignment : current.Alignment
                };
            }
        }
        var headerStyle = new Button { Text = "Wygląd nagłówka…", AutoSize = true };
        var dataStyle = new Button { Text = "Wygląd danych…", AutoSize = true };
        headerStyle.Click += (_, _) => EditStyle(true);
        dataStyle.Click += (_, _) => EditStyle(false);
        bar.Controls.AddRange([headerStyle, dataStyle]);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var valueOptions = new Dictionary<DataGridViewColumn, string>();
        var valueButton = new Button { Text = "Dane kolumny…", AutoSize = true };
        void UpdateValueButton() => valueButton.Enabled = list.SelectedItem is DataGridViewColumn selected
            && DocumentColumnOptions.Choices(selected).Length > 0;
        list.SelectedIndexChanged += (_, _) => UpdateValueButton();
        valueButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not DataGridViewColumn column) return;
            var options = DocumentColumnOptions.Choices(column);
            using var picker = new Form
            {
                Text = column.HeaderText, StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(300, 85), FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            };
            var choices = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            choices.Items.AddRange(options);
            string? current = valueOptions.GetValueOrDefault(column) ?? DocumentColumnOptions.Read(column);
            choices.SelectedIndex = Math.Max(0, Array.IndexOf(options, current));
            var apply = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            picker.Controls.AddRange([choices, apply]);
            picker.AcceptButton = apply;
            if (DialogPlacement.Show(picker, dialog, "column-value-options") == DialogResult.OK && choices.SelectedItem is string value)
                valueOptions[column] = value;
        };
        bar.Controls.Add(valueButton);
        UpdateValueButton();
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
        if (DialogPlacement.Show(dialog, owner, "list-parameters") != DialogResult.OK) return;
        for (int i = 0; i < list.Items.Count; i++)
        {
            var column = (DataGridViewColumn)list.Items[i]!;
            column.DisplayIndex = i;
            column.Visible = list.GetItemChecked(i);
        }
        foreach (var (column, value) in valueOptions)
            DocumentColumnOptions.Apply(column, value);
        foreach (var (key, style) in styles)
            style.Apply(grid, key.Column, key.Header);
        grid.MultiSelect = multi.Checked;
        grid.RowHeadersVisible = headers.Checked;
        grid.CellBorderStyle = lines.Checked ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.None;
    }
}
