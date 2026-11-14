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
    }

    public void SetSound(Track track)
    {
        nudSplit.Maximum = (decimal)track.DurationSeconds;
        nudSplit.Value = (decimal)(track.DurationSeconds / 2);
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
