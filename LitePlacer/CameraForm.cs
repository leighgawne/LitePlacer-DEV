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
        private VisionPipeline visionPipeline = new VisionPipeline();
        private ImageProcessor imageProcessor;
        private ImageFilter imageFilter;
        private MeasureFeatures measureFeatures = new MeasureFeatures();
        private DrawFeatures drawFeatures = new DrawFeatures();

        private Guid JobGuid;
        private bool configCompleted = false;
        private const int THREAD_PERIOD_MS = 1000;
        private ThreadHelper threadHelper;
        public FormMain MainForm;
        private Stopwatch stopwatch;


        public CameraForm()
        {
            InitializeComponent();

            threadHelper = new ThreadHelper("Camera Form", THREAD_PERIOD_MS, ImageProcessingThread, ThreadExceptionHandler);
            threadHelper.StartThreads();

            FormClosing += CameraForm_FormClosing;
        }

        private void CameraForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if ((configCompleted) && (JobGuid != null))
            {
                visionPipeline.CancelJob(JobGuid);
            }

            threadHelper.StopThreads();
        }

        private void ImageProcessingThread()
        {
            if (MainForm.DownCamera.IsRunning())
            {
                if (!configCompleted)
                {
                    imageFilter = new ImageFilter();
                    imageFilter.CreateFilter(E_ImageFilters.Threshold, true, 100);
                    imageProcessor = new ImageProcessor(MainForm.DownCamera, imageFilter);
                    /*{
                        FindCircles = true,
                        DrawCross = true
                    };*/

                    JobGuid = visionPipeline.CreateJob(
                        ImageProcessed,
                        imageProcessor,
                        imageFilter);

                    configCompleted = true;
                }
            }
        }

        private void ImageProcessed(object sender, ProcessedFrameEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    ImageProcessedThreadSafe(sender, e);
                }));
            }
            else
            {
                ImageProcessedThreadSafe(sender, e);
            }
        }

        private void ImageProcessedThreadSafe(object sender, ProcessedFrameEventArgs e)
        {
            if (stopwatch == null)
            {
                stopwatch = Stopwatch.StartNew();
            }
            else
            {
                stopwatch.Stop();
                FrameRate.Text = (1000L / stopwatch.ElapsedMilliseconds).ToString("N1");
                stopwatch.Reset();
                stopwatch.Start();
            }

            var clonedFrame = (Bitmap)e.Frame.Clone();
            var closestCircle = measureFeatures.GetClosestCircle(clonedFrame, imageProcessor, 200);

            if (closestCircle.Count > 0)
            {
                drawFeatures.DrawCircles(clonedFrame);
                drawFeatures.DrawCross(ref clonedFrame);

                FeatureDetails.Text = "X: " + closestCircle.X.ToString() + " Y: " + closestCircle.Y.ToString();
                FeatureDetails.BackColor = Color.DarkGreen;
            }
            else
            {
                FeatureDetails.BackColor = Color.Red;
            }

            Cam_pictureBox.Image = clonedFrame;
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
