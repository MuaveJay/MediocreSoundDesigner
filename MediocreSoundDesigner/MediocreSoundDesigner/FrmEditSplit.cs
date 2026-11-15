using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace MediocreSoundDesigner;

public partial class FrmEditSplit : Form
{
    public FrmEditSplit()
    {
        InitializeComponent();
        Text = "Split at Time";
        lblSplit.Text = "Track time (seconds):";
        nudSplit.DecimalPlaces = 6;
        nudSplit.Increment = 0.001m;
        AcceptButton = btnSplit;
        CancelButton = btnCancel;
        StartPosition = FormStartPosition.CenterParent;
    }

    public void SetSound(Track track, double? initialSeconds = null)
    {
        nudSplit.Maximum = (decimal)track.DurationSeconds;
        double seconds = initialSeconds is double candidate && double.IsFinite(candidate) &&
                         candidate > 0 && candidate < track.DurationSeconds
            ? candidate : track.DurationSeconds / 2;
        nudSplit.Value = (decimal)seconds;
    }

    public double SplitSeconds => (double)nudSplit.Value;

    private void btnSplit_Click(object sender, EventArgs e)
    {
        if (nudSplit.Value <= 0 || nudSplit.Value >= nudSplit.Maximum)
        {
            MessageBox.Show("Choose a time within the recording.");
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
