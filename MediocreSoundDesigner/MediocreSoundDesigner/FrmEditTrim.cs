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

public partial class FrmEditTrim : Form
{
    public FrmEditTrim()
    {
        InitializeComponent();
        Text = "Trim at Times";
        nudStart.DecimalPlaces = nudEnd.DecimalPlaces = 6;
        nudStart.Increment = nudEnd.Increment = 0.001m;
        AcceptButton = btnTrim;
        CancelButton = btnCancel;
        StartPosition = FormStartPosition.CenterParent;
    }

    public void SetSound(Track track, double? initialStart = null, double? initialEnd = null)
    {
        nudStart.Maximum = (decimal)track.DurationSeconds;
        nudEnd.Maximum = (decimal)track.DurationSeconds;
        bool valid = initialStart is double start && initialEnd is double end && double.IsFinite(start) &&
                     double.IsFinite(end) && start >= 0 && start < end && end <= track.DurationSeconds;
        nudStart.Value = valid ? (decimal)initialStart.Value : 0;
        nudEnd.Value = valid ? (decimal)initialEnd.Value : (decimal)track.DurationSeconds;
    }

    public double StartSeconds => (double)nudStart.Value;
    public double EndSeconds => (double)nudEnd.Value;

    private void btnTrim_Click(object sender, EventArgs e)
    {
        if (nudStart.Value >= nudEnd.Value)
        {
            MessageBox.Show("End time must be greater than start time.");
            return;
        }

        DialogResult = DialogResult.OK;
    }
}
