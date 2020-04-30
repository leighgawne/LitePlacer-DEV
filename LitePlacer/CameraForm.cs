using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Terpsichore.Machine.Vision;

namespace LitePlacer
{
    public partial class CameraForm : Form
    {
        public dynamic MainForm
        {
            set
            {
                cameraUC.MainForm = value;
            }
        }

        private CameraUC cameraUC;

        public CameraForm()
        {
            InitializeComponent();

            cameraUC = new CameraUC() { Dock = DockStyle.Fill };
            Controls.Add(cameraUC);

            FormClosing += CameraForm_FormClosing;
        }

        private void CameraForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            cameraUC.Shutdown();
        }
    }
}
