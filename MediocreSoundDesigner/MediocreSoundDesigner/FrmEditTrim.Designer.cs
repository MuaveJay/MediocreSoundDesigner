namespace MediocreSoundDesigner
{
    partial class FrmEditTrim
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
            label1 = new Label();
            label2 = new Label();
            nudStart = new NumericUpDown();
            nudEnd = new NumericUpDown();
            btnCancel = new Button();
            btnTrim = new Button();
            ((System.ComponentModel.ISupportInitialize)nudStart).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudEnd).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(75, 149);
            label1.Name = "label1";
            label1.Size = new Size(204, 30);
            label1.TabIndex = 0;
            label1.Text = "Start Time (seconds):";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(75, 248);
            label2.Name = "label2";
            label2.Size = new Size(197, 30);
            label2.TabIndex = 1;
            label2.Text = "End Time (seconds):";
            // 
            // nudStart
            // 
            nudStart.DecimalPlaces = 2;
            nudStart.Location = new Point(428, 147);
            nudStart.Name = "nudStart";
            nudStart.Size = new Size(131, 35);
            nudStart.TabIndex = 2;
            // 
            // nudEnd
            // 
            nudEnd.DecimalPlaces = 2;
            nudEnd.Location = new Point(428, 246);
            nudEnd.Name = "nudEnd";
            nudEnd.Size = new Size(131, 35);
            nudEnd.TabIndex = 3;
            // 
            // btnCancel
            // 
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(271, 379);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(131, 40);
            btnCancel.TabIndex = 4;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnTrim
            // 
            btnTrim.Location = new Point(428, 379);
            btnTrim.Name = "btnTrim";
            btnTrim.Size = new Size(131, 40);
            btnTrim.TabIndex = 5;
            btnTrim.Text = "Trim";
            btnTrim.UseVisualStyleBackColor = true;
            btnTrim.Click += btnTrim_Click;
            // 
            // FrmEditTrim
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(btnTrim);
            Controls.Add(btnCancel);
            Controls.Add(nudEnd);
            Controls.Add(nudStart);
            Controls.Add(label2);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            Name = "FrmEditTrim";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Trim Audio";
            ((System.ComponentModel.ISupportInitialize)nudStart).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudEnd).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label label2;
        private NumericUpDown nudStart;
        private NumericUpDown nudEnd;
        private Button btnCancel;
        private Button btnTrim;
    }
}