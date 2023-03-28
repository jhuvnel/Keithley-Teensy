using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using static KeithleyCrosspoint.D;

namespace KeithleyCrosspoint
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            this.KeyPreview = true;
        }

        private void cmdProcessor1_KeyPress(object sender, KeyPressEventArgs e)
        {
            //D.printf("Got key in FORM: %d\n", (int)e.KeyChar);
            dprint($"Got key in FORM: {(int)e.KeyChar}\n");
        }

        private void cmdProcessor1_KeyDown(object sender, KeyEventArgs e)
        {
            //D.printf("Got key in FORM: %d\n", (int)e.KeyCode);
            dprint($"Got key in FORM: {(int)e.KeyCode}\n");
        }

        private void cmdProcessor1_Load(object sender, EventArgs e)
        {

        }

        private void BlankingTrigger_CheckedChanged(object sender, EventArgs e)
        {

        }
    }
}
