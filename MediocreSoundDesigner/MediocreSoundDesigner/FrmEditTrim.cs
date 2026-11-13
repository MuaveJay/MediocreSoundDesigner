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
    }

    public void SetSound(Track track)
    {
        nudStart.Maximum = (decimal)track.DurationSeconds;
        nudEnd.Maximum = (decimal)track.DurationSeconds;
        nudEnd.Value = (decimal)track.DurationSeconds;
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
