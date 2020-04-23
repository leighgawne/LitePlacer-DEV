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
    public partial class CameraForm : Form
    {
        private const int THREAD_PERIOD_MS = 1000;
        private ThreadHelper threadHelper;
        public FormMain MainForm;

        public CameraForm()
        {
            InitializeComponent();

            threadHelper = new ThreadHelper("Camera Form", THREAD_PERIOD_MS, UpdateData, ThreadExceptionHandler);
            threadHelper.StartThreads();

            FormClosing += CameraForm_FormClosing;
        }

        private void CameraForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            threadHelper.StopThreads();
        }

        private void UpdateData()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    UpdateCameraOverlay();
                }));
            }
            else
            {
                UpdateCameraOverlay();
            }
        }

        private void UpdateCameraOverlay()
        {
            if (MainForm.DownCamera.IsRunning())
            {
                MainForm.DownCamera.UseCalibrationMeasurementFunctions();

                if (MainForm.DownCamera.GetClosestCircle(out double X, out double Y, 20.0 / MainForm.Setting.DownCam_XmmPerPixel) > 0)
                {
                    X = X * MainForm.Setting.DownCam_XmmPerPixel;
                    Y = -Y * MainForm.Setting.DownCam_YmmPerPixel;

                    if (FeatureDetails != null)
                    {
                        FeatureDetails.Text = "X: " + X.ToString() + " Y: " + Y.ToString();
                    }
                }
            }
        }

        private void ThreadExceptionHandler(Exception ex)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    MessageBox.Show("Exception: " + ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
            else
            {
                MessageBox.Show("Exception: " + ex.Message, "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
