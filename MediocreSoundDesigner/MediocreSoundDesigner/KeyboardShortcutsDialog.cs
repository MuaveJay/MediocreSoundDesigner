namespace MediocreSoundDesigner;

public sealed class KeyboardShortcutsDialog : Form
{
    private readonly TextBox search = new();
    private readonly DataGridView shortcutGrid = new();
    private readonly Label resultCount = new();
    private readonly IReadOnlyList<ShortcutHelpEntry> entries;

    public KeyboardShortcutsDialog(IEnumerable<ShortcutHelpEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = entries.ToArray();
        AutoScaleMode = AutoScaleMode.Font;
        Text = "Keyboard & Mouse Shortcuts";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Color background = Settings.Color.WinBg;
        Color surface = Color.FromArgb(Math.Min(255, background.R + 16), Math.Min(255, background.G + 16), Math.Min(255, background.B + 16));
        Color foreground = TrackVisuals.Readable(Settings.Color.WinFg, surface);
        BackColor = surface; ForeColor = foreground;

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (SizeType type in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize })
            layout.RowStyles.Add(new RowStyle(type, type == SizeType.Percent ? 100 : 0));
        Label title = new() { Text = "Keyboard shortcuts and mouse controls", AutoSize = true, Margin = new Padding(0, 0, 0, 6), Font = new Font(Font, FontStyle.Bold) };
        search.Name = "shortcutSearch";
        search.PlaceholderText = "Search a shortcut, action or section...";
        search.AccessibleName = "Search keyboard shortcuts and mouse controls";
        search.Dock = DockStyle.Fill;
        search.Margin = new Padding(0, 0, 0, 10);
        search.BackColor = background; search.ForeColor = foreground;
        search.TextChanged += (sender, e) => RefreshEntries();

        shortcutGrid.Name = "shortcutGrid";
        shortcutGrid.AccessibleName = "Keyboard shortcuts and mouse controls";
        shortcutGrid.Dock = DockStyle.Fill;
        shortcutGrid.Margin = Padding.Empty;
        shortcutGrid.ReadOnly = true;
        shortcutGrid.AllowUserToAddRows = shortcutGrid.AllowUserToDeleteRows = shortcutGrid.AllowUserToResizeRows = false;
        shortcutGrid.RowHeadersVisible = false;
        shortcutGrid.MultiSelect = false;
        shortcutGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        shortcutGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        shortcutGrid.BackgroundColor = background;
        shortcutGrid.BorderStyle = BorderStyle.None;
        shortcutGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        shortcutGrid.EnableHeadersVisualStyles = false;
        shortcutGrid.GridColor = surface;
        shortcutGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = background, ForeColor = TrackVisuals.Readable(foreground, background),
            SelectionBackColor = surface, SelectionForeColor = foreground,
            WrapMode = DataGridViewTriState.True, Padding = new Padding(5, 7, 5, 7)
        };
        shortcutGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = surface, ForeColor = foreground, Padding = new Padding(5), Font = new Font(Font, FontStyle.Bold)
        };
        shortcutGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        shortcutGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Section", HeaderText = "Section", Width = 116, MinimumWidth = 90, SortMode = DataGridViewColumnSortMode.NotSortable });
        shortcutGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Input", HeaderText = "Shortcut / gesture", Width = 205, MinimumWidth = 150, SortMode = DataGridViewColumnSortMode.NotSortable });
        shortcutGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 240, SortMode = DataGridViewColumnSortMode.NotSortable });

        resultCount.AutoSize = true;
        resultCount.Margin = new Padding(0, 8, 0, 0);
        Label scope = new()
        {
            Text = "Volume and Delete affect selected tracks. Split, trim and effects use the active track.\nStop and Loop Playback are available from the Playback menu.",
            AutoSize = true, Margin = new Padding(0, 8, 0, 8)
        };
        Button close = new() { Text = "Close", AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel, AccessibleName = "Close keyboard shortcuts" };
        close.FlatStyle = FlatStyle.Flat;
        close.UseVisualStyleBackColor = false;
        close.BackColor = surface; close.ForeColor = foreground;
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        footer.Controls.Add(close);
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(search, 0, 1); layout.Controls.Add(shortcutGrid, 0, 2);
        layout.Controls.Add(resultCount, 0, 3); layout.Controls.Add(scope, 0, 4); layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout);
        CancelButton = close;
        Shown += (sender, e) => search.Focus();
        RefreshEntries();
    }

    private void RefreshEntries()
    {
        var matches = ShortcutHelpCatalog.Filter(entries, search.Text);
        shortcutGrid.Rows.Clear();
        foreach (ShortcutHelpEntry entry in matches) shortcutGrid.Rows.Add(entry.Section, entry.Input, entry.Action);
        shortcutGrid.ClearSelection();
        resultCount.Text = matches.Count == 0 ? "No matching controls. Try another search." : $"{matches.Count} controls shown";
    }
}
