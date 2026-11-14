namespace MediocreSoundDesigner
{
    partial class FrmEditSplit
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            lblSplit = new Label();
            nudSplit = new NumericUpDown();
            btnSplit = new Button();
            btnCancel = new Button();
            ((System.ComponentModel.ISupportInitialize)nudSplit).BeginInit();
            SuspendLayout();
            // 
            // lblSplit
            // 
            lblSplit.AutoSize = true;
            lblSplit.Location = new Point(76, 70);
            lblSplit.Name = "lblSplit";
            lblSplit.Size = new Size(175, 30);
            lblSplit.TabIndex = 0;
            lblSplit.Text = "Split at (seconds):";
            // 
            // nudSplit
            // 
            nudSplit.DecimalPlaces = 2;
            nudSplit.Location = new Point(278, 70);
            nudSplit.Name = "nudSplit";
            nudSplit.Size = new Size(210, 35);
            nudSplit.TabIndex = 1;
            // 
            // btnSplit
            // 
            btnSplit.Location = new Point(357, 202);
            btnSplit.Name = "btnSplit";
            btnSplit.Size = new Size(131, 40);
            btnSplit.TabIndex = 2;
            btnSplit.Text = "Split";
            btnSplit.UseVisualStyleBackColor = true;
            btnSplit.Click += btnSplit_Click;
            // 
            // btnCancel
            // 
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(191, 202);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(131, 40);
            btnCancel.TabIndex = 3;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            // 
            // FrmEditSplit
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(btnCancel);
            Controls.Add(btnSplit);
            Controls.Add(nudSplit);
            Controls.Add(lblSplit);
            Name = "FrmEditSplit";
            Text = "FrmEditSplit";
            ((System.ComponentModel.ISupportInitialize)nudSplit).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblSplit;
        private NumericUpDown nudSplit;
        private Button btnSplit;
        private Button btnCancel;
    }
}