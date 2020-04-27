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
    public partial class CameraUC : UserControl
    {
        private VisionPipeline visionPipeline = new VisionPipeline();
        private ImageProcessor imageProcessor;
        private ImageFilter imageFilter;
        private MeasureFeatures measureFeatures = new MeasureFeatures();
        private DrawFeatures drawFeatures = new DrawFeatures();

        private Guid JobGuid;
        private bool configCompleted = false;
        private const int THREAD_PERIOD_MS = 50;
        private ThreadHelper threadHelper;
        public FormMain MainForm;
        private Stopwatch stopwatch;

        private object receivedFrameLock = new object();
        private Bitmap receivedFrame;
        private bool imageReceivedUnprocessed = false;
        private string frameRate = string.Empty;

        public CameraUC()
        {
            InitializeComponent();

            threadHelper = new ThreadHelper(
                "Camera Form",
                THREAD_PERIOD_MS,
                ImageProcessingThread,
                ThreadExceptionHandler);

            threadHelper.StartThreads();
        }

        public void Shutdown()
        {
            if ((configCompleted) && (JobGuid != null))
            {
                visionPipeline.CancelJob(JobGuid);
            }

            threadHelper.StopThreads();

            if (Cam_pictureBox.Image != null)
            {
                Cam_pictureBox.Image.Dispose();
            }
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

                    JobGuid = visionPipeline.CreateJob(
                        ImageReceived,
                        imageProcessor,
                        imageFilter);

                    configCompleted = true;
                }
                else
                {
                    ProcessReceivedImage();
                }
            }
        }

        private void ProcessReceivedImage()
        {
            Bitmap clonedFrame = null;

            lock (receivedFrameLock)
            {
                if (imageReceivedUnprocessed)
                {
                    if (receivedFrame != null)
                    {
                        clonedFrame = (Bitmap)receivedFrame.Clone();
                    }

                    imageReceivedUnprocessed = false;
                }
            }

            if (clonedFrame != null)
            {
                var closestCircle = measureFeatures.GetClosestCircle(clonedFrame, imageProcessor, 200);

                if (closestCircle.Count > 0)
                {
                    drawFeatures.DrawCircles(clonedFrame);
                }

                drawFeatures.DrawCross(ref clonedFrame);

                if (InvokeRequired)
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        UpdateUIControlsThreadSafe(clonedFrame, closestCircle);
                    }));
                }
                else
                {
                    UpdateUIControlsThreadSafe(clonedFrame, closestCircle);
                }
            }
        }

        private void UpdateUIControlsThreadSafe(Bitmap frame, Primitive closestCircle)
        {
            UpdateRate.Text = frameRate;

            if (closestCircle.Count > 0)
            {
                FeatureDetails.Text = "X: " + closestCircle.X.ToString() + " Y: " + closestCircle.Y.ToString();
                FeatureDetails.BackColor = Color.DarkGreen;
            }
            else
            {
                FeatureDetails.BackColor = Color.Red;
            }

            var existingImage = Cam_pictureBox.Image;
            Cam_pictureBox.Image = (Bitmap)frame.Clone();

            if (existingImage != null)
            {
                existingImage.Dispose();
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

        private void ImageReceived(object sender, ProcessedFrameEventArgs e)
        {
            lock (receivedFrameLock)
            {
                if (receivedFrame != null)
                {
                    receivedFrame.Dispose();
                }

                receivedFrame = (Bitmap)e.Frame.Clone();
                imageReceivedUnprocessed = true;
            }

            if (stopwatch == null)
            {
                stopwatch = Stopwatch.StartNew();
            }
            else
            {
                stopwatch.Stop();
                frameRate = (1000L / Math.Max(1, stopwatch.ElapsedMilliseconds)).ToString("N1") + " FPS";
                stopwatch.Reset();
                stopwatch.Start();
            }
        }
    }
}
