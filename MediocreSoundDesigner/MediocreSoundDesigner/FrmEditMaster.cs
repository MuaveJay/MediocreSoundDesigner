namespace MediocreSoundDesigner;

public partial class FrmEditMaster : Form {
  // fields
  protected Track track;
  [System.ComponentModel.Browsable(false)]
  [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public Action<Action> EditExecutor { get; set; }

  // ctor
  public FrmEditMaster() {
    InitializeComponent();
  }

  // sound
  public virtual void SetSound(Track sound) {
    this.track = sound;
    Init();
  }

  // gui
  protected void ApplySettings() {
    BackColor = Settings.Color.WinBg;
    ForeColor = Settings.Color.WinFg;
  }

  public void btnCancel_Click(object sender, EventArgs e) {
    Cancel();
    DialogResult = DialogResult.Cancel;
    Close();
  }

  public void btnApply_Click(object sender, EventArgs e) {
    if (EditExecutor is null)
      Apply();
    else
      EditExecutor(Apply);
    track?.PanDrawSamples?.Invalidate();
    DialogResult = DialogResult.OK;
    Close();
  }

  // override in subclass if needed
  protected virtual void Init() { ApplySettings(); }
  protected virtual void Apply() { }
  protected virtual void Cancel() { }
}
