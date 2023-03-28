namespace KeithleyCrosspoint
{
    partial class Form1
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
            this.cmdProcessor1 = new KeithleyCrosspoint.CMDProcessor();
            this.SuspendLayout();
            // 
            // cmdProcessor1
            // 
            this.cmdProcessor1.Location = new System.Drawing.Point(-1, 1);
            this.cmdProcessor1.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.cmdProcessor1.Name = "cmdProcessor1";
            this.cmdProcessor1.Size = new System.Drawing.Size(1173, 598);
            this.cmdProcessor1.TabIndex = 0;
            this.cmdProcessor1.Load += new System.EventHandler(this.cmdProcessor1_Load);
            this.cmdProcessor1.KeyDown += new System.Windows.Forms.KeyEventHandler(this.cmdProcessor1_KeyDown);
            this.cmdProcessor1.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.cmdProcessor1_KeyPress);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1184, 631);
            this.Controls.Add(this.cmdProcessor1);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);

        }

        #endregion

        private CMDProcessor cmdProcessor1;
    }
}

