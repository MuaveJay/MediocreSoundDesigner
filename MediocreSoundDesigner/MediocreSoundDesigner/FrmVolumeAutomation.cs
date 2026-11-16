
namespace MediocreSoundDesigner;

public sealed class FrmVolumeAutomation : Form
{
    private readonly Track track;
    private readonly NumericUpDown numTime = new();
    private readonly NumericUpDown numVolume = new();
    private readonly ListView pointList = new();
    private readonly Button btnRemove = new();
    private readonly Button btnClear = new();
    private readonly List<(long Frame, float Gain)> shownPoints = new();
    private bool refreshingList;

    public FrmVolumeAutomation(Track selectedTrack)
    {
        track = selectedTrack ?? throw new ArgumentNullException(nameof(selectedTrack));

        Text = $"Volume Automation - {track.Name}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(575, 410);
        MinimumSize = new Size(525, 365);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        Controls.Add(layout);

        Label instructions = new()
        {
            Dock = DockStyle.Fill,
            Text = $"Track: {track.Name}  |  Length: {track.DurationSeconds:0.00} seconds\n" +
                   "Add volume points at different times. The volume changes smoothly " +
                   "between points. Times start at the beginning of this track.",
            AutoSize = false
        };
        layout.Controls.Add(instructions, 0, 0);

        FlowLayoutPanel inputs = new()
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        layout.Controls.Add(inputs, 0, 1);

        inputs.Controls.Add(new Label
        {
            Text = "Time (seconds):",
            AutoSize = true,
            Margin = new Padding(0, 9, 5, 0)
        });

        numTime.DecimalPlaces = 6;
        numTime.Minimum = 0;
        numTime.Maximum = (decimal)Math.Ceiling(track.DurationSeconds);
        numTime.Increment = 0.25M;
        numTime.Width = 115;
        inputs.Controls.Add(numTime);

        inputs.Controls.Add(new Label
        {
            Text = "Volume (%):",
            AutoSize = true,
            Margin = new Padding(15, 9, 5, 0)
        });

        numVolume.DecimalPlaces = 1;
        numVolume.Minimum = 0;
        numVolume.Maximum = 200;
        numVolume.Value = 100;
        numVolume.Increment = 5;
        numVolume.Width = 90;
        inputs.Controls.Add(numVolume);

        GroupBox listBox = new()
        {
            Dock = DockStyle.Fill,
            Text = "Control points"
        };
        layout.Controls.Add(listBox, 0, 2);

        pointList.Dock = DockStyle.Fill;
        pointList.View = View.Details;
        pointList.FullRowSelect = true;
        pointList.HideSelection = false;
        pointList.MultiSelect = false;
        pointList.Columns.Add("Time (seconds)", 180);
        pointList.Columns.Add("Volume", 160);
        pointList.SelectedIndexChanged += PointList_SelectedIndexChanged;
        listBox.Controls.Add(pointList);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        layout.Controls.Add(actions, 0, 3);

        Button btnAdd = new()
        {
            Text = "Add / Update",
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 0)
        };
        btnAdd.Click += (sender, e) => AddOrUpdatePoint();
        actions.Controls.Add(btnAdd);

        btnRemove.Text = "Remove Selected";
        btnRemove.AutoSize = true;
        btnRemove.Margin = new Padding(0, 5, 8, 0);
        btnRemove.Click += (sender, e) => RemoveSelectedPoint();
        actions.Controls.Add(btnRemove);

        btnClear.Text = "Clear All";
        btnClear.AutoSize = true;
        btnClear.Margin = new Padding(0, 5, 8, 0);
        btnClear.Click += (sender, e) =>
        {
            track.ClearAutomation();
            RefreshPoints();
        };
        actions.Controls.Add(btnClear);

        Button btnClose = new()
        {
            Text = "Close",
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0)
        };
        btnClose.Click += (sender, e) => Close();
        actions.Controls.Add(btnClose);

        AcceptButton = btnAdd;
        CancelButton = btnClose;
        RefreshPoints();
    }

    private void RefreshPoints(long? selectFrame = null)
    {
        refreshingList = true;
        pointList.BeginUpdate();
        pointList.Items.Clear();
        shownPoints.Clear();

        foreach (var point in track.AutomationPoints)
        {
            shownPoints.Add(point);
            double seconds = point.Frame / (double)track.SampleRate;
            ListViewItem item = new($"{seconds:0.000000}");
            item.SubItems.Add($"{point.Gain * 100:0.0}%");
            pointList.Items.Add(item);
        }

        if (selectFrame.HasValue)
        {
            int index = shownPoints.FindIndex(p => p.Frame == selectFrame.Value);
            if (index >= 0)
            {
                pointList.Items[index].Selected = true;
                pointList.Items[index].EnsureVisible();
            }
        }

        pointList.EndUpdate();
        refreshingList = false;
        btnRemove.Enabled = pointList.SelectedIndices.Count > 0;
        btnClear.Enabled = shownPoints.Count > 0;
    }

    private void PointList_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (refreshingList)
            return;

        btnRemove.Enabled = pointList.SelectedIndices.Count > 0;
        if (pointList.SelectedIndices.Count == 0)
            return;

        var point = shownPoints[pointList.SelectedIndices[0]];
        decimal seconds = decimal.Round(
            (decimal)(point.Frame / (double)track.SampleRate), 6);

        numTime.Value = Math.Clamp(
            seconds, numTime.Minimum, numTime.Maximum);

        numVolume.Value = Math.Clamp(
            decimal.Round((decimal)point.Gain * 100, 1),
            numVolume.Minimum, numVolume.Maximum);
    }

    private void AddOrUpdatePoint()
    {
        double seconds = (double)numTime.Value;
        float gain = (float)(numVolume.Value / 100M);

        if (!track.SetAutomationPoint(seconds, gain))
        {
            MessageBox.Show(this,
                "No point was changed. Check that the time is within the track's length " +
                "and that the volume is different from the existing value.",
                "Volume Automation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        long frame = (long)Math.Round(
            seconds * track.SampleRate,
            MidpointRounding.AwayFromZero);

        RefreshPoints(frame);
    }

    private void RemoveSelectedPoint()
    {
        if (pointList.SelectedIndices.Count == 0)
            return;

        long frame = shownPoints[pointList.SelectedIndices[0]].Frame;
        track.RemoveAutomationPoint(
            frame / (double)track.SampleRate);

        RefreshPoints();
    }
}
