using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LitePlacer
{
    public partial class MultiCameraForm : Form
    {
        private List<CameraUC> cameraUCs = new List<CameraUC>();

        public MultiCameraForm()
        {
            InitializeComponent();

            int threshold = 10;

            for (int rowIndex = 0; rowIndex < cameraTableLayoutPanel.RowCount; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < cameraTableLayoutPanel.ColumnCount; columnIndex++)
                {
                    var cameraUC = new CameraUC()
                    {
                        Dock = DockStyle.Fill,
                        FilterThreshold = threshold
                    };

                    threshold += 10;

                    cameraUCs.Add(cameraUC);
                    cameraTableLayoutPanel.Controls.Add(cameraUC, columnIndex, rowIndex);
                }
            }

            FormClosing += CameraForm_FormClosing;
        }

        private void CameraForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            cameraUCs.ForEach(x => x.Shutdown());
        }
    }
}
