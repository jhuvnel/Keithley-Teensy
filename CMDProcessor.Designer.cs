namespace KeithleyCrosspoint
{
    partial class CMDProcessor
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.RunPauseButton = new System.Windows.Forms.Button();
            this.OpenFile = new System.Windows.Forms.Button();
            this.InitButton = new System.Windows.Forms.Button();
            this.coilCheckBox = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.filenameTextBox = new System.Windows.Forms.TextBox();
            this.BlankingTrigger = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // listBox1
            // 
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 16;
            this.listBox1.Location = new System.Drawing.Point(32, 62);
            this.listBox1.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(1079, 292);
            this.listBox1.TabIndex = 0;
            // 
            // RunPauseButton
            // 
            this.RunPauseButton.Location = new System.Drawing.Point(115, 393);
            this.RunPauseButton.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.RunPauseButton.Name = "RunPauseButton";
            this.RunPauseButton.Size = new System.Drawing.Size(541, 71);
            this.RunPauseButton.TabIndex = 1;
            this.RunPauseButton.Text = "button1";
            this.RunPauseButton.UseVisualStyleBackColor = true;
            this.RunPauseButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.RunPauseButton_MouseClick);
            // 
            // OpenFile
            // 
            this.OpenFile.Location = new System.Drawing.Point(53, 15);
            this.OpenFile.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.OpenFile.Name = "OpenFile";
            this.OpenFile.Size = new System.Drawing.Size(197, 39);
            this.OpenFile.TabIndex = 2;
            this.OpenFile.Text = "Open Script File";
            this.OpenFile.UseVisualStyleBackColor = true;
            this.OpenFile.MouseClick += new System.Windows.Forms.MouseEventHandler(this.OpenFile_MouseClick);
            // 
            // InitButton
            // 
            this.InitButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.InitButton.Location = new System.Drawing.Point(685, 393);
            this.InitButton.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.InitButton.Name = "InitButton";
            this.InitButton.Size = new System.Drawing.Size(117, 71);
            this.InitButton.TabIndex = 3;
            this.InitButton.Text = "Init\r\n(debugging)";
            this.InitButton.UseVisualStyleBackColor = true;
            this.InitButton.MouseClick += new System.Windows.Forms.MouseEventHandler(this.InitButton_MouseClick);
            // 
            // coilCheckBox
            // 
            this.coilCheckBox.AutoSize = true;
            this.coilCheckBox.Location = new System.Drawing.Point(293, 26);
            this.coilCheckBox.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.coilCheckBox.Name = "coilCheckBox";
            this.coilCheckBox.Size = new System.Drawing.Size(143, 21);
            this.coilCheckBox.TabIndex = 4;
            this.coilCheckBox.Text = "Using Coil System";
            this.coilCheckBox.UseVisualStyleBackColor = true;
            this.coilCheckBox.CheckedChanged += new System.EventHandler(this.coilCheckBox_CheckedChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(488, 26);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(81, 17);
            this.label1.TabIndex = 5;
            this.label1.Text = "File Details:";
            // 
            // filenameTextBox
            // 
            this.filenameTextBox.Location = new System.Drawing.Point(588, 20);
            this.filenameTextBox.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.filenameTextBox.Name = "filenameTextBox";
            this.filenameTextBox.Size = new System.Drawing.Size(212, 22);
            this.filenameTextBox.TabIndex = 6;
            // 
            // BlankingTrigger
            // 
            this.BlankingTrigger.AutoSize = true;
            this.BlankingTrigger.Location = new System.Drawing.Point(834,402);
            this.BlankingTrigger.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.BlankingTrigger.Name = "BlankingTrigger";
            this.BlankingTrigger.Size = new System.Drawing.Size(143, 21);
            this.BlankingTrigger.TabIndex = 7;
            this.BlankingTrigger.Text = "Blanking Trigger";
            this.BlankingTrigger.UseVisualStyleBackColor = true;
            // 
            // CMDProcessor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.filenameTextBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.coilCheckBox);
            this.Controls.Add(this.InitButton);
            this.Controls.Add(this.OpenFile);
            this.Controls.Add(this.RunPauseButton);
            this.Controls.Add(this.listBox1);
            this.Controls.Add(this.BlankingTrigger);
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Name = "CMDProcessor";
            this.Size = new System.Drawing.Size(1148, 526);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.Button RunPauseButton;
        private System.Windows.Forms.Button OpenFile;
        private System.Windows.Forms.Button InitButton;
        private System.Windows.Forms.CheckBox coilCheckBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox filenameTextBox;
        private System.Windows.Forms.CheckBox BlankingTrigger;
    }
}
