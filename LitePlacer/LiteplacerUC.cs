#define TINYG_SHORTUNITS
// Some firmvare versions use units in millions, some don't. If not, comment out the above line.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Input;
using System.Net;

using MathNet.Numerics;
using HomographyEstimation;

using AForge;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Imaging;
using AForge.Imaging.Filters;
using AForge.Math.Geometry;
using Newtonsoft.Json;
using LitePlacer.Calibration.Actions;

using Terpsichore.Machine.Sensors;
using Terpsichore.Common;
using Terpsichore.Machine.Interfaces;
using Terpsichore.Machine;
using Terpsichore.Machine.EmbeddedController;
//using Solvethirteen.Desktop.Workspace;

namespace LitePlacer
{
    // Note: For function success/failure, I use bool return code. (instead of C# exceptions; a philosophical debate, let's not go there.)
    // The naming convention is xxx_m() for functions that have already displayed an error message to user. If a function only
    // calls _m functions, it can consider itself a _m function.

    public partial class LiteplacerUC : UserControl
    {
        private IMachine Machine { get; } = DIBindings.Resolve<IMachine>();
        //public ICNC Cnc;
        //public IMove CncMove;
        //public ICamera Machine.DownCamera;
        //ICamera Machine.UpCamera;
        NozzleClass Nozzle;
        TapesClass Tapes;
        public ISettings Setting;
        public BoardSettings.TinyG TinyGBoard = new BoardSettings.TinyG();
        public BoardSettings.qQuintic qQuinticBoard = new BoardSettings.qQuintic();

        public IAppLogger AppLogger = Terpsichore.Common.DIBindings.Resolve<IAppLogger>();
        //private ICommsProcessor CommsProcessor = DIBindings.Resolve<ICommsProcessor>();


        AppSettings SettingsOps;

        // =================================================================================
        // General and "global" functions 
        // =================================================================================
        #region General

        // =================================================================================
        // Note about thread guards: The prologue "if(InvokeRequired) {something long}" at a start of a function, 
        // makes the function safe to call from another thread.
        // See http://stackoverflow.com/questions/661561/how-to-update-the-gui-from-another-thread-in-c, 
        // "MajesticRa"'s answer near the bottom of first page


        // =================================================================================
        // Thread safe dialog box:
        // (see http://stackoverflow.com/questions/559252/does-messagebox-show-automatically-marshall-to-the-ui-thread )

        public DialogResult ShowMessageBox(String message, String header, MessageBoxButtons buttons)
        {
            if (this.InvokeRequired)
            {
                return (DialogResult)this.Invoke(new PassStringStringReturnDialogResultDelegate(ShowMessageBox), message, header, buttons);
            }
            //CenteredMessageBox2.PrepToCenterMessageBoxOnForm(this);
            return MessageBox.Show(this, message, header, buttons);
        }
        public delegate DialogResult PassStringStringReturnDialogResultDelegate(String s1, String s2, MessageBoxButtons buttons);

        // =================================================================================

        // We need "goto" to different features, currently circles, rectangles or both
        public LiteplacerUC()
        {
            Font = new Font(Font.Name, 8.25f * 96f / CreateGraphics().DpiX, Font.Style, Font.Unit, Font.GdiCharSet, Font.GdiVerticalFont);

            InitializeComponent();
            this.MouseWheel += new MouseEventHandler(MouseWheel_event);
        }

        // =================================================================================
        public bool StartingUp = false; // we want to react to some changes, but not during startup data load (which is counts as a change)

        public void Form1_Load(object sender, EventArgs e)
        {
            StartingUp = true;
            this.Size = new Size(1280, 900);

            DisplayText("Application Start", KnownColor.Black, true);
            DisplayText("Version: " + Assembly.GetEntryAssembly().GetName().Version.ToString() + ", build date: " + BuildDate());

            SettingsOps = new AppSettings();

            //Do_Upgrade();
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);

            Setting = SettingsOps.Load(path + "LitePlacer.Appsettings");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-us");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            //Cnc = Terpsichore.Common.DIBindings.Resolve<ICNC>();
            //Cnc.AddValueUpdaterHandler(ValueUpdater);
            //Cnc.UpdateCncConnectionStatus += UpdateCncConnectionStatus;
            //CncMove = Terpsichore.Common.DIBindings.Resolve<IMove>();
            //CncMove.UpdateCncConnectionStatus += UpdateCncConnectionStatus;

            RegisterForParameterUpdates();

            //Machine.DownCamera = Terpsichore.Common.DIBindings.Resolve<IDownCamera>();
            //Machine.DownCamera.ReportInfoCallback = DisplayText;
            //Machine.DownCamera = new ICamera() { ReportInfoCallback = DisplayText };
            //Machine.UpCamera = Terpsichore.Common.DIBindings.Resolve<IUpCamera>();
            //Machine.UpCamera.ReportInfoCallback = DisplayText;
            //Machine.UpCamera = new Camera() { ReportInfoCallback = DisplayText };
            Nozzle = new NozzleClass(Machine.UpCamera, this);
            Tapes = new TapesClass(Tapes_dataGridView, Nozzle, Machine.DownCamera);
            Tapes.GoToFeatureLocation_mEventAsync += GoToFeatureLocation_mAsync;
            Tapes.UseCoordinatesDirectlyEvent += UseCoordinatesDirectly;
            Tapes.SetPaperTapeMeasurementEvent += SetPaperTapeMeasurement;
            Tapes.SetBlackTapeMeasurementEvent += SetBlackTapeMeasurement;
            Tapes.SetClearTapeMeasurementEvent += SetClearTapeMeasurement;
            BoardSettings.MainForm = this;

            // Setup error handling for Tapes_dataGridViews
            // This is necessary, because programmatically changing a combobox cell value raises this error. (@MS: booooo!)
            Tapes_dataGridView.DataError += new DataGridViewDataErrorEventHandler(Tapes_dataGridView_DataError);
            TapesOld_dataGridView.DataError += new DataGridViewDataErrorEventHandler(Tapes_dataGridView_DataError);

            //this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(My_KeyDown);
            this.KeyUp += new KeyEventHandler(My_KeyUp);

            // The components tab is more a distraction than useful.
            // To add data, comment out the next line.
            tabControlPages.TabPages.Remove(Components_tabPage);
            // and uncomment this:
            // LoadDataGrid(path + "LitePlacer.ComponentData", ComponentData_dataGridView);

            LoadDataGrid(path + "LitePlacer.HomingFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref Homing_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.FiducialsFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref Fiducials_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.ComponentsFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref Components_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.PaperTapeFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref PaperTape_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.BlackTapeFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref BlackTape_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.ClearTapeFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref ClearTape_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.SnapshotFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref DowncamSnapshot_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.NozzleFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref Nozzle_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.Nozzle2Functions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref Nozzle2_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.UpCamComponentsFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref UpCamComponents_dataGridView, false);

            LoadDataGrid(path + "LitePlacer.UpCamSnapshotFunctions", Temp_dataGridView, DataTableType.VideoProcessing);
            DataGridViewCopy(Temp_dataGridView, ref UpcamSnapshot_dataGridView, false);

            LoadTapesTable(path + "LitePlacer.TapesData_v2");
            // LoadDataGrid(path + "LitePlacer.TapesData", Tapes_dataGridView, DataTableType.Tapes);
            Nozzle.LoadCalibration(path + "LitePlacer.NozzlesCalibrationData");

            ContextmenuLoadNozzle = Setting.Nozzles_default;
            ContextmenuUnloadNozzle = Setting.Nozzles_default;
            Nozzles_initialize();   // must be after Nozzle.LoadCalibration

            SetProcessingFunctions(Display_dataGridView);
            SetProcessingFunctions(Homing_dataGridView);
            SetProcessingFunctions(Fiducials_dataGridView);
            SetProcessingFunctions(Components_dataGridView);
            SetProcessingFunctions(PaperTape_dataGridView);
            SetProcessingFunctions(BlackTape_dataGridView);
            SetProcessingFunctions(ClearTape_dataGridView);
            SetProcessingFunctions(DowncamSnapshot_dataGridView);
            SetProcessingFunctions(Nozzle_dataGridView);
            SetProcessingFunctions(Nozzle2_dataGridView);
            SetProcessingFunctions(UpCamComponents_dataGridView);
            SetProcessingFunctions(UpcamSnapshot_dataGridView);

            Bookmark1_button.Text = Setting.General_Mark1Name;
            Bookmark2_button.Text = Setting.General_Mark2Name;
            Bookmark3_button.Text = Setting.General_Mark3Name;
            Bookmark4_button.Text = Setting.General_Mark4Name;
            Bookmark5_button.Text = Setting.General_Mark5Name;
            Bookmark6_button.Text = Setting.General_Mark6Name;
            Mark1_textBox.Text = Setting.General_Mark1Name;
            Mark2_textBox.Text = Setting.General_Mark2Name;
            Mark3_textBox.Text = Setting.General_Mark3Name;
            Mark4_textBox.Text = Setting.General_Mark4Name;
            Mark5_textBox.Text = Setting.General_Mark5Name;
            Mark6_textBox.Text = Setting.General_Mark6Name;

            VigorousHoming_checkBox.Checked = Setting.General_VigorousHoming;

            Relative_Button.Checked = true;

            Zlb_label.Text = "";
            Zlb_label.Visible = false;

            Display_dataGridView.DataError += new
                DataGridViewDataErrorEventHandler(Display_dataGridView_DataError);

            LoadCalibrationSettingsToUI();

            //CalibrationAction.CNC_XYZ_m = Cnc.CNC_XYA_m;

            tabControlPages.SelectedTab = tabControlPages.TabPages["tabPageBasicSetup"];

            //OpenSecondaryDownCameraForm();
        }

        // ==============================================================================================
        // New software release checks

        private string BuildDate()
        {
            // see http://stackoverflow.com/questions/1600962/displaying-the-build-date
            var version = Assembly.GetEntryAssembly().GetName().Version;
            var buildDateTime = new DateTime(2000, 1, 1).Add(new TimeSpan(
            TimeSpan.TicksPerDay * version.Build + // days since 1 January 2000
            TimeSpan.TicksPerSecond * 2 * version.Revision)); // seconds since midnight, (multiply by 2 to get original)
            return buildDateTime.ToString();
        }

        private bool UpdateAvailable(out string UpdateDescription)
        {
            try
            {
                var url = "http://www.liteplacer.com/Downloads/release.txt";
                UpdateDescription = (new WebClient()).DownloadString(url);
                string UpdateDate = "";
                for (int i = 0; i < UpdateDescription.Length; i++)
                {
                    if (UpdateDescription[i] == '\n')
                    {
                        break;
                    }
                    UpdateDate += UpdateDescription[i];
                }
                UpdateDate = UpdateDate.Trim();
                string BuildDateText = BuildDate();
                BuildDateText = "Build date " + BuildDate().Substring(0, BuildDateText.IndexOf(' '));
                if (UpdateDate != BuildDateText)
                {
                    return true;
                }
            }
            catch
            {
                DisplayText("Could not read http://www.liteplacer.com/Downloads/release.txt, update info not available.");
                UpdateDescription = "";
                return false;
            }
            return false;
        }

        private bool CheckForUpdate()
        {
            string UpdateText;
            if (UpdateAvailable(out UpdateText))
            {
                ShowMessageBox(
                    "There is software update available:\n\r" + UpdateText,
                    "Update available",
                    MessageBoxButtons.OK);
                return true;
            }
            return false;
        }

        private void CheckNow_button_Click(object sender, EventArgs e)
        {
            if (!CheckForUpdate())
            {
                ShowMessageBox(
                    "The software is up to date",
                    "Up to date",
                    MessageBoxButtons.OK);
            }
        }

        // ==============================================================================================
        // For diagnostics, log button presses. (Customers tend to send log window contents,
        // but the window does not log user actions without this.)
        // http://stackoverflow.com/questions/17949390/log-all-button-clicks-in-win-forms-app

        public void AttachButtonLogging(System.Windows.Forms.Control.ControlCollection controls)
        {
            foreach (var control in controls.Cast<System.Windows.Forms.Control>())
            {
                if (control is Button)
                {
                    Button button = (Button)control;
                    button.MouseDown += LogButtonClick; // MoseDown comes before mouse click, we want this to fire first)
                }
                else
                {
                    AttachButtonLogging(control.Controls);
                }
            }
        }

        private void LogButtonClick(object sender, EventArgs eventArgs)
        {

            Button button = sender as Button;
            DisplayText(button.Text.ToString(), KnownColor.DarkGreen);
        }
        // ==============================================================================================

        private string LastTabPage = "";

        // ==============================================================================================
        public async Task FormMain_ShownAsync(object sender, EventArgs e)
        {
            LabelTestButtons();
            AttachButtonLogging(this.Controls);

            LoadTempCADdata();
            LoadTempJobData();

            CheckForUpdate_checkBox.Checked = Setting.General_CheckForUpdates;
            if (CheckForUpdate_checkBox.Checked)
            {
                CheckForUpdate();
            }

            OmitNozzleCalibration_checkBox.Checked = Setting.Placement_OmitNozzleCalibration;
            SkipMeasurements_checkBox.Checked = Setting.Placement_SkipMeasurements;

            DownCamZoom_checkBox.Checked = Setting.DownCam_Zoom;
            Machine.DownCamera.Zoom = Setting.DownCam_Zoom;
            DownCamZoomFactor_textBox.Text = Setting.DownCam_Zoomfactor.ToString("0.0", CultureInfo.InvariantCulture);
            Machine.DownCamera.ZoomFactor = Setting.DownCam_Zoomfactor;

            UpCamZoom_checkBox.Checked = Setting.UpCam_Zoom;
            Machine.UpCamera.Zoom = Setting.UpCam_Zoom;
            UpCamZoomFactor_textBox.Text = Setting.UpCam_Zoomfactor.ToString("0.0", CultureInfo.InvariantCulture);
            Machine.UpCamera.ZoomFactor = Setting.UpCam_Zoomfactor;

            RobustFast_checkBox.Checked = Setting.Cameras_RobustSwitch;
            KeepActive_checkBox.Checked = Setting.Cameras_KeepActive;
            if (KeepActive_checkBox.Checked)
            {
                RobustFast_checkBox.Enabled = false;
            }
            else
            {
                RobustFast_checkBox.Enabled = true;
            }
            RobustFast_checkBox.Checked = Setting.Cameras_RobustSwitch;

            await Task.Run(() => StartCameras());
            
            tabControlPages.SelectedTab = tabPageBasicSetup;
            LastTabPage = "tabPageBasicSetup";

            SlackCompensation_checkBox.Checked = Setting.CNC_SlackCompensation;
            SlackCompensationA_checkBox.Checked = Setting.CNC_SlackCompensationA;

            MouseScroll_checkBox.Checked = Setting.CNC_EnableMouseWheelJog;
            NumPadJog_checkBox.Checked = Setting.CNC_EnableNumPadJog;

            ZTestTravel_textBox.Text = Setting.General_ZTestTravel.ToString();
            ShadeGuard_textBox.Text = Setting.General_ShadeGuard_mm.ToString();
            NozzleBelowPCB_textBox.Text = Setting.General_BelowPCB_Allowance.ToString();

            Z0_textBox.Text = Setting.General_ZtoPCB.ToString("0.00", CultureInfo.InvariantCulture);
            BackOff_textBox.Text = Setting.General_PlacementBackOff.ToString("0.00", CultureInfo.InvariantCulture);
            PlacementDepth_textBox.Text = Setting.Placement_Depth.ToString("0.00", CultureInfo.InvariantCulture);
            Hysteresis_textBox.Text = Setting.General_ZprobingHysteresis.ToString("0.00", CultureInfo.InvariantCulture);

            JobOffsetX_textBox.Text = Setting.Job_Xoffset.ToString("0.000", CultureInfo.InvariantCulture);
            JobOffsetY_textBox.Text = Setting.Job_Yoffset.ToString("0.000", CultureInfo.InvariantCulture);

            if (Setting.Nozzles_current == 0)
            {
                NozzleNo_textBox.Text = "--";
            }
            else
            {
                NozzleNo_textBox.Text = Setting.Nozzles_current.ToString();
            }
            ForceNozzle_numericUpDown.Value = Setting.Nozzles_default;
            DefaultNozzle_label.Text = Setting.Nozzles_default.ToString();

            PumpInvert_checkBox.Checked= Setting.General_PumpOutputInverted;
            VacuumInvert_checkBox.Checked = Setting.General_VacuumOutputInverted;
            Pump_checkBox.Checked = false;
            Vacuum_checkBox.Checked = false;

            Machine.CommsProcessor.Connect(Setting.CNC_SerialPort);  // moved to here, as this can raise error condition, needing the form up
            UpdateCncConnectionStatus();

            if (Machine.IsConnected)
            {
                if (await ControlBoardJustConnectedAsync())
                {
                    Machine.Pump.PumpDefaultSetting();
                    Machine.Vacuum.VacuumDefaultSetting();
                    await OfferHomingAsync();
                }
            }

            DisableLog_checkBox.Checked = Setting.General_MuteLogging;
            MotorPower_timer.Enabled = true;
            StartingUp = false;
            DisplayText("Startup completed.");
        }

        // =================================================================================

        public void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            bool OK = true;
            bool res;
            Setting.CNC_EnableMouseWheelJog = MouseScroll_checkBox.Checked;
            Setting.CNC_EnableNumPadJog = NumPadJog_checkBox.Checked;
            Setting.General_CheckForUpdates = CheckForUpdate_checkBox.Checked;
            Setting.General_MuteLogging = DisableLog_checkBox.Checked;

            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);

            res = SaveTempCADdata();
            OK = OK && res;

            res = SaveTempJobData();
            OK = OK && res;

            res = SettingsOps.Save(Setting, path + "LitePlacer.Appsettings");
            OK = OK && res;

            res = SaveDataGrid(path + "LitePlacer.ComponentData_v2", ComponentData_dataGridView);
            OK = OK && res;

            res = SaveDataGrid(path + "LitePlacer.TapesData_v2", Tapes_dataGridView);
            OK = OK && res;

            res = SaveDataGrid(path + "LitePlacer.NozzlesLoadData_v2", NozzlesLoad_dataGridView);
            OK = OK && res;

            res = SaveDataGrid(path + "LitePlacer.NozzlesUnLoadData_v2", NozzlesUnload_dataGridView);
            OK = OK && res;

            res = SaveDataGrid(path + "LitePlacer.NozzlesParameters_v2", NozzlesParameters_dataGridView);
            OK = OK && res;


            DataGridViewCopy(Homing_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.HomingFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(Fiducials_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.FiducialsFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(Components_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.ComponentsFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(PaperTape_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.PaperTapeFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(BlackTape_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.BlackTapeFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(ClearTape_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.ClearTapeFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(DowncamSnapshot_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.SnapshotFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(Nozzle_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.NozzleFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(Nozzle2_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.Nozzle2Functions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(UpCamComponents_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.UpCamComponentsFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            DataGridViewCopy(UpcamSnapshot_dataGridView, ref Temp_dataGridView, false);
            res = SaveDataGrid(path + "LitePlacer.UpCamSnapshotFunctions_v2", Temp_dataGridView);
            OK = OK && res;

            res = Nozzle.SaveCalibration(path + "LitePlacer.NozzlesCalibrationData");
            OK = OK && res;

            res = BoardSettings.Save(TinyGBoard, qQuinticBoard, path + "LitePlacer.BoardSettings");
            OK = OK && res;

            if (!OK)
            {
                DialogResult dialogResult = ShowMessageBox(
                    "some data could not be saved (see log window). Quit anyway?",
                    "Data save problem", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (Machine.IsConnected)
            {
                Machine.Pump.PumpIsOn = true;        // so it will be turned off, no matter what we think the status
                Machine.Pump.PumpOff_NoWorkaround();
                Machine.Vacuum.VacuumDefaultSetting();
                Machine.Motors.MotorPowerOff();  // motor power off
            }
            Machine.CommsProcessor.Disconnect();

            if (Machine.DownCamera.IsRunning())
            {
                Machine.DownCamera.Close();
                Thread.Sleep(200);   // camera feed needs a frame to close.
            }
            if (Machine.UpCamera.IsRunning())
            {
                Machine.UpCamera.Close();
                Thread.Sleep(200);   // camera feed needs a frame to close.
            }
            Thread.Sleep(200);
        }

        // =================================================================================
        // Get and save settings from old version if necessary
        // http://blog.johnsworkshop.net/automatically-upgrading-user-settings-after-an-application-version-change/
        /*
        private void Do_Upgrade()
        {
            try
            {
                if (Setting.General_UpgradeRequired)
                {
                    DisplayText("Updating from previous version");
                    Setting.Upgrade();
                    Setting.General_UpgradeRequired = false;
                    Setting.Save();
                }
            }
            catch (SettingsPropertyNotFoundException)
            {
                DisplayText("Updating from previous version (through ex)");
                Setting.Upgrade();
                Setting.General_UpgradeRequired = false;
                Setting.Save();
            }

        }
        */
        // =================================================================================
        public bool SetupCamerasPageVisible = false;

        private void tabControlPages_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetupCamerasPageVisible = false;
            switch (tabControlPages.SelectedTab.Name)
            {
                case "RunJob_tabPage":
                    RunJob_tabPage_Begin();
                    LastTabPage = "RunJob_tabPage";
                    break;
                case "tabPageBasicSetup":
                    BasicSetupTab_Begin();
                    LastTabPage = "tabPageBasicSetup";
                    break;
                case "tabPageSetupCameras":
                    SetupCamerasPageVisible = true;
                    tabPageSetupCameras_Begin();
                    LastTabPage = "tabPageSetupCameras";
                    break;
                case "Tapes_tabPage":
                    Tapes_tabPage_Begin();
                    LastTabPage = "Tapes_tabPage";
                    break;
                case "Nozzles_tabPage":
                    Nozzles_tabPage_Begin();
                    LastTabPage = "Nozzles_tabPage";
                    break;
            }
        }

        private void tabControlPages_Selecting(object sender, TabControlCancelEventArgs e)
        {
            switch (LastTabPage)
            {
                case "RunJob_tabPage":
                    RunJob_tabPage_End();
                    break;
                case "tabPageBasicSetup":
                    BasicSetupTab_End();
                    break;
                case "tabPageSetupCameras":
                    tabPageSetupCameras_End();
                    break;
                case "Tapes_tabPage":
                    Tapes_tabPage_End();
                    break;
                case "Nozzles_tabPage":
                    Nozzles_tabPage_End();
                    break;
            }
        }

        // =================================================================================
        // Saving and restoring data tables (Note: Not job files)
        // =================================================================================

        // Reading ver2 format allows changing the data grid itself at a software update, 
        // adding and removing columns, and still read in a saved file from previous software version.

        private int Ver2FormatID = 20000001;  // Just in case we need to identify the format we are using. 
        public bool LoadingDataGrid = false;  // to avoid problems with cell value changed event and unfilled grids


        public void LoadDataGrid(string FileName, DataGridView dgv, DataTableType TableType)
        {
            try
            {
                bool Ver2 = false;
                LoadingDataGrid = true;
                int first;


                if (File.Exists(FileName + "_v2"))
                {
                    FileName = FileName + "_v2";
                }
                else
                {
                    if (!File.Exists(FileName))
                    {
                        DisplayText("Didn't find file " + FileName);
                        return;   // Didint find the specified file name nor filename+v2 (these would be the default files)
                    }
                }

                // Find out the version
                using (BinaryReader br1 = new BinaryReader(File.Open(FileName, FileMode.Open)))
                {
                    first = br1.ReadInt32();
                    if (first == Ver2FormatID)
                    {
                        Ver2 = true;
                    }
                    br1.Close();
                }

                
                using (BinaryReader br = new BinaryReader(File.Open(FileName, FileMode.Open)))
                {
                    dgv.Rows.Clear();
                    if (Ver2)
                    {
                        DisplayText("Reading v2 format file " + FileName);
                        first = br.ReadInt32();
                    }
                    else
                    {
                        DisplayText("Reading v1 format file " + FileName);
                    }

                    int cols = br.ReadInt32();
                    int rows = br.ReadInt32();

                    if (dgv.AllowUserToAddRows)
                    {
                        // There is an empty row in the bottom that is visible for manual add.
                        // It is saved in the file. It is automatically added, so we don't want to add it again.
                        rows = rows - 1;
                    }
                    // read headers;
                    List<string> Headers = new List<string>();

                    if (Ver2)
	                {
                        for (int j = 0; j < cols; ++j)
                        {
                            Headers.Add(br.ReadString());
                        }
	                }
                    else
                    {
                        Headers = Addv1Headers(FileName, TableType);
                    }

                    // read data
                    int i_out;
                    for (int i = 0; i < rows; ++i)
                    {
                        dgv.Rows.Add();
                        for (int j = 0; j < cols; ++j)
                        {
                            if (MapHeaders(dgv, Headers, j, out i_out))
                            {
                                if (br.ReadBoolean())
                                {
                                    dgv.Rows[i].Cells[i_out].Value = br.ReadString();
                                }
                                else
                                {
                                    br.ReadBoolean();
                                }
                            }
                            else
                            {
                                // column is removed: dummy read, discard the data
                                if (br.ReadBoolean())
                                {
                                    br.ReadString();
                                }
                                else br.ReadBoolean();
                            }
                        }
                    }
                    br.Close();
                }
                LoadingDataGrid = false;
            }
            catch (System.Exception excep)
            {
                MessageBox.Show(excep.Message);
                LoadingDataGrid = false;
            }
        }

        private bool MapHeaders(DataGridView Grid, List<string> Headers, int i_in, out int i_out)
        {
            // i_in= column index of the read-in data
            // sets i_out to the column index of the currect header in grid
            // returns if match was found
            i_out = -1;
            string label = Headers[i_in];
            for (int i = 0; i < Grid.Columns.Count; i++)
            {
                if (Grid.Columns[i].Name == label)
                {
                    i_out = i;
                    return true;
                }
            }
            return false;
        }



        public bool SaveDataGrid(string FileName, DataGridView dgv)
        {
            try
            {
                using (BinaryWriter bw = new BinaryWriter(File.Open(FileName, FileMode.Create)))
                {
                    bw.Write(Ver2FormatID);
                    bw.Write(dgv.Columns.Count);
                    bw.Write(dgv.Rows.Count);
                    LoadingDataGrid = false;

                    // write headers
                    foreach (DataGridViewColumn column in dgv.Columns)
                    {
                        bw.Write(column.Name);
                    }

                    foreach (DataGridViewRow dgvR in dgv.Rows)
                    {
                        for (int j = 0; j < dgv.Columns.Count; ++j)
                        {
                            object val = dgvR.Cells[j].Value;
                            if (val == null)
                            {
                                bw.Write(false);
                                bw.Write(false);
                            }
                            else
                            {
                                bw.Write(true);
                                bw.Write(val.ToString());
                            }
                        }
                    }
                    bw.Flush();
                    bw.Close();
                }
                return true;
            }
            catch (System.Exception excep)
            {
                DisplayText(excep.Message);
                return false;
            }
        }

        // =================================================================================
        // To be able to change columns and read in old format data file, we need to manually set the old
        // format headers, since I didn't have the insight to write them to the file from beginning.
        // this routine does it, called from LoadDataGrid()

        public List<string> Addv1Headers(string filename, DataTableType TableType )
        {
            List<string> Headers = new List<string>();

            switch (TableType)
            {
                //case DataTableType.Nozzles:
                //    Headers.Add("NozzleNo_Column");
                //    Headers.Add("StartX_Column");
                //    Headers.Add("StartY_Column");
                //    Headers.Add("StartZ_Column");
                //    for (int i = 1; i < 6; i++)
                //    {
                //        Headers.Add("MoveNumber" + i.ToString() + "axis_Column");
                //        Headers.Add("MoveNumber" + i.ToString() + "amount_Column");
                //    }
                //    break;

                case DataTableType.Tapes:
                    Headers.Add("SelectButton_Column");
                    Headers.Add("Id_Column");
                    Headers.Add("Orientation_Column");
                    Headers.Add("Rotation_Column");
                    Headers.Add("WidthColumn");
                    Headers.Add("Type_Column");
                    // Headers.Add("CapacityColumn");   // not in v1 files
                    Headers.Add("NextPart_Column");
                    Headers.Add("TrayID_Column");
                    Headers.Add("FirstX_Column");
                    Headers.Add("FirstY_Column");
                    Headers.Add("Z_Pickup_Column");
                    Headers.Add("Z_Place_Column");
                    Headers.Add("Next_X_Column");
                    Headers.Add("Next_Y_Column");
                    break;

                case DataTableType.ComponentData:
                    Headers.Add("PartialName_column");
                    Headers.Add("SizeX_column");
                    Headers.Add("SizeY_column");
                break;

                case DataTableType.VideoProcessing:
                    Headers.Add("Funct_column");
                    Headers.Add("Enabled_column");
                    Headers.Add("Int1_column");
                    Headers.Add("Double1_column");
                    Headers.Add("R_column");
                    Headers.Add("G_column");
                    Headers.Add("B_column");
                break;

                case DataTableType.PanelFiducials:
                    Headers.Add("Designator_Column");
                    Headers.Add("Footprint_Column");
                    Headers.Add("FirstX_Column");
                    Headers.Add("FirstY_Column");
                    Headers.Add("Rotation_Column");
                break;



                default:
                    ShowMessageBox(
                        "*** Header description for " + TableType.ToString() + " missing. Programmer's error. ***",
                        "Sloppy programmer",
                        MessageBoxButtons.OK);
                break;
            }

            return Headers;
        }

        // =================================================================================
        // This routine reads in old format file
        public void LoadDataGrid_V1(string FileName, DataGridView dgv)
        {
            try
            {
                if (!File.Exists(FileName))
                {
                    return;
                }
                LoadingDataGrid = true;
                dgv.Rows.Clear();
                using (BinaryReader bw = new BinaryReader(File.Open(FileName, FileMode.Open)))
                {
                    int Cols = bw.ReadInt32();
                    int Rows = bw.ReadInt32();
                    string debug = "foo";
                    if (dgv.AllowUserToAddRows)
                    {
                        // There is an empty row in the bottom that is visible for manual add.
                        // It is saved in the file. It is automatically added, so we don't want to add it also.
                        // It is not there when rows are added only programmatically, so we need to do it here.
                        Rows = Rows - 1;
                    }
                    for (int i = 0; i < Rows; ++i)
                    {
                        dgv.Rows.Add();
                        for (int j = 0; j < Cols; ++j)
                        {
                            if (bw.ReadBoolean())
                            {
                                debug = bw.ReadString();
                                dgv.Rows[i].Cells[j].Value = "";
                                dgv.Rows[i].Cells[j].Value = debug;
                            }
                            else bw.ReadBoolean();
                            if (dgv.Rows[i].Cells[j].Value == null)
                            {
                                dgv.Rows[i].Cells[j].Value = "--";
                            }
                            if (dgv.Rows[i].Cells[j].Value.ToString() == "")
                            {
                                dgv.Rows[i].Cells[j].Value = "--";
                            }
                        }
                    }
                    bw.Close();
                }
                LoadingDataGrid = false;
            }
            catch (System.Exception excep)
            {
                MessageBox.Show(excep.Message);
                LoadingDataGrid = false;
            }
        }


        // =================================================================================
        // I changed the data in tapes table, but I don't want to make customers to redo their tables
        // This routine looks if the data format is old and converts it to new if needed
        // This is called at startup
        private void LoadTapesTable(string filename)
        {
            if (!File.Exists(filename))
            {
                DisplayText("Did not find file " + filename);
                return;
            }

            if (filename.EndsWith("_v2"))
            {
                ReadV2TapesFile(filename);
            }
            else
            {
                ReadV1TapesFile(filename);
            }
            return;
        }

        private void ReadV2TapesFile(string filename)
        {
            // Logic:
            // Check that file exists.
            // Read the headers.
            // If headers have "WidthColumn", read in to dummy datagridview and convert to new format
            // else read normally
            try
            {
                using (BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open)))
                {
                    int first = br.ReadInt32();
                    int cols = br.ReadInt32();
                    int rows = br.ReadInt32();
                    // read headers;
                    List<string> Headers = new List<string>();
                    for (int j = 0; j < cols; ++j)
                    {
                        Headers.Add(br.ReadString());
                    }
                    br.Close();
                    if (Headers.Contains("WidthColumn"))
                    {
                        // read in to old format datagridview and convert to new format
                        DisplayText("Loading tapes without nozzles data");
                        LoadDataGrid(filename, TapesOld_dataGridView, DataTableType.Tapes);
                        // convert to new
                        double X;
                        double Y;
                        double pitch;
                        LoadingDataGrid = true;  // to avoid cell value changed events
                        Tapes_dataGridView.Rows.Clear();

                        for (int i = 0; i < TapesOld_dataGridView.RowCount; i++)
                        {
                            Tapes_dataGridView.Rows.Add();

                            // most values go as is, just column names have changed
                            if (TapesOld_dataGridView.Rows[i].Cells["SelectButtonColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["SelectButton_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["SelectButtonColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["IdColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Id_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["IdColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["OrientationColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Orientation_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["OrientationColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["RotationColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Rotation_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["RotationColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["NozzleColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Nozzle_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["NozzleColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["CapacityColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Capacity_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["CapacityColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["TypeColumn"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Type_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["TypeColumn"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["Tray_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["TrayID_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["Tray_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["Next_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["NextPart_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["Next_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["X_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["FirstX_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["X_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["Y_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["FirstY_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["Y_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["PickupZ_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Z_Pickup_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["PickupZ_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["PlaceZ_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Z_Place_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["PlaceZ_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["NextX_Column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Next_X_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["NextX_Column"].Value.ToString();
                            }
                            if (TapesOld_dataGridView.Rows[i].Cells["NextY_column"].Value != null)
                            {
                                Tapes_dataGridView.Rows[i].Cells["Next_Y_Column"].Value = TapesOld_dataGridView.Rows[i].Cells["NextY_column"].Value.ToString();
                            }
                            // conversion
                            if (TapesOld_dataGridView.Rows[i].Cells["WidthColumn"].Value != null)
                            {
                                if (TapesOld_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString() != "custom")
                                {
                                    TapeWidthStringToValues(TapesOld_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString(), out X, out Y, out pitch);
                                    Tapes_dataGridView.Rows[i].Cells["Pitch_Column"].Value = pitch.ToString();
                                    Tapes_dataGridView.Rows[i].Cells["OffsetX_Column"].Value = X.ToString();
                                    Tapes_dataGridView.Rows[i].Cells["OffsetY_Column"].Value = Y.ToString();
                                }
                            }
                            Tapes_dataGridView.Rows[i].Cells["RotationDirect_Column"].Value = "0.00";
                            Tapes_dataGridView.Rows[i].Cells["CoordinatesForParts_Column"].Value = false;
                        }
                        LoadingDataGrid = false;
                    }
                    else
                    {
                        // read in new format 
                        DisplayText("Loading tapes with nozzles data");
                        LoadDataGrid(filename, Tapes_dataGridView, DataTableType.Tapes);
                    }
                }
            }
            catch (System.Exception excep)
            {
                // Get stack trace for the exception with source file information
                var st = new StackTrace(excep, true);
                // Get the top stack frame
                var frame = st.GetFrame(0);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                LoadingDataGrid = false;

                MessageBox.Show(excep.Message);
            }
        }

        private void ReadV1TapesFile(string filename)
        {
            // Logic:
            // Check that file exists.
            // Read the headers.
            // If headers have "WidthColumn", read in to dummy datagridview and convert to new format
            // else read normally
            try
            {
                // read in to old format datagridview and convert to new format
                DisplayText("Loading v1 tapes data");
                LoadDataGrid(filename, Tapes_dataGridView, DataTableType.Tapes);
                // convert to new
                double X;
                double Y;
                double pitch;
                LoadingDataGrid = true;  // to avoid cell value changed events

                for (int i = 0; i < TapesOld_dataGridView.RowCount; i++)
                {
                    if (Tapes_dataGridView.Rows[i].Cells["WidthColumn"].Value != null)
                    {
                        if (Tapes_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString() != "custom")
                        {
                            TapeWidthStringToValues(Tapes_dataGridView.Rows[i].Cells["WidthColumn"].Value.ToString(), out X, out Y, out pitch);
                            Tapes_dataGridView.Rows[i].Cells["Pitch_Column"].Value = pitch.ToString();
                            Tapes_dataGridView.Rows[i].Cells["OffsetX_Column"].Value = X.ToString();
                            Tapes_dataGridView.Rows[i].Cells["OffsetY_Column"].Value = Y.ToString();
                        }
                    }
                    Tapes_dataGridView.Rows[i].Cells["RotationDirect_Column"].Value = "0.00";
                    Tapes_dataGridView.Rows[i].Cells["CoordinatesForParts_Column"].Value = false;
                }
                LoadingDataGrid = false;
            }
            catch (System.Exception excep)
            {
                // Get stack trace for the exception with source file information
                var st = new StackTrace(excep, true);
                // Get the top stack frame
                var frame = st.GetFrame(0);
                // Get the line number from the stack frame
                var line = frame.GetFileLineNumber();
                LoadingDataGrid = false;

                MessageBox.Show(excep.Message);
            }
        }

        // =================================================================================
        // Scrolling & arranging datagridviews...
        // =================================================================================
        private void DataGrid_Up_button(DataGridView Grid)
        {
            int row = Grid.CurrentCell.RowIndex;
            int col = Grid.CurrentCell.ColumnIndex;
            if (row == 0)
            {
                return;
            }
            if (Grid.SelectedCells.Count != 1)
            {
                return;
            }
            DataGridViewRow temp = Grid.Rows[row];
            Grid.Rows.Remove(Grid.Rows[row]);
            Grid.Rows.Insert(row - 1, temp);
            Grid.ClearSelection();
            Grid.CurrentCell = Grid[col, row - 1];
            HandleGridScrolling(true, Grid);
        }

        private void DataGrid_Down_button(DataGridView Grid)
        {
            int row = Grid.CurrentCell.RowIndex;
            int col = Grid.CurrentCell.ColumnIndex;
            int i = Grid.RowCount;
            if (row == Grid.RowCount - 1)
            {
                return;
            }
            if (Grid.SelectedCells.Count != 1)
            {
                return;
            }
            DataGridViewRow temp = Grid.Rows[row];
            Grid.Rows.Remove(Grid.Rows[row]);
            Grid.Rows.Insert(row + 1, temp);
            Grid.ClearSelection();
            Grid.CurrentCell = Grid[col, row + 1];
            Grid.Rows[row + 1].Cells[col].Selected = true;
            HandleGridScrolling(false, Grid);
        }

        private void HandleGridScrolling(bool Up, DataGridView Grid)
        {
            // Makes sure the selected row is visible
            int VisibleRows = Grid.DisplayedRowCount(false);
            int FirstVisibleRow = Grid.FirstDisplayedCell.RowIndex;
            int LastVisibleRow = (FirstVisibleRow + VisibleRows) - 1;
            int SelectedRow = Grid.CurrentCell.RowIndex;

            if (VisibleRows >= Grid.RowCount)
            {
                return;
            }

            if (Up)
            {
                if (FirstVisibleRow == 0)
                {
                    return;
                }
                if (SelectedRow < FirstVisibleRow + 2)
                {
                    if (SelectedRow > 2)
                    {
                        Grid.FirstDisplayedScrollingRowIndex = SelectedRow - 2;
                    }
                    else
                    {
                        Grid.FirstDisplayedScrollingRowIndex = 0;
                    }
                }
            }
            else
            {
                if (LastVisibleRow == Grid.RowCount)
                {
                    return;
                }
                if (SelectedRow > LastVisibleRow - 3)
                {
                    if (SelectedRow < Grid.RowCount - 3)
                    {
                        Grid.FirstDisplayedScrollingRowIndex = SelectedRow - VisibleRows + 3;
                    }
                    else
                    {
                        Grid.FirstDisplayedScrollingRowIndex =
                            Grid.RowCount - VisibleRows;
                    }
                }
            }
        }

        // =================================================================================
        // Forcing a DataGridview display update
        // Ugly hack if you ask me, but MS didn't give us any other reliable way...
        public void Update_GridView(DataGridView Grid)
        {
            Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            BindingSource bs = new BindingSource(); // create a BindingSource
            bs.DataSource = Grid.DataSource;  // copy jobdata to bs
            DataTable dat = (DataTable)(bs.DataSource);  // make a datatable from bs
            Grid.DataSource = dat;  // and copy datatable data to jobdata, forcing redraw
            Grid.RefreshEdit();
            Grid.Refresh();
        }

        #endregion General

        // =================================================================================
        // Jogging
        // =================================================================================
        #region Jogging

        // see https://github.com/synthetos/TinyG/wiki/TinyG-Feedhold-and-Resume

        // =================================================================================
        private async void MouseWheel_event(object sender, MouseEventArgs e)
        {
            if (!MouseScroll_checkBox.Checked)
            {
                return;
            }

            double Mag = 0.0;
            if (e.Delta < 0)
            {
                Mag = -1.0;
            }
            else if (e.Delta > 0)
            {
                Mag = 1.0;
            }
            else
            {
                return;
            }
            if (System.Windows.Forms.Control.ModifierKeys == Keys.Shift)
            {
                Mag = Mag * 10.0;
            }
            if (System.Windows.Forms.Control.ModifierKeys == Keys.Control)
            {
                Mag = Mag / 10.0;
            }

            Machine.Jogging.JoggingBusy = true;
            await Machine.Move.MoveASafeAsync(Machine.Position.CurrentA + Mag);
            
            if (Machine.DownCamera.Draw_Snapshot)
            {
                Machine.DownCamera.RotateSnapshot(Machine.Position.CurrentA);
                while (Machine.DownCamera.rotating)
                {
                    Thread.Sleep(10);
                }
            }

            Machine.Jogging.JoggingBusy = false;
        }


        
        List<Keys> JoggingKeys = new List<Keys>()
	    {
	        Keys.NumPad1,   // down + left
	        Keys.NumPad2,   // down
	        Keys.NumPad3,   // down + right
	        Keys.NumPad4,   // left
            Keys.NumPad6,   // right
	        Keys.NumPad7,   // up + left
	        Keys.NumPad8,   // up
 	        Keys.NumPad9,   // up + right
            Keys.Add,
            Keys.Subtract,
            Keys.Divide,
            Keys.Multiply,
            Keys.F5,
            Keys.F6,
            Keys.F7,
            Keys.F8,
            Keys.F9,
            Keys.F10,
            Keys.F11,
            Keys.F12
	    };

        public void My_KeyUp(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.NumPad1) || (e.KeyCode == Keys.NumPad2) || (e.KeyCode == Keys.NumPad3) ||
                (e.KeyCode == Keys.NumPad4) || (e.KeyCode == Keys.NumPad6) ||
                (e.KeyCode == Keys.NumPad7) || (e.KeyCode == Keys.NumPad8) || (e.KeyCode == Keys.NumPad9) ||
                (e.KeyCode == Keys.Add) || (e.KeyCode == Keys.Subtract) || (e.KeyCode == Keys.Divide) || (e.KeyCode == Keys.Multiply))
            {
                if (!NumPadJog_checkBox.Checked)
                {
                    return;
                }
                if ((ActiveControl is TextBox) || (ActiveControl is NumericUpDown) || (ActiveControl is MaskedTextBox))
                {
                    return;
                };
                Machine.Jogging.JoggingBusy = false;
                e.Handled = true;
                e.SuppressKeyPress = true;
                Machine.CommsProcessor.SendCommand("!%");
            }
        }

        static bool EnterKeyHit = true;   // petegit: Why is this initialized with true???
        static string Movestr;

        public async void My_KeyDown(object sender, KeyEventArgs e)
        {
            //DisplayText("My_KeyDown: " + e.KeyCode.ToString());

            if (e.KeyCode == Keys.Enter)
            {
                EnterKeyHit = true;
                return;
            }

            // Abort placment should also be triggered by keyboard
            // In some situations this is much faster then using the mouse
            if (e.KeyCode == Keys.Escape) 
            {
                Machine.Move.AbortPlacement = true;
                Machine.Move.AbortPlacementShown = false;
            }

            if ( (e.KeyCode == Keys.F4) &&
                    !( (e.Alt) || (e.Control) || (e.Shift) ) 
                )
            {
                Demo_button.Visible = !Demo_button.Visible;
                StopDemo_button.Visible = !StopDemo_button.Visible;
                return;
            }

            if ((e.KeyCode == Keys.F4) && (e.Alt) )
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Close program; are you sure?",
                    "Close program?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    Application.Exit();
                }
                e.Handled = true;
                return;
            }

            if (!JoggingKeys.Contains(e.KeyCode))
            {
                return;
            }

            if ((e.KeyCode == Keys.NumPad1) || (e.KeyCode == Keys.NumPad2) || (e.KeyCode == Keys.NumPad3) ||
                (e.KeyCode == Keys.NumPad4) || (e.KeyCode == Keys.NumPad6) ||
                (e.KeyCode == Keys.NumPad7) || (e.KeyCode == Keys.NumPad8) || (e.KeyCode == Keys.NumPad9) ||
                (e.KeyCode == Keys.Add) || (e.KeyCode == Keys.Subtract) || (e.KeyCode == Keys.Divide) || (e.KeyCode == Keys.Multiply))
            {
                if (!NumPadJog_checkBox.Checked)
                {
                    return;
                }
                if ((ActiveControl is TextBox) || (ActiveControl is NumericUpDown) || (ActiveControl is MaskedTextBox))
                {
                    return;
                }
            }

            if (System.Windows.Forms.Control.ModifierKeys == Keys.Alt)
            {
                Movestr = "{\"gc\":\"G1 F" + AltJogSpeed_numericUpDown.Value.ToString() + " ";
            }
            else if (System.Windows.Forms.Control.ModifierKeys == Keys.Control)
            {
                Movestr = "{\"gc\":\"G1 F" + CtlrJogSpeed_numericUpDown.Value.ToString() + " ";
            }
            else
            {
                Movestr = "{\"gc\":\"G1 F" + NormalJogSpeed_numericUpDown.Value.ToString() + " ";
            }


            e.Handled  = true;

            if (Machine.Jogging.JoggingBusy)
            {
                return;
            }

            if (!Machine.IsConnected)
            {
                return;
            }

            if (e.KeyCode == Keys.NumPad1)
            {
                Machine.Jogging.JoggingBusy = true;
                //DisplayText("up");
                //return;
                Machine.CommsProcessor.SendCommand(Movestr + "X0 Y0\"}");
            }
            else if (e.KeyCode == Keys.NumPad2)
            {
                Machine.Jogging.JoggingBusy = true;
                //DisplayText("down");
                //return;
                Machine.CommsProcessor.SendCommand(Movestr + "Y0\"}");
            }
            else if (e.KeyCode == Keys.NumPad3)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "Y0" + "X" + Setting.General_MachineSizeX.ToString() + "\"}");
            }
            else if (e.KeyCode == Keys.NumPad4)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "X0\"}");
            }
            else if (e.KeyCode == Keys.NumPad6)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "X" + Setting.General_MachineSizeX.ToString() + "\"}");
            }
            else if (e.KeyCode == Keys.NumPad7)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "X0" + "Y" + Setting.General_MachineSizeY.ToString() + "\"}");
            }
            else if (e.KeyCode == Keys.NumPad8)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "Y" + Setting.General_MachineSizeY.ToString() + "\"}");
            }
            else if (e.KeyCode == Keys.NumPad9)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "X" + Setting.General_MachineSizeX.ToString() + "Y" + Setting.General_MachineSizeY.ToString() + "\"}");
            }
           //     (e.KeyCode == Keys.Add) || (e.KeyCode == Keys.Subtract) || (e.KeyCode == Keys.Divide) || (e.KeyCode == Keys.Multiply))
            else if (e.KeyCode == Keys.Add)
            {
                Machine.Jogging.JoggingBusy = true;
                double Ztarget;
                if (!double.TryParse(Setting.General_ZtoPCB.ToString().Replace(',', '.'), out Ztarget))
                {
                    DisplayText("Z to PCB internal value error!");
                    return;
                }
                double Zadd;
                if (!double.TryParse(Setting.General_BelowPCB_Allowance.ToString().Replace(',', '.'), out Zadd))
                {
                    DisplayText("Below PCB allowance internal value error!");
                    return;
                }
                Ztarget += Zadd;
                Machine.CommsProcessor.SendCommand(Movestr + "Z" + Ztarget.ToString() + "\"}");
            }
            else if (e.KeyCode == Keys.Subtract)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "Z0\"}");
            }
            else if (e.KeyCode == Keys.Divide)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "A0\"}");
            }
            else if (e.KeyCode == Keys.Multiply)
            {
                Machine.Jogging.JoggingBusy = true;
                Machine.CommsProcessor.SendCommand(Movestr + "A10000\"}");  // should be enough
            }
            else
            {
                await Jog(sender, e);
            }
        }


        [DllImport("user32.dll")]
        private static extern int HideCaret(IntPtr hwnd);

        private async Task Jog(object sender, KeyEventArgs e)
        {

            if (Machine.Jogging.JoggingBusy)
            {
                return;
            }

            if (!Machine.IsConnected)
            {
                return;
            }

            double Mag = 0.0;
            if ((e.Alt) && (e.Shift))
            {
                Mag = 100.0;
            }
            else if ((e.Alt) && (e.Control))
            {
                Mag = 4.0;
            }
            else if (e.Alt)
            {
                Mag = 10.0;
            }
            else if (e.Shift)
            {
                Mag = 1.0;
            }
            else if (e.Control)
            {
                Mag = 0.01;
            }
            else
            {
                Mag = 0.1;
            };

            // move right
            if (e.KeyCode == Keys.F5)
            {
                Machine.Jogging.JoggingBusy = true;
                await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX - Mag, Machine.Position.CurrentY);
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            }

            // move left
            if (e.KeyCode == Keys.F6)
            {
                Machine.Jogging.JoggingBusy = true;
                await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX + Mag, Machine.Position.CurrentY);
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            }

            // move away
            if (e.KeyCode == Keys.F7)
            {
                Machine.Jogging.JoggingBusy = true;
                await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX, Machine.Position.CurrentY + Mag);
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            }

            // move closer
            if (e.KeyCode == Keys.F8)
            {
                Machine.Jogging.JoggingBusy = true;
                await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX, Machine.Position.CurrentY - Mag);
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            };

            // rotate ccw
            if (e.KeyCode == Keys.F9)
            {
                Machine.Jogging.JoggingBusy = true;
                if ((Mag > 99) && (Mag < 101))
                {
                    Mag = 90.0;
                }
                await Machine.Move.MoveASafeAsync(Machine.Position.CurrentA + Mag);
                if (Machine.DownCamera.Draw_Snapshot)
                {
                    Machine.DownCamera.RotateSnapshot(Machine.Position.CurrentA);
                    while (Machine.DownCamera.rotating)
                    {
                        Thread.Sleep(10);
                    }
                }
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            }

            // rotate cw
            if (e.KeyCode == Keys.F10)
            {
                Machine.Jogging.JoggingBusy = true;
                if ((Mag > 99) && (Mag < 101))
                {
                    Mag = 90.0;
                }
                await Machine.Move.MoveASafeAsync(Machine.Position.CurrentA - Mag);
                if (Machine.DownCamera.Draw_Snapshot)
                {
                    Machine.DownCamera.RotateSnapshot(Machine.Position.CurrentA);
                    while (Machine.DownCamera.rotating)
                    {
                        Thread.Sleep(10);
                    }
                }
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            }

            // move up
            if (e.KeyCode == Keys.F11)
            {
                Machine.Jogging.JoggingBusy = true;
                await Machine.Move.MoveZSafeAsync(Machine.Position.CurrentZ - Mag);
                e.Handled = true;
                Machine.Jogging.JoggingBusy = false;
                return;
            }

            // move down
            if ((e.KeyCode == Keys.F12) && (Mag < 50))
            {
                Machine.Jogging.JoggingBusy = true;
                await Machine.Move.MoveZSafeAsync(Machine.Position.CurrentZ + Mag);
                Machine.Jogging.JoggingBusy = false;
                e.Handled = true;
                return;
            }

        }

        // =================================================================================
        // Picturebox mouse functions

        private void BoxTo_mms(out double Xmm, out double Ymm, int MouseX, int MouseY, PictureBox Box)
        {
            // Input is mouse position inside picture box.
            // Output is mouse position in mm's from image center (machine position)
            Xmm = 0.0;
            Ymm = 0.0;
            int X = MouseX - Box.Size.Width / 2;  // X= diff from centern in pixels
            int Y = MouseY - Box.Size.Height / 2;

            double XmmPerPixel;
            double YmmPerPixel;
            double Xscale = 1.0;
            double Yscale = 1.0;
            int Xres = 0;
            int Yres = 0;
            int pol = 1;

            ICamera cam = Machine.DownCamera;
            if (Machine.DownCamera.Active)
            {
                cam = Machine.DownCamera;
                XmmPerPixel = Setting.DownCam_XmmPerPixel;
                YmmPerPixel = Setting.DownCam_XmmPerPixel;
                Xres = Setting.DownCam_DesiredX;
                Yres = Setting.DownCam_DesiredY;
            }
            else if (Machine.UpCamera.Active)
            {
                cam = Machine.UpCamera;
                XmmPerPixel = Setting.UpCam_XmmPerPixel;
                YmmPerPixel = Setting.UpCam_YmmPerPixel;
                Xres = Setting.UpCam_DesiredX;
                Yres = Setting.UpCam_DesiredY;
                pol = -1;
            }
            else
            {
                DisplayText("No camera running");
                return;
            };




            if (!CamShowPixels_checkBox.Checked)
            {
                // image on screen is not at camera resolution
                Xscale = Convert.ToDouble(Xres) / Convert.ToDouble(Box.Size.Width);
                Yscale = Convert.ToDouble(Yres) / Convert.ToDouble(Box.Size.Height);
            }
            Xmm = Convert.ToDouble(X) * XmmPerPixel * Xscale * Convert.ToDouble(pol);
            Ymm = Convert.ToDouble(Y) * YmmPerPixel * Yscale * Convert.ToDouble(pol);


            if (cam.Zoom)  // if zoomed for display
            {
                Xmm = Xmm / cam.ZoomFactor;
                Ymm = Ymm / cam.ZoomFactor;
            };
            Xmm = Xmm / cam.GetDisplayZoom();	// Might also be zoomed for processing
            Ymm = Ymm / cam.GetDisplayZoom();

            DisplayText("BoxTo_mms: MouseX: " + MouseX.ToString() + ", X: " + X.ToString() + ", Xmm: " + Xmm.ToString());
            DisplayText("BoxTo_mms: MouseY: " + MouseY.ToString() + ", Y: " + Y.ToString() + ", Ymm: " + Ymm.ToString());
        }


        bool PictureBox_MouseDragged = false;

        private void General_pictureBox_MouseMove(PictureBox Box, int MouseX, int MouseY)
        {
            if (MouseButtons == MouseButtons.Left)
            {
                DisplayText("X: " + MouseX.ToString() + ", Y: " + MouseY.ToString());
            }
        }

        private async void General_pictureBox_MouseClick(PictureBox Box, int MouseX, int MouseY)
        {
            double Xmm, Ymm;

            if (PictureBox_MouseDragged)
            {
                PictureBox_MouseDragged = false;
                return;
            }
            if (System.Windows.Forms.Control.ModifierKeys == Keys.Control)
            {
                // Cntrl-click
                double X = Convert.ToDouble(MouseX) / Convert.ToDouble(Box.Size.Width);
                X = X * Setting.General_MachineSizeX;
                double Y = Convert.ToDouble(Box.Size.Height - MouseY) / Convert.ToDouble(Box.Size.Height);
                Y = Y * Setting.General_MachineSizeY;
                
                await Machine.Move.MoveXYSafeAsync(X, Y);
            }

            else
            {
                BoxTo_mms(out Xmm, out Ymm, MouseX, MouseY, Box);
                
                await Machine.Move.MoveXYSafeAsync(
                    Machine.Position.CurrentX + Xmm, 
                    Machine.Position.CurrentY - Ymm);
            }
        }

        // =================================================================================
        private async void GoX_button_Click(object sender, EventArgs e)
        {
            double X;
            double Y = Machine.Position.CurrentY;
            double A = Machine.Position.CurrentA;

            if (!double.TryParse(GotoX_textBox.Text.Replace(',', '.'), out X))
            {
                return;
            }

            if (Relative_Button.Checked)
            {
                X += Machine.Position.CurrentX;
            }

            await Machine.Move.MoveXYASafeAsync(X, Y, A);
        }

        private void GoY_button_Click(object sender, EventArgs e)
        {
            double X = Machine.Position.CurrentX;
            double Y;
            double A = Machine.Position.CurrentA;

            if (!double.TryParse(GotoY_textBox.Text.Replace(',', '.'), out Y))
            {
                return;
            }

            if (Relative_Button.Checked)
            {
                Y += Machine.Position.CurrentY;
            }

            Machine.Move.MoveXYASafeAsync(X, Y, A);
        }

        private async void GoZ_button_Click(object sender, EventArgs e)
        {
            double Z;

            if (!double.TryParse(GotoZ_textBox.Text.Replace(',', '.'), out Z))
            {
                return;
            }

            if (Relative_Button.Checked)
            {
                Z += Machine.Position.CurrentZ;
            }

            await Machine.Move.MoveZSafeAsync(Z);
        }

        private async void GoA_button_Click(object sender, EventArgs e)
        {
            double X = Machine.Position.CurrentX;
            double Y = Machine.Position.CurrentY;
            double A;
            if (!double.TryParse(GotoA_textBox.Text.Replace(',', '.'), out A))
            {
                return;
            }

            if (Relative_Button.Checked)
            {
                A += Machine.Position.CurrentA;
            }

            await Machine.Move.MoveXYASafeAsync(X, Y, A);
        }

        private async void Goto_button_Click(object sender, EventArgs e)
        {
            double X;  // target coordinates
            double Y;
            double Z;
            double A;
            if (!double.TryParse(GotoX_textBox.Text.Replace(',', '.'), out X))
            {
                return;
            }
            if (!double.TryParse(GotoY_textBox.Text.Replace(',', '.'), out Y))
            {
                return;
            }
            if (!double.TryParse(GotoZ_textBox.Text.Replace(',', '.'), out Z))
            {
                return;
            }
            if (!double.TryParse(GotoA_textBox.Text.Replace(',', '.'), out A))
            {
                return;
            }

            if (Relative_Button.Checked)
            {
                X += Machine.Position.CurrentX;
                Y += Machine.Position.CurrentY;
                Z += Machine.Position.CurrentZ;
                A += Machine.Position.CurrentA;
            }
            if (Math.Abs(Z) < 0.01)  // allow raising Z and move at one go
            {
                if (!await Machine.Move.MoveZSafeAsync(Z))
                {
                    return;
                }
            };
            // move X, Y, A if needed
            if (!(
                (Math.Abs(X - Machine.Position.CurrentX) < 0.01) && 
                (Math.Abs(Y - Machine.Position.CurrentY) < 0.01) && 
                (Math.Abs(A - Machine.Position.CurrentA) < 0.01)))
            {
                // Allow raise Z, goto and low Z:
                if (!(Math.Abs(Z) < 0.01))
                {
                    if (!await Machine.Move.MoveZSafeAsync(0))
                    {
                        return;
                    }
                }
                if (!await Machine.Move.MoveXYASafeAsync(X, Y, A))
                {
                    return;
                }
                if (!(Math.Abs(Z - Machine.Position.CurrentZ) < 0.01))
                {
                    if (!await Machine.Move.MoveZSafeAsync(Z))
                    {
                        return;
                    }
                };
            }
            // move Z if needed
            if (!(Math.Abs(Z - Machine.Position.CurrentZ) < 0.01))
            {
                if (!await Machine.Move.MoveZSafeAsync(Z))
                {
                    return;
                }
            }
        }

        private void LoadCurrentPosition_button_Click(object sender, EventArgs e)
        {
            GotoX_textBox.Text = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            GotoY_textBox.Text = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            GotoZ_textBox.Text = Machine.Position.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture);
            GotoA_textBox.Text = Machine.Position.CurrentA.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private void SetCurrentPosition_button_Click(object sender, EventArgs e)
        {
            double tst;
            string Xstr = "";
            string Ystr = "";
            string Zstr = "";
            string Astr = "";
            if (double.TryParse(GotoX_textBox.Text.Replace(',', '.'), out tst))
            {
                Xstr = tst.ToString();
            }
            else
            {
                DisplayText("X value error", KnownColor.Red, true);
                return;
            }

            if (double.TryParse(GotoY_textBox.Text.Replace(',', '.'), out tst))
            {
                Ystr = tst.ToString();
            }
            else
            {
                DisplayText("Y value error", KnownColor.Red, true);
                return;
            }

            if (double.TryParse(GotoZ_textBox.Text.Replace(',', '.'), out tst))
            {
                Zstr = tst.ToString();
            }
            else
            {
                DisplayText("Z value error", KnownColor.Red, true);
                return;
            }

            if (double.TryParse(GotoA_textBox.Text.Replace(',', '.'), out tst))
            {
                Astr = tst.ToString();
            }
            else
            {
                DisplayText("A value error", KnownColor.Red, true);
                return;
            }
            Machine.CommsProcessor.SendCommand("{\"gc\":\"G28.3 X" + Xstr + " Y" + Ystr + " Z" + Zstr + " A" + Astr + "\"}");
            Thread.Sleep(50);
        }

        #endregion Jogging

        // =================================================================================
        // CNC interface functions
        // =================================================================================
        #region CNC interface functions

        // =================================================================================
        // Different types of control hardware and settings
        // =================================================================================

        private async Task UpdateCNCBoardType_mAsync()
        {
            var tinyGSystemParameters = DIBindings.Resolve<ITinyGSystemParameters>();

            DisplayText("Finding board type:");

            await Machine.CommsProcessor.SendCommandWaitResponseAsync("{\"" + tinyGSystemParameters.hp.Name + "\":\"\"}");
        }

        private bool UpdateCNCBoardSettings_m()
        {
            // When called, the parameters are already read from storage.
            // If board is TinyG, compare to what we have. If different, ask what to do
            // (on some crash situations, TinyG can loose the settings)
            // If board is qQuintic, write the values

            return true;
        }

        // =================================================================================
        // Position confidence, motor power timer:
        // we want to keep track about TinyG motor power state. To do that, we run our own timer, MotorPower_timer.
        // The timer ticks every second and runs all the time. In normal operation state, we count the ticks and
        // reset the count every time the machine stops.
        // The TinyG motor power timeout value is retrieved at startup, like other TinyG values.
        // If the machine is homed, and either timer get bigger than motor power timeout (motors are powered off by TinyG),
        // or an error occurs, we've lost position confidence and machine needs to be re-homed.

        private double PowerTimerCount = 0;
        private double TinyGMotorTimeout = 300.0;
        private bool PositionConfidence = false;

        private void Update_mt(string ValStr)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_mt), new[] { ValStr }); return; }

            double val;
            if (double.TryParse(ValStr.Replace(',', '.'), out val))
            {
                TinyGMotorTimeout = val;
                DisplayText("mt value: " + TinyGMotorTimeout.ToString());
            }
            else
            {
                DisplayText("Bad mt value: " + ValStr, KnownColor.DarkRed);
            }

        }

        public void ResetMotorTimer()
        {
            PowerTimerCount = 0;
        }

        [DebuggerStepThrough]
        private async void MotorPower_timer_Tick(object sender, EventArgs e)
        {
            if (PositionConfidence)         // == if timer should run
            {
                // DisplayText("timer: " + PowerTimerCount.ToString());
                PowerTimerCount = PowerTimerCount + 1.0;
                if ((PowerTimerCount + 0.1) > TinyGMotorTimeout)
                {
                    await OfferHomingAsync();
                }
            }
        }

         private async Task OfferHomingAsync()
         {
            PositionConfidence = false;
            DialogResult dialogResult = ShowMessageBox(
                "Home machine now?",
                "Home Now?", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.No)
            {
                OpticalHome_button.BackColor = Color.Red;
            }
            else
            {
                await DoHomingAsync();
            }
        }

        private async Task<bool> DoHomingAsync()
        {
            PositionConfidence = false;
            ValidMeasurement_checkBox.Checked = false;
            OpticalHome_button.BackColor = Color.Red;
            if (!await MechanicalHoming_mAsync())
            {
                OpticalHome_button.BackColor = Color.Red;
                return false;
            }
            if (!await OpticalHoming_mAsync())
            {
                OpticalHome_button.BackColor = Color.Red;
                return false;
            }
            if (VigorousHoming_checkBox.Checked)
            {
                // shake the machine
                if (!await DoTheShakeAsync())
                {
                    return false;
                }
                // home again
                if (!await OpticalHoming_mAsync())
                {
                    OpticalHome_button.BackColor = Color.Red;
                    return false;
                }
            }
            OpticalHome_button.BackColor = default(Color);
            OpticalHome_button.UseVisualStyleBackColor = true;
            PositionConfidence = true;
            return true;
        }

        private async Task<bool> DoTheShakeAsync()
        {
            DisplayText("Vigorous homing");
            if ((Setting.General_MachineSizeX<300)|| (Setting.General_MachineSizeY < 300))
            {
                DisplayText("Machine too small for vigorous homing routine (Please give feedback!)");
                return true;
            }
            int[] x = new int[] { 250, 250, 250, 50, 50,0 };
            int[] y = new int[] { 250, 50, 150, 50, 150,0 };
            for (int i = 0; i < x.Length; i++)
            {
                if (!await Machine.Move.MoveXYSafeAsync(x[i], y[i]))
                {
                    return false;
                }
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!await Machine.Move.MoveXYSafeAsync(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> Nozzle_ProbeDown_mAsync()
        {
            int homingTimeout;

            if (!Machine.Homing.HomingTimeout_m(out homingTimeout, "Z"))
            {
                return false;
            }

            Machine.Homing.CNC_HomingTimeout = homingTimeout;

            DisplayText("Probing Z, timeout value: " + Machine.Homing.CNC_HomingTimeout.ToString());

            Machine.Move.ProbingMode(true);
            Machine.Homing.IsHoming = true;

            try
            {
                await Machine.CommsProcessor.SendCommandWaitResponseAsync("{\"gc\":\"G28.4 Z0\"}", 4000);
            }
            catch
            {
                Machine.Homing.IsHoming = false;
                Machine.Move.ProbingMode(false);
                return false;
            }
            Machine.Homing.IsHoming = false;
            Machine.Move.ProbingMode(false);
            return true;
        }

        public async Task<bool> CalibrateNozzle_mAsync()
        {
            if (Setting.Placement_OmitNozzleCalibration)
            {
                DisplayText("Nozzle calibration asked, but disabled.");
                return true;
            };

            double MarkX = Machine.Position.CurrentX;
            double MarkY = Machine.Position.CurrentY;
            double MarkZ = Machine.Position.CurrentZ;
            double MarkA = Machine.Position.CurrentA;

            bool UpCamWasRunning = false;
            if (Machine.UpCamera.Active)
            {
                UpCamWasRunning = true;
            }
            SelectCamera(Machine.UpCamera);
            if (!Machine.UpCamera.IsRunning())
            {
                ShowMessageBox(
                    "Up camera not running, can't calibrate Nozzle.",
                    "Nozzle calibration failed.",
                    MessageBoxButtons.OK);
                return false;
            }
            Machine.UpCamera.PauseProcessing = true;

            // take Nozzle up
            bool result = true;
            result &= await Machine.Move.MoveZSafeAsync(0.0);

            // take Nozzle to camera
            result &= await Machine.Move.MoveXYSafeAsync(Setting.UpCam_PositionX, Setting.UpCam_PositionY);
            result &= await Machine.Move.MoveZSafeAsync(Setting.General_ZtoPCB - 0.5); // Average small component height 0.5mm (?)


            // measure the values
            SetNozzleMeasurement();
            if (Setting.Nozzles_Enabled)
            {
                double MinSize = 0.0;
                double MaxSize = 10.0;
                int nozzle = Setting.Nozzles_current;
                if (nozzle > 0)
                {
                    if (NozzlesParameters_dataGridView.Rows[nozzle - 1].Cells[1].Value == null)
                    {
                        ShowMessageBox(
                            "Bad data at Nozzles vision parameters table, nozzle " + nozzle.ToString() + ", min. size",
                            "Bad data",
                            MessageBoxButtons.OK);
                        return false;
                    }

                    if (NozzlesParameters_dataGridView.Rows[nozzle - 1].Cells[2].Value == null)
                    {
                        ShowMessageBox(
                            "Bad data at Nozzles vision parameters table, nozzle " + nozzle.ToString() + ", max. size",
                            "Bad data",
                            MessageBoxButtons.OK);
                        return false;
                    }

                    if (!double.TryParse(NozzlesParameters_dataGridView.Rows[nozzle - 1].Cells[1].Value.ToString().Replace(',', '.'), out MinSize))
                    {
                        ShowMessageBox(
                            "Bad data at Nozzles vision parameters table, nozzle " + nozzle.ToString() + ", min. size",
                            "Bad data",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    if (!double.TryParse(NozzlesParameters_dataGridView.Rows[nozzle - 1].Cells[2].Value.ToString().Replace(',', '.'), out MaxSize))
                    {
                        ShowMessageBox(
                            "Bad data at Nozzles vision parameters table, nozzle " + nozzle.ToString() + ", max. size",
                            "Bad data",
                            MessageBoxButtons.OK);
                        return false;
                    }
                }
                DisplayText("Measuring nozzle, min. size "+ MinSize.ToString() + ", max. size" + MaxSize.ToString());
                Machine.UpCamera.MaxSize = MaxSize / Setting.UpCam_XmmPerPixel;
                Machine.UpCamera.MinSize = MinSize / Setting.UpCam_XmmPerPixel;
                Machine.UpCamera.SizeLimited = true;
            }

            result &= Nozzle.Calibrate();  

            // take Nozzle up
            result &= await Machine.Move.MoveZSafeAsync(0.0);

            Machine.UpCamera.PauseProcessing = false;
            
            if (!UpCamWasRunning)
            {
                SelectCamera(Machine.DownCamera);
            }
            
            if (result)
            {
                for (int i = 0; i < Nozzle.CalibrationPoints.Count; i++)
                {
                    DisplayText("A: " + Nozzle.CalibrationPoints[i].Angle.ToString("0.000") +
                        ", X: " + Nozzle.CalibrationPoints[i].X.ToString("0.000") +
                        ", Y: " + Nozzle.CalibrationPoints[i].Y.ToString("0.000"));
                }
            }
            else
            {
                ShowMessageBox(
                    "Nozzle calibration failed.",
                    "Nozzle calibration failed.",
                    MessageBoxButtons.OK);
            }
            
            Machine.UpCamera.SizeLimited = false;
            return (result);
        }


        // =====================================================================
        // This routine finds an accurate location of a circle/rectangle that downcamera is looking at.
        // Used in homing, locating fiducials and locating tape holes.
        // Tolerances in mm; find: how far from center to accept a circle, move: how close to go (set small to ensure view from straight up)
        // At return, the camera is located on top of the circle.
        // X and Y are set to remainding error (true position: currect + error)
        // =====================================================================

        public async Task<Tuple<bool, double, double>> GoToFeatureLocation_mAsync(FeatureType Shape, double FindTolerance, double MoveTolerance)
        {
            double X = 0.0F;
            double Y = 0.0F;

            bool isRunning = Machine.DownCamera.IsRunning();

            DisplayText("GoToFeatureLocation_m(), FindTolerance: " + FindTolerance.ToString() + ", MoveTolerance: " + MoveTolerance.ToString());
            SelectCamera(Machine.DownCamera);
            X = 100;
            Y = 100;
            FindTolerance = FindTolerance / Setting.DownCam_XmmPerPixel;
            if (!Machine.DownCamera.IsRunning())
            {
                ShowMessageBox(
                    "Attempt to find circle, downcamera is not running.",
                    "ICamera not running",
                    MessageBoxButtons.OK);
                return new Tuple<bool, double, double>(false, X, Y);
            }
            int count = 0;
            int res = 0;
            int tries = 0;
            //bool ProcessingStateSave = Machine.DownCamera.PauseProcessing;
            //Machine.DownCamera.PauseProcessing = true;
            do
            {
                // Measure location
                for (tries = 0; tries < 8; tries++)
                {
                    if (Shape==FeatureType.Circle)
                    {
                        res = Machine.DownCamera.GetClosestCircle(out X, out Y, FindTolerance);
                    }
                    else if (Shape == FeatureType.Rectangle)
                    {
                        res = Machine.DownCamera.GetClosestRectangle(out X, out Y, FindTolerance);
                    }
                    else if (Shape == FeatureType.Both)
                    {
                        res = Machine.DownCamera.GetClosestCircle(out X, out Y, FindTolerance);
                        if (res==0)
                        {
                            res = Machine.DownCamera.GetClosestRectangle(out X, out Y, FindTolerance);
                        }
                    }
                    else
                    {
                        ShowMessageBox(
                            "GoToFeatureLocation called with unknown feature " + Shape.ToString(),
                            "Programmer error:",
                            MessageBoxButtons.OK);
                        return new Tuple<bool, double, double>(false, X, Y);
                    }

                    if (res != 0)
                    {
                        break;
                    }
                    Thread.Sleep(80); // next frame + vibration damping
                    if (tries >= 7)
                    {
                        DisplayText("Failed in 8 tries.");
                        ShowMessageBox(
                            "Optical positioning: Can't find Feature",
                            "No found",
                            MessageBoxButtons.OK);
                        //Machine.DownCamera.PauseProcessing = ProcessingStateSave;
                        return new Tuple<bool, double, double>(false, X, Y);
                    }
                }
                X = X * Setting.DownCam_XmmPerPixel;
                Y = -Y * Setting.DownCam_YmmPerPixel;
                DisplayText("Optical positioning, round " + count.ToString() + ", dX= " + X.ToString() + ", dY= " + Y.ToString() + ", tries= " + tries.ToString());
                // If we are further than move tolerance, go there
                if ((Math.Abs(X) > MoveTolerance) || (Math.Abs(Y) > MoveTolerance))
                {
                    if (!await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX + X, Machine.Position.CurrentY + Y))
                    {
                        return new Tuple<bool, double, double>(false, X, Y);
                    }
                }
                count++;
            }  // repeat this until we didn't need to move
            while ((count < 8)
                && ((Math.Abs(X) > MoveTolerance)
                || (Math.Abs(Y) > MoveTolerance)));

            //Machine.DownCamera.PauseProcessing = ProcessingStateSave;
            if (count >= 7)
            {
                ShowMessageBox(
                    "Optical positioning: Process is unstable, result is unreliable!",
                    "Count exeeded",
                    MessageBoxButtons.OK);
                return new Tuple<bool, double, double>(false, X, Y);
            }
            return new Tuple<bool, double, double>(true, X, Y);
        }


        private async Task<bool> OpticalHoming_mAsync()
        {
            DisplayText("Optical homing");
            SetHomingMeasurement();
            // Find within 20mm, goto within 0.05
            var result = await GoToFeatureLocation_mAsync(FeatureType.Circle, 20.0, 0.05);

            if (!result.Item1)
            {
                return false;
            }

            double X = result.Item2;
            double Y = result.Item3;

            // Measure 7 times, get median: 
            SetHomingMeasurement();
            List<double> Xlist = new List<double>();
            List<double> Ylist = new List<double>();
            int res;
            int Successes = 0;
            int Tries = 0;
            do
            {
                Tries++;
                res = Machine.DownCamera.GetClosestCircle(out X, out Y, 0.1/ Setting.DownCam_XmmPerPixel); 
                if (res==1)
                {
                    Successes++;
                    X = -X * Setting.DownCam_XmmPerPixel;
                    Y = -Y * Setting.DownCam_YmmPerPixel;
                    Xlist.Add(X);
                    Ylist.Add(Y);
                    DisplayText("X: " + X.ToString("0.000") + ", Y: " + Y.ToString("0.000"));
                }
            }
            while ((Successes<7)&&(Tries<20));
            if (Tries >= 20)
            {
                DisplayText("Optical homing failed, 20 tries did not give 7 results.");
                return false;
            }
            Xlist.Sort();
            Ylist.Sort();
            X = Xlist[3];
            Y = Ylist[3];
            // CNC_RawWrite("G28.3 X" + X.ToString("0.000") + " Y" + Y.ToString("0.000"));
            Machine.CommsProcessor.SendCommand("{\"gc\":\"G28.3 X" + X.ToString("0.000") + " Y" + Y.ToString("0.000") + "\"}");
            Thread.Sleep(50);
            Machine.Position.CurrentX = 0.0;
            Machine.Position.CurrentY = 0.0;
            Update_xpos("0.00");
            Update_ypos("0.00");
            DisplayText("Optical homing OK.");
            return true;
        }

        private async Task<bool> MechanicalHoming_mAsync()
        {
            Machine.Move.ProbingMode(false);
            if (!await Machine.Homing.CNC_Home_mAsync("Z"))
            {
                return false;
            };
            // DisplayText("move Z");
            if (!await Machine.Move.MoveZSafeAsync(Setting.General_ShadeGuard_mm))		// make room for shade
            {
                return false;
            };
            if (!await Machine.Homing.CNC_Home_mAsync("Y"))
            {
                return false;
            };
            if (!await Machine.Homing.CNC_Home_mAsync("X"))
            {
                return false;
            };
            // DisplayText("move A");
            if (!await Machine.Move.MoveASafeAsync(0))
            {
                return false;
            };
            if (Setting.General_ShadeGuard_mm > 0.0)
            {
                Machine.Move.ZGuardOff();

                if (!await Machine.Move.MoveXYSafeAsync(10, 10))
                {
                    Machine.Move.ZGuardOn();
                    return false;
                };

                DisplayText("Z back up Z");  // Z back up

                if (!await Machine.Move.MoveZSafeAsync(0))
                {
                    Machine.Move.ZGuardOn();
                    return false;
                };

                Machine.Move.ZGuardOn();
            };

            return true;
        }

        private async void OpticalHome_button_Click(object sender, EventArgs e)
        {
            await DoHomingAsync();
        }

        #endregion CNC interface functions

        // =================================================================================
        // Up/Down camera setup page functions
        // =================================================================================
        #region ICamera setup pages functions

        // =================================================================================
        // Common
        // =================================================================================

        private void KeepActive_checkBox_Click(object sender, EventArgs e)
        {
            // We handle checked state ourselves to avoid automatic call to StartCameras at startup
            // (Changing Checked activatges CheckedChanged event
            if (KeepActive_checkBox.Checked)
            {
                // KeepActive_checkBox.Checked = true;
                RobustFast_checkBox.Enabled = false;
                StartCameras();
            }
            else
            {
                // KeepActive_checkBox.Checked = false;
                RobustFast_checkBox.Enabled = true;
            }
            Setting.Cameras_KeepActive = KeepActive_checkBox.Checked;
        }


        private void StartCameras()
        {
            // Called at startup. 
            Machine.DownCamera.Active = false;
            Machine.UpCamera.Active = false;
            Machine.DownCamera.Close();
            Machine.UpCamera.Close();
            SetDownCameraDefaults();
            SetUpCameraDefaults();
            if (KeepActive_checkBox.Checked)
            {
                StartUpCamera_m();
                Machine.UpCamera.Active = false;
                StartDownCamera_m();
            }
            SelectCamera(Machine.DownCamera);

        }

        private void UpCamStop_button_Click(object sender, EventArgs e)
        {
            Machine.UpCamera.Close();
        }

        private void UpCamStart_button_Click(object sender, EventArgs e)
        {
            StartUpCamera_m();
        }
        private void DownCamStop_button_Click(object sender, EventArgs e)
        {
            Machine.DownCamera.Close();
        }

        private void DownCamStart_button_Click(object sender, EventArgs e)
        {
            StartDownCamera_m();
        }


        private void RobustFast_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Cameras_RobustSwitch = RobustFast_checkBox.Checked;
        }


        private void SelectCamera(ICamera cam)
        {
            if (cam == Machine.DownCamera)
            {
                return;
            }

            if (KeepActive_checkBox.Checked)
            {
                Machine.DownCamera.Active = false;
                Machine.UpCamera.Active = false;
                cam.Active = true;
                if (cam == Machine.DownCamera)
                {
                    DisplayText("Machine.DownCamera activated");
                }
                else
                {
                    DisplayText("Machine.UpCamera activated");
                }
                return;
            }

            if (Setting.Cameras_RobustSwitch)
            {
                if (Machine.UpCamera.IsRunning())
                {
                    Machine.UpCamera.Close();
                };
                if (Machine.DownCamera.IsRunning())
                {
                    Machine.DownCamera.Close();
                };
            }
            if (cam == Machine.DownCamera)
            {
                if (Machine.UpCamera.IsRunning())
                {
                    Machine.UpCamera.Close();
                };
                StartDownCamera_m();
            }
            else
            {
                if (Machine.DownCamera.IsRunning())
                {
                    Machine.DownCamera.Close();
                };
                StartUpCamera_m();
            }
        }


        // =================================================================================
        private bool StartDownCamera_m()
        {
            Machine.UpCamera.Active = false;
            if (Machine.DownCamera.IsRunning())
            {
                DisplayText("Machine.DownCamera already running");
                Machine.DownCamera.Active = true;
                UpdateCameraCameraStatus_labelThreadSafe();
                return true;
            };

            Machine.DownCamera.Active = false;
            if (Setting.DowncamMoniker == "")
            {
                // Very first runs, no attempt to connect cameras yet. This is ok.
                UpdateCameraCameraStatus_labelThreadSafe();
                return true;
            };
            // Check that the device exists
            List<string> monikers = Machine.DownCamera.GetMonikerStrings();
            if (!monikers.Contains(Setting.DowncamMoniker))
            {
                DisplayText("Downcamera moniker not found. Moniker: " + Setting.DowncamMoniker);
                UpdateCameraCameraStatus_labelThreadSafe();
                return false;
            }

            if (Setting.UpcamMoniker == Setting.DowncamMoniker)
            {
                ShowMessageBox(
                    "Up camera and Down camera point to same physical device.",
                    "ICamera selection isse",
                    MessageBoxButtons.OK
                );
                UpdateCameraCameraStatus_labelThreadSafe();
                return false;
            }

            if (!Machine.DownCamera.Start("Machine.DownCamera", Setting.DowncamMoniker))
            {
                ShowMessageBox(
                    "Problem Starting down camera.",
                    "Down ICamera problem",
                    MessageBoxButtons.OK
                );
                CameraStatus_label.Text = "Not Connected";
                Machine.DownCamera.Active = false;
                UpdateCameraCameraStatus_labelThreadSafe();
                return false;
            };
            Machine.DownCamera.Active = true;
            UpdateCameraCameraStatus_labelThreadSafe();
            return true;
        }

        // ====
        private bool StartUpCamera_m()
        {

            Machine.DownCamera.Active = false;
            if (Machine.UpCamera.IsRunning())
            {
                DisplayText("Machine.UpCamera already running");
                Machine.UpCamera.Active = true;
                UpdateCameraCameraStatus_labelThreadSafe();
                return true;
            };

            Machine.UpCamera.Active = false;
            if (Setting.UpcamMoniker == "")
            {
                // Very first runs, no attempt to connect cameras yet. This is ok.
                UpdateCameraCameraStatus_labelThreadSafe();
                return true;
            };
            // Check that the device exists
            List<string> monikers = Machine.UpCamera.GetMonikerStrings();
            if (!monikers.Contains(Setting.UpcamMoniker))
            {
                DisplayText("Upcamera moniker not found. Moniker: " + Setting.UpcamMoniker);
                UpdateCameraCameraStatus_labelThreadSafe();
                return false;
            }

            if (Setting.UpcamMoniker == Setting.DowncamMoniker)
            {
                ShowMessageBox(
                    "Up camera and Down camera point to same physical device.",
                    "ICamera selection issue",
                    MessageBoxButtons.OK
                );
                UpdateCameraCameraStatus_labelThreadSafe();
                return false;
            }

            if (!Machine.UpCamera.Start("Machine.UpCamera", Setting.UpcamMoniker))
            {
                ShowMessageBox(
                    "Problem Starting up camera.",
                    "Up camera problem",
                    MessageBoxButtons.OK
                );
                UpdateCameraCameraStatus_labelThreadSafe();
                return false;
            };
            Machine.UpCamera.Active = true;
            UpdateCameraCameraStatus_labelThreadSafe();
            return true;
        }

        // =================================================================================

        // =================================================================================

        private void Cam_pictureBox_MouseClick(object sender, MouseEventArgs e)
        {
            if (System.Windows.Forms.Control.ModifierKeys == Keys.Alt)
            {
                PickColor(e.X, e.Y);
            }
            else
            {
                General_pictureBox_MouseClick(Cam_pictureBox, e.X, e.Y);
            }
        }

        // =================================================================================


        private void SetDownCameraDefaults()
        {
            Machine.DownCamera.Id = "Downcamera";
            Machine.DownCamera.DesiredX = Setting.DownCam_DesiredX;
            Machine.DownCamera.DesiredY = Setting.DownCam_DesiredY;
            Machine.DownCamera.BoxSizeX = 200;
            Machine.DownCamera.BoxSizeY = 200;
            Machine.DownCamera.BoxRotationDeg = 0;
            Machine.DownCamera.ImageBox = Cam_pictureBox;
            Machine.DownCamera.Mirror = false;
            Machine.DownCamera.ClearDisplayFunctionsList();
            Machine.DownCamera.SnapshotColor = Setting.DownCam_SnapshotColor;
            // Draws
            Machine.DownCamera.DrawCross = true;
            DownCameraDrawCross_checkBox.Checked = true;
            Machine.DownCamera.DrawDashedCross = false;
            Machine.DownCamera.DrawGrid = false;
            DownCameraDrawDashedCross_checkBox.Checked = false;
            Machine.DownCamera.Draw_Snapshot = false;
            // Finds:
            Machine.DownCamera.FindCircles = false;
            DownCamFindCircles_checkBox.Checked = false;
            Machine.DownCamera.FindRectangles = false;
            DownCamFindRectangles_checkBox.Checked = false;
            Machine.DownCamera.FindComponent = false;
            DownCam_FindComponents_checkBox.Checked = false;
            Machine.DownCamera.TestAlgorithm = false;
            ImageTest_checkBox.Checked = false;
            Overlay_checkBox.Checked = false;
            Machine.DownCamera.DrawBox = false;
            Machine.DownCamera.DrawArrow = false;
            DownCameraDrawBox_checkBox.Checked = false;
        }
        // ====
        private void SetUpCameraDefaults()
        {
            Machine.UpCamera.Id = "Upcamera";
            Machine.UpCamera.DesiredX = Setting.UpCam_DesiredX;
            Machine.UpCamera.DesiredY = Setting.UpCam_DesiredY;

            Machine.UpCamera.BoxSizeX = 200;
            Machine.UpCamera.BoxSizeY = 200;
            Machine.UpCamera.BoxRotationDeg = 0;
            Machine.UpCamera.ImageBox = Cam_pictureBox;
            Machine.UpCamera.Mirror = true;
            Machine.UpCamera.ClearDisplayFunctionsList();
            Machine.UpCamera.SnapshotColor = Setting.UpCam_SnapshotColor;
            // Draws
            Machine.UpCamera.DrawCross = true;
            UpCameraDrawCross_checkBox.Checked = true;
            Machine.UpCamera.DrawGrid = false;
            Machine.UpCamera.DrawDashedCross = false;
            UpCameraDrawDashedCross_checkBox.Checked = false;
            Machine.UpCamera.Draw_Snapshot = false;
            // Finds:
            Machine.UpCamera.FindCircles = false;
            Machine.UpCamera.FindRectangles = false;
            Machine.UpCamera.FindComponent = false;
            Machine.UpCamera.TestAlgorithm = false;
            Machine.UpCamera.DrawBox = false;
            Machine.UpCamera.DrawArrow = false;
            UpCameraDrawBox_checkBox.Checked = false;
        }

        // =================================================================================
        private void tabPageSetupCameras_Begin()
        {
            SetDownCameraDefaults();
            DownCameraDesiredX_textBox.Text = Setting.DownCam_DesiredX.ToString();
            DownCameraDesiredY_textBox.Text = Setting.DownCam_DesiredY.ToString();
            Machine.DownCamera.DrawBox = DownCameraDrawBox_checkBox.Checked;
            Machine.DownCamera.DrawCross = DownCameraDrawCross_checkBox.Checked;
            Machine.DownCamera.DrawSidemarks = DownCameraDrawTicks_checkBox.Checked;
            Machine.DownCamera.Draw_Snapshot = Overlay_checkBox.Checked;
            Machine.DownCamera.FindCircles = DownCamFindCircles_checkBox.Checked;
            Machine.DownCamera.FindRectangles = DownCamFindRectangles_checkBox.Checked;
            Machine.DownCamera.FindComponent = DownCam_FindComponents_checkBox.Checked;

            SetUpCameraDefaults();
            UpCameraDesiredX_textBox.Text = Setting.UpCam_DesiredX.ToString();
            UpCameraDesiredY_textBox.Text = Setting.UpCam_DesiredY.ToString();
            Machine.UpCamera.DrawBox = UpCameraDrawBox_checkBox.Checked;
            Machine.UpCamera.DrawCross = UpCameraDrawCross_checkBox.Checked;
            Machine.UpCamera.Draw_Snapshot = Overlay_checkBox.Checked;
            Machine.UpCamera.FindCircles = UpCamFindCircles_checkBox.Checked;
            Machine.UpCamera.FindComponent = UpCam_FindComponents_checkBox.Checked;

            NozzleOffset_label.Visible = false;
            ClearEditTargets();

            double f;
            f = Setting.DownCam_XmmPerPixel * Machine.DownCamera.BoxSizeX;
            DownCameraBoxX_textBox.Text = f.ToString("0.00", CultureInfo.InvariantCulture);
            DownCameraBoxXmmPerPixel_label.Text = "(" + Setting.DownCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            f = Setting.DownCam_YmmPerPixel * Machine.DownCamera.BoxSizeY;
            DownCameraBoxY_textBox.Text = f.ToString("0.00", CultureInfo.InvariantCulture);
            DownCameraBoxYmmPerPixel_label.Text = "(" + Setting.DownCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";

            f = Setting.UpCam_XmmPerPixel * Machine.UpCamera.BoxSizeX;
            UpCameraBoxX_textBox.Text = f.ToString("0.00", CultureInfo.InvariantCulture);
            UpCameraBoxXmmPerPixel_label.Text = "(" + Setting.UpCam_XmmPerPixel.ToString("0.000", CultureInfo.InvariantCulture) + "mm/pixel)";
            f = Setting.UpCam_YmmPerPixel * Machine.UpCamera.BoxSizeY;
            UpCameraBoxY_textBox.Text = f.ToString("0.00", CultureInfo.InvariantCulture);
            UpCameraBoxYmmPerPixel_label.Text = "(" + Setting.UpCam_YmmPerPixel.ToString("0.000", CultureInfo.InvariantCulture) + "mm/pixel)";

            JigX_textBox.Text = Setting.General_JigOffsetX.ToString("0.00", CultureInfo.InvariantCulture);
            JigY_textBox.Text = Setting.General_JigOffsetY.ToString("0.00", CultureInfo.InvariantCulture);
            PickupCenterX_textBox.Text = Setting.General_PickupCenterX.ToString("0.00", CultureInfo.InvariantCulture);
            PickupCenterY_textBox.Text = Setting.General_PickupCenterY.ToString("0.00", CultureInfo.InvariantCulture);
            NozzleOffsetX_textBox.Text = Setting.DownCam_NozzleOffsetX.ToString("0.00", CultureInfo.InvariantCulture);
            NozzleOffsetY_textBox.Text = Setting.DownCam_NozzleOffsetY.ToString("0.00", CultureInfo.InvariantCulture);
            Z0toPCB_CamerasTab_label.Text = Setting.General_ZtoPCB.ToString("0.00", CultureInfo.InvariantCulture) + " mm";

            UpcamPositionX_textBox.Text = Setting.UpCam_PositionX.ToString("0.00", CultureInfo.InvariantCulture);
            UpcamPositionY_textBox.Text = Setting.UpCam_PositionY.ToString("0.00", CultureInfo.InvariantCulture);

            Machine.DownCamera.SideMarksX = Setting.General_MachineSizeX / 100;
            Machine.DownCamera.SideMarksY = Setting.General_MachineSizeY / 100;
            DownCameraDrawTicks_checkBox.Checked = Setting.DownCam_DrawTicks;
            DownCameraDrawGrid_checkBox.Checked = false;

            DowncamSnapshot_ColorBox.BackColor = Setting.DownCam_SnapshotColor;
            UpcamSnapshot_ColorBox.BackColor = Setting.UpCam_SnapshotColor;

            NozzleDistance_textBox.Text = Setting.Nozzles_CalibrationDistance.ToString();
            NozzleMaxSize_textBox.Text = Setting.Nozzles_CalibrationMaxSize.ToString();
            NozzleMinSize_textBox.Text = Setting.Nozzles_CalibrationMinSize.ToString();
            CamShowPixels_checkBox.Checked = true;
            Cam_pictureBox.SizeMode = PictureBoxSizeMode.CenterImage;

            FiducialManConfirmation_checkBox.Checked=Setting.Placement_FiducialConfirmation;
            if (Setting.Placement_FiducialsType==0)
            {
                RoundFiducial_radioButton.Checked = true;
            }
            else if (Setting.Placement_FiducialsType == 1)
            {
                RectangularFiducial_radioButton.Checked = true;
            }
            else if (Setting.Placement_FiducialsType == 2)
            {
                AutoFiducial_radioButton.Checked = true;
            }
            else
            {
                ShowMessageBox(
                   "Unknown fiducial type "+ Setting.Placement_FiducialsType.ToString()+", possibly corrupted settings",
                   "Corrupted settings",
                   MessageBoxButtons.OK);
                RoundFiducial_radioButton.Checked = true;
            }
            FiducialsTolerance_textBox.Text= Setting.Placement_FiducialTolerance.ToString("0.00", CultureInfo.InvariantCulture);

            Display_dataGridView.Rows.Clear();
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
            Machine.UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
            getDownCamList();
            getUpCamList();
            UpdateCameraCameraStatus_labelThreadSafe();

            // SelectCamera(Machine.DownCamera);
        }

        // =================================================================================
        // get the devices         
        private void getDownCamList()
        {
            List<string> Devices = Machine.DownCamera.GetDeviceList();
            DownCam_comboBox.Items.Clear();
            if (Devices.Count != 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    DownCam_comboBox.Items.Add(i.ToString() + ": " + Devices[i]);
                    DisplayText("Device " + i.ToString() + ": " + Devices[i]);
                }
            }
            else
            {
                DownCam_comboBox.Items.Add("----");
                CameraStatus_label.Text = "No Cam";
            }
            if (
                (Devices.Count > Setting.DownCam_index) && (Setting.DownCam_index > 0))
            {
                DownCam_comboBox.SelectedIndex = Setting.DownCam_index;
            }
            else
            {
                DownCam_comboBox.SelectedIndex = 0;  // default to first
            }
            DisplayText("DownCam_comboBox.SelectedIndex= " + DownCam_comboBox.SelectedIndex.ToString());
        }

        // ====
        private void getUpCamList()
        {
            List<string> Devices = Machine.UpCamera.GetDeviceList();
            UpCam_comboBox.Items.Clear();
            int d = Setting.UpCam_index;
            if (Devices.Count != 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    UpCam_comboBox.Items.Add(i.ToString() + ": " + Devices[i]);
                    DisplayText("Device " + i.ToString() + ": " + Devices[i]);
                }
            }
            else
            {
                UpCam_comboBox.Items.Add("----");
            }
            if ((Devices.Count > Setting.UpCam_index) && (Setting.UpCam_index > 0))
            {
                DisplayText("UpCam_comboBox.SelectedIndex= " + Setting.UpCam_index.ToString());
                UpCam_comboBox.SelectedIndex = Setting.UpCam_index;
            }
            else
            {
                DisplayText("UpCam_comboBox.SelectedIndex= 0");
                UpCam_comboBox.SelectedIndex = 0;  // default to first
            }

        }


        // =================================================================================
        private void tabPageSetupCameras_End()
        {
            Machine.DownCamera.DrawGrid = false;
            Machine.Move.ZGuardOn();
        }

        // =================================================================================
        private void RefreshDownCameraList_button_Click(object sender, EventArgs e)
        {
            getDownCamList();
        }

        // ====
        private void RefreshUpCameraList_button_Click(object sender, EventArgs e)
        {
            getUpCamList();
        }

        // =================================================================================

        private void SetCurrentCameraParameters()
        {
            ICamera CurrentCam;
            if (Machine.UpCamera.IsRunning())
            {
                CurrentCam = Machine.UpCamera;
            }
            else if (Machine.DownCamera.IsRunning())
            {
                CurrentCam = Machine.DownCamera;
            }
            else
            {
                return;
            };
            double val;
            if (Machine.UpCamera.IsRunning())
            {
                CurrentCam.Zoom = UpCamZoom_checkBox.Checked;
                if (double.TryParse(UpCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.UpCamera.ZoomFactor = val;
                }
            }
            else
            {
                CurrentCam.Zoom = DownCamZoom_checkBox.Checked;
                if (double.TryParse(DownCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.DownCamera.ZoomFactor = val;
                }
            }
        }

        // =================================================================================

        private void ConnectDownCamera_button_Click(object sender, EventArgs e)
        {
            DisplayText("DownCam_comboBox.SelectedIndex= " + DownCam_comboBox.SelectedIndex.ToString());
            Setting.DownCam_index = DownCam_comboBox.SelectedIndex;
            List<string> Monikers = Machine.DownCamera.GetMonikerStrings();
            Setting.DowncamMoniker = Monikers[DownCam_comboBox.SelectedIndex];
            Machine.DownCamera.MonikerString = Monikers[DownCam_comboBox.SelectedIndex];
            Machine.DownCamera.DesiredX = Setting.DownCam_DesiredX;
            Machine.DownCamera.DesiredY = Setting.DownCam_DesiredY;
            SelectCamera(Machine.DownCamera);

            if (Machine.DownCamera.IsRunning())
            {
                SetCurrentCameraParameters();
            }
            else
            {
                ShowMessageBox(
                    "Problem starting this camera",
                    "Problem starting camera",
                    MessageBoxButtons.OK);
            }
        }

        // ====
        private void ConnectUpCamera_button_Click(object sender, EventArgs e)
        {
            DisplayText("UpCam_comboBox.SelectedIndex= " + UpCam_comboBox.SelectedIndex.ToString());
            Setting.UpCam_index = UpCam_comboBox.SelectedIndex;
            List<string> Monikers = Machine.UpCamera.GetMonikerStrings();
            Setting.UpcamMoniker = Monikers[UpCam_comboBox.SelectedIndex];
            Machine.UpCamera.MonikerString = Monikers[UpCam_comboBox.SelectedIndex];
            Machine.UpCamera.DesiredX = Setting.UpCam_DesiredX;
            Machine.UpCamera.DesiredY = Setting.UpCam_DesiredY;
            SelectCamera(Machine.UpCamera);
            if (Machine.UpCamera.IsRunning())
            {
                SetCurrentCameraParameters();
            }
            else
            {
                ShowMessageBox(
                    "Problem starting this camera",
                    "Problem starting camera",
                    MessageBoxButtons.OK);
            }
        }

        // =================================================================================
        private void DownCameraDrawDashedCross_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCameraDrawDashedCross_checkBox.Checked)
            {
                Machine.DownCamera.DrawDashedCross = true;
            }
            else
            {
                Machine.DownCamera.DrawDashedCross = false;
            }
        }

        // ====
        private void UpCameraDrawDashedCross_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (UpCameraDrawDashedCross_checkBox.Checked)
            {
                Machine.UpCamera.DrawDashedCross = true;
            }
            else
            {
                Machine.UpCamera.DrawDashedCross = false;
            }
        }


        // =================================================================================
        private void DownCameraDrawTicks_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCameraDrawTicks_checkBox.Checked)
            {
                Machine.DownCamera.DrawSidemarks = true;
                Setting.DownCam_DrawTicks = true;
            }
            else
            {
                Machine.DownCamera.DrawSidemarks = false;
                Setting.DownCam_DrawTicks = false;
            }
        }

        private void DownCameraDrawGrid_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Machine.DownCamera.DrawGrid = DownCameraDrawGrid_checkBox.Checked;
        }
        // =================================================================================
        private void DownCameraDrawCross_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCameraDrawCross_checkBox.Checked)
            {
                Machine.DownCamera.DrawCross = true;
            }
            else
            {
                Machine.DownCamera.DrawCross = false;
            }
        }

        // ====
        private void UpCameraDrawCross_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (UpCameraDrawCross_checkBox.Checked)
            {
                Machine.UpCamera.DrawCross = true;
            }
            else
            {
                Machine.UpCamera.DrawCross = false;
            }
        }

        // =================================================================================
        private void DownCameraDrawBox_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCameraDrawBox_checkBox.Checked)
            {
                Machine.DownCamera.DrawBox = true;
            }
            else
            {
                Machine.DownCamera.DrawBox = false;
            }
        }

        // ====
        private void UpCameraDrawBox_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (UpCameraDrawBox_checkBox.Checked)
            {
                Machine.UpCamera.DrawBox = true;
            }
            else
            {
                Machine.UpCamera.DrawBox = false;
            }
        }

        // =================================================================================
        private void DownCameraBoxX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                DownCameraUpdateXmmPerPixel();
            }
        }

        private void DownCameraBoxX_textBox_Leave(object sender, EventArgs e)
        {
            DownCameraUpdateXmmPerPixel();
        }


        private void DownCameraUpdateXmmPerPixel()
        {
            double val;
            if (double.TryParse(DownCameraBoxX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.DownCam_XmmPerPixel = val / Machine.DownCamera.BoxSizeX;
                DownCameraBoxXmmPerPixel_label.Text = "(" + Setting.DownCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            }
        }

        // ====
        private void UpCameraBoxX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                UpCameraUpdateXmmPerPixel();
            }
        }

        private void UpCameraBoxX_textBox_Leave(object sender, EventArgs e)
        {
            UpCameraUpdateXmmPerPixel();
        }

        private void UpCameraUpdateXmmPerPixel()
        {
            double val;
            if (double.TryParse(UpCameraBoxX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.UpCam_XmmPerPixel = val / Machine.UpCamera.BoxSizeX;
                UpCameraBoxXmmPerPixel_label.Text = "(" + Setting.UpCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            }
        }

        // =================================================================================
        private void DownCameraBoxY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                DownCameraUpdateYmmPerPixel();
            }
        }

        private void DownCameraBoxY_textBox_Leave(object sender, EventArgs e)
        {
            DownCameraUpdateYmmPerPixel();
        }

        private void DownCameraUpdateYmmPerPixel()
        {
            double val;
            if (double.TryParse(DownCameraBoxY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.DownCam_YmmPerPixel = val / Machine.DownCamera.BoxSizeY;
                DownCameraBoxYmmPerPixel_label.Text = "(" + Setting.DownCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            }
        }

        // ====
        private void UpCameraBoxY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                UpCameraUpdateYmmPerPixel();
            }
        }

        private void UpCameraBoxY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpCameraBoxY_textBox.Text.Replace(',', '.'), out val))
            {
                UpCameraUpdateYmmPerPixel();
            }
        }

        private void UpCameraUpdateYmmPerPixel()
        {
            double val;
            if (double.TryParse(UpCameraBoxY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.UpCam_YmmPerPixel = val / Machine.UpCamera.BoxSizeY;
                UpCameraBoxYmmPerPixel_label.Text = "(" + Setting.UpCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            }
        }



        // =================================================================================
        private void DownCamZoom_checkBox_Click(object sender, EventArgs e)
        {
            if (DownCamZoom_checkBox.Checked)
            {
                Machine.DownCamera.Zoom = true;
                Setting.DownCam_Zoom = true;
            }
            else
            {
                Machine.DownCamera.Zoom = false;
                Setting.DownCam_Zoom = false;
            }
        }
        // ====
        private void UpCamZoom_checkBox_Click(object sender, EventArgs e)
        {
            if (UpCamZoom_checkBox.Checked)
            {
                Machine.UpCamera.Zoom = true;
                Setting.UpCam_Zoom = true;
            }
            else
            {
                Machine.UpCamera.Zoom = false;
                Setting.UpCam_Zoom = false;
            }
        }

        // =================================================================================
        private void DownCamZoomFactor_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(DownCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.DownCamera.ZoomFactor = val;
                    Setting.DownCam_Zoomfactor = val;
                }
            }
        }

        private void DownCamZoomFactor_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(DownCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
            {
                Machine.DownCamera.ZoomFactor = val;
                Setting.DownCam_Zoomfactor = val;
            }
        }

        // ====
        private void UpCamZoomFactor_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(UpCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.UpCamera.ZoomFactor = val;
                    Setting.UpCam_Zoomfactor = val;
                }
            }
        }

        private void UpCamZoomFactor_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpCamZoomFactor_textBox.Text.Replace(',', '.'), out val))
            {
                Machine.UpCamera.ZoomFactor = val;
                Setting.UpCam_Zoomfactor = val;
            }
        }


        // ==========================================================================================================

        private void DownCamFindCircles_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCamFindCircles_checkBox.Checked)
            {
                Machine.DownCamera.FindCircles = true;
            }
            else
            {
                Machine.DownCamera.FindCircles = false;
            }
        }

        private void UpCamFindCircles_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (UpCamFindCircles_checkBox.Checked)
            {
                Machine.UpCamera.FindCircles = true;
            }
            else
            {
                Machine.UpCamera.FindCircles = false;
            }
        }

        // =================================================================================
        private void DownCamFindRectangles_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCamFindRectangles_checkBox.Checked)
            {
                Machine.DownCamera.FindRectangles = true;
            }
            else
            {
                Machine.DownCamera.FindRectangles = false;
            }

        }

        // =================================================================================
        private void DownCam_FindComponents_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DownCam_FindComponents_checkBox.Checked)
            {
                Machine.DownCamera.FindComponent = true;
            }
            else
            {
                Machine.DownCamera.FindComponent = false;
            }
        }

        private void UpCam_FindComponents_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (UpCam_FindComponents_checkBox.Checked)
            {
                Machine.UpCamera.FindComponent = true;
            }
            else
            {
                Machine.UpCamera.FindComponent = false;
            }
        }

        // =================================================================================
        private void Overlay_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (Overlay_checkBox.Checked)
            {
                Machine.DownCamera.Draw_Snapshot = true;
            }
            else
            {
                Machine.DownCamera.Draw_Snapshot = false;
            }
        }

        private void UpCamOverlay_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (UpCamOverlay_checkBox.Checked)
            {
                Machine.UpCamera.Draw_Snapshot = true;
            }
            else
            {
                Machine.UpCamera.Draw_Snapshot = false;
            }
        }


        // =================================================================================
        // DownCam specific functions
        // =================================================================================

        // =================================================================================
        private void ImageTest_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ImageTest_checkBox.Checked)
            {
                Machine.DownCamera.TestAlgorithm = true;
            }
            else
            {
                Machine.DownCamera.TestAlgorithm = false;
            }
        }

        // =================================================================================
        private void JigX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(JigX_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_JigOffsetX = val;
                }
            }
        }

        private void JigX_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JigX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_JigOffsetX = val;
            }
        }

        // =================================================================================
        private void JigY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(JigY_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_JigOffsetY = val;
                }
            }
        }

        private void JigY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JigY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_JigOffsetY = val;
            }
        }

        // =================================================================================
        private async void GotoPCB0_button_Click(object sender, EventArgs e)
        {
            await Machine.Move.MoveXYSafeAsync(Setting.General_JigOffsetX, Setting.General_JigOffsetY);
        }

        // =================================================================================
        private void SetPCB0_button_Click(object sender, EventArgs e)
        {
            JigX_textBox.Text = Machine.Position.CurrentX.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_JigOffsetX = Machine.Position.CurrentX;
            JigY_textBox.Text = Machine.Position.CurrentY.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_JigOffsetY = Machine.Position.CurrentY;
        }



        // =================================================================================
        private void PickupCenterX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            PickupCenterX_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(PickupCenterX_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_PickupCenterX = val;
                    PickupCenterX_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void PickupCenterX_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(PickupCenterX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_PickupCenterX = val;
                PickupCenterX_textBox.ForeColor = Color.Black;
            }
        }

        // =================================================================================
        private void PickupCenterY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            PickupCenterY_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(PickupCenterY_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_PickupCenterY = val;
                    PickupCenterY_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void PickupCenterY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(PickupCenterY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_PickupCenterY = val;
                PickupCenterY_textBox.ForeColor = Color.Black;
            }
        }

        // =================================================================================
        private async void GotoPickupCenter_button_Click(object sender, EventArgs e)
        {
            await Machine.Move.MoveXYSafeAsync(Setting.General_PickupCenterX, Setting.General_PickupCenterY);
        }

        // =================================================================================
        private void SetPickupCenter_button_Click(object sender, EventArgs e)
        {
            PickupCenterX_textBox.Text = Machine.Position.CurrentX.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_PickupCenterX = Machine.Position.CurrentX;
            PickupCenterY_textBox.Text = Machine.Position.CurrentY.ToString("0.00", CultureInfo.InvariantCulture);
            Setting.General_PickupCenterY = Machine.Position.CurrentY;
            PickupCenterX_textBox.ForeColor = Color.Black;
            PickupCenterY_textBox.ForeColor = Color.Black;
        }

        // =================================================================================
        // Nozzle calibration

        private static int SetNozzleOffset_stage = 0;
        private static double NozzleOffsetMarkX = 0.0;
        private static double NozzleOffsetMarkY = 0.0;

        private async void ZDown_button_Click(object sender, EventArgs e)
        {
            Machine.Move.ZGuardOff();
            await Machine.Move.MoveZSafeAsync(Setting.General_ZtoPCB);
        }

        private async void ZUp_button_Click(object sender, EventArgs e)
        {
            Machine.Move.ZGuardOn();
            await Machine.Move.MoveZSafeAsync(0);
        }

        private void NozzleOffsetX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(NozzleOffsetX_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.DownCam_NozzleOffsetX = val;
                }
            }
        }

        private void NozzleOffsetX_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleOffsetX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.DownCam_NozzleOffsetX = val;
            }
        }

        private void NozzleOffsetY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(NozzleOffsetY_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.DownCam_NozzleOffsetY = val;
                }
            }
        }

        private void NozzleOffsetY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleOffsetY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.DownCam_NozzleOffsetY = val;
            }
        }


        private async void Offset2Method_button_Click(object sender, EventArgs e)
        {
            // Nozzle calibration button
            Machine.Move.ZGuardOff();
            SelectCamera(Machine.DownCamera);
            SetCurrentCameraParameters();
            switch (SetNozzleOffset_stage)
            {
                case 0:
                    SetNozzleOffset_stage = 1;
                    Offset2Method_button.Text = "Next";
                    await Machine.Move.MoveASafeAsync(0.0);
                    NozzleOffset_label.Visible = true;
                    NozzleOffset_label.Text = "Jog Nozzle to a point on a PCB, then click \"Next\"";
                    break;

                case 1:
                    SetNozzleOffset_stage = 2;
                    NozzleOffsetMarkX = Machine.Position.CurrentX;
                    NozzleOffsetMarkY = Machine.Position.CurrentY;
                    await Machine.Move.MoveZSafeAsync(0);
                    await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX - 75.0, Machine.Position.CurrentY - 29.0);
                    Machine.DownCamera.DrawCross = true;
                    NozzleOffset_label.Text = "Jog camera above the same point, \n\rthen click \"Next\"";
                    break;

                case 2:
                    SetNozzleOffset_stage = 0;
                    Setting.DownCam_NozzleOffsetX = NozzleOffsetMarkX - Machine.Position.CurrentX;
                    Setting.DownCam_NozzleOffsetY = NozzleOffsetMarkY - Machine.Position.CurrentY;
                    NozzleOffsetX_textBox.Text = Setting.DownCam_NozzleOffsetX.ToString("0.00", CultureInfo.InvariantCulture);
                    NozzleOffsetY_textBox.Text = Setting.DownCam_NozzleOffsetY.ToString("0.00", CultureInfo.InvariantCulture);
                    NozzleOffset_label.Visible = false;
                    NozzleOffset_label.Text = "   ";
                    ShowMessageBox(
                        "Now, jog the Nozzle above the up camera,\n\rtake Nozzle down, jog it to the image center\n\rand set Up ICamera location",
                        "Done here",
                        MessageBoxButtons.OK);
                    SelectCamera(Machine.UpCamera);
                    // SetNozzleMeasurement();
                    Offset2Method_button.Text = "Start";
                    await Machine.Move.MoveZSafeAsync(0.0);
                    Machine.Move.ZGuardOn();
                    break;
            }
        }


        // =================================================================================
        // UpCam specific functions
        // =================================================================================

        private void UpcamPositionX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                double val;
                if (double.TryParse(UpcamPositionX_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.UpCam_PositionX = val;
                }
            }
        }

        private void UpcamPositionX_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpcamPositionX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.UpCam_PositionX = val;
            }
        }

        private void UpcamPositionY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                double val;
                if (double.TryParse(UpcamPositionY_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.UpCam_PositionY = val;
                }
            }
        }

        private void UpcamPositionY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(UpcamPositionY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.UpCam_PositionY = val;
            }
        }

        private void SetUpCamPosition_button_Click(object sender, EventArgs e)
        {
            UpcamPositionX_textBox.Text = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Setting.UpCam_PositionX = Machine.Position.CurrentX;
            UpcamPositionY_textBox.Text = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Setting.UpCam_PositionY = Machine.Position.CurrentY;
            DisplayText("True position (with Nozzle offset):");
            DisplayText("X: " + (Machine.Position.CurrentX - Setting.DownCam_NozzleOffsetX).ToString());
            DisplayText("Y: " + (Machine.Position.CurrentY - Setting.DownCam_NozzleOffsetY).ToString());
        }

        private async void GotoUpCamPosition_button_Click(object sender, EventArgs e)
        {
            await Machine.Move.MoveXYSafeAsync(Setting.UpCam_PositionX, Setting.UpCam_PositionY);
        }

        #endregion Up/Down ICamera setup pages functions

        // =================================================================================
        // Basic setup page functions
        // =================================================================================
        #region Basic setup page functions

        private void BasicSetupTab_Begin()
        {
            // SetDownCameraDefaults();

            //Machine.UpCamera.Active = false;
            //Machine.DownCamera.Active = false;

            UpdateCncConnectionStatus();
            SizeXMax_textBox.Text = Setting.General_MachineSizeX.ToString();
            SizeYMax_textBox.Text = Setting.General_MachineSizeY.ToString();

            ParkLocationX_textBox.Text = Setting.General_ParkX.ToString();
            ParkLocationY_textBox.Text = Setting.General_ParkY.ToString();
            SquareCorrection_textBox.Text = Setting.CNC_SquareCorrection.ToString();
            VacuumTime_textBox.Text = Setting.General_PickupVacuumTime.ToString();
            VacuumRelease_textBox.Text = Setting.General_PickupReleaseTime.ToString();
            SmallMovement_numericUpDown.Value = Setting.CNC_SmallMovementSpeed;

            CtlrJogSpeed_numericUpDown.Value = Setting.CNC_CtlrJogSpeed;
            NormalJogSpeed_numericUpDown.Value = Setting.CNC_NormalJogSpeed;
            AltJogSpeed_numericUpDown.Value = Setting.CNC_AltJogSpeed;

            // Does this machine have any ports? (Maybe not, if TinyG is powered down.)
            RefreshPortList();
            if (comboBoxSerialPorts.Items.Count == 0)
            {
                return;
            };

            // At least there are some ports. Show the default port, if it is still there:
            bool found = false;
            int i = 0;
            foreach (var item in comboBoxSerialPorts.Items)
            {
                if (item.ToString() == Setting.CNC_SerialPort)
                {
                    found = true;
                    comboBoxSerialPorts.SelectedIndex = i;
                    break;
                }
                i++;
            }
            if (found)
            {
                // Yes, the default port is still there, show it
                comboBoxSerialPorts.SelectedIndex = i;
            }
            else
            {
                // show the first available port
                comboBoxSerialPorts.SelectedIndex = 0;
                return;
            }
        }

        private void BasicSetupTab_End()
        {
            Machine.Move.ZGuardOn();
        }

        private void buttonRefreshPortList_Click(object sender, EventArgs e)
        {
            RefreshPortList();
        }

        private void RefreshPortList()
        {
            comboBoxSerialPorts.Items.Clear();
            foreach (string s in SerialPort.GetPortNames())
            {
                comboBoxSerialPorts.Items.Add(s);
            }
            if (comboBoxSerialPorts.Items.Count == 0)
            {
                labelSerialPortStatus.Text = "No serial ports found. Is TinyG powered on?";
            }
            else
            {
                // show the first available port
                comboBoxSerialPorts.SelectedIndex = 0;
                return;
            }
        }


        public void UpdateCncConnectionStatus()
        {
            if (InvokeRequired) { Invoke(new Action(UpdateCncConnectionStatus)); return; }

            if (Machine.Move.ErrorState)
            {
                buttonConnectSerial.Text = "Clear Err.";
                labelSerialPortStatus.Text = "ERROR";
                labelSerialPortStatus.ForeColor = Color.Red;
                ValidMeasurement_checkBox.Checked = false;
                PositionConfidence = false;
            }
            else if (Machine.IsConnected)
            {
                buttonConnectSerial.Text = "Close";
                labelSerialPortStatus.Text = "Connected";
                labelSerialPortStatus.ForeColor = Color.Black;
            }
            else
            {
                PositionConfidence = false;
                buttonConnectSerial.Text = "Connect";
                labelSerialPortStatus.Text = "Not connected";
                labelSerialPortStatus.ForeColor = Color.Red;
                ValidMeasurement_checkBox.Checked = false;
            }
        }

        private async void buttonConnectSerial_Click(object sender, EventArgs e)
        {
            try
            {
                if (comboBoxSerialPorts.SelectedItem == null)
                {
                    return;  // no ports
                };

                if (Machine.Move.ErrorState || !Machine.IsConnected)
                {
                    if (Setting.CNC_SerialPort != comboBoxSerialPorts.SelectedItem.ToString())
                    {
                        // user changed the selection
                        buttonConnectSerial.Text = "Closing..";
                        Machine.CommsProcessor.Disconnect();
                        // 0.5 s delay for the system to clear buffers etc
                        for (int i = 0; i < 250; i++)
                        {
                            Thread.Sleep(2);
                            Application.DoEvents();
                        }
                    }
                    // reconnect, attempt to clear the error
                    try
                    {
                        Machine.CommsProcessor.Connect(comboBoxSerialPorts.SelectedItem.ToString());
                        buttonConnectSerial.Text = "Connecting..";
                        Machine.Move.ErrorState = false;
                        Setting.CNC_SerialPort = comboBoxSerialPorts.SelectedItem.ToString();
                        UpdateCncConnectionStatus();
                        if (await ControlBoardJustConnectedAsync())
                        {
                            await OfferHomingAsync();
                        }
                    }
                    catch
                    {
                        //Cnc.CncError();
                    }
                }
                else
                {
                    // Close connection
                    buttonConnectSerial.Text = "Closing..";
                    Machine.CommsProcessor.Disconnect();

                    for (int i = 0; i < 250; i++)
                    {
                        Thread.Sleep(2);
                        Application.DoEvents();
                    }
                    UpdateCncConnectionStatus();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex.Message);
            }
        }


        // =================================================================================
        // Logging textbox

        Color DisplayTxtCol = Color.Black;

        public void DisplayText(string txt, KnownColor col = KnownColor.White, bool force = false)
        {
            if (DisableLog_checkBox.Checked && !force)
            {
                return;
            }
            DisplayTxtCol = Color.FromKnownColor(col);
            DisplayTxt(txt);
        }

        public void DisplayTxt(string txt)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    DisplayTxt(txt);

                    if (AppLogger != null)
                    {
                        //AppLogger.Info(txt);
                    }
                }));

                return;
            }

            txt = txt.Replace("\n", "");
            txt = txt.Replace("\r", "");
            // TinyG sends \n, textbox needs \r\n. (TinyG could be set to send \n\r, which does not work with textbox.)
            // Adding end of line here saves typing elsewhere
            txt = txt + "\r\n";

            if (SerialMonitor_richTextBox.Text.Length > 1000000)
            {
                SerialMonitor_richTextBox.Text = SerialMonitor_richTextBox.Text.Substring(SerialMonitor_richTextBox.Text.Length - 10000);
            }
            SerialMonitor_richTextBox.AppendText(txt, DisplayTxtCol);

            if (AppLogger != null)
            {
                //AppLogger.Info(txt);
            }
        }

        
        /*
        public void DisplayTxt(string txt)
        {
            try
            {
                txt = txt.Replace("\n", "");
                txt = txt.Replace("\r", "");
                // TinyG sends \n, textbox needs \r\n. (TinyG could be set to send \n\r, which does not work with textbox.)
                // Adding end of line here saves typing elsewhere
                txt = txt + "\r\n";
                if (SerialMonitor_richTextBox.Text.Length > 1000000)
                {
                    SerialMonitor_richTextBox.Text = SerialMonitor_richTextBox.Text.Substring(SerialMonitor_richTextBox.Text.Length - 10000);
                }
                SerialMonitor_richTextBox.AppendText("**" + linecount.ToString() + AppCol.ToString() + "\r\n", Color.DarkViolet);
                SerialMonitor_richTextBox.ScrollToCaret();
                SerialMonitor_richTextBox.AppendText(txt, AppCol);
                SerialMonitor_richTextBox.ScrollToCaret();
                SerialMonitor_richTextBox.AppendText("**" + linecount++.ToString() + "\r\n");
                SerialMonitor_richTextBox.ScrollToCaret();
            }
            catch
            {
            }
        }
        */

        private void textBoxSendtoTinyG_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                //Cnc.ForceWrite(textBoxSendtoTinyG.Text);
                textBoxSendtoTinyG.Clear();
            }
        }

        // Sends the calls that will result to messages that update the values shown on UI
        private bool LoopParameters(Type type)
        {
            return (Task.Run(() => LoopParametersAsync(type)).GetAwaiter().GetResult());
        }

        private async Task<bool> LoopParametersAsync(Type type)
        {
            foreach (var parameter in type.GetFields())
            {
                // The motor parameters are <motor number><parameter>, such as 1ma, 1sa, 1tr etc.
                // These are not valid parameter names, so motor1ma, motor1sa etc are used.
                // to retrieve the values, we remove the "motor"
                string Name = parameter.Name;
                if (Name.StartsWith("motor"))
                {
                    Name = Name.Substring(5);
                }

                try
                {
                    await Machine.CommsProcessor.SendCommandWaitResponseAsync("{\"" + Name + "\":\"\"}");
                }
                catch
                {
                    return false;
                }
                //Thread.Sleep(500);
            }
            return true;
        }

        private bool ControlBoardJustConnected()
        {
            return (Task.Run(() => ControlBoardJustConnectedAsync()).GetAwaiter().GetResult());
        }

        private async Task<bool> ControlBoardJustConnectedAsync()
        {
            // Called when control board conenction is estabished.
            // First, find out the board type. Then, 
            // for TinyG boards, read the parameter values stored on board.
            // For qQuintic boards that dont' have on-board storage, write the values.

            Thread.Sleep(200); // Give TinyG time to wake up
            await UpdateCNCBoardType_mAsync();

            Machine.CommsProcessor.SendCommand("\x11");  // Xon
            Thread.Sleep(50);   // TinyG wakeup

            // initial position
            try
            {
                await Machine.CommsProcessor.SendCommandWaitResponseAsync("{sr:n}");
            }
            catch
            {
                return false;
            }

            DisplayText("Reading TinyG settings:");
            if (!await LoopParametersAsync(typeof(BoardSettings.TinyG)))
            {
                return false;
            }

            // Do settings that need to be done always
            //Cnc.IgnoreError = true;
            Machine.Move.ProbingMode(false);
            Machine.Pump.PumpDefaultSetting();
            Machine.Vacuum.VacuumDefaultSetting();
            //Thread.Sleep(100);
            //Vacuum_checkBox.Checked = true;
            //Cnc.IgnoreError = false;
            await Machine.CommsProcessor.SendCommandWaitResponseAsync("{\"me\":\"\"}");  // motor power on
            MotorPower_checkBox.Checked = true;
            return true;
        }


        private void RegisterForParameterUpdates()
        {
            var tinyGSystemParameters = DIBindings.Resolve<ITinyGSystemParameters>();
            var tinyGCalibrations = DIBindings.Resolve<ITinyGCalibrations>();
            var tinyGVariables = DIBindings.Resolve<ITinyGVariables>();

            Machine.CommsProcessor.RegisterDataReceived(tinyGSystemParameters.hp, (x) => { UpdateControlThreadSafe(Update_hp, x); });

            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.mt, (x) => { UpdateControlThreadSafe(Update_mt, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor1sa, (x) => { UpdateControlThreadSafe(Update_1sa, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor1tr, (x) => { UpdateControlThreadSafe(Update_1tr, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor1mi, (x) => { UpdateControlThreadSafe(Update_1mi, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor2sa, (x) => { UpdateControlThreadSafe(Update_2sa, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor2tr, (x) => { UpdateControlThreadSafe(Update_2tr, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor2mi, (x) => { UpdateControlThreadSafe(Update_2mi, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor3sa, (x) => { UpdateControlThreadSafe(Update_3sa, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor3tr, (x) => { UpdateControlThreadSafe(Update_3tr, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor3mi, (x) => { UpdateControlThreadSafe(Update_3mi, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor4sa, (x) => { UpdateControlThreadSafe(Update_4sa, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor4tr, (x) => { UpdateControlThreadSafe(Update_4tr, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.motor4mi, (x) => { UpdateControlThreadSafe(Update_4mi, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.xvm, (x) => { UpdateControlThreadSafe(Update_xvm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.xjm, (x) => { UpdateControlThreadSafe(Update_xjm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.xjh, (x) => { UpdateControlThreadSafe(Update_xjh, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.xsv, (x) => { UpdateControlThreadSafe(Update_xsv, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.yvm, (x) => { UpdateControlThreadSafe(Update_yvm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.yjm, (x) => { UpdateControlThreadSafe(Update_yjm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.yjh, (x) => { UpdateControlThreadSafe(Update_yjh, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.ysv, (x) => { UpdateControlThreadSafe(Update_ysv, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.zvm, (x) => { UpdateControlThreadSafe(Update_zvm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.zjm, (x) => { UpdateControlThreadSafe(Update_zjm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.zjh, (x) => { UpdateControlThreadSafe(Update_zjh, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.zsv, (x) => { UpdateControlThreadSafe(Update_zsv, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.avm, (x) => { UpdateControlThreadSafe(Update_avm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.ajm, (x) => { UpdateControlThreadSafe(Update_ajm, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.xsn, (x) => { UpdateControlThreadSafe(Update_xsn, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.xsx, (x) => { UpdateControlThreadSafe(Update_xsx, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.ysn, (x) => { UpdateControlThreadSafe(Update_ysn, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.ysx, (x) => { UpdateControlThreadSafe(Update_ysx, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.zsn, (x) => { UpdateControlThreadSafe(Update_zsn, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGCalibrations.zsx, (x) => { UpdateControlThreadSafe(Update_zsx, x); });

            Machine.CommsProcessor.RegisterDataReceived(tinyGVariables.posx, (x) => { UpdateControlThreadSafe(Update_xpos, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGVariables.posy, (x) => { UpdateControlThreadSafe(Update_ypos, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGVariables.posz, (x) => { UpdateControlThreadSafe(Update_zpos, x); });
            Machine.CommsProcessor.RegisterDataReceived(tinyGVariables.posa, (x) => { UpdateControlThreadSafe(Update_apos, x); });
        }

        private void UpdateControlThreadSafe(Action<string> action, string value)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    action(value);
                }));
            }
            else
            {
                action(value);
            }
        }

        // Called from CNC class when UI need updating
        public void ValueUpdater(string item, string value)
        {
            //if (InvokeRequired) { Invoke(new Action<string, string>(ValueUpdater), new[] { item, value }); return; }

            if (InvokeRequired) 
            { 
                BeginInvoke(new Action<string, string>(ValueUpdater), new[] { item, value }); 
                return; 
            }

            var tinyG = DIBindings.Resolve<ITinyGCalibrations>();
            var settings = new Settings();
            //settings.RegisterParamUpdate(tinyG.mt, Update_mt);


            // DisplayText("ValueUpdater: item= " + item + ", value= " + value);

            switch (item)
            {
                // ==========  position values  ==========
                case "posx":
                    Update_xpos(value);
                    break;
                case "posy":
                    Update_ypos(value);
                    break;
                case "posz":
                    Update_zpos(value);
                    break;
                case "posa":
                    Update_apos(value);
                    break;

                // ==========  System values  ==========
                case "st":     // switch type, [0=NO,1=NC]
                    break;
                case "mt":   // motor idle timeout, in seconds
                    Update_mt(value);
                    break;
                case "jv":     // json verbosity, [0=silent,1=footer,2=messages,3=configs,4=linenum,5=verbose]
                    break;
                case "js":     // json serialize style [0=relaxed,1=strict]
                    break;
                case "tv":     // text verbosity [0=silent,1=verbose]
                    break;
                case "qv":     // queue report verbosity [0=off,1=single,2=triple]
                    break;
                case "sv":     // status report verbosity [0=off,1=filtered,2=verbose
                    break;
                case "si":   // status interval, in ms
                    break;
                case "gun":    // default gcode units mode [0=G20,1=G21] (1=mm)
                    break;

                // ========== motor 1 ==========
                case "1ma":        // map to axis [0=X,1=Y,2=Z...]
                    TinyGBoard.motor1ma = value;
                    qQuinticBoard.motor1ma = value;
                    break;
                case "1sa":    // step angle, deg
                    TinyGBoard.motor1sa = value;
                    qQuinticBoard.motor1sa = value;
                    Update_1sa(value);
                    break;
                case "1tr":  // travel per revolution, mm
                    TinyGBoard.motor1tr = value;
                    qQuinticBoard.motor1tr = value;
                    Update_1tr(value);
                    break;
                case "1mi":        // microsteps [1,2,4,8], qQuintic [1,2,4,8,16,32]
                    TinyGBoard.motor1mi = value;
                    qQuinticBoard.motor1mi = value;
                    Update_1mi(value);
                    break;
                case "1po":        // motor polarity [0=normal,1=reverse]
                    TinyGBoard.motor1po = value;
                    qQuinticBoard.motor1po = value;
                    break;
                case "1pm":        // power management [0=disabled,1=always on,2=in cycle,3=when moving]
                    TinyGBoard.motor1pm = value;
                    qQuinticBoard.motor1pm = value;
                    break;
                case "1pl":    // motor power level [0.000=minimum, 1.000=maximum]
                    qQuinticBoard.motor1pl = value;
                    break;

                // ========== motor 2 ==========
                case "2ma":        // map to axis [0=X,1=Y,2=Z...]
                    TinyGBoard.motor2ma = value;
                    qQuinticBoard.motor2ma = value;
                    break;
                case "2sa":    // step angle, deg
                    TinyGBoard.motor2sa = value;
                    qQuinticBoard.motor2sa = value;
                    Update_2sa(value);
                    break;
                case "2tr":  // travel per revolution, mm
                    TinyGBoard.motor2tr = value;
                    qQuinticBoard.motor2tr = value;
                    Update_2tr(value);
                    break;
                case "2mi":        // microsteps [1,2,4,8], qQuintic [1,2,4,8,16,32]
                    TinyGBoard.motor2mi = value;
                    qQuinticBoard.motor2mi = value;
                    Update_2mi(value);
                    break;
                case "2po":        // motor polarity [0=normal,1=reverse]
                    TinyGBoard.motor2po = value;
                    qQuinticBoard.motor2po = value;
                    break;
                case "2pm":        // power management [0=disabled,1=always on,2=in cycle,3=when moving]
                    TinyGBoard.motor2pm = value;
                    qQuinticBoard.motor2pm = value;
                    break;
                case "2pl":    // motor power level [0.000=minimum, 1.000=maximum]
                    qQuinticBoard.motor2pl = value;
                    break;

                // ========== motor 3 ==========
                case "3ma":        // map to axis [0=X,1=Y,2=Z...]
                    TinyGBoard.motor3ma = value;
                    qQuinticBoard.motor3ma = value;
                    break;
                case "3sa":    // step angle, deg
                    TinyGBoard.motor3sa = value;
                    qQuinticBoard.motor3sa = value;
                    Update_3sa(value);
                    break;
                case "3tr":  // travel per revolution, mm
                    TinyGBoard.motor3tr = value;
                    qQuinticBoard.motor3tr = value;
                    Update_3tr(value);
                    break;
                case "3mi":        // microsteps [1,2,4,8], qQuintic [1,2,4,8,16,32]
                    TinyGBoard.motor3mi = value;
                    qQuinticBoard.motor3mi = value;
                    Update_3mi(value);
                    break;
                case "3po":        // motor polarity [0=normal,1=reverse]
                    TinyGBoard.motor3po = value;
                    qQuinticBoard.motor3po = value;
                    break;
                case "3pm":        // power management [0=disabled,1=always on,2=in cycle,3=when moving]
                    TinyGBoard.motor3pm = value;
                    qQuinticBoard.motor3pm = value;
                    break;
                case "3pl":    // motor power level [0.000=minimum, 1.000=maximum]
                    qQuinticBoard.motor3pl = value;
                    break;

                // ========== motor 4 ==========
                case "4ma":        // map to axis [0=X,1=Y,2=Z...]
                    TinyGBoard.motor4ma = value;
                    qQuinticBoard.motor4ma = value;
                    break;
                case "4sa":    // step angle, deg
                    TinyGBoard.motor4sa = value;
                    qQuinticBoard.motor4sa = value;
                    Update_4sa(value);
                    break;
                case "4tr":  // travel per revolution, mm
                    TinyGBoard.motor4tr = value;
                    qQuinticBoard.motor4tr = value;
                    Update_4tr(value);
                    break;
                case "4mi":        // microsteps [1,2,4,8], qQuintic [1,2,4,8,16,32]
                    TinyGBoard.motor4mi = value;
                    qQuinticBoard.motor4mi = value;
                    Update_4mi(value);
                    break;
                case "4po":        // motor polarity [0=normal,1=reverse]
                    TinyGBoard.motor4po = value;
                    qQuinticBoard.motor4po = value;
                    break;
                case "4pm":        // power management [0=disabled,1=always on,2=in cycle,3=when moving]
                    TinyGBoard.motor4pm = value;
                    qQuinticBoard.motor4pm = value;
                    break;
                case "4pl":    // motor power level [0.000=minimum, 1.000=maximum]
                    qQuinticBoard.motor4pl = value;
                    break;

                // ========== motor 5 (qQuintic only) ==========
                case "5ma":
                    qQuinticBoard.motor5ma = value;
                    break;
                case "5pm":        // power management [0=disabled,1=always on,2=in cycle,3=when moving]
                    qQuinticBoard.motor5pm = value;
                    break;
                case "5pl":    // motor power level [0.000=minimum, 1.000=maximum]
                    qQuinticBoard.motor5pl = value;
                    break;

                // ========== X axis ==========
                case "xam":        // x axis mode, 1=standard
                    TinyGBoard.xam = value;
                    qQuinticBoard.xam = value;
                    break;
                case "xvm":    // x velocity maximum, mm/min
                    Update_xvm(value);
                    TinyGBoard.xvm = value;
                    qQuinticBoard.xvm = value;
                    break;
                case "xfr":    // x feedrate maximum, mm/mi
                    TinyGBoard.xfr = value;
                    qQuinticBoard.xfr = value;
                    break;
                case "xtn":        // x travel minimum, mm
                    TinyGBoard.xtn = value;
                    qQuinticBoard.xtn = value;
                    break;
                case "xtm":      // x travel maximum, mm
                    TinyGBoard.xtm = value;
                    qQuinticBoard.xtm = value;
                    break;
                case "xjm":     // x jerk maximum, mm/min^3 * 1 million
                    TinyGBoard.xjm = value;
                    qQuinticBoard.xjm = value;
                    Update_xjm(value);
                    break;
                case "xjh":     // x jerk homing, mm/min^3 * 1 million
                    TinyGBoard.xjh = value;
                    qQuinticBoard.xjh = value;
                    Update_xjh(value);
                    break;
                case "xsv":     // x search velocity, mm/min
                    TinyGBoard.xsv = value;
                    qQuinticBoard.xsv = value;
                    Update_xsv(value);
                    break;
                case "xlv":      // x latch velocity, mm/min
                    TinyGBoard.xlv = value;
                    qQuinticBoard.xlv = value;
                    break;
                case "xlb":        // x latch backoff, mm
                    TinyGBoard.xlb = value;
                    qQuinticBoard.xlb = value;
                    break;
                case "xzb":        // x zero backoff, mm
                    TinyGBoard.xzb = value;
                    qQuinticBoard.xzb = value;
                    break;

                // ========== Y axis ==========
                case "yam":        // y axis mode, 1=standard
                    TinyGBoard.yam = value;
                    qQuinticBoard.yam = value;
                    break;
                case "yvm":    // y velocity maximum, mm/min
                    Update_yvm(value);
                    TinyGBoard.yvm = value;
                    qQuinticBoard.yvm = value;
                    break;
                case "yfr":    // y feedrate maximum, mm/min
                    TinyGBoard.yfr = value;
                    qQuinticBoard.yfr = value;
                    break;
                case "ytn":        // y travel minimum, mm
                    TinyGBoard.ytn = value;
                    qQuinticBoard.ytn = value;
                    break;
                case "ytm":      // y travel mayimum, mm
                    TinyGBoard.ytm = value;
                    qQuinticBoard.ytm = value;
                    break;
                case "yjm":     // y jerk maximum, mm/min^3 * 1 million
                    TinyGBoard.yjm = value;
                    qQuinticBoard.yjm = value;
                    Update_yjm(value);
                    break;
                case "yjh":     // y jerk homing, mm/min^3 * 1 million
                    TinyGBoard.yjh = value;
                    qQuinticBoard.yjh = value;
                    Update_yjh(value);
                    break;
                case "ysv":     // y search velocity, mm/min
                    TinyGBoard.ysv = value;
                    qQuinticBoard.ysv = value;
                    Update_ysv(value);
                    break;
                case "ylv":      // y latch velocity, mm/min
                    TinyGBoard.ylv = value;
                    qQuinticBoard.ylv = value;
                    break;
                case "ylb":        // y latch backoff, mm
                    TinyGBoard.ylb = value;
                    qQuinticBoard.ylb = value;
                    break;
                case "yzb":        // y zero backoff, mm
                    TinyGBoard.yzb = value;
                    qQuinticBoard.yzb = value;
                    break;

                // ========== Z axis ==========
                case "zam":        // z axis mode, 1=standard
                    TinyGBoard.zam = value;
                    qQuinticBoard.zam = value;
                    break;
                case "zvm":     // z velocity maximum, mm/min
                    Update_zvm(value);
                    TinyGBoard.zvm = value;
                    qQuinticBoard.zvm = value;
                    break;
                case "zfr":     // z feedrate maximum, mm/min
                    TinyGBoard.zfr = value;
                    qQuinticBoard.zfr = value;
                    break;
                case "ztn":        // z travel minimum, mm
                    TinyGBoard.ztn = value;
                    qQuinticBoard.ztn = value;
                    break;
                case "ztm":       // z travel mazimum, mm
                    TinyGBoard.ztm = value;
                    qQuinticBoard.ztm = value;
                    break;
                case "zjm":      // z jerk mazimum, mm/min^3 * 1 million
                    TinyGBoard.zjm = value;
                    qQuinticBoard.zjm = value;
                    Update_zjm(value);
                    break;
                case "zjh":      // z jerk homing, mm/min^3 * 1 million
                    TinyGBoard.zjh = value;
                    qQuinticBoard.zjh = value;
                    Update_zjh(value);
                    break;
                case "zsv":     // z search velocity, mm/min
                    TinyGBoard.zsv = value;
                    qQuinticBoard.zsv = value;
                    Update_zsv(value);
                    break;
                case "zlv":      // z latch velocity, mm/min
                    TinyGBoard.zlv = value;
                    qQuinticBoard.zlv = value;
                    break;
                case "zlb":        // z latch backoff, mm
                    TinyGBoard.zlb = value;
                    qQuinticBoard.zlb = value;
                    break;
                case "zzb":        // z zero backoff, mm
                    TinyGBoard.zzb = value;
                    qQuinticBoard.zzb = value;
                    break;

                // ========== A axis ==========
                case "aam":        // a axis mode, 1=standard
                    TinyGBoard.aam = value;
                    qQuinticBoard.aam = value;
                    break;
                case "avm":    // a velocity maximum, mm/min
                    TinyGBoard.avm = value;
                    qQuinticBoard.avm = value;
                    Update_avm(value);
                    break;
                case "afr":   // a feedrate maximum, mm/min
                    TinyGBoard.afr = value;
                    qQuinticBoard.afr = value;
                    break;
                case "atn":        // a travel minimum, mm
                    TinyGBoard.atn = value;
                    qQuinticBoard.atn = value;
                    break;
                case "atm":      // a travel maximum, mm
                    TinyGBoard.atm = value;
                    qQuinticBoard.atm = value;
                    break;
                case "ajm":     // a jerk maximum, mm/min^3 * 1 million
                    TinyGBoard.ajm = value;
                    qQuinticBoard.ajm = value;
                    Update_ajm(value);
                    break;
                case "ajh":     // a jerk homing, mm/min^3 * 1 million
                    TinyGBoard.ajh = value;
                    qQuinticBoard.ajh = value;
                    break;
                case "asv":     // a search velocity, mm/min
                    TinyGBoard.asv = value;
                    qQuinticBoard.asv = value;
                    break;


                // ========== TinyG switch values ==========
                case "xsn":   // x switch min [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.xsn = value;
                    Update_xsn(value);
                    break;
                case "xsx":   // x switch max [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.xsx = value;
                    Update_xsx(value);
                    break;
                case "ysn":   // y switch min [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.ysn = value;
                    Update_ysn(value);
                    break;
                case "ysx":   // y switch max [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.ysx = value;
                    Update_ysx(value);
                    break;
                case "zsn":   // z switch min [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.zsn = value;
                    Update_zsn(value);
                    break;
                case "zsx":   // z switch max [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.zsx = value;
                    Update_zsx(value);
                    break;
                case "asn":   // a switch min [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.asn = value;
                    break;
                case "asx":   // a switch max [0=off,1=homing,2=limit,3=limit+homing];
                    TinyGBoard.asx = value;
                    break;

                // ========== qQuintic switch values ==========
                case "xhi":     // x homing input [input 1-N or 0 to disable homing this axis]
                    qQuinticBoard.xhi = value;
                    break;
                case "xhd":     // x homing direction [0=search-to-negative, 1=search-to-positive]
                    qQuinticBoard.xhd = value;
                    break;
                case "yhi":     // x homing input [input 1-N or 0 to disable homing this axis]
                    qQuinticBoard.yhi = value;
                    break;
                case "yhd":     // x homing direction [0=search-to-negative, 1=search-to-positive]
                    qQuinticBoard.yhd = value;
                    break;
                case "zhi":     // x homing input [input 1-N or 0 to disable homing this axis]
                    qQuinticBoard.zhi = value;
                    break;
                case "zhd":     // x homing direction [0=search-to-negative, 1=search-to-positive]
                    qQuinticBoard.zhd = value;
                    break;
                case "ahi":     // x homing input [input 1-N or 0 to disable homing this axis]
                    qQuinticBoard.ahi = value;
                    break;
                case "bhi":     // x homing input [input 1-N or 0 to disable homing this axis]
                    qQuinticBoard.bhi = value;
                    break;

                // Hardware platform
                case "hp":
                    Update_hp(value);
                    break;

                default:
                    break;
            }
        }

        // =========================================================================
        // Thread-safe update functions and value setting fuctions
        // =========================================================================
        #region hp  // hardware platform

        private void Update_hp(string value)
        {
            if (value=="1")
            {
                //Cnc.Controlboard = ControlBoardType.TinyG;
                DisplayText("TinyG board found.");
            }
            else if (value == "3")
            {
                //Cnc.Controlboard = ControlBoardType.qQuintic;
                DisplayText("qQuintic board found.");
            }
            else
            {
                //Cnc.Controlboard = ControlBoardType.other;
                DisplayText("Unknown control board.");
            }
        }

        #endregion

        // =========================================================================
        #region jm  // *jm: jerk maximum
        // *jm update
        private void Update_xjm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_xjm), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            xjm_maskedTextBox.Text = val.ToString();
        }

        private void Update_yjm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_yjm), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            yjm_maskedTextBox.Text = val.ToString();
        }

        private void Update_zjm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_zjm), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            zjm_maskedTextBox.Text = val.ToString();
        }

        private void Update_ajm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_ajm), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            ajm_maskedTextBox.Text = val.ToString();
        }

        // =========================================================================
        // *jm setting
        private void xjm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            xjm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {

#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"xjm\":" + xjm_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"xjm\":" + xjm_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                xjm_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void yjm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            yjm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {

#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"yjm\":" + yjm_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"yjm\":" + yjm_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                yjm_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void zjm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            zjm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {

#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"zjm\":" + zjm_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"zjm\":" + zjm_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                zjm_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void ajm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            ajm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {

#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"ajm\":" + ajm_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"ajm\":" + ajm_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                ajm_maskedTextBox.ForeColor = Color.Black;
            }
        }

        #endregion

        // =========================================================================
        #region jh  // *jh: jerk homing
        // *jh update

        private void Update_xjh(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_xjh), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            xjh_maskedTextBox.Text = val.ToString();
        }

        private void Update_yjh(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_yjh), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            yjh_maskedTextBox.Text = val.ToString();
        }

        private void Update_zjh(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_zjh), new[] { value }); return; }

#if (TINYG_SHORTUNITS)
            double val = Convert.ToDouble(value);
#else
            double val = Convert.ToDouble(value) / 1000000;
#endif
            zjh_maskedTextBox.Text = val.ToString();
        }

        // =========================================================================
        // *jh setting

        private void xjh_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            xjh_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {

#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"xjh\":" + xjh_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"xjh\":" + xjh_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                xjh_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void yjh_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            yjh_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"yjh\":" + yjh_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"yjh\":" + yjh_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                yjh_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void zjh_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            zjh_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
#if (TINYG_SHORTUNITS)
                Machine.CommsProcessor.SendCommand("{\"zjh\":" + zjh_maskedTextBox.Text + "}");
#else
                CNC_Write_m("{\"zjh\":" + zjh_maskedTextBox.Text + "000000}");
#endif
                Thread.Sleep(50);
                zjh_maskedTextBox.ForeColor = Color.Black;
            }
        }

        #endregion

        // =========================================================================
        #region sv  // *sv: search velocity
        // * update

        private void Update_xsv(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_xsv), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            Machine.Homing.HomingSpeedX = val;
            xsv_maskedTextBox.Text = val.ToString();
        }

        private void Update_ysv(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_ysv), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            Machine.Homing.HomingSpeedY = val;
            ysv_maskedTextBox.Text = val.ToString();
        }

        private void Update_zsv(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_zsv), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            Machine.Homing.HomingSpeedZ = val;
            zsv_maskedTextBox.Text = val.ToString();
        }

        // =========================================================================
        // *sv setting

        private void xsv_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            xsv_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"xsv\":" + xsv_maskedTextBox.Text + "}");
                Thread.Sleep(50);
                xsv_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void ysv_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            ysv_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"ysv\":" + ysv_maskedTextBox.Text + "}");
                Thread.Sleep(50);
                ysv_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void zsv_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            zsv_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"zsv\":" + zsv_maskedTextBox.Text + "}");
                Thread.Sleep(50);
                zsv_maskedTextBox.ForeColor = Color.Black;
            }
        }

        #endregion

        // =========================================================================
        #region sn  // *sn: Negative limit switch
        // *sn update

        private void Update_xsn(string value)
        {
            switch (value)
            {
                case "0":
                    Xhome_checkBox.Checked = false;
                    Xlim_checkBox.Checked = false;
                    break;
                case "1":
                    Xhome_checkBox.Checked = true;
                    Xlim_checkBox.Checked = false;
                    break;
                case "2":
                    Xhome_checkBox.Checked = false;
                    Xlim_checkBox.Checked = true;
                    break;
                case "3":
                    Xhome_checkBox.Checked = true;
                    Xlim_checkBox.Checked = true;
                    break;
            }
        }

        private void Update_ysn(string value)
        {
            switch (value)
            {
                case "0":
                    Yhome_checkBox.Checked = false;
                    Ylim_checkBox.Checked = false;
                    break;
                case "1":
                    Yhome_checkBox.Checked = true;
                    Ylim_checkBox.Checked = false;
                    break;
                case "2":
                    Yhome_checkBox.Checked = false;
                    Ylim_checkBox.Checked = true;
                    break;
                case "3":
                    Yhome_checkBox.Checked = true;
                    Ylim_checkBox.Checked = true;
                    break;
            }
        }

        private void Update_zsn(string value)
        {
            switch (value)
            {
                case "0":
                    Zhome_checkBox.Checked = false;
                    Zlim_checkBox.Checked = false;
                    break;
                case "1":
                    Zhome_checkBox.Checked = true;
                    Zlim_checkBox.Checked = false;
                    break;
                case "2":
                    Zhome_checkBox.Checked = false;
                    Zlim_checkBox.Checked = true;
                    break;
                case "3":
                    Zhome_checkBox.Checked = true;
                    Zlim_checkBox.Checked = true;
                    break;
            }
        }

        // =========================================================================
        // *sn setting

        private void Xhome_checkBox_Click(object sender, EventArgs e)
        {
            int i = 0;
            if (Xlim_checkBox.Checked) i = 2;
            if (Xhome_checkBox.Checked) i++;
            Machine.CommsProcessor.SendCommand("{\"xsn\":" + i.ToString() + "}");
            Thread.Sleep(50);
        }

        private void Xlim_checkBox_Click(object sender, EventArgs e)
        {
            int i = 0;
            if (Xlim_checkBox.Checked) i = 2;
            if (Xhome_checkBox.Checked) i++;
            Machine.CommsProcessor.SendCommand("{\"xsn\":" + i.ToString() + "}");
            Thread.Sleep(50);
        }

        private void Yhome_checkBox_Click(object sender, EventArgs e)
        {
            int i = 0;
            if (Ylim_checkBox.Checked) i = 2;
            if (Yhome_checkBox.Checked) i++;
            Machine.CommsProcessor.SendCommand("{\"ysn\":" + i.ToString() + "}");
            Thread.Sleep(50);
        }

        private void Ylim_checkBox_Click(object sender, EventArgs e)
        {
            int i = 0;
            if (Ylim_checkBox.Checked) i = 2;
            if (Yhome_checkBox.Checked) i++;
            Machine.CommsProcessor.SendCommand("{\"ysn\":" + i.ToString() + "}");
            Thread.Sleep(50);
        }

        private void Zhome_checkBox_Click(object sender, EventArgs e)
        {
            int i = 0;
            if (Zlim_checkBox.Checked) i = 2;
            if (Zhome_checkBox.Checked) i++;
            Machine.CommsProcessor.SendCommand("{\"zsn\":" + i.ToString() + "}");
            Thread.Sleep(50);
        }

        private void Zlim_checkBox_Click(object sender, EventArgs e)
        {
            int i = 0;
            if (Zlim_checkBox.Checked) i = 2;
            if (Zhome_checkBox.Checked) i++;
            Machine.CommsProcessor.SendCommand("{\"zsn\":" + i.ToString() + "}");
            Thread.Sleep(50);
        }

        #endregion

        // =========================================================================
        #region sx  // *sx: Maximum limit switch
        // *sx update

        private void Update_xsx(string value)
        {
            if (value == "2")
            {
                Xmax_checkBox.Checked = true;
            }
            else
            {
                Xmax_checkBox.Checked = false;
            }
        }

        private void Update_ysx(string value)
        {
            if (value == "2")
            {
                Ymax_checkBox.Checked = true;
            }
            else
            {
                Ymax_checkBox.Checked = false;
            }
        }

        private void Update_zsx(string value)
        {
            if (value == "2")
            {
                Zmax_checkBox.Checked = true;
            }
            else
            {
                Zmax_checkBox.Checked = false;
            }
        }

        // =========================================================================
        // *sx setting

        private void Xmax_checkBox_Click(object sender, EventArgs e)
        {
            if (Xmax_checkBox.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"xsx\":2}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"xsx\":0}");
                Thread.Sleep(50);
            }
        }

        private void Ymax_checkBox_Click(object sender, EventArgs e)
        {
            if (Ymax_checkBox.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"ysx\":2}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"ysx\":0}");
                Thread.Sleep(50);
            }
        }

        private void Zmax_checkBox_Click(object sender, EventArgs e)
        {
            if (Zmax_checkBox.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"zsx\":2}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"zsx\":0}");
                Thread.Sleep(50);
            }
        }

        #endregion

        // =========================================================================
        #region vm  // *vm: Velocity maximum
        // *vm update

        private void Update_xvm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_xvm), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            val = val / 1000;
            xvm_maskedTextBox.Text = val.ToString();
        }

        private void Update_yvm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_yvm), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            val = val / 1000;
            yvm_maskedTextBox.Text = val.ToString();
        }

        private void Update_zvm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_zvm), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            zvm_maskedTextBox.Text = val.ToString();
        }


        private void Update_avm(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_avm), new[] { value }); return; }

            int ind = value.IndexOf('.');   // Cut off the decimal portion, otherwise convert fails in some non-US cultures 
            if (ind > 0)
            {
                value = value.Substring(0, ind);
            };
            double val = Convert.ToDouble(value);
            val = val / 1000;
            avm_maskedTextBox.Text = val.ToString();
        }

        // =========================================================================
        // *vm setting

        private void xvm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            xvm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"xvm\":" + xvm_maskedTextBox.Text + "000}");
                Thread.Sleep(50);
                Machine.CommsProcessor.SendCommand("{\"xfr\":" + xvm_maskedTextBox.Text + "000}");
                Thread.Sleep(50);
                xvm_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void yvm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            yvm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"yvm\":" + yvm_maskedTextBox.Text + "000}");
                Thread.Sleep(50);
                Machine.CommsProcessor.SendCommand("{\"yfr\":" + yvm_maskedTextBox.Text + "000}");
                Thread.Sleep(50);
                yvm_maskedTextBox.ForeColor = Color.Black;
            }
        }

        private void zvm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            zvm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"zvm\":" + zvm_maskedTextBox.Text + "}");
                Thread.Sleep(50);
                Machine.CommsProcessor.SendCommand("{\"zfr\":" + zvm_maskedTextBox.Text + "}");
                Thread.Sleep(50);
                zvm_maskedTextBox.ForeColor = Color.Black;
                int peek = Convert.ToInt32(zvm_maskedTextBox.Text);
                Setting.CNC_ZspeedMax = peek;
            }
        }

        private void avm_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            avm_maskedTextBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                Machine.CommsProcessor.SendCommand("{\"avm\":" + avm_maskedTextBox.Text + "000}");
                Thread.Sleep(50);
                Machine.CommsProcessor.SendCommand("{\"afr\":" + avm_maskedTextBox.Text + "000}");
                Thread.Sleep(50);
                avm_maskedTextBox.ForeColor = Color.Black;
                int peek = Convert.ToInt32(avm_maskedTextBox.Text);
                Setting.CNC_AspeedMax = peek;
            }
        }

        #endregion

        // =========================================================================
        #region mi  // *mi: microstepping
        // *mi update

        private void Update_1mi(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_1mi), new[] { value }); return; }

            int val = Convert.ToInt32(value);
            mi1_maskedTextBox.Text = val.ToString();
        }

        private void Update_2mi(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_2mi), new[] { value }); return; }

            int val = Convert.ToInt32(value);
            mi2_maskedTextBox.Text = val.ToString();
        }

        private void Update_3mi(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_3mi), new[] { value }); return; }

            int val = Convert.ToInt32(value);
            mi3_maskedTextBox.Text = val.ToString();
        }

        private void Update_4mi(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_4mi), new[] { value }); return; }

            int val = Convert.ToInt32(value);
            mi4_maskedTextBox.Text = val.ToString();
        }

        // =========================================================================
        // *mi setting

        private void Microstep_KeyPress(MaskedTextBox box, int BoxNo, KeyPressEventArgs e)
        {
            box.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                List<String> GoodValues = new List<string> { "1", "2", "4", "8" };
                /*if (Cnc.Controlboard == ControlBoardType.qQuintic)
                {
                    GoodValues.Add("16");
                    GoodValues.Add("32");
                }*/
                if (GoodValues.Contains(box.Text))
                {
                    Machine.CommsProcessor.SendCommand("{\"" + BoxNo.ToString() + "mi\":" + box.Text + "}");
                    Thread.Sleep(50);
                    box.ForeColor = Color.Black;
                }
            }
        }
        private void mi1_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            Microstep_KeyPress(mi1_maskedTextBox, 1, e);
        }

        private void mi2_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            Microstep_KeyPress(mi2_maskedTextBox, 2, e);
        }


        private void mi3_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            Microstep_KeyPress(mi3_maskedTextBox, 3, e);
        }

        private void mi4_maskedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            Microstep_KeyPress(mi4_maskedTextBox, 4, e);
        }

        #endregion

        // =========================================================================
        #region tr  // *tr: Travel per revolution
        // *tr update

        private void Update_1tr(string value)
        {
            tr1_textBox.Text = value;
        }

        private void Update_2tr(string value)
        {
            tr2_textBox.Text = value;
        }

        private void Update_3tr(string value)
        {
            tr3_textBox.Text = value;

        }

        private void Update_4tr(string value)
        {
            tr4_textBox.Text = value;
        }

        // =========================================================================
        // *tr setting
        private void tr1_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            tr1_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(tr1_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.CommsProcessor.SendCommand("{\"1tr\":" + tr1_textBox.Text + "}");
                    Thread.Sleep(50);
                    tr1_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void tr2_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            tr2_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(tr2_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.CommsProcessor.SendCommand("{\"2tr\":" + tr2_textBox.Text + "}");
                    Thread.Sleep(50);
                    tr2_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void tr3_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            tr3_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(tr3_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.CommsProcessor.SendCommand("{\"3tr\":" + tr3_textBox.Text + "}");
                    Thread.Sleep(50);
                    tr3_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void tr4_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            tr4_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(tr1_textBox.Text.Replace(',', '.'), out val))
                {
                    Machine.CommsProcessor.SendCommand("{\"4tr\":" + tr4_textBox.Text + "}");
                    Thread.Sleep(50);
                    tr4_textBox.ForeColor = Color.Black;
                }
            }
        }

        #endregion

        // =========================================================================
        #region sa  // *sa: Step angle, 0.9 or 1.8
        // *sa update

        private void Update_1sa(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_1sa), new[] { value }); return; }

            if ((value == "0.90") || (value == "0.900"))
            {
                m1deg09_radioButton.Checked = true;
            }
            else if ((value == "1.80") || (value == "1.800"))
            {
                m1deg18_radioButton.Checked = true;
            }
        }

        private void Update_2sa(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_2sa), new[] { value }); return; }

            if ((value == "0.90") || (value == "0.900"))
            {
                m2deg09_radioButton.Checked = true;
            }
            else if ((value == "1.80") || (value == "1.800"))
            {
                m2deg18_radioButton.Checked = true;
            }
        }

        private void Update_3sa(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_3sa), new[] { value }); return; }

            if ((value == "0.90") || (value == "0.900"))
            {
                m3deg09_radioButton.Checked = true;
            }
            else if ((value == "1.80") || (value == "1.800"))
            {
                m3deg18_radioButton.Checked = true;
            }
        }

        private void Update_4sa(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_4sa), new[] { value }); return; }

            if ((value == "0.90") || (value == "0.900"))
            {
                m4deg09_radioButton.Checked = true;
            }
            else if ((value == "1.80") || (value == "1.800"))
            {
                m4deg18_radioButton.Checked = true;
            }
        }

        // =========================================================================
        // *sa setting

        private void m1deg09_radioButton_Click(object sender, EventArgs e)
        {
            if (m1deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"1sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"1sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        private void m1deg18_radioButton_Click(object sender, EventArgs e)
        {
            if (m1deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"1sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"1sa\":1.8}");
                Thread.Sleep(50);
            }
        }


        private void m2deg09_radioButton_Click(object sender, EventArgs e)
        {
            if (m2deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"2sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"2sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        private void m2deg18_radioButton_Click(object sender, EventArgs e)
        {
            if (m2deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"2sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"2sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        private void m3deg09_radioButton_Click(object sender, EventArgs e)
        {
            if (m3deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"3sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"3sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        private void m3deg18_radioButton_Click(object sender, EventArgs e)
        {
            if (m3deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"3sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"3sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        private void m4deg09_radioButton_Click(object sender, EventArgs e)
        {
            if (m4deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"4sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"4sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        private void m4deg18_radioButton_Click(object sender, EventArgs e)
        {
            if (m4deg09_radioButton.Checked)
            {
                Machine.CommsProcessor.SendCommand("{\"4sa\":0.9}");
                Thread.Sleep(50);
            }
            else
            {
                Machine.CommsProcessor.SendCommand("{\"4sa\":1.8}");
                Thread.Sleep(50);
            }
        }

        #endregion

        // =========================================================================
        #region mpo  // mpo*: Position
        // * update
        private void Update_xpos(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_xpos), new[] { value }); return; }
            TrueX_label.Text = value;
            xpos_textBox.Text = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            //DisplayText("Update_xpos: " + Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture));
        }

        private void Update_ypos(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_ypos), new[] { value }); return; }
            ypos_textBox.Text = value;
            xpos_textBox.Text = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            //DisplayText("Update_ypos, x: " + Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture));
        }

        private void Update_zpos(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_zpos), new[] { value }); return; }
            zpos_textBox.Text = value;
        }

        private void Update_apos(string value)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Update_apos), new[] { value }); return; }
            apos_textBox.Text = value;
        }

        #endregion

        // =========================================================================
        #region Machine_Size

        private void SizeXMax_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            SizeXMax_textBox.ForeColor = Color.Red;
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(SizeXMax_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_MachineSizeX = val;
                    SizeXMax_textBox.ForeColor = Color.Black;
                    Machine.DownCamera.SideMarksX = Setting.General_MachineSizeX / 100;
                }
            }
        }

        private void SizeXMax_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(SizeXMax_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_MachineSizeX = val;
                SizeXMax_textBox.ForeColor = Color.Black;
                Machine.DownCamera.SideMarksX = Setting.General_MachineSizeX / 100;

            }
        }

        private void SizeYMax_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            SizeYMax_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(SizeYMax_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_MachineSizeY = val;
                    SizeYMax_textBox.ForeColor = Color.Black;
                    Machine.DownCamera.SideMarksY = Setting.General_MachineSizeY / 100;
                }
            }
        }

        private void SizeYMax_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(SizeYMax_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_MachineSizeY = val;
                SizeYMax_textBox.ForeColor = Color.Black;
                Machine.DownCamera.SideMarksY = Setting.General_MachineSizeY / 100;
            }
        }


        #endregion

        // =========================================================================
        #region park_location

        private void ParkLocationX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(ParkLocationX_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_ParkX = val;
                }
            }
        }

        private void ParkLocationX_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ParkLocationX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_ParkX = val;
            }
        }

        private void ParkLocationY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(ParkLocationY_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_ParkY = val;
                }
            }
        }

        private void ParkLocationY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ParkLocationY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_ParkY = val;
            }
        }

        private void Park_button_Click(object sender, EventArgs e)
        {
            Machine.Move.CNC_Park();
        }

        #endregion

        // =========================================================================

        private void reset_button_Click(object sender, EventArgs e)
        {
            Machine.CommsProcessor.SendCommand("\x18");
        }

        private void TestX_thread()
        {
            //if (!CNC_XY_m(0.0, Machine.Position.CurrentY))
            //    return;
            //if (!CNC_XY_m(Setting.General_MachineSizeX, Machine.Position.CurrentY))
            //    return;
            //if (!CNC_XY_m(0.0, Machine.Position.CurrentY))
            //    return;
        }

        private async void TestX_button_Click(object sender, EventArgs e)
        {
            await Machine.Move.MoveXYSafeAsync(0.0, Machine.Position.CurrentY);
            await Machine.Move.MoveXYSafeAsync(Setting.General_MachineSizeX, Machine.Position.CurrentY);
            await Machine.Move.MoveXYSafeAsync(0.0, Machine.Position.CurrentY);
            //Thread t = new Thread(() => TestX_thread());
            //t.IsBackground = true;
            //t.Start();
        }

        private async void TestY_thread()
        {
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX, 0);
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX, Setting.General_MachineSizeY);
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX, 0);
        }

        private void TestY_button_Click(object sender, EventArgs e)
        {
            Thread t = new Thread(() => TestY_thread());
            t.IsBackground = true;
            t.Start();
        }

        private async void TestXYA_thread()
        {
            await Machine.Move.MoveXYASafeAsync(0, 0, 0);
            await Machine.Move.MoveXYASafeAsync(Setting.General_MachineSizeX, Setting.General_MachineSizeY, 360.0);
            await Machine.Move.MoveXYASafeAsync(0, 0, 0);
        }

        private void TestXYA_button_Click(object sender, EventArgs e)
        {
            Thread t = new Thread(() => TestXYA_thread());
            t.IsBackground = true;
            t.Start();
        }

        private async void TestXY_thread()
        {
            await Machine.Move.MoveXYSafeAsync(0, 0);
            await Machine.Move.MoveXYSafeAsync(Setting.General_MachineSizeX, Setting.General_MachineSizeY);
            await Machine.Move.MoveXYSafeAsync(0, 0);
        }

        private void TestXY_button_Click(object sender, EventArgs e)
        {
            Thread t = new Thread(() => TestXY_thread());
            t.IsBackground = true;
            t.Start();
        }

        private async void TestYX_thread()
        {
            if (!await Machine.Move.MoveXYSafeAsync(Setting.General_MachineSizeX, 0))
                return;
            if (!await Machine.Move.MoveXYSafeAsync(0, Setting.General_MachineSizeY))
                return;
            if (!await Machine.Move.MoveXYSafeAsync(Setting.General_MachineSizeX, 0))
                return;
        }

        private void TestYX_button_Click(object sender, EventArgs e)
        {
            Thread t = new Thread(() => TestYX_thread());
            t.IsBackground = true;
            t.Start();
        }

        private async void HomeX_button_Click(object sender, EventArgs e)
        {
            await Machine.Homing.CNC_Home_mAsync("X");
        }

        private async void HomeXY_button_Click(object sender, EventArgs e)
        {
            if (!await Machine.Homing.CNC_Home_mAsync("X"))
                return;
            await Machine.Homing.CNC_Home_mAsync("Y");
        }

        private async void HomeY_button_Click(object sender, EventArgs e)
        {
            await Machine.Homing.CNC_Home_mAsync("Y");
        }

        private async void HomeZ_button_Click(object sender, EventArgs e)
        {
            Machine.Move.ProbingMode(false);
            await Machine.Homing.CNC_Home_mAsync("Z");
        }

        private async void TestZ_thread()
        {
            if (!await Machine.Move.MoveZSafeAsync(0))
                return;
            if (!await Machine.Move.MoveZSafeAsync(Setting.General_ZTestTravel))
                return;
            if (!await Machine.Move.MoveZSafeAsync(0))
                return;
        }

        private void TestZ_button_Click(object sender, EventArgs e)
        {
            Thread t = new Thread(() => TestZ_thread());
            t.IsBackground = true;
            t.Start();
        }

        private void NozzleBelowPCB_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleBelowPCB_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_BelowPCB_Allowance = val;
            }
        }

        private void ZTestTravel_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ZTestTravel_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_ZTestTravel = val;
            }
        }

        private void ShadeGuard_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(ShadeGuard_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.General_ShadeGuard_mm = val;
            }
        }


        private async void TestA_thread()
        {
            if (!await Machine.Move.MoveASafeAsync(0))
                return;
            if (!await Machine.Move.MoveASafeAsync(360))
                return;
            if (!await Machine.Move.MoveASafeAsync(0))
                return;
        }

        private void TestA_button_Click(object sender, EventArgs e)
        {
            Thread t = new Thread(() => TestA_thread());
            t.IsBackground = true;
            t.Start();
        }

        private async void Homebutton_Click(object sender, EventArgs e)
        {
            if (!await Machine.Homing.CNC_Home_mAsync("Z"))
                return;
            if (!await Machine.Homing.CNC_Home_mAsync("X"))
                return;
            if (!await Machine.Homing.CNC_Home_mAsync("Y"))
                return;
            await Machine.Move.MoveASafeAsync(0);
        }

        private void MotorPower_checkBox_Click(object sender, EventArgs e)
        {
            if (MotorPower_checkBox.Checked)
            {
                Machine.Motors.MotorPowerOn();
            }
            else
            {
                Machine.Motors.MotorPowerOff();
            }
        }

        private void Pump_checkBox_Click(object sender, EventArgs e)
        {
            if (Pump_checkBox.Checked)
            {
                Machine.Pump.PumpOn();
            }
            else
            {
                Machine.Pump.PumpOff();
            }
        }

        private void PumpInvert_checkBox_Click(object sender, EventArgs e)
        {
            Setting.General_PumpOutputInverted = PumpInvert_checkBox.Checked;
        }

        private void Vacuum_checkBox_Click(object sender, EventArgs e)
        {
            if (Vacuum_checkBox.Checked)
            {
                Machine.Vacuum.VacuumOn();
            }
            else
            {
                Machine.Vacuum.VacuumOff();
            }
        }

        private void VacuumInvert_checkBox_Click(object sender, EventArgs e)
        {
            Setting.General_VacuumOutputInverted = VacuumInvert_checkBox.Checked;
        }


        private void VacuumTime_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            int val;
            VacuumTime_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (int.TryParse(VacuumTime_textBox.Text, out val))
                {
                    Setting.General_PickupVacuumTime = val;
                    VacuumTime_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void VacuumTime_textBox_Leave(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(VacuumTime_textBox.Text, out val))
            {
                Setting.General_PickupVacuumTime = val;
                VacuumTime_textBox.ForeColor = Color.Black;
            }
        }

        private void VacuumRelease_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            int val;
            VacuumRelease_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (int.TryParse(VacuumRelease_textBox.Text, out val))
                {
                    Setting.General_PickupReleaseTime = val;
                    VacuumRelease_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void VacuumRelease_textBox_Leave(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(VacuumRelease_textBox.Text, out val))
            {
                Setting.General_PickupReleaseTime = val;
                VacuumRelease_textBox.ForeColor = Color.Black;
            }
        }


        private void SlackCompensation_checkBox_Click(object sender, EventArgs e)
        {
            if (SlackCompensation_checkBox.Checked)
            {
                Machine.Move.SlackCompensation = true;
                Setting.CNC_SlackCompensation = true;
            }
            else
            {
                Machine.Move.SlackCompensation = false;
                Setting.CNC_SlackCompensation = false;
            }
        }


        private static int SetProbing_stage = 0;

        private async Task CancelProbingAsync()
        {
            SetProbing_button.Text = "Start";
            SetProbing_button.Enabled = false;
            SetProbing_stage = 0;
            Machine.Move.ZGuardOn();
            CancelProbing_button.Visible = false;
            Zlb_label.Visible = false;
            Machine.Move.ProbingMode(false);
            await Machine.Homing.CNC_Home_mAsync("Z");
            SetProbing_button.Enabled = true;
        }

        private async void CancelProbing_button_Click(object sender, EventArgs e)
        {
            await CancelProbingAsync();
        }

        private async void SetProbing_button_Click(object sender, EventArgs e)
        {
            Machine.Move.ZGuardOff();
            switch (SetProbing_stage)
            {
                case 0:
                    CancelProbing_button.Visible = true;
                    CancelProbing_button.Enabled = true;
                    Zlb_label.Text = "Put a regular height PCB under the Nozzle, \n\rthen click \"Next\"";
                    Zlb_label.Visible = true;
                    SetProbing_button.Text = "Next";
                    SetProbing_stage = 1;
                    break;

                case 1:
                    SetProbing_button.Enabled = false;
                    CancelProbing_button.Enabled = false;
                    if (!await Nozzle_ProbeDown_mAsync())
                    {
                        await CancelProbingAsync();
                        return;
                    }
                    SetProbing_button.Enabled = true;
                    CancelProbing_button.Enabled = true;
                    Setting.General_ZtoPCB = Machine.Position.CurrentZ;
                    Zlb_label.Text = "Jog Z axis until the Nozzle just barely touches the PCB\nThen click \"Next\"";
                    SetProbing_stage = 2;
                    break;

                case 2:
                    Setting.General_PlacementBackOff = Setting.General_ZtoPCB - Machine.Position.CurrentZ;
                    Setting.General_ZtoPCB = Machine.Position.CurrentZ;
                    Machine.Move.ProbingMode(false);
                    SetProbing_button.Text = "Start";
                    Zlb_label.Text = "";
                    Zlb_label.Visible = false;
                    CancelProbing_button.Visible = false;
                    SetProbing_button.Enabled = false;
                    await Machine.Homing.CNC_Home_mAsync("Z");
                    Machine.Move.ZGuardOn();
                    SetProbing_button.Enabled = true;
                    ShowMessageBox(
                       "Probing Backoff set successfully.\n" +
                            "PCB surface: " + Setting.General_ZtoPCB.ToString("0.00", CultureInfo.InvariantCulture) +
                            "\nBackoff:  " + Setting.General_PlacementBackOff.ToString("0.00", CultureInfo.InvariantCulture),
                       "Done",
                       MessageBoxButtons.OK);
                    SetProbing_stage = 0;
                    Z0_textBox.Text = Setting.General_ZtoPCB.ToString("0.00", CultureInfo.InvariantCulture);
                    BackOff_textBox.Text = Setting.General_PlacementBackOff.ToString("0.00", CultureInfo.InvariantCulture);

                    // If tapes have z heights set, offer to zero out those:
                    bool ZisSet = false;
                    foreach (DataGridViewRow Row in Tapes_dataGridView.Rows)
                    {
                        if (Row.Cells["Z_Pickup_Column"].Value.ToString() != "--")
                        {
                            ZisSet = true;
                            break;
                        }
                        if (Row.Cells["Z_Place_Column"].Value.ToString() != "--")
                        {
                            ZisSet = true;
                            break;
                        }
                    }
                    if (ZisSet)
                    {
                        DialogResult dialogResult = ShowMessageBox(
                            "Reset pickup and placement heights on tapes?",
                            "Reset Z's?",
                            MessageBoxButtons.OKCancel);
                        if (dialogResult == DialogResult.Cancel)
                        {
                            break;
                        }
                        foreach (DataGridViewRow Row in Tapes_dataGridView.Rows)
                        {
                            Row.Cells["Z_Pickup_Column"].Value = "--";
                            Row.Cells["Z_Place_Column"].Value = "--";
                        }
                    }

                    break;
            }
        }


        private void SetMark1_button_Click(object sender, EventArgs e)
        {
            Setting.General_Mark1X = Machine.Position.CurrentX;
            Setting.General_Mark1Y = Machine.Position.CurrentY;
            Setting.General_Mark1A = Machine.Position.CurrentA;
            Setting.General_Mark1Name = Mark1_textBox.Text;
            Bookmark1_button.Text = Setting.General_Mark1Name;
        }

        private void SetMark2_button_Click(object sender, EventArgs e)
        {
            Setting.General_Mark2X = Machine.Position.CurrentX;
            Setting.General_Mark2Y = Machine.Position.CurrentY;
            Setting.General_Mark2A = Machine.Position.CurrentA;
            Setting.General_Mark2Name = Mark2_textBox.Text;
            Bookmark2_button.Text = Setting.General_Mark2Name;
        }

        private void SetMark3_button_Click(object sender, EventArgs e)
        {
            Setting.General_Mark3X = Machine.Position.CurrentX;
            Setting.General_Mark3Y = Machine.Position.CurrentY;
            Setting.General_Mark3A = Machine.Position.CurrentA;
            Setting.General_Mark3Name = Mark3_textBox.Text;
            Bookmark3_button.Text = Setting.General_Mark3Name;
        }

        private void SetMark4_button_Click(object sender, EventArgs e)
        {
            Setting.General_Mark4X = Machine.Position.CurrentX;
            Setting.General_Mark4Y = Machine.Position.CurrentY;
            Setting.General_Mark4A = Machine.Position.CurrentA;
            Setting.General_Mark4Name = Mark4_textBox.Text;
            Bookmark4_button.Text = Setting.General_Mark4Name;
        }

        private void SetMark5_button_Click(object sender, EventArgs e)
        {
            Setting.General_Mark5X = Machine.Position.CurrentX;
            Setting.General_Mark5Y = Machine.Position.CurrentY;
            Setting.General_Mark5A = Machine.Position.CurrentA;
            Setting.General_Mark5Name = Mark5_textBox.Text;
            Bookmark5_button.Text = Setting.General_Mark5Name;
        }

        private void SetMark6_button_Click(object sender, EventArgs e)
        {
            Setting.General_Mark6X = Machine.Position.CurrentX;
            Setting.General_Mark6Y = Machine.Position.CurrentY;
            Setting.General_Mark6A = Machine.Position.CurrentA;
            Setting.General_Mark6Name = Mark6_textBox.Text;
            Bookmark6_button.Text = Setting.General_Mark6Name;
        }

        private async void Bookmark1_button_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark1X = Machine.Position.CurrentX;
                Setting.General_Mark1Y = Machine.Position.CurrentY;
                Setting.General_Mark1A = Machine.Position.CurrentA;
                return;
            };
            await Machine.Move.MoveXYASafeAsync(Setting.General_Mark1X, Setting.General_Mark1Y, Setting.General_Mark1A);
        }

        private async void Bookmark2_button_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark2X = Machine.Position.CurrentX;
                Setting.General_Mark2Y = Machine.Position.CurrentY;
                Setting.General_Mark2A = Machine.Position.CurrentA;
                return;
            };
            await Machine.Move.MoveXYASafeAsync(Setting.General_Mark2X, Setting.General_Mark2Y, Setting.General_Mark2A);
        }

        private async void Bookmark3_button_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark3X = Machine.Position.CurrentX;
                Setting.General_Mark3Y = Machine.Position.CurrentY;
                Setting.General_Mark3A = Machine.Position.CurrentA;
                return;
            };
            await Machine.Move.MoveXYASafeAsync(Setting.General_Mark3X, Setting.General_Mark3Y, Setting.General_Mark3A);
        }

        private async void Bookmark4_button_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark4X = Machine.Position.CurrentX;
                Setting.General_Mark4Y = Machine.Position.CurrentY;
                Setting.General_Mark4A = Machine.Position.CurrentA;
                return;
            };
            await Machine.Move.MoveXYASafeAsync(Setting.General_Mark4X, Setting.General_Mark4Y, Setting.General_Mark4A);
        }

        private async void Bookmark5_button_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark5X = Machine.Position.CurrentX;
                Setting.General_Mark5Y = Machine.Position.CurrentY;
                Setting.General_Mark5A = Machine.Position.CurrentA;
                return;
            };
            await Machine.Move.MoveXYASafeAsync(Setting.General_Mark5X, Setting.General_Mark5Y, Setting.General_Mark5A);
        }

        private async void Bookmark6_button_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                Setting.General_Mark6X = Machine.Position.CurrentX;
                Setting.General_Mark6Y = Machine.Position.CurrentY;
                Setting.General_Mark6A = Machine.Position.CurrentA;
                return;
            };
            await Machine.Move.MoveXYASafeAsync(Setting.General_Mark6X, Setting.General_Mark6Y, Setting.General_Mark6A);
        }

        private void SmallMovement_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_SmallMovementSpeed = SmallMovement_numericUpDown.Value;
            Machine.Move.SmallMovementString = "G1 F" + Setting.CNC_SmallMovementSpeed.ToString() + " ";
        }

        private void SquareCorrection_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(SquareCorrection_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.CNC_SquareCorrection = val;
                //CNC.SquareCorrection = val;
            }
        }

        private void SquareCorrection_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(SquareCorrection_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.CNC_SquareCorrection = val;
                    //CNC.SquareCorrection = val;
                }
            }
        }

        private void CtlrJogSpeed_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_CtlrJogSpeed = (int)CtlrJogSpeed_numericUpDown.Value;

        }

        private void NormalJogSpeed_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_NormalJogSpeed = (int)NormalJogSpeed_numericUpDown.Value;
        }

        private void AltJogSpeed_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            Setting.CNC_AltJogSpeed = (int)AltJogSpeed_numericUpDown.Value;
        }

        private void DisableLog_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (DisableLog_checkBox.Checked)
            {
                DisplayText("** Logging disabled **", KnownColor.Black, true);
            }
        }

        private void SlackCompensationA_checkBox_Click(object sender, EventArgs e)
        {
            if (SlackCompensationA_checkBox.Checked)
            {
                Machine.Move.SlackCompensationA = true;
                Setting.CNC_SlackCompensationA = true;
            }
            else
            {
                Machine.Move.SlackCompensationA = false;
                Setting.CNC_SlackCompensationA = false;
            }
        }

        private void Z0_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            Z0_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(Z0_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_ZtoPCB = val;
                    Z0_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void BackOff_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            BackOff_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(BackOff_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_PlacementBackOff = val;
                    BackOff_textBox.ForeColor = Color.Black;
                }
            }
        }

        private void Hysteresis_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            Hysteresis_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(Hysteresis_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.General_ZprobingHysteresis = val;
                    Hysteresis_textBox.ForeColor = Color.Black;
                }
            }
        }


        private void PlacementDepth_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            PlacementDepth_textBox.ForeColor = Color.Red;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(PlacementDepth_textBox.Text.Replace(',','.'), out val))
                {
                    Setting.Placement_Depth = val;
                    PlacementDepth_textBox.ForeColor = Color.Black;
                }
            }
        }


        #endregion Basic setup page functions

        // =================================================================================
        // Run job page functions
        // =================================================================================
        #region Job page functions

        private class PhysicalComponent
        {
            public string Designator { get; set; }
            public string Footprint { get; set; }
            public double X_nominal { get; set; }
            public double Y_nominal { get; set; }
            public double Rotation { get; set; }
            public double X_machine { get; set; }
            public double Y_machine { get; set; }
            public string Method { get; set; }
            public string MethodParameter { get; set; }
        }

        private void Placement_pictureBox_MouseClick(object sender, MouseEventArgs e)
        {
            General_pictureBox_MouseClick(Placement_pictureBox, e.X, e.Y);
        }


        private void Placement_pictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            General_pictureBox_MouseMove(Tapes_pictureBox, e.X, e.Y);
        }


        private void RunJob_tabPage_Begin()
        {
            SetDownCameraDefaults();
            Machine.DownCamera.ImageBox = Placement_pictureBox;
            SelectCamera(Machine.DownCamera);
            if (!Machine.DownCamera.IsRunning())
            {
                ShowMessageBox(
                    "Problem starting down camera. Please fix before continuing.",
                    "Down ICamera problem",
                    MessageBoxButtons.OK
                );
            }
            Machine.DownCamera.DrawCross = true;
            Machine.DownCamera.BoxSizeX = 200;
            Machine.DownCamera.BoxSizeY = 200;
            Machine.DownCamera.BoxRotationDeg = 0;
            Machine.DownCamera.TestAlgorithm = false;
            Machine.DownCamera.Draw_Snapshot = false;
            Machine.DownCamera.FindCircles = false;
            Machine.DownCamera.DrawDashedCross = false;
        }

        private void RunJob_tabPage_End()
        {
        }

        // =================================================================================
        // CAD data and Job datagrid colum definitions
        const int CADdata_ComponentColumn = 0;
        const int CADdata_ComponentType_Column = 1;
        const int CADdata_PlacedColumn = 2;
        const int CADdata_XNomColumn = 3;
        const int CADdata_YNomColumn = 4;
        const int CADdata_RotNomColumn = 5;
        const int CADdata_XMachColumn = 6;
        const int CADdata_YMachColumn = 7;
        const int CADdata_RotMachColumn = 8;

        const int Jobdata_CountColumn = 0;
        const int Jobdata_ComponentTypColumn = 1;
        const int Jobdata_MethodColumn = 2;
        const int Jobdata_MethodParametersColumn = 3;
        const int Jobdata_NozzleColumn = 4;
        const int Jobdata_ComponentsColumn = 5;

        // =================================================================================
        private void resetPlacedDataToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // the foreach loop below ignores last checked box, unless we manually select some other cell before. ??
            CadData_GridView.CurrentCell = CadData_GridView[0, 0];
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                Row.Cells[CADdata_PlacedColumn].Value = false;
            }
            CadData_GridView.ClearSelection();  // and we don't want a random celected cell
            Update_GridView(CadData_GridView);
        }


        // =================================================================================
        // CAD data and Job data load and save functions
        // =================================================================================
        private string CadDataFileName = "";
        private string JobFileName = "";

        private bool LoadCadData_m()
        {
            String[] AllLines;

            // read in CAD data (.csv file)
            if (CAD_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    bool result;
                    CadDataFileName = CAD_openFileDialog.FileName;
                    CadFileName_label.Text = Path.GetFileName(CadDataFileName);
                    CadFilePath_label.Text = Path.GetDirectoryName(CadDataFileName);
                    AllLines = File.ReadAllLines(CadDataFileName, Encoding.Default);
                    if (Path.GetExtension(CAD_openFileDialog.FileName) == ".pos")
                    {
                        result = ParseKiCadData_m(AllLines);
                    }
                    else
                    {
                        result = ParseCadData_m(AllLines, false);
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    Cursor.Current = Cursors.Default;
                    ShowMessageBox(
                        "Error in file, Msg: " + ex.Message,
                        "Can't read CAD file",
                        MessageBoxButtons.OK);
                    CadData_GridView.Rows.Clear();
                    CadFileName_label.Text = "--";
                    CadFilePath_label.Text = "--";
                    CadDataFileName = "--";
                    return false;
                };
            }
            else
            {
                return false;
            }
        }

        // =================================================================================
        private bool LoadJobData_m(string JobFileName)
        {
            String[] AllLines;
            try
            {
                AllLines = File.ReadAllLines(JobFileName);
                JobFileName_label.Text = Path.GetFileName(JobFileName);
                JobFilePath_label.Text = Path.GetDirectoryName(JobFileName);
                ParseJobData(AllLines);
                ValidMeasurement_checkBox.Checked = false;
            }
            catch (Exception ex)
            {
                ShowMessageBox(
                    "Error in file, Msg: " + ex.Message,
                    "Can't read job file (" + JobFileName + ")",
                    MessageBoxButtons.OK);
                JobData_GridView.Rows.Clear();
                JobFileName_label.Text = "--";
                JobFilePath_label.Text = "--";
                CadDataFileName = "--";
                return false;
            };
            return true;
        }

        // =================================================================================
        private void LoadCadData_button_Click(object sender, EventArgs e)
        {
            ValidMeasurement_checkBox.Checked = false;
            if (LoadCadData_m())
            {
                SaveTempCADdata();
                // Read in job data (.lpj file), if exists
                string ext = Path.GetExtension(CadDataFileName);
                JobFileName = CadDataFileName.Replace(ext, ".lpj");
                if (File.Exists(JobFileName))
                {
                    if (!LoadJobData_m(JobFileName))
                    {
                        ShowMessageBox(
                            "Attempt to read in existing Job Data file failed. Job data automatically created, review situation!",
                            "Job Data load error",
                            MessageBoxButtons.OK);
                        FillJobData_GridView();
                        int dummy;
                        FindFiducials_m(out dummy);  // don't care of the result, just trying to find fids
                    }
                }
                else
                {
                    // If not, build job data ourselves.
                    FillJobData_GridView();
                    int dummy;      // don't care of the result, just trying to find fids
                    if (FindFiducials_m(out dummy))
                    {
                        FillDefaultJobValues();
                    }
                    JobFileName_label.Text = "--";
                    JobFilePath_label.Text = "--";
                }
            }
            else
            {
                // CAD data load failed, clear to false data
                CadData_GridView.Rows.Clear();
                CadFileName_label.Text = "--";
                CadFilePath_label.Text = "--";
                CadDataFileName = "--";
            }
            MakeCADdataClean();
        }

        // =================================================================================
        private void SaveCadData_button_Click(object sender, EventArgs e)
        {
            if (CadData_GridView.RowCount < 1)
            {
                ShowMessageBox(
                    "No Data",
                    "No Data",
                    MessageBoxButtons.OK
                );
                return;
            }

            Job_saveFileDialog.Filter = "CSV placement files (*.csv)|*.csv|All files (*.*)|*.*";

            if (Job_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveCADdata(Job_saveFileDialog.FileName);
                CadDataFileName = Job_saveFileDialog.FileName;
                CadFileName_label.Text = Path.GetFileName(CadDataFileName);
                CadFilePath_label.Text = Path.GetDirectoryName(CadDataFileName);
            }
        }

        private bool SaveCADdata(string filename, bool tempfile = false)
        {
            try
            {
                string OutLine;
                using (StreamWriter f = new StreamWriter(filename))
                {
                    if (tempfile)
                    {
                        f.WriteLine(CadFileName_label.Text);
                        f.WriteLine(CadFilePath_label.Text);
                        MakeCADdataDirty();  // it is dirty, since it didn't came from the original file
                    }
                    else 
                    {
                        MakeCADdataClean();
                    }
                    // Write header
                    OutLine = "\"Component\",\"Value\",\"Footprint\",\"Placed\",\"X\",\"Y\",\"Rotation\"";
                    f.WriteLine(OutLine);
                    // write data
                    foreach (DataGridViewRow Row in CadData_GridView.Rows)
                    {
                        OutLine = "\"" + Row.Cells["Component"].Value.ToString() + "\"";
                        string temp = Row.Cells["Value_Footprint"].Value.ToString();
                        int i = temp.IndexOf('|');
                        OutLine += ",\"" + temp.Substring(0, i - 2) + "\"";
                        OutLine += ",\"" + temp.Substring(i + 3, temp.Length - i - 3) + "\"";
                        if (Row.Cells["Placed_column"].Value == null)
                        {
                            OutLine += ",\"false\"";
                        }
                        else
                        {
                            OutLine += ",\"" + Row.Cells["Placed_column"].Value.ToString() + "\"";
                        }
                        OutLine += ",\"" + Row.Cells["X_nominal"].Value.ToString() + "\"";
                        OutLine += ",\"" + Row.Cells["Y_nominal"].Value.ToString() + "\"";
                        OutLine += ",\"" + Row.Cells["Rotation"].Value.ToString() + "\"";
                        f.WriteLine(OutLine);
                    }
                }
                return true;
            }
            catch (System.Exception excep)
            {

                DisplayText("SaveCADdata failed: "+ excep.Message);
                return false;
            }
        }


        // =================================================================================
        private void LoadTempJobData()
        {
            String[] AllLines;
            string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = FileName.LastIndexOf('\\');
            FileName = FileName.Remove(i + 1);
            FileName = FileName + "JobDataSave.csv";
            if (!File.Exists(FileName))
            {
                DisplayText("No saved temp job data file");
                MakeJobDataClean();
                return;
            }
            else
            {
                MakeJobDataDirty();
            }
            try
            {
                DisplayText("Loading temp job data file");
                AllLines = File.ReadAllLines(FileName);
                JobFileName_label.Text = AllLines[0];
                JobFilePath_label.Text = AllLines[1];
                AllLines = AllLines.Skip(2).ToArray();
                ParseJobData(AllLines);
                return;
            }
            catch (Exception ex)
            {
                DisplayText("Could not read temp job data file", KnownColor.DarkRed, true);
                DisplayText("Msg: " + ex.Message, KnownColor.DarkRed, true);
                JobData_GridView.Rows.Clear();
                return;
            };
        }

        private void LoadTempCADdata()
        {
            String[] AllLines;
            string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = FileName.LastIndexOf('\\');
            FileName = FileName.Remove(i + 1);
            FileName = FileName + "CadDataSave.csv";
            if (!File.Exists(FileName))
            {
                DisplayText("No saved temp CAD data file");
                MakeCADdataClean();
                return;
            }
            else
            {
                MakeCADdataDirty();
            }
            try
            {
                DisplayText("Loading temp CAD data file");
                AllLines = File.ReadAllLines(FileName);
                CadFileName_label.Text = AllLines[0];
                CadFilePath_label.Text = AllLines[1];
                AllLines = AllLines.Skip(2).ToArray();
                ParseCadData_m(AllLines, false);
                return;
            }
            catch (Exception ex)
            {
                DisplayText("Could not read temp CAD data file", KnownColor.DarkRed, true);
                DisplayText("Msg: " + ex.Message, KnownColor.DarkRed, true);
                CadData_GridView.Rows.Clear();
                return;
            };
        }

        // =================================================================================
        private bool SaveTempCADdata()
        {
            if ( CadFileName_label.Text!="----")
            {
                string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                int i = FileName.LastIndexOf('\\');
                FileName = FileName.Remove(i + 1);
                FileName = FileName + "CadDataSave.csv";
                return SaveCADdata(FileName, true);
            }
            return true;
        }

        private bool SaveTempJobData()
        {
            if (CadFileName_label.Text != "----")
            {
                string FileName = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                int i = FileName.LastIndexOf('\\');
                FileName = FileName.Remove(i + 1);
                FileName = FileName + "JobDataSave.csv";
                return SaveJobData(FileName, true);
            }
            return true;
        }

        // =================================================================================
        private void MakeCADdataDirty()
        {
            CAD_label.Text = "FileName*:";
        }

        private void MakeCADdataClean()
        {
            CAD_label.Text = "FileName:";
        }

        // =================================================================================
        private void MakeJobDataDirty()
        {
            Job_label.Text = "FileName*:";
        }

        private void MakeJobDataClean()
        {
            Job_label.Text = "FileName:";
        }


        // =================================================================================
        private void CadData_GridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (CadData_GridView.CurrentCell.ColumnIndex == CADdata_PlacedColumn)
            {
                MakeCADdataDirty();

            }
        }

        private void CadData_GridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            // Machine coordinates can be manually edited (but I don't know why you would want to do that),
            // but they are not saved, therefore the edit of those does not make data dirty, but edit of other cells do
            if ((CadData_GridView.CurrentCell.ColumnIndex == CADdata_ComponentColumn) ||
                (CadData_GridView.CurrentCell.ColumnIndex == CADdata_ComponentType_Column) ||
                (CadData_GridView.CurrentCell.ColumnIndex == CADdata_XNomColumn) ||
                (CadData_GridView.CurrentCell.ColumnIndex == CADdata_YNomColumn) ||
                (CadData_GridView.CurrentCell.ColumnIndex == CADdata_RotNomColumn))
            {
                MakeCADdataDirty();
            }
        }



        // =================================================================================
        private void JobDataLoad_button_Click(object sender, EventArgs e)
        {
            if (Job_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                JobFileName = Job_openFileDialog.FileName;
                LoadJobData_m(JobFileName);
            }
        }

        // =================================================================================
        private void JobDataSave_button_Click(object sender, EventArgs e)
        {
            if (JobData_GridView.RowCount < 1)
            {
                ShowMessageBox(
                    "No Data",
                    "Empty Job",
                    MessageBoxButtons.OK
                );
                return;
            }

            Job_saveFileDialog.Filter = "LitePlacer Job files (*.lpj)|*.lpj|All files (*.*)|*.*";

            if (Job_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveJobData(Job_saveFileDialog.FileName);
                JobFileName = Job_saveFileDialog.FileName;
                JobFileName_label.Text = Path.GetFileName(JobFileName);
                JobFilePath_label.Text = Path.GetDirectoryName(JobFileName);
            }
        }

        private bool SaveJobData(string filename, bool tempfile = false)
        {
            try
            {
                string OutLine;
                using (StreamWriter f = new StreamWriter(filename))
                {
                    if (tempfile)
                    {
                        f.WriteLine(JobFileName_label.Text);
                        f.WriteLine(JobFilePath_label.Text);
                        MakeJobDataDirty();  // it is dirty, since it didn't came from the original file
                    }
                    else
                    {
                        MakeJobDataClean();
                    }
                    // for each row in job datagrid,
                    for (int i = 0; i < JobData_GridView.RowCount; i++)
                    {
                        OutLine = "\"" + JobData_GridView.Rows[i].Cells["ComponentCount"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["ComponentType"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["GroupMethod"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["MethodParamAllComponents"].Value.ToString() + "\"";
                        OutLine += ",\"" + JobData_GridView.Rows[i].Cells["ComponentList"].Value.ToString() + "\"";
                        if (JobData_GridView.Rows[i].Cells["JobDataNozzle_Column"].Value != null)
                        {
                            OutLine += ",\"" + JobData_GridView.Rows[i].Cells["JobDataNozzle_Column"].Value.ToString() + "\"";
                        }
                        else
                        {
                            OutLine += ",\"" + "\"";
                        }
                        f.WriteLine(OutLine);
                    }
                    return true;
                }
            }
            catch (System.Exception excep)
            {

                DisplayText("SaveJobData failed: " + excep.Message);
                return false;
            }
        }


        // =================================================================================
        private void ParseJobData(String[] AllLines)
        {
            int LineIndex = 0;
            JobData_GridView.Rows.Clear();
            // ComponentCount ComponentType GroupMethod MethodParamAllComponents ComponentList
            foreach (string s in AllLines)
            {
                List<String> Line = SplitCSV(AllLines[LineIndex++], ',');
                JobData_GridView.Rows.Add();
                int Last = JobData_GridView.RowCount - 1;
                JobData_GridView.Rows[Last].Cells["ComponentCount"].Value = Line[0];
                JobData_GridView.Rows[Last].Cells["ComponentType"].Value = Line[1];
                JobData_GridView.Rows[Last].Cells["GroupMethod"].Value = Line[2];
                JobData_GridView.Rows[Last].Cells["MethodParamAllComponents"].Value = Line[3];
                JobData_GridView.Rows[Last].Cells["ComponentList"].Value = Line[4];
                if (Line.Count>5)
                {
                    JobData_GridView.Rows[Last].Cells["JobDataNozzle_Column"].Value = Line[5];
                }
            }
            JobData_GridView.ClearSelection();
        }

        // =================================================================================
        // JobData_GridView and CadData_GridView handling
        // JobData_GridView has the components of same type grouped to one component type per line.
        // Public, as it is called from panelize process, too.
        // =================================================================================
        public void FillJobData_GridView()
        {
            string CurrentComponentType = "";
            int ComponentCount = 0;
            JobData_GridView.Rows.Clear();
            int TypeRow;

            foreach (DataGridViewRow InRow in CadData_GridView.Rows)
            {
                CurrentComponentType = InRow.Cells["Value_Footprint"].Value.ToString();
                TypeRow = -1;
                // Have we seen this component type already?
                foreach (DataGridViewRow JobRow in JobData_GridView.Rows)
                {
                    if (JobRow.Cells["ComponentType"].Value.ToString() == CurrentComponentType)
                    {
                        TypeRow = JobRow.Index;
                        break;
                    }
                }
                if (TypeRow == -1)
                {
                    // No, create a new row
                    JobData_GridView.Rows.Add();
                    DataGridViewRow OutRow = JobData_GridView.Rows[JobData_GridView.RowCount - 1];
                    OutRow.Cells["ComponentCount"].Value = "1";
                    OutRow.Cells["ComponentType"].Value = CurrentComponentType;
                    OutRow.Cells["GroupMethod"].Value = "?";
                    OutRow.Cells["MethodParamAllComponents"].Value = "--";
                    OutRow.Cells["ComponentList"].Value = InRow.Cells["Component"].Value.ToString();
                }
                else
                {
                    // Yes, increment component count and add component name to list
                    ComponentCount = Convert.ToInt32(JobData_GridView.Rows[TypeRow].Cells["ComponentCount"].Value.ToString());
                    ComponentCount++;
                    JobData_GridView.Rows[TypeRow].Cells["ComponentCount"].Value = ComponentCount.ToString();
                    // and add component name to list
                    string CurrentComponentList = JobData_GridView.Rows[TypeRow].Cells["ComponentList"].Value.ToString();
                    CurrentComponentList = CurrentComponentList + ',' + InRow.Cells["Component"].Value.ToString();
                    JobData_GridView.Rows[TypeRow].Cells["ComponentList"].Value = CurrentComponentList;
                }
            }
        }

        private void FillDefaultJobValues()
        {
            foreach (DataGridViewRow JobRow in JobData_GridView.Rows)
            {
                if (JobRow.Cells["GroupMethod"].Value.ToString() == "?")
                {
                    // it is not fiducials row
                    JobRow.Cells["GroupMethod"].Value = "Place Fast";
                    if (Setting.Nozzles_Enabled)
                    {
                        JobRow.Cells["JobDataNozzle_Column"].Value = Setting.Nozzles_default.ToString();
                    }
                }
            }
        }


        private void Up_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            DataGrid_Up_button(JobData_GridView);
        }

        private void Down_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            DataGrid_Down_button(JobData_GridView);
        }

        private void DeleteComponentGroup_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            foreach (DataGridViewCell oneCell in JobData_GridView.SelectedCells)
            {
                if (oneCell.Selected)
                    JobData_GridView.Rows.RemoveAt(oneCell.RowIndex);
            }
        }

        private DataGridViewRow ClipBoardRow;

        private void CopyRow_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            ClipBoardRow = JobData_GridView.CurrentRow;
        }

        private void PasteRow_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            for (int i = 0; i < JobData_GridView.ColumnCount; i++)
            {
                JobData_GridView.CurrentRow.Cells[i].Value = ClipBoardRow.Cells[i].Value;
            }
        }


        private void NewRow_button_Click(object sender, EventArgs e)
        {
            MakeJobDataDirty();
            int index = 0;
            if (JobData_GridView.RowCount != 0)
            {
                index = JobData_GridView.CurrentRow.Index;
            }
            JobData_GridView.Rows.Insert(index);
            JobData_GridView.Rows[index].Cells["ComponentCount"].Value = "--";
            JobData_GridView.Rows[index].Cells["ComponentType"].Value = "--";
            JobData_GridView.Rows[index].Cells["GroupMethod"].Value = "?";
            JobData_GridView.Rows[index].Cells["MethodParamAllComponents"].Value = "--";
            JobData_GridView.Rows[index].Cells["ComponentList"].Value = "--";
        }

        private void AddCadDataRow_button_Click(object sender, EventArgs e)
        {
            int index = 0;
            if (CadData_GridView.RowCount!=0)
            {
                index = CadData_GridView.CurrentRow.Index;
            }
            CadData_GridView.Rows.Insert(index);
            CadData_GridView.Rows[index].Cells["Component"].Value = "new_component";
            CadData_GridView.Rows[index].Cells["Value_Footprint"].Value = "value "+" | "+" footprint";
            CadData_GridView.Rows[index].Cells["X_nominal"].Value = "0.0";
            CadData_GridView.Rows[index].Cells["Y_nominal"].Value = "0.0";
            CadData_GridView.Rows[index].Cells["Rotation"].Value = "0.0";
            CadData_GridView.CurrentCell = CadData_GridView.Rows[index].Cells[0];
            SaveTempCADdata();  // makes data dirty
        }

        private void RebuildJobData_button_Click(object sender, EventArgs e)
        {
            // TO DO: Error checking here
            FillJobData_GridView();
            int dummy;
            FindFiducials_m(out dummy);  // don't care of the result, just trying to find fids
            JobFileName_label.Text = "--";
            JobFilePath_label.Text = "--";

        }

        private void DeleteCadDataRow_button_Click(object sender, EventArgs e)
        {
            foreach (DataGridViewCell oneCell in CadData_GridView.SelectedCells)
            {
                if (oneCell.Selected)
                    CadData_GridView.Rows.RemoveAt(oneCell.RowIndex);
            }
            SaveTempCADdata();  // makes data dirty
        }

        private void CopyCadDataRow_button_Click(object sender, EventArgs e)
        {
            ClipBoardRow = CadData_GridView.CurrentRow;
            SaveTempCADdata();  // makes data dirty
        }

        private void PasteCadDataRow_button_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < CadData_GridView.ColumnCount; i++)
            {
                CadData_GridView.CurrentRow.Cells[i].Value = ClipBoardRow.Cells[i].Value;
            }
            SaveTempCADdata();  // makes data dirty
        }

        // =================================================================================
        // JobData editing
        // =================================================================================
        private void JobData_GridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            MakeJobDataDirty();
        }

        private void JobData_GridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if(e.RowIndex == -1)
            {
                // user clicked header, most likely to sor for nozzles
                return;
            }
            if (JobData_GridView.CurrentCell.ColumnIndex == Jobdata_MethodColumn)
            {
                // For method, show a form with explanation texts
                MakeJobDataDirty();
                MethodSelectionForm MethodDialog = new MethodSelectionForm();
                MethodDialog.ShowCheckBox = false;
                MethodDialog.ShowDialog(this);
                if (MethodDialog.SelectedMethod != "")
                {
                    foreach (DataGridViewCell cell in JobData_GridView.SelectedCells)
                    {
                        JobData_GridView.Rows[cell.RowIndex].Cells[2].Value = MethodDialog.SelectedMethod;
                    }
                }
                Update_GridView(JobData_GridView);
                return;
            };

            if (JobData_GridView.CurrentCell.ColumnIndex == Jobdata_MethodParametersColumn)
            {
                // For method parameter, show the tape selection form if method is "place" 
                MakeJobDataDirty();
                string TapeID;
                int TapeNo;
                int row = JobData_GridView.CurrentCell.RowIndex;
                if ((JobData_GridView.Rows[row].Cells["GroupMethod"].Value.ToString() == "Place") ||
                     (JobData_GridView.Rows[row].Cells["GroupMethod"].Value.ToString() == "Place Assisted") ||
                     (JobData_GridView.Rows[row].Cells["GroupMethod"].Value.ToString() == "Place Fast"))
                {
                    TapeID = SelectTape("Select tape for " + JobData_GridView.Rows[row].Cells["ComponentType"].Value.ToString());
                    if (TapeID=="none")
                    {
                        // user closed it
                        return;
                    }
                    JobData_GridView.Rows[row].Cells["MethodParamAllComponents"].Value = TapeID;
                    if (Tapes.IdValidates_m(TapeID, out TapeNo))
                    {
                        JobData_GridView.Rows[row].Cells["JobDataNozzle_Column"].Value = 
                            Tapes_dataGridView.Rows[TapeNo].Cells["Nozzle_Column"].Value.ToString();
                    }
                    Update_GridView(JobData_GridView);
                    return;
                }
            }
        }

        // If components are edited, update count automatically
        private void JobData_GridView_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            int col = JobData_GridView.CurrentCell.ColumnIndex;

            if (JobData_GridView.Columns[col].Name == "ComponentList" )
            {
                // components
                MakeJobDataDirty();
                List<String> Line = SplitCSV(JobData_GridView.CurrentCell.Value.ToString(), ',');
                int row = JobData_GridView.CurrentCell.RowIndex;
                JobData_GridView.Rows[row].Cells["ComponentCount"].Value = Line.Count.ToString();
                Update_GridView(JobData_GridView);
            }
        }


        // =================================================================================
        // Do someting to a group of components:
        // =================================================================================
        // Several rows are selected at Job data:

        private async void PlaceThese_button_Click(object sender, EventArgs e)
        {
            this.ActiveControl = null; // User might need press enter during the process, which would run this again...
            if (!await PrepareToPlace_mAsync())
            {
                ShowMessageBox(
                    "Placement operation failed, nothing done.",
                    "Placement failed",
                    MessageBoxButtons.OK
                 );
                return;
            }

            DataGridViewRow Row;
            bool DoRow = false;
            // Get first row #...
            int FirstRow;
            for (FirstRow = 0; FirstRow < JobData_GridView.RowCount; FirstRow++)
            {
                Row = JobData_GridView.Rows[FirstRow];
                DoRow = false;
                foreach (DataGridViewCell oneCell in Row.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }
                if (DoRow)
                    break;
            }
            if (!DoRow)
            {
                CleanupPlacement(true);
                ShowMessageBox(
                    "Nothing selected, nothing done.",
                    "Done",
                    MessageBoxButtons.OK);
                return;
            };
            // ... so that we can put next row label in place:
            NextGroup_label.Text = JobData_GridView.Rows[FirstRow].Cells["ComponentType"].Value.ToString() + " (" +
                    JobData_GridView.Rows[FirstRow].Cells["ComponentCount"].Value.ToString() + " pcs.)";

            // We know there is something to do, NextGroup_label is updated. Place all rows:
            for (int CurrentRow = 0; CurrentRow < JobData_GridView.RowCount; CurrentRow++)
            {
                // Find the Row we need to do now:
                Row = JobData_GridView.Rows[CurrentRow];
                DoRow = false;
                foreach (DataGridViewCell oneCell in Row.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }

                if (!DoRow)
                {
                    continue;
                };
                // Handle labels:
                PreviousGroup_label.Text = CurrentGroup_label.Text;
                CurrentGroup_label.Text = NextGroup_label.Text;
                // See if there is something more to do, so the "Next" label is corrently updated:
                bool NotLast = false;
                int NextRow;
                for (NextRow = CurrentRow + 1; NextRow < JobData_GridView.RowCount; NextRow++)
                {
                    foreach (DataGridViewCell someCell in JobData_GridView.Rows[NextRow].Cells)
                    {
                        if (someCell.Selected)
                        {
                            NotLast = true;
                            break;
                        }
                    }
                    if (NotLast)
                        break;
                }
                if (NotLast)
                {
                    NextGroup_label.Text = JobData_GridView.Rows[NextRow].Cells["ComponentType"].Value.ToString() + " (" +
                                            JobData_GridView.Rows[NextRow].Cells["ComponentCount"].Value.ToString() + " pcs.)";
                }
                else
                {
                    NextGroup_label.Text = "--";
                };

                // Labels are updated, place the row:
                if (!await PlaceRow_mAsync(CurrentRow))
                {
                    ShowMessageBox(
                        "Placement operation failed. Review job status.",
                        "Placement failed",
                        MessageBoxButtons.OK);
                    CleanupPlacement(false);
                    return;
                }
            };

            CleanupPlacement(true);
            ShowMessageBox(
                "Selected components succesfully placed.",
                "Done",
                MessageBoxButtons.OK);
        }


        // =================================================================================
        // This routine places selected component(s) from CAD data grid view.
        // The mechanism is the same as from job data: a temp row is addded to job data
        // and then that row is processed.
        private async void PlaceOne_button_Click(object sender, EventArgs e)
        {
            // Do we need to do something in the first place?
            // is something actually selected?
            if (CadData_GridView.SelectedCells.Count == 0)
            {
                DisplayText("Nothing selected.");
                return;
            };
            // Are the components already placed?
            DataGridViewRow CadRow;
            bool DoSomething = false;
            foreach (DataGridViewCell oneCell in CadData_GridView.SelectedCells)
            {
                DataGridViewCheckBoxCell cell = CadData_GridView.Rows[oneCell.RowIndex].Cells["Placed_column"] as DataGridViewCheckBoxCell;
                if (cell.Value != null)
                {
                    if (cell.Value.ToString().ToLower() == "false")
                    {
                        DoSomething = true;
                        break;
                    }
                }
            }
            if (!DoSomething)
            {
                DisplayText("Selected component(s) already placed.", KnownColor.DarkRed);
                return;
            }

            // OK, we actually need to do something.
            // Preparations:
            if (!await PrepareToPlace_mAsync())
            {
                ShowMessageBox(
                    "Placement operation failed, nothing done.",
                    "Placement failed",
                    MessageBoxButtons.OK
                 );
                return;
            };

            // if a cell is selected on a row, place that component:
            bool DoRow = false;
            bool ok = true;
            for (int CadRowNo = 0; CadRowNo < CadData_GridView.RowCount; CadRowNo++)
            {
                CadRow = CadData_GridView.Rows[CadRowNo];
                DoRow = false;
                foreach (DataGridViewCell oneCell in CadRow.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }
                if (!DoRow)
                {
                    continue;
                };
                // Found something to do. Find the row from Job data
                string component = CadRow.Cells["Component"].Value.ToString();
                int JobRowNo;
                for (JobRowNo = 0; JobRowNo < JobData_GridView.RowCount; JobRowNo++)
                {
                    List<String> componentlist = SplitCSV(JobData_GridView.Rows[JobRowNo].Cells["ComponentList"].Value.ToString(), ',');
                    if (componentlist.Contains(component))
                    {
                        break;
                    }
                }
                if (JobRowNo >= JobData_GridView.RowCount)
                {
                    Machine.Pump.PumpOff();
                    ShowMessageBox(
                        "Did not find " + component + " from Job data.",
                        "Job data error",
                        MessageBoxButtons.OK);
                    return;
                }

                // is the component already placed?
                DataGridViewCheckBoxCell cell = CadData_GridView.Rows[CadRowNo].Cells["Placed_column"] as DataGridViewCheckBoxCell;
                if (cell.Value != null)
                {
                    if (cell.Value.ToString() == "True")
                    {
                        DisplayText(Component + " already placed");
                        break;
                    }
                }

                // Make a copy of it to the end of the Job data grid view
                JobData_GridView.Rows.Add();
                int LastRowNo = JobData_GridView.Rows.Count - 1;
                for (int i = 0; i < JobData_GridView.ColumnCount; i++)
                {
                    JobData_GridView.Rows[LastRowNo].Cells[i].Value = JobData_GridView.Rows[JobRowNo].Cells[i].Value;
                }

                // Make the copy row to have only one component on it
                JobData_GridView.Rows[LastRowNo].Cells["ComponentCount"].Value = "1";
                JobData_GridView.Rows[LastRowNo].Cells["ComponentList"].Value = component;
                // Update labels
                PreviousGroup_label.Text = CurrentGroup_label.Text;
                CurrentGroup_label.Text = JobData_GridView.Rows[LastRowNo].Cells["ComponentType"].Value.ToString() + " (1 pcs.)";

                // Place that row
                ok = await PlaceRow_mAsync(LastRowNo);
                // delete the row
                JobData_GridView.Rows.RemoveAt(LastRowNo);
                if (!ok)
                {
                    break;
                }
            }
            CleanupPlacement(ok);
            Update_GridView(JobData_GridView);
            SaveTempCADdata();
        }

        // =================================================================================
        // Checks if the component is placed already
        // returns success of operation, sets palced status to placed
        private bool AlreadyPlaced_m(string component, ref bool placed)
        {
            string comp;
            // find the row
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                comp = Row.Cells["Component"].Value.ToString();
                if (Row.Cells["Component"].Value.ToString()==component)
                {
                    DataGridViewCheckBoxCell cell = Row.Cells["Placed_column"] as DataGridViewCheckBoxCell;
                    if (cell.Value != null)
                    {
                        placed = (cell.Value.ToString().ToLower() == "true");
                    }
                    else
                    {
                        placed = false;
                    }
                    return true;
                }
            }
            ShowMessageBox(
                "Component " + component + "not found in CAD data. CAD data and job data don't match",
                "CAD data vs job data mismatch",
                MessageBoxButtons.OK);
            return false;
        }

        // =================================================================================
        // This routine places the [index] row from Job data grid view:
        private async Task<bool> PlaceRow_mAsync(int RowNo)
        {
            DisplayText("PlaceRow_m(" + RowNo.ToString() + ")", KnownColor.Blue);
            // Select the row and keep it visible
            JobData_GridView.Rows[RowNo].Selected = true;
            HandleGridScrolling(false, JobData_GridView);

            string[] Components;  // This array has the list of components to place:
            string NewID = "";
            bool RestoreRow = false;
            // RestoreRow: If we'll replace the jobdata row with new data at the end. 
            // There isn't any new data, unless method is "?".

            // If the Method is "?", ask the user what to do:
            DataGridViewRow tempRow = (DataGridViewRow)JobData_GridView.Rows[RowNo].Clone();
            if (JobData_GridView.Rows[RowNo].Cells["GroupMethod"].Value.ToString() == "?")
            {
                // We'll now get new data from the user. By default, we don't mess with the data we have
                // So, take a copy of current row
                RestoreRow = true;
                for (int i = 0; i < tempRow.Cells.Count; i++)
                {
                    tempRow.Cells[i].Value = JobData_GridView.Rows[RowNo].Cells[i].Value;
                }

                // Display Method selection form:
                bool UserHasNotDecided = false;
                string NewMethod;
                MethodSelectionForm MethodDialog = new MethodSelectionForm();
                do
                {
                    MethodDialog.ShowCheckBox = true;
                    MethodDialog.HeaderString = CurrentGroup_label.Text;
                    MethodDialog.ShowDialog(this);
                    if (Setting.Placement_UpdateJobGridAtRuntime)
                    {
                        RestoreRow = false;
                    };
                    NewMethod = MethodDialog.SelectedMethod;
                    if ((NewMethod == "Place") || (NewMethod == "Place Assisted") || (NewMethod == "Place Fast"))
                    {
                        // show the tape selection dialog
                        NewID = SelectTape("Select tape for " + JobData_GridView.Rows[RowNo].Cells["ComponentType"].Value.ToString());
                        if (!Setting.Placement_UpdateJobGridAtRuntime)
                        {
                            RestoreRow = true;   // In case user unselected it at the tape selection dialog
                        };

                        if (NewID == "none")
                        {
                            UserHasNotDecided = true; // User did select "Place", but didn't select TapeNumber, we'll ask again.
                        }
                        else if (NewID == "Ignore")
                        {
                            NewMethod = "Ignore";
                            NewID = "";
                        }
                        else if (NewID == "Abort")
                        {
                            NewMethod = "Ignore";
                            NewID = "";
                            Machine.Move.AbortPlacement = true;
                            Machine.Move.AbortPlacementShown = true;
                            RestoreRow = true;		// something went astray, keep method at "?"
                        }
                    }

                } while (UserHasNotDecided);
                if (NewMethod == "")
                {
                    return false;   // user pressed x
                }
                // Put the values to JobData_GridView
                JobData_GridView.Rows[RowNo].Cells["GroupMethod"].Value = NewMethod;
                JobData_GridView.Rows[RowNo].Cells["MethodParamAllComponents"].Value = NewID;
                Update_GridView(JobData_GridView);
                MethodDialog.Dispose();
            }
            // Method is now selected, even if it was ?. If user quits the operation, PlaceComponent_m() notices.

            // The place operation does not necessarily have any components for it (such as a manual nozzle change).
            // Make sure there is valid data at ComponentList anyway.
            if (JobData_GridView.Rows[RowNo].Cells["ComponentList"].Value == null)
            {
                JobData_GridView.Rows[RowNo].Cells["ComponentList"].Value = "--";
            };
            if (JobData_GridView.Rows[RowNo].Cells["ComponentList"].Value.ToString() == "--")
            {
                Components = new string[] { "--" };
            }
            else
            {
                Components = JobData_GridView.Rows[RowNo].Cells["ComponentList"].Value.ToString().Split(',');
            };
            bool ReturnValue = true;

            // Prepare for placement
            string method = JobData_GridView.Rows[RowNo].Cells["GroupMethod"].Value.ToString();
            int nozzle;
            // Check, that the row isn't placed already
            bool EverythingPlaced = true;
            bool thisPlaced = true;
            int placedCount = 0;
            if ((method == "Place Fast") || (method == "Place") || (method == "LoosePart") || (method == "LoosePart Assisted") || (method == "Place Assisted"))
            {
                foreach (string component in Components)
                {
                    if (!AlreadyPlaced_m(component, ref thisPlaced))
                    {
                        return false;
                    }
                    if (!thisPlaced)
                    {
                        EverythingPlaced = false;
                    }
                    else
                    {
                        placedCount++;
                    }
                }
                if (EverythingPlaced)
                {
                    DisplayText("All components on row " + JobData_GridView.Rows[RowNo].Cells["ComponentType"].Value.ToString() + " already placed.", KnownColor.DarkRed);
                    return true;
                }
            }
            // Check nozzle, change if needed
            // if we are using a method that potentially needs a nozzle and automatic change is enabled:
            if (
                ((method == "Place Fast") || (method == "Place") || (method == "LoosePart") || (method == "LoosePart Assisted") || (method == "Place Assisted"))  
                && Setting.Nozzles_Enabled) 
            {
                if (JobData_GridView.Rows[RowNo].Cells["JobDataNozzle_Column"].Value == null)
                {
                    nozzle = Setting.Nozzles_default;
                }
                else
                {
                    if (!int.TryParse(JobData_GridView.Rows[RowNo].Cells["JobDataNozzle_Column"].Value.ToString(), out nozzle))
                    {
                        ShowMessageBox(
                            "Bad data at Nozzle column for " + JobData_GridView.Rows[RowNo].Cells["ComponentType"].Value.ToString(),
                            "Nozzle?",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    if ((nozzle<1) || (nozzle > Setting.Nozzles_count))
                    {
                        ShowMessageBox(
                            "Invalid value at Nozzle column for " + JobData_GridView.Rows[RowNo].Cells["ComponentType"].Value.ToString(),
                            "Nozzle?",
                            MessageBoxButtons.OK);
                        return false;
                    }
                }
                if (!await ChangeNozzle_mAsync(nozzle))
                {
                    return false;
                }
            }

            bool FirstInRow = true;
            if (method == "Place Fast")
            {
                string TapeID = JobData_GridView.Rows[RowNo].Cells["MethodParamAllComponents"].Value.ToString();
                int count;
                if (!int.TryParse(JobData_GridView.Rows[RowNo].Cells["ComponentCount"].Value.ToString(), out count))
                {
                    ShowMessageBox(
                        "Bad data at component count",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                }
                if(count <= placedCount)
                {
                    count = 1;
                }
                else
                {
                    count -= placedCount;
                }
                if (!await Tapes.PrepareForFastPlacement_mAsync(TapeID, count))
                {
                    return false;
                }
                Tapes.FastParametersOk = true;
            }
            // Place parts:
            foreach (string Component in Components)
            {
                if (!await PlaceComponent_mAsync(Component, RowNo, FirstInRow))
                {
                    JobData_GridView.Rows[RowNo].Selected = false;
                    ReturnValue = false;
                    Tapes.FastParametersOk = false;
                    break;
                };
                FirstInRow = false;
            };
            Tapes.FastParametersOk = false;

            // restore the row if needed
            if (RestoreRow)
            {
                for (int i = 0; i < tempRow.Cells.Count; i++)
                {
                    JobData_GridView.Rows[RowNo].Cells[i].Value = tempRow.Cells[i].Value;
                }
                Update_GridView(JobData_GridView);
            };

            JobData_GridView.Rows[RowNo].Selected = false;
            return ReturnValue;
        }


        // =================================================================================
        // All rows:
        private async void PlaceAll_button_Click(object sender, EventArgs e)
        {

            if (!await PrepareToPlace_mAsync())
            {
                ShowMessageBox(
                    "Placement operation failed, nothing done.",
                    "Placement failed",
                    MessageBoxButtons.OK
                 );
                return;
            }
            PreviousGroup_label.Text = "--";
            CurrentGroup_label.Text = "--";
            NextGroup_label.Text = JobData_GridView.Rows[0].Cells["ComponentType"].Value.ToString() + " (" +
                JobData_GridView.Rows[0].Cells["ComponentCount"].Value.ToString() + " pcs.)";

            for (int i = 0; i < JobData_GridView.RowCount; i++)
            {
                PreviousGroup_label.Text = CurrentGroup_label.Text;
                CurrentGroup_label.Text = NextGroup_label.Text;
                if (i < (JobData_GridView.RowCount - 1))
                {
                    NextGroup_label.Text = JobData_GridView.Rows[i + 1].Cells["ComponentType"].Value.ToString() + " (" +
                        JobData_GridView.Rows[i + 1].Cells["ComponentCount"].Value.ToString() + " pcs.)";
                }
                else
                {
                    NextGroup_label.Text = "--";
                };

                if (!await PlaceRow_mAsync(i))
                {
                    break;
                }
            }

            CleanupPlacement(true);
            ShowMessageBox(
                "All components placed.",
                "Done",
                MessageBoxButtons.OK);
        }


        // =================================================================================

        // =================================================================================
        // ComponentDataValidates_m():
        // Checks that CAD data for a component is good. (Used by PlaceComponent_m() )

        private bool ComponentDataValidates_m(string Component, int CADdataRow, int GroupRow)
        {
            // Component exist. Footprint, X, Y and rotation data should be there:
            if (JobData_GridView.Rows[GroupRow].Cells["ComponentType"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No Footprint",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (CadData_GridView.Rows[CADdataRow].Cells["X_nominal"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No X data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (CadData_GridView.Rows[CADdataRow].Cells["Y_nominal"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No Y data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }

            if (CadData_GridView.Rows[CADdataRow].Cells["Rotation"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ": No Rotation data",
                        "Missing Data",
                        MessageBoxButtons.OK);
                return false;
            }
            else
            {
                double Rotation;
                if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Rotation"].Value.ToString().Replace(',', '.'), out Rotation))
                {
                    ShowMessageBox(
                        "Component " + Component + ": Bad Rotation data",
                        "Conversion Error",
                        MessageBoxButtons.OK);
                    return false;
                }
            }

            // Machine coordinate data should be also there, properly formatted:
            double X;
            double Y;
            double A;
            if ((!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["X_machine"].Value.ToString().Replace(',', '.'), out X))
                ||
                (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Y_machine"].Value.ToString().Replace(',', '.'), out Y)))
            {
                ShowMessageBox(
                    "Component " + Component + ", bad machine coordinate",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }
            if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Rotation_machine"].Value.ToString().Replace(',', '.'), out A))
            {
                ShowMessageBox(
                    "Bad data at Rotation, machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            };

            // Even if component is not specified, Method data should be there:
            if (JobData_GridView.Rows[GroupRow].Cells["GroupMethod"].Value == null)
            {
                ShowMessageBox(
                        "Component " + Component + ", No method data",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                return false;
            }
            return true;
        } // end of ComponentDataValidates_m()


        // =================================================================================
        // PlaceComponent_m()
        // This routine does the actual placement of a single component.
        // Component is the component name (Such as R15); based to this, we'll find the coordinates from CAD data
        // GroupRow is the row index to Job data grid view.
        // =================================================================================

        private async Task<bool> PlaceComponent_mAsync(string Component, int GroupRow, bool FirstInRow)
        {
            DisplayText("PlaceComponent_m: Component: " + Component + ", Row:" + GroupRow.ToString(), KnownColor.Blue);
            // Skip fiducials
            if (JobData_GridView.Rows[GroupRow].Cells["GroupMethod"].Value.ToString() == "Fiducials")
            {
                DisplayText("PlaceComponent_m(): Skip fiducials");
                return true;
            }
            // If component is specified, find its row in CAD data:
            int CADdataRow = -1;
            if (Component != "--")
            {
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    if (Row.Cells["Component"].Value.ToString() == Component)
                    {
                        CADdataRow = Row.Index;
                        break;
                    }
                }
                if (CADdataRow < 0)
                {
                    ShowMessageBox(
                        "Component " + Component + " data not found.",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                }
            }

            // Validate data:
            string Footprint = "n/a";
            string Xstr = "n/a";
            string Ystr = "n/a";
            string RotationStr = "n/a";
            double Rotation = Double.NaN;
            double X_machine = 0;
            double Y_machine = 0;
            double A_machine = 0;
            string Method = "";
            string MethodParameter = "";

            if (Component != "--") // if component exists:
            {
                if (SkipMeasurements_checkBox.Checked)
                {
                    // User wants to use the nominal coordinates. Copy the nominal to machine for this to happen:
                    double X;
                    if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["X_nominal"].Value.ToString().Replace(',', '.'), out X))
                    {
                        DisplayText("Bad data X nominal at component " + Component);
                    }
                    X = X + Setting.General_JigOffsetX + Setting.Job_Xoffset;
                    CadData_GridView.Rows[CADdataRow].Cells["X_machine"].Value = X.ToString();

                    double Y;
                    if (!double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out Y))
                    {
                        DisplayText("Bad data Y nominal at component " + Component);
                    }
                    Y = Y + Setting.General_JigOffsetY + Setting.Job_Yoffset;
                    CadData_GridView.Rows[CADdataRow].Cells["Y_machine"].Value = Y.ToString();

                    CadData_GridView.Rows[CADdataRow].Cells["Rotation_machine"].Value = CadData_GridView.Rows[CADdataRow].Cells["Rotation"].Value;
                }
                // check data consistency
                if (!ComponentDataValidates_m(Component, CADdataRow, GroupRow))
                {
                    return false;
                }
                // and fill values:
                Footprint = JobData_GridView.Rows[GroupRow].Cells["ComponentType"].Value.ToString();
                Xstr = CadData_GridView.Rows[CADdataRow].Cells["X_nominal"].Value.ToString();
                Ystr = CadData_GridView.Rows[CADdataRow].Cells["Y_nominal"].Value.ToString();
                RotationStr = CadData_GridView.Rows[CADdataRow].Cells["Rotation"].Value.ToString();
                double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Rotation"].Value.ToString().Replace(',', '.'), out Rotation);
                double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["X_machine"].Value.ToString().Replace(',', '.'), out X_machine);
                double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Y_machine"].Value.ToString().Replace(',', '.'), out Y_machine);
                double.TryParse(CadData_GridView.Rows[CADdataRow].Cells["Rotation_machine"].Value.ToString().Replace(',', '.'), out A_machine);
            }

            Method = JobData_GridView.Rows[GroupRow].Cells["GroupMethod"].Value.ToString();
            // Even if component is not specified, Method data should be there:
            // it is not an error if method does not have parameters.
            if (JobData_GridView.Rows[GroupRow].Cells["MethodParamAllComponents"].Value != null)
            {
                MethodParameter = JobData_GridView.Rows[GroupRow].Cells["MethodParamAllComponents"].Value.ToString();
            };

            // Data is now validated, all variables have values that check out. Place the component.
            // Update "Now placing" labels:
            if ((Method == "LoosePart") || (Method == "LoosePart Assisted") || (Method == "Place") || (Method == "Place Assisted") || (Method == "Place Fast"))
            {
                PlacedComponent_label.Text = Component;
                PlacedComponent_label.Update();
                PlacedValue_label.Text = Footprint;
                PlacedValue_label.Update();
                PlacedX_label.Text = Xstr;
                PlacedX_label.Update();
                PlacedY_label.Text = Ystr;
                PlacedY_label.Update();
                PlacedRotation_label.Text = RotationStr;
                PlacedRotation_label.Update();
                MachineCoords_label.Text = "( " +
                    CadData_GridView.Rows[CADdataRow].Cells["X_machine"].Value.ToString() + ", " +
                    CadData_GridView.Rows[CADdataRow].Cells["Y_machine"].Value.ToString() + " )";
                MachineCoords_label.Update();
            };

            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.Move.AbortPlacement = false;
                return false;
            }

            // Component is at CadData_GridView.Rows[CADdataRow]. 
            // What to do to it is at  JobData_GridView.Rows[GroupRow].

            int time;
            switch (Method)
            {
                case "?":
                    ShowMessageBox(
                        "Method is ? at run time",
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;

                case "Pause":
                    ShowMessageBox(
                        "Job pause, click OK to continue.",
                        "Pause",
                        MessageBoxButtons.OK);
                    return true;

                case "DownCam Snapshot":
                case "UpCam Snapshot":
                case "LoosePart":
                case "LoosePart Assisted":
                case "Place Fast":
                case "Place":
                case "Place Assisted":
                    if (Component == "--")
                    {
                        ShowMessageBox(
                            "Attempt to \"place\" non-existing component(\"--\")",
                            "Data error",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    DataGridViewCheckBoxCell cell = CadData_GridView.Rows[CADdataRow].Cells["Placed_column"] as DataGridViewCheckBoxCell;
                    if (cell.Value != null)
                    {
                        if (cell.Value.ToString() == "True")
                        {
                            DisplayText(Component + " already placed");
                            break;
                        }
                    }
                    if (!await PlacePart_mAsync(CADdataRow, GroupRow, X_machine, Y_machine, A_machine, FirstInRow))
                    {
                        return false;
                    }
                    else
                    {
                        CadData_GridView.Rows[CADdataRow].Cells["Placed_column"].Value = true;
                        SaveTempCADdata();
                    }
                    break;

                case "Change Nozzle":
                    if (!await ChangeNozzleManually_mAsync())
                        return false;
                    break;

                case "Recalibrate":
                    ValidMeasurement_checkBox.Checked = false;
                    if (!await PrepareToPlace_mAsync())
                        return false;
                    break;

                case "Ignore":
                    // Optionally, method parameter specifies pause time (used in debugging, I can't think of a real-life use case for that)
                    if (int.TryParse(JobData_GridView.Rows[GroupRow].Cells["MethodParamAllComponents"].Value.ToString(), out time))
                    {
                        for (int z = 0; z < time / 5; z++)
                        {
                            Application.DoEvents();  // keeping video running
                            Thread.Sleep(5);
                        }
                    };
                    return true;  // To next row...
                // break;

                case "Fiducials":
                    return true;  // To next row...
                // break; 


                // Used this for debugging, but is unreachable now
                case "Locate":
                    // Shows the component and its footprint(if known)...
                    await Machine.Move.MoveXYSafeAsync(X_machine, Y_machine);
                    if (!ShowFootPrint_m(CADdataRow))
                        return false;

                    // ... either for the time specified in method parameter
                    if (int.TryParse(JobData_GridView.Rows[GroupRow].Cells["MethodParamAllComponents"].Value.ToString(), out time))
                    {
                        for (int z = 0; z < time / 5; z++)
                        {
                            Application.DoEvents();  // keeping video running
                            Thread.Sleep(5);
                        }
                    }
                    else
                    {
                        // If no time specified, show a dialog:
                        ShowMessageBox(
                            "This is " + Component,
                            "Locate Component",
                            MessageBoxButtons.OK);
                    }
                    Machine.DownCamera.DrawBox = false;
                    break;

                default:
                    ShowMessageBox(
                        "No code for method " + JobData_GridView.Rows[GroupRow].Cells["GroupMethod"].Value.ToString(),
                        "Lazy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                // break;
            }
            return true;
        }

        // =================================================================================
        private async Task<bool> ChangeNozzleManually_mAsync()
        {
            Machine.CommsProcessor.SendCommand("{\"zsn\":0}");
            Thread.Sleep(50);
            Machine.CommsProcessor.SendCommand("{\"zsx\":0}");
            Thread.Sleep(50);
            Machine.Pump.PumpOff();
            Machine.Motors.MotorPowerOff();
            ShowMessageBox(
                "Change Nozzle now, press OK when done",
                "Nozzle change pause",
                MessageBoxButtons.OK);
            Machine.Motors.MotorPowerOn();
            Zlim_checkBox.Checked = true;
            Zhome_checkBox.Checked = true;
            Nozzle.Calibrated = false;
            ValidMeasurement_checkBox.Checked = false;
            Machine.CommsProcessor.SendCommand("{\"zsn\":3}");
            Thread.Sleep(50);
            Machine.CommsProcessor.SendCommand("{\"zsx\":2}");
            Thread.Sleep(50);
            if (!await MechanicalHoming_mAsync())
            {
                return false;
            }
            if (!await OpticalHoming_mAsync())
            {
                return false;
            }
            if (!await CalibrateNozzle_mAsync())
            {
                return false;
            }
            if (!await BuildMachineCoordinateData_mAsync())
            {
                return false;
            }
            Machine.Pump.PumpOn();
            return true;
        }

        // =================================================================================
        // This is called before any placement is done:
        // =================================================================================
        private async Task<bool> PrepareToPlace_mAsync()
        {
            if (JobData_GridView.RowCount < 1)
            {
                ShowMessageBox(
                    "No Job loaded.",
                    "No Job",
                    MessageBoxButtons.OK
                );
                return false;
            }

            if (Setting.Nozzles_Enabled)
            {
                if (Setting.Nozzles_current!=0)
                {
                    Nozzle.UseCalibration(Setting.Nozzles_current);
                }
            }
            else
            {
                if (!Nozzle.Calibrated || !ValidMeasurement_checkBox.Checked)
                {
                    CurrentGroup_label.Text = "Calibrating Nozzle";
                    if (!await CalibrateNozzle_mAsync())
                    {
                        CurrentGroup_label.Text = "--";
                        return false;
                    }
                }
            }

            if(!ValidMeasurement_checkBox.Checked)
            {
                CurrentGroup_label.Text = "Measuring PCB";
                if (!await BuildMachineCoordinateData_mAsync())
                {
                    CurrentGroup_label.Text = "--";
                    return false;
                }
            }

            Machine.Move.AbortPlacement = false;
            Machine.Move.AbortPlacementShown = false;
            PlaceThese_button.Capture = false;
            PlaceAll_button.Capture = false;
            JobData_GridView.ReadOnly = true;
            Machine.Pump.PumpOn();
            return true;
        }  // end PrepareToPlace_m

        // =================================================================================
        // This cleans up the UI after placement operations
        // =================================================================================
        private void CleanupPlacement(bool success)
        {
            PlacedComponent_label.Text = "--";
            PlacedValue_label.Text = "--";
            PlacedX_label.Text = "--";
            PlacedY_label.Text = "--";
            PlacedRotation_label.Text = "--";
            MachineCoords_label.Text = "( -- )";
            PreviousGroup_label.Text = "--";
            CurrentGroup_label.Text = "--";
            NextGroup_label.Text = "--";
            JobData_GridView.ReadOnly = false;
            Machine.Pump.PumpDefaultSetting();
            if (success)
            {
                Machine.Move.CNC_Park();  // if fail, it helps debugging if machine stays still
            }
            Machine.Vacuum.VacuumDefaultSetting();
        }


        // =================================================================================
        // PickUpThis_m(): Actual pickup, assumes Nozzle is on top of the part
        private async Task<bool> PickUpThis_mAsync(int TapeNumber)
        {
            string Z_str = Tapes_dataGridView.Rows[TapeNumber].Cells["Z_Pickup_Column"].Value.ToString();
            if (Z_str == "--")
            {
                DisplayText("PickUpPart_m(): Probing pickup Z", KnownColor.Blue);
                if (!await Nozzle_ProbeDown_mAsync())
                {
                    return false;
                }
                double Zpickup = Machine.Position.CurrentZ - Setting.General_PlacementBackOff + Setting.Placement_Depth;
                Tapes_dataGridView.Rows[TapeNumber].Cells["Z_Pickup_Column"].Value = Zpickup.ToString();
                DisplayText("PickUpPart_m(): Probed Z= " + Machine.Position.CurrentZ.ToString());
            }
            else
            {
                double Z;
                if (!double.TryParse(Z_str.Replace(',', '.'), out Z))
                {
                    ShowMessageBox(
                        "Bad pickup Z data at Tape #" + TapeNumber.ToString(),
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                };
                // Z += 0.5;
                DisplayText("PickUpPart_m(): Part pickup, Z" + Z.ToString(), KnownColor.Blue);
                if (!await Machine.Move.MoveZSafeAsync(Z))
                {
                    return false;
                }
            }
            Machine.Vacuum.VacuumOn();
            DisplayText("PickUpPart_m(): Nozzle up");
            if (!await Machine.Move.MoveZSafeAsync(0))
            {
                return false;
            }
            return true;
        }

        // ========================================================================================
        public async Task<bool> PickUpPartFast_mAsync(int TapeNum)
        {
            if (UseCoordinatesDirectly(TapeNum))
            {
                return (await PickUpPartWithDirectCoordinates_mAsync(TapeNum));
            }

            if (!Tapes.FastParametersOk)
            {
                ShowMessageBox(
                    "FastParameters not ok",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }

            // find the part location and go there:
            double PartX = 0.0;
            double PartY = 0.0;
            double A = 0.0;

            // GetPartLocationFromHolePosition_m calculates A correctly, but we have already figured out X and Y
            if (!Tapes.GetPartLocationFromHolePosition_m(TapeNum, Tapes.FastXpos, Tapes.FastYpos, out PartX, out PartY, out A))
            {
                ShowMessageBox(
                    "Can't find tape hole",
                    "Tape error",
                    MessageBoxButtons.OK
                );
            }

            // Now, PartX, PartY, A tell the position of the part.
            if (!Nozzle.Move_m(PartX, PartY, A))
            {
                return false;
            }
            if (!await PickUpThis_mAsync(TapeNum))
            {
                return false;
            }

            if (!Tapes.IncrementTape_Fast_m(TapeNum))
            {
                return false;
            }

            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.Move.AbortPlacement = false;
                return false;
            }
            return true;
        }


        // ========================================================================================
        public async Task CameraToFirstComponent(int TapeNum)
        {
            if (!await Tapes.PrepareForFastPlacement_mAsync("CC0603KRX7R9BB103", 1))
            {
                return;
            }

            // find the part location and go there:
            double PartX = 0.0;
            double PartY = 0.0;
            double A = 0.0;

            // GetPartLocationFromHolePosition_m calculates A correctly, but we have already figured out X and Y
            if (!Tapes.GetPartLocationFromHolePosition_m(TapeNum, Tapes.FastXpos, Tapes.FastYpos, out PartX, out PartY, out A))
            {
                ShowMessageBox(
                    "Can't find tape hole",
                    "Tape error",
                    MessageBoxButtons.OK
                );
            }

            await Machine.Move.MoveXYSafeAsync(PartX, PartY);
        }

        // =================================================================================
        // PickUpPartWithHoleMeasurement_m(): Picks next part from the tape, measuring the hole
        private async Task<bool> PickUpPartWithHoleMeasurement_mAsync(int TapeNumber)
        {
            if (UseCoordinatesDirectly(TapeNumber))
            {
                return (await PickUpPartWithDirectCoordinates_mAsync(TapeNumber));
            }

            // If this succeeds, we update next hole location at the end, but these values are measured at start
            double HoleX = 0;
            double HoleY = 0;
            DisplayText("PickUpPart_m(), tape no: " + TapeNumber.ToString());
            // Go to part location:
            Machine.Vacuum.VacuumOff();

            var result = await Tapes.GotoNextPartByMeasurement_m(TapeNumber);
            HoleX = result.Item2;
            HoleY = result.Item3;

            if (!result.Item1) 
            {
                return false;
            }
            // Pick it up:
            if (!await PickUpThis_mAsync(TapeNumber))
            {
                return false;
            }

            if (!Tapes.IncrementTape(TapeNumber, HoleX, HoleY))
            {
                return false;
            }

            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.Move.AbortPlacement = false;
                return false;
            }
            return true;
        }

        // =================================================================================
        // PickUpPartWithDirectCoordinates_m(): Picks next part, using coordinates directly
        // First, some helper functions:

        public bool UseCoordinatesDirectly(int TapeNum)
        {
            DataGridViewCheckBoxCell cell = Tapes_dataGridView.Rows[TapeNum].Cells["CoordinatesForParts_Column"] as DataGridViewCheckBoxCell;
            if (cell.Value != null)
            {
                if (cell.Value.ToString() == "True")
                {
                    return true;
                }
            }
            return false;
        }

        private bool FindPartWithDirectCoordinates_m(int TapeNum, out double X, out double Y, out double A, out bool increment)
        {
            X = 0.0;
            Y = 0.0;
            A = 0.0;
            increment = false;

            double FirstX;
            double FirstY;

            if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["FirstX_Column"].Value.ToString().Replace(',', '.'), out FirstX))
            {
                DisplayText("Bad data, X column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["FirstY_Column"].Value.ToString().Replace(',', '.'), out FirstY))
            {
                DisplayText("Bad data, Y column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            double LastX = 0.0;
            double LastY = 0.0;
            // Not all values need to be set, but if they are set, they need to be valid
            if (Tapes_dataGridView.Rows[TapeNum].Cells["LastX_Column"].Value != null)
            {
                if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["LastX_Column"].Value.ToString().Replace(',', '.'), out LastX))
                {
                    DisplayText("Bad data, Last X column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
            }
            if (Tapes_dataGridView.Rows[TapeNum].Cells["LastY_Column"].Value != null)
            {
                if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["LastY_Column"].Value.ToString().Replace(',', '.'), out LastY))
                {
                    DisplayText("Bad data, Last Y column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
            }
            double pitch;
            if (Tapes_dataGridView.Rows[TapeNum].Cells["Pitch_Column"].Value == null)
            {
                DisplayText("Bad data, Pitch column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["Pitch_Column"].Value.ToString().Replace(',', '.'), out pitch))
            {
                DisplayText("Bad data, Pitch column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }

            if (
                (Math.Abs(pitch) < 0.00001) ||
                ((Math.Abs(LastX) < 0.00001) && (Math.Abs(LastY) < 0.00001))
               )
            {
                // no increment, use first coordinates directly
                X = FirstX;
                Y = FirstY;
            }
            else
            {
                // more than one part defined, need to tell the caller the next column needs to be incremented if operation is succesful
                increment = true;
                // calculate location
                int increments;
                if (!int.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["NextPart_Column"].Value.ToString(), out increments))
                {
                    DisplayText("Bad data, Next column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
                increments = increments - 1;
                if (increments==0)
                {
                    X = FirstX;
                    Y = FirstY;
                }
                else
                {
                    double mag = Math.Sqrt(((LastX - FirstX) * (LastX - FirstX)) + ((LastY - FirstY) * (LastY - FirstY)));
                    double Xincr = (LastX - FirstX) * pitch / mag;
                    double Yincr = (LastY - FirstY) * pitch / mag;
                    X = FirstX + Xincr * increments;
                    Y = FirstY + Yincr * increments;
                }
            }

            if (Tapes_dataGridView.Rows[TapeNum].Cells["RotationDirect_Column"].Value != null)
            {
                if (!double.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["RotationDirect_Column"].Value.ToString().Replace(',', '.'), out A))
                {
                    DisplayText("Bad data, A correction column, Tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> PickUpPartWithDirectCoordinates_mAsync(int TapeNum)
        {
            double X;
            double Y;
            double A;
            bool increment;
            if (!FindPartWithDirectCoordinates_m(TapeNum, out X, out Y, out A, out increment))
            {
                return false;
            }
            DisplayText("PickUpPartWithDirectCoordinates_m(), tape " + Tapes_dataGridView.Rows[TapeNum].Cells["Id_Column"].Value.ToString()
                + ", X: " + X.ToString("0.000") + ", Y: " + Y.ToString("0.000") + ", A: " + A.ToString("0.000"));

            if (!Nozzle.Move_m(X, Y, A))
            {
                return false;
            }
            if (!await PickUpThis_mAsync(TapeNum))
            {
                return false;
            }

            if (increment)
            {
                int i;
                int.TryParse(Tapes_dataGridView.Rows[TapeNum].Cells["NextPart_Column"].Value.ToString(), out i);  // we know it parses
                i++;
                Tapes_dataGridView.Rows[TapeNum].Cells["NextPart_Column"].Value = i.ToString();
            }
            return true;
        }

        // =================================================================================
        // PutPartDown_m(): Puts part down at this position. 
        // If placement Z isn't known already, updates the tape info.
        private async Task<bool> PutPartDown_mAsync(int TapeNum)
        {
            string Z_str = Tapes_dataGridView.Rows[TapeNum].Cells["Z_Place_Column"].Value.ToString();
            if (Z_str == "--")
            {
                DisplayText("PutPartDown_m(): Probing placement Z", KnownColor.Blue);
                if (!await Nozzle_ProbeDown_mAsync())
                {
                    return false;
                };
                double Zplace = Machine.Position.CurrentZ - Setting.General_PlacementBackOff + Setting.Placement_Depth;
                Tapes_dataGridView.Rows[TapeNum].Cells["Z_Place_Column"].Value = Zplace.ToString();
                DisplayText("PutPartDown_m(): Probed placement Z= " + Machine.Position.CurrentZ.ToString());
            }
            else
            {
                double Z;
                if (!double.TryParse(Z_str.Replace(',', '.'), out Z))
                {
                    ShowMessageBox(
                        "Bad place Z data at Tape #" + TapeNum,
                        "Sloppy programmer error",
                        MessageBoxButtons.OK);
                    return false;
                };
                DisplayText("PlacePart_m(): Part down, Z" + Z.ToString(), KnownColor.Blue);
                if (!await Machine.Move.MoveZSafeAsync(Z))
                {
                    return false;
                }
            }
            //ShowMessageBox(
            //    "Debug: Nozzle down at component." + Component,
            //    "Debug",
            //    MessageBoxButtons.OK);
            DisplayText("PlacePart_m(): Nozzle up.");
            Machine.Vacuum.VacuumOff();
            if (!await Machine.Move.MoveZSafeAsync(0))  // back up
            {
                return false;
            }
            //ShowMessageBox(
            //    "Debug: Nozzle up.",
            //    "Debug",
            //    MessageBoxButtons.OK);
            return true;
        }

        // =================================================================================
        // 
        private async Task<bool> PutLoosePartDown_mAsync(bool Probe)
        {
            if (Probe)
            {
                DisplayText("PutLoosePartDown_m(): Probing placement Z");
                if (!await Nozzle_ProbeDown_mAsync())
                {
                    return false;
                }
                LoosePartPlaceZ = Machine.Position.CurrentZ - Setting.General_PlacementBackOff + Setting.Placement_Depth;
                DisplayText("PutLoosePartDown_m(): probed Z= " + Machine.Position.CurrentZ.ToString());
                DisplayText("PutLoosePartDown_m(): placement Z= " + LoosePartPlaceZ.ToString());
            }
            else
            {
                if (!await Machine.Move.MoveZSafeAsync(LoosePartPlaceZ))
                {
                    return false;
                }
            }
            DisplayText("PutLoosePartDown_m(): Nozzle up.");
            Machine.Vacuum.VacuumOff();
            if (!await Machine.Move.MoveZSafeAsync(0))  // back up
            {
                return false;
            }
            return true;
        }

        // ====================================================================================
        // Moves the part a certain distance above the placement position and allows manually
        // fine tuning of the position.
        // After done ENTER will trigger the final placement.
        //
        // MethodeParameter: For Loose Part Assisted => stop distance above board in mm 
        // ====================================================================================
        private async Task<bool> PutLoosePartDownAssisted_mAsync(bool Probe, string MethodeParameter)
        {
            double distance2pcb;

            // secure convert of string to double
            try
            {
                distance2pcb = Convert.ToDouble(MethodeParameter);
            }
            catch (FormatException)
            {
                distance2pcb = 2.0; // if convertion faild, set minimum stop distance to board
            }

            // we need a minimum stop distance
            if (distance2pcb < 2.0)
            {
                distance2pcb = 2.0;
            };

            if (!await Machine.Move.MoveZSafeAsync(Setting.General_ZtoPCB - distance2pcb))
            {
                return false;
            }

            DisplayText("Now fine tune part position. If done press ENTER");

            // Switch off slack compensation
            // save the present state to restore
            bool SaveSlackCompState = Machine.Move.SlackCompensation;
            bool SaveSlackCompAState = Machine.Move.SlackCompensationA;

            Machine.Move.SlackCompensation = false;
            Machine.Move.SlackCompensationA = false;

            Machine.Move.ZGuardOff(); // Allow nozzle movment while nozzle is down

            // Wait for enter key pressed. 
            EnterKeyHit = false;
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
                if (Machine.Move.AbortPlacement)
                {
                    ShowMessageBox(
                        "Operation aborted.",
                        "Operation aborted.",
                        MessageBoxButtons.OK);

                    Machine.Move.AbortPlacement = false;                    
                    
                    if (!await Machine.Move.MoveZSafeAsync(0))  // move nozzle to zero position
                    {
                        return false;
                    }

                    Machine.Move.ZGuardOn();

                    //Restore Slack Compensation state
                    Machine.Move.SlackCompensation = SaveSlackCompState;
                    Machine.Move.SlackCompensationA = SaveSlackCompAState;

                    return false;
                }
            } while (!EnterKeyHit);

            // fine tuning part position done, place now part on board
            if (!await Nozzle_ProbeDown_mAsync())
            {
                return false;
            }

            Machine.Vacuum.VacuumOff();
            Machine.Move.ZGuardOn();

            if (!await Machine.Move.MoveZSafeAsync(0))  // move nozzle to zero position
            {
                return false;
            }

            //Restore Slack Compensation states
            Machine.Move.SlackCompensation = SaveSlackCompState;
            Machine.Move.SlackCompensationA = SaveSlackCompAState;

            return true;
        }

        // =================================================================================
        // Actual placement 
        // =================================================================================
        private double LoosePartPickupZ = 0.0;
        private double LoosePartPlaceZ = 0.0;

        private double ReduceRotation(double rot)
        {
            // takes a rotation value, rotates it to +- 45degs.
            while (rot > 45.01)
            {
                rot = rot - 90;
            };
            while (rot < -45.01)
            {
                rot = rot + 90;
            }
            return rot;
        }

        private int MeasureClosestComponentInPx(out double X, out double Y, out double A, ICamera Cam, double Tolerance, int averages)
        {
            X = 0;
            double Xsum = 0;
            Y = 0;
            double Ysum = 0;
            A = 0.0;
            double Asum = 0.0;
            int count = 0;
            for (int i = 0; i < averages; i++)
            {
                if (Cam.GetClosestComponent(out X, out Y, out A, Tolerance) > 0)
                {
                    //DisplayText("GetClosestComponent, X: " + X.ToString() + ", Y: " + Y.ToString() + ", A: " + A.ToString()+
                    //    ", red: "+ReduceRotation(A).ToString());
                    count++;
                    Xsum += X;
                    Ysum += Y;
                    Asum += ReduceRotation(A);
                }
            }
            if (count == 0)
            {
                return 0;
            }
            X = Xsum / (float)count;
            Y = Ysum / (float)count;
            A = -Asum / (float)count;
            return count;
        }

        private async Task<bool> PickUpLoosePart_mAsync(bool Probe, bool Snapshot, int CADdataRow, string Component)
        {
            DisplayText("PickUpLoosePart_m: " + Probe.ToString() + ", " + Snapshot.ToString() + ", " + CADdataRow.ToString() + ", " + Component, KnownColor.Blue);
            if (!await Machine.Move.MoveXYSafeAsync(Setting.General_PickupCenterX, Setting.General_PickupCenterY))
            {
                return false;
            }

            // ask for it 
            string ComponentType = CadData_GridView.Rows[CADdataRow].Cells["Value_Footprint"].Value.ToString();
            DialogResult dialogResult = ShowMessageBox(
                "Put one " + ComponentType + " to the pickup location.",
                "Placing " + Component,
                MessageBoxButtons.OKCancel);
            if (dialogResult == DialogResult.Cancel)
            {
                return false;
            }

            // Find component
            double X = 0;
            double Y = 0;
            double A = 0.0;
            SetComponentsMeasurement();
            // If we don't get a look from straight up (more than 2mm off) we need to re-measure
            for (int i = 0; i < 2; i++)
            {
                // measure 5 averages, component must be 8.0mm from its place
                int count = MeasureClosestComponentInPx(out X, out Y, out A, Machine.DownCamera, (8.0 / Setting.DownCam_XmmPerPixel), 5);
                if (count == 0)
                {
                    ShowMessageBox(
                        "Could not see component",
                        "No component",
                        MessageBoxButtons.OK);
                    return false;
                }
                X = X * Setting.DownCam_XmmPerPixel;
                Y = -Y * Setting.DownCam_YmmPerPixel;
                DisplayText("PickUpLoosePart_m(): measurement " + i.ToString() + ", X: " + X.ToString() + ", Y: " + Y.ToString() + ", A: " + A.ToString());
                if ((Math.Abs(X) < 2.0) && (Math.Abs(Y) < 2.0))
                {
                    break;
                }
                else
                {
                    if (!await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX + X, Machine.Position.CurrentY + Y))
                    {
                        return false;
                    }
                }
            }
            // go exactly on top of component for user confidence and for snapshot to be at right place
            if (Snapshot)
            {
                if (!await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX + X, Machine.Position.CurrentY + Y))
                {
                    return false;
                }
                Machine.DownCamera.SnapshotRotation = A;
                Machine.DownCamera.BuildMeasurementFunctionsList(DowncamSnapshot_dataGridView);
                Machine.DownCamera.TakeSnapshot();
                Machine.DownCamera.Draw_Snapshot = true;
                X = 0.0;
                Y = 0.0;
            };

            if (!Nozzle.Move_m(Machine.Position.CurrentX + X, Machine.Position.CurrentY + Y, A))
            {
                return false;
            }
            // pick it up
            if (Probe)
            {
                DisplayText("PickUpLoosePart_m(): Probing pickup Z");
                if (!await Nozzle_ProbeDown_mAsync())
                {
                    Machine.DownCamera.Draw_Snapshot = true;
                    return false;
                }
                LoosePartPickupZ = Machine.Position.CurrentZ - Setting.General_PlacementBackOff + Setting.Placement_Depth;
                DisplayText("PickUpLoosePart_m(): Probed Z= " + Machine.Position.CurrentZ.ToString());
                DisplayText("PickUpLoosePart_m(): Pickup Z= " + LoosePartPickupZ.ToString());
            }
            else
            {
                DisplayText("PickUpLoosePart_m(): Part pickup, Z" + LoosePartPickupZ.ToString());
                if (!await Machine.Move.MoveZSafeAsync(LoosePartPickupZ))
                {
                    Machine.DownCamera.Draw_Snapshot = true;
                    return false;
                }
            }
            Machine.Vacuum.VacuumOn();
            DisplayText("PickUpLoosePart_m(): Nozzle up");
            if (!await Machine.Move.MoveZSafeAsync(0))
            {
                Machine.DownCamera.Draw_Snapshot = true;
                return false;
            }
            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.Move.AbortPlacement = false;
                return false;
            }
            return true;
        }

        // ========================================================================================
        // PlacePart_m(): This routine places a single component.
        // Component is at CadData_GridView.Rows[CADdataRow]. 
        // Tape ID and method are at JobDataRow.Rows[JobDataRow]. 
        // It should go to X, Y, A
        // CAD data is validated already.

        private async Task<bool> PlacePart_mAsync(int CADdataRow, int JobDataRow, double X, double Y, double A, bool FirstInRow)
        {
            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.Move.AbortPlacement = false;
                return false;
            };
            string id = JobData_GridView.Rows[JobDataRow].Cells["MethodParamAllComponents"].Value.ToString();
            string Method = JobData_GridView.Rows[JobDataRow].Cells["GroupMethod"].Value.ToString();

            int TapeNum = 0;
            string Component = CadData_GridView.Rows[CADdataRow].Cells["Component"].Value.ToString();
            DisplayText("PlacePart_m, Component: " + Component + ", CAD data row: " + CADdataRow.ToString(), KnownColor.Blue);

            // Preparing:
            switch (Method)
            {
                case "Place":
                case "Place Assisted":
                case "Place Fast":
                    if (!Tapes.IdValidates_m(id, out TapeNum))
                    {
                        return false;
                    }
                    // First component in a row: We don't necessarily know the correct pickup and placement height, even though there
                    // might be values from previous runs. (PCB and Nozzle might have changed.)
                    if (FirstInRow && MeasureZs_checkBox.Checked)
                    {
                        // Clear heights
                        Tapes_dataGridView.Rows[TapeNum].Cells["Z_Pickup_Column"].Value = "--";
                        Tapes_dataGridView.Rows[TapeNum].Cells["Z_Place_Column"].Value = "--";
                    };
                    break;

                default:
                    // Other methods don't use tapes, nothing to do here.
                    break;
            };

            // Pickup:
            switch (Method)
            {
                case "Place":
                case "Place Assisted":
                    if (!await PickUpPartWithHoleMeasurement_mAsync(TapeNum))
                    {
                        return false;
                    }
                    break;

                case "Place Fast":
                    if (!await PickUpPartFast_mAsync(TapeNum))
                    {
                        return false;
                    }
                    break;

                case "LoosePart":
                case "LoosePart Assisted":
                    if (!await PickUpLoosePart_mAsync(FirstInRow, false, CADdataRow, Component))
                    {
                        return false;
                    }
                    break;

                case "DownCam Snapshot":
                case "UpCam Snapshot":
                    if (!await PickUpLoosePart_mAsync(FirstInRow, true, CADdataRow, Component))
                    {
                        return false;
                    }
                    break;

                default:
                    ShowMessageBox(
                        "Unknown method at placement time: ",
                        "Operation aborted.",
                        MessageBoxButtons.OK);
                    return false;
                // break;
            };

            // Take the part to position. With snapshot, we want to fine tune it here:
            if (Method == "UpCam Snapshot")
            {
                DisplayText("PlacePart_m: take Upcam snapshot");
                // Take part to upcam
                if (!await Machine.Move.MoveXYASafeAsync(Setting.UpCam_PositionX, Setting.UpCam_PositionY, 0.0))
                {
                    return false;
                };
                if (!await Machine.Move.MoveZSafeAsync(LoosePartPickupZ))
                {
                    return false;
                };
                // take snapshot
                SelectCamera(Machine.UpCamera);
                UpCam_TakeSnapshot();
                SelectCamera(Machine.DownCamera);
                if (!await Machine.Move.MoveZSafeAsync(0.0))
                {
                    Machine.DownCamera.Draw_Snapshot = false;
                    return false;
                };
            }

            if ((Method == "DownCam Snapshot") || (Method == "UpCam Snapshot"))
            {
                DisplayText("PlacePart_m: Snapshot place");
                // Take cam to where we think part is going. This might not be exactly right.
                Machine.DownCamera.RotateSnapshot(A);
                if (!await Machine.Move.MoveXYASafeAsync(X, Y, A))
                {
                    // VacuumOff();  if the above failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                    Machine.DownCamera.Draw_Snapshot = false;
                    return false;
                };
                // Wait for enter key press. Before enter, user jogs the part and the image to right place
                EnterKeyHit = false;
                do
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                    if (Machine.Move.AbortPlacement)
                    {
                        if (!Machine.Move.AbortPlacementShown)
                        {
                            Machine.Move.AbortPlacementShown = true;
                            ShowMessageBox(
                                       "Operation aborted",
                                       "Operation aborted",
                                       MessageBoxButtons.OK);
                        }
                        Machine.Move.AbortPlacement = false;
                        return false;
                    }
                } while (!EnterKeyHit);
                Machine.DownCamera.Draw_Snapshot = false;
                X = Machine.Position.CurrentX;
                Y = Machine.Position.CurrentY;
                A = Machine.Position.CurrentA;
            };

            // Take the part to position:
            DisplayText("PlacePart_m: goto placement position");
            if (!Nozzle.Move_m(X, Y, A))
            {
                // VacuumOff();  if the above failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                Machine.DownCamera.Draw_Snapshot = false;
                Machine.UpCamera.Draw_Snapshot = false;
                return false;
            }

            // Place it:
            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.DownCamera.Draw_Snapshot = false;
                Machine.UpCamera.Draw_Snapshot = false;
                Machine.Move.AbortPlacement = false;
                return false;
            }

            switch (Method)
            {
                case "Place Assisted": // For parts from tapes allows manually correction of part position before placing them
                    // since for tape parts id contains the Tape index, we use here a fixed stop distance above board
                    if (!await PutLoosePartDownAssisted_mAsync(FirstInRow, "2.5"))  
                    {
                        // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                        return false;
                    }
                    break;
                case "LoosePart Assisted":
                    if (!await PutLoosePartDownAssisted_mAsync(FirstInRow, id)) // id contains stop distance above board
                    {
                        // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                        return false;
                    }
                    break;
                case "LoosePart":
                case "DownCam Snapshot":
                case "UpCam Snapshot":
                    Machine.DownCamera.Draw_Snapshot = false;
                    Machine.UpCamera.Draw_Snapshot = false;
                    if (!await PutLoosePartDown_mAsync(FirstInRow))
                    {
                        // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                        return false;
                    }
                    break;

                default:
                    if (!await PutPartDown_mAsync(TapeNum))
                    {
                        // VacuumOff();  if this failed CNC seems to be down; low chances that VacuumOff() would go thru either. 
                        Machine.DownCamera.Draw_Snapshot = false;
                        Machine.UpCamera.Draw_Snapshot = false;
                        return false;
                    }
                    break;
            };
            if (Machine.Move.AbortPlacement)
            {
                if (!Machine.Move.AbortPlacementShown)
                {
                    Machine.Move.AbortPlacementShown = true;
                    ShowMessageBox(
                               "Operation aborted",
                               "Operation aborted",
                               MessageBoxButtons.OK);
                }
                Machine.DownCamera.Draw_Snapshot = false;
                Machine.UpCamera.Draw_Snapshot = false;
                Machine.Move.AbortPlacement = false;
                return false;
            }
            return true;
        }


        // =================================================================================
        // GetCorrentionForPartAtNozzle():
        // takes a look from Upcam, sets the correction values for the part at Nozzle
        private bool GetCorrentionForPartAtNozzle(out double dX, out double dY, out double dA)
        {
            SelectCamera(Machine.UpCamera);
            dX = 0;
            dY = 0;
            dA = 0;

            if (!Machine.UpCamera.IsRunning())
            {
                SelectCamera(Machine.DownCamera);
                return false;
            }
            SetUpCamComponentsMeasurement();
            bool GoOn = false;
            bool result = false;
            while (!GoOn)
            {
                if (MeasureUpCamComponent(3.0, out dX, out dY, out dA))
                {
                    result = true;
                    GoOn = true;
                }
                else
                {
                    DialogResult dialogResult = ShowMessageBox(
                        "Did not get correction values from camera.\n Abort job / Retry / Place anyway?",
                        "Did not see component",
                        MessageBoxButtons.AbortRetryIgnore
                    );
                    if (dialogResult == DialogResult.Abort)
                    {
                        Machine.Move.AbortPlacement = true;
                        Machine.Move.AbortPlacementShown = true;
                        result = false;
                        GoOn = true;
                    }
                    else if (dialogResult == DialogResult.Retry)
                    {
                        GoOn = false;
                    }
                    else
                    {
                        // ignore
                        result = true;
                        GoOn = true;
                    }
                }
            };
            SelectCamera(Machine.DownCamera);
            return result;
        }

        private bool MeasureUpCamComponent(double Tolerance, out double dX, out double dY, out double dA)
        {
            double X = 0;
            double Xsum = 0;
            double Y = 0;
            double Ysum = 0;
            int count = 0;
            dX = 0;
            dY = 0;
            dA = 0;
            for (int i = 0; i < 5; i++)
            {
                if (Machine.UpCamera.GetClosestComponent(out X, out Y, out dA, Tolerance * Setting.UpCam_XmmPerPixel) > 0)
                {
                    count++;
                    Xsum += X;
                    Ysum += Y;
                }
            };
            if (count == 0)
            {
                return false;
            }
            X = Xsum / Setting.UpCam_XmmPerPixel;
            dX = X / (float)count;
            Y = -Y / Setting.UpCam_XmmPerPixel;
            dY = Y / (float)count;
            DisplayText("Component measurement:");
            DisplayText("X: " + X.ToString("0.000", CultureInfo.InvariantCulture) + " (" + count.ToString() + " results out of 5)");
            DisplayText("Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture));
            return true;
        }

        // =================================================================================
        // BuildMachineCoordinateData_m routine builds the machine coordinates data 
        // based on fiducials true (machine coord) location.
        // =================================================================================

        // =================================================================================
        // FindFiducials_m():
        // Finds the fiducials from job data (which by now, exists).
        // Sets FiducialsRow to indicate the row in JobData_GridView

        public bool FindFiducials_m(out int FiducialsRow)
        {
            // I) Find fiducials nominal placement data
            // Ia) Are fiducials indicated and only once?
            bool FiducialsFound = false;
            FiducialsRow = 0;

            for (int i = 0; i < JobData_GridView.RowCount; i++)
            {
                if (JobData_GridView.Rows[i].Cells["GroupMethod"].Value.ToString() == "Fiducials")
                {
                    if (FiducialsFound)
                    {
                        ShowMessageBox(
                            "Fiducials selected twice in Methods. Please fix!",
                            "Double Fiducials",
                            MessageBoxButtons.OK);
                        return false;
                    }
                    else
                    {
                        FiducialsFound = true;
                        FiducialsRow = i;
                    }
                }
            }
            if (!FiducialsFound)
            {
                // Ib) OriginalFiducials not pointed out yet. Find them automatically.
                // OriginalFiducials designators are FI*** or FID*** where *** is a number.
                string Fids;
                bool FidsOnThisRow = false;
                for (int i = 0; i < JobData_GridView.RowCount; i++)
                {
                    Fids = JobData_GridView.Rows[i].Cells["ComponentList"].Value.ToString();
                    if (Fids.StartsWith("FI", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (Char.IsDigit(Fids[2]))
                        {
                            FidsOnThisRow = true;
                        }
                        else if (Fids.StartsWith("FID", StringComparison.CurrentCultureIgnoreCase))
                        {
                            if (Char.IsDigit(Fids[3]))
                            {
                                FidsOnThisRow = true;
                            }
                        }
                    }
                    if (FidsOnThisRow)
                    {
                        if (FiducialsFound)
                        {
                            ShowMessageBox(
                                "Two group of components starting with FI*. Please indicate Fiducials",
                                "Double Fiducials",
                                MessageBoxButtons.OK);
                            return false;
                        }
                        else
                        {
                            FiducialsFound = true;
                            FiducialsRow = i;
                            FidsOnThisRow = false;
                        }
                    }
                }
                // If fids were found, the row is unique. Mark it:
                if (FiducialsFound)
                {
                    JobData_GridView.Rows[FiducialsRow].Cells["GroupMethod"].Value = "Fiducials";
                }
            }
            if (!FiducialsFound)
            {
                ShowMessageBox(
                    "Fiducials definitions not found.",
                    "Fiducials not defined",
                    MessageBoxButtons.OK);
                return false;
            }
            return true;
        }

        // =================================================================================
        // MeasureFiducial_m():
        // Takes the parameter nominal location and measures its physical location.
        // Assumes measurement parameters already set.

        private async Task<bool> MeasureFiducial_mAsync(PhysicalComponent fid)
        {
            if (!await Machine.Move.MoveXYSafeAsync(fid.X_nominal + Setting.Job_Xoffset + Setting.General_JigOffsetX,
                     fid.Y_nominal + Setting.Job_Yoffset + Setting.General_JigOffsetY))
            {
                return false;
            }
            FeatureType FidShape= FeatureType.Circle;
            if (Setting.Placement_FiducialsType==0)
            {
                FidShape = FeatureType.Circle;
            }
            else if (Setting.Placement_FiducialsType == 1)
            {
                FidShape = FeatureType.Rectangle;
            }
            else if (Setting.Placement_FiducialsType == 2)
            {
                FidShape = FeatureType.Both;
            }
            else
            {
                ShowMessageBox(
                    "MeasureFiducial_m, unknown fiducial type " + FidShape.ToString(),
                    "Programmer error:",
                    MessageBoxButtons.OK);
                return false;
            }
            double FindTolerance = Setting.Placement_FiducialTolerance;

            var result = await GoToFeatureLocation_mAsync(FidShape, FindTolerance, 0.1);

            if (!result.Item1)
            {
                ShowMessageBox(
                    "Finding fiducial: Can't regognize fiducial " + fid.Designator,
                    "No Circle found",
                    MessageBoxButtons.OK);
                return false;
            }

            double X = result.Item2;
            double Y = result.Item3;

            fid.X_machine = Machine.Position.CurrentX + X;
            fid.Y_machine = Machine.Position.CurrentY + Y;
            // For user confidence, show it:
            for (int i = 0; i < 50; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return true;
        }

        // =================================================================================
        // ValidateCADdata_m(): Checks, that supplied nominal values are good numbers:
        private bool ValidateCADdata_m()
        {
            double x;
            double y;
            double r;
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                if (!double.TryParse(Row.Cells["X_nominal"].Value.ToString().Replace(',', '.'), out x))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["Component"].Value.ToString() + " X coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                if (!double.TryParse(Row.Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out y))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["Component"].Value.ToString() + " Y coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                if (!double.TryParse(Row.Cells["Rotation"].Value.ToString().Replace(',', '.'), out r))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["Component"].Value.ToString() + " rotation data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                // DisplayText(Row.Cells["Component"].Value.ToString() + ": x= " + x.ToString() + ", y= " + y.ToString() + ", r= " + r.ToString());
            }
            return true;
        }


        // =================================================================================
        // BuildMachineCoordinateData_m():
        private async Task<bool> BuildMachineCoordinateData_mAsync()
        {
            double X_nom = 0;
            double Y_nom = 0;
            if (Setting.Placement_SkipMeasurements)
            {
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    // Cad data is validated.
                    double.TryParse(Row.Cells["X_nominal"].Value.ToString().Replace(',', '.'), out X_nom);
                    double.TryParse(Row.Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out Y_nom);
                    X_nom += Setting.General_JigOffsetX;
                    Y_nom += Setting.General_JigOffsetY;
                    Row.Cells["X_machine"].Value = X_nom.ToString("0.000", CultureInfo.InvariantCulture);
                    Row.Cells["Y_machine"].Value = Y_nom.ToString("0.000", CultureInfo.InvariantCulture);

                    Row.Cells["Rotation_machine"].Value = Row.Cells["Rotation"].Value;
                }
                // Refresh UI:
                Update_GridView(CadData_GridView);
                return true;
            };

            if (ValidMeasurement_checkBox.Checked)
            {
                return true;
            }
            // Check, that our data is good:
            if (!ValidateCADdata_m())
                return false;

            // Find the fiducials form CAD data:
            int FiducialsRow = 0;
            if (!FindFiducials_m(out FiducialsRow))
            {
                return false;
            }
            // OriginalFiducials are at JobData_GridView.Rows[FiducialsRow]
            string[] FiducialDesignators = JobData_GridView.Rows[FiducialsRow].Cells["ComponentList"].Value.ToString().Split(',');
            // Are there at least two?
            if (FiducialDesignators.Length < 2)
            {
                ShowMessageBox(
                    "Only one fiducial found.",
                    "Too few fiducials",
                    MessageBoxButtons.OK);
                return false;
            }
            // Get ready for position measurements
            DisplayText("SetFiducialsMeasurement");
            SetFiducialsMeasurement();
            // move them to our array, checking the data:
            PhysicalComponent[] Fiducials = new PhysicalComponent[FiducialDesignators.Length];  // store the data here
            // double X_nom = 0;
            // double Y_nom = 0;
            for (int i = 0; i < FiducialDesignators.Length; i++)  // for each fiducial in our OriginalFiducials array,
            {
                Fiducials[i] = new PhysicalComponent();
                Fiducials[i].Designator = FiducialDesignators[i];
                // find the fiducial in CAD data.
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    if (Row.Cells["Component"].Value.ToString() == FiducialDesignators[i]) // If this is the fiducial we want,
                    {
                        // Get its nominal position (value already checked).
                        double.TryParse(Row.Cells["X_nominal"].Value.ToString().Replace(',', '.'), out X_nom);
                        double.TryParse(Row.Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out Y_nom);
                        break;
                    }
                }
                Fiducials[i].X_nominal = X_nom;
                Fiducials[i].Y_nominal = Y_nom;
                // And measure it's true location:
                if (!await MeasureFiducial_mAsync(Fiducials[i]))
                {
                    return false;
                }
                if (Setting.Placement_FiducialConfirmation)
                {
                    DialogResult dialogResult = ShowMessageBox(
                        "Fiducial location OK?",
                        "Confirm Fiducial",
                        MessageBoxButtons.OKCancel
                    );
                    if (dialogResult == DialogResult.Cancel)
                    {
                        return false;
                    }
                }
                // We could put the machine data in place at this point. However, 
                // we don't, as if the algorithms below are correct, the data will not change more than measurement error.
                // During development, that is a good checkpoint.
            }

            // Find the homographic tranformation from CAD data (fiducials.nominal) to measured machine coordinates
            // (fiducials.machine):
            Transform transform = new Transform();
            int num_corr_points = Fiducials.Length;
            // Special case for 2 fiducials: Inject 3rd bogus fiducal, see below
            if (Fiducials.Length == 2)
            {
                num_corr_points = 3;
            }
            HomographyEstimation.Point[] nominals = new HomographyEstimation.Point[num_corr_points];
            HomographyEstimation.Point[] measured = new HomographyEstimation.Point[num_corr_points];
            // build point data arrays:
            for (int i = 0; i < Fiducials.Length; i++)
            {
                nominals[i].X = Fiducials[i].X_nominal;
                nominals[i].Y = Fiducials[i].Y_nominal;
                nominals[i].W = 1.0;
                measured[i].X = Fiducials[i].X_machine;
                measured[i].Y = Fiducials[i].Y_machine;
                measured[i].W = 1.0;
            }

            // Special case for 2 fiducials: Inject 3rd bogus fiducal
            // The 3rd fiducial is simply the 2nd fiducial rotated +90 degrees around the 1st fiducial
            // This should imply a shear and inversion free transformation, hence collapsing a full affine
            // solution space to a similarity transform
            if (Fiducials.Length == 2)
            {
                double deltaXnominal = nominals[1].X - nominals[0].X;
                double deltaYnominal = nominals[1].Y - nominals[0].Y;
                nominals[2].X = nominals[0].X - deltaYnominal;
                nominals[2].Y = nominals[0].Y + deltaXnominal;
                nominals[2].W = 1.0;

                double deltaXmeasured = measured[1].X - measured[0].X;
                double deltaYmeasured = measured[1].Y - measured[0].Y;
                measured[2].X = measured[0].X - deltaYmeasured;
                measured[2].Y = measured[0].Y + deltaXmeasured;
                measured[2].W = 1.0;
            }

            // find the tranformation
            bool res = transform.Estimate(nominals, measured, ErrorMetric.Transfer, 450, 450);  // the PCBs are smaller than 450mm
            if (!res)
            {
                ShowMessageBox(
                    "Transform estimation failed.",
                    "Data error",
                    MessageBoxButtons.OK);
                return false;
            }
            // Analyze the transform: Displacement is for debug. We could also calculate X & Y stretch and shear, but why bother.
            // Find out the displacement in the transform (where nominal origin ends up):
            HomographyEstimation.Point Loc, Loc2;
            Loc.X = 0.0;
            Loc.Y = 0.0;
            Loc.W = 1.0;
            Loc = transform.TransformPoint(Loc);
            Loc = Loc.NormalizeHomogeneous();
            DisplayText("Transform:");
            DisplayText("dX= " + (Loc.X).ToString());
            DisplayText("dY= " + Loc.Y.ToString());
            // We do need rotation. Find out by rotatíng a unit vector:
            Loc2.X = 1.0;
            Loc2.Y = 0.0;
            Loc2.W = 1.0;
            Loc2 = transform.TransformPoint(Loc2);
            Loc2 = Loc2.NormalizeHomogeneous();
            DisplayText("dX= " + Loc2.X.ToString());
            DisplayText("dY= " + Loc2.Y.ToString());
            double angle = Math.Asin(Loc2.Y - Loc.Y) * 180.0 / Math.PI; // in degrees
            DisplayText("angle= " + angle.ToString());

            // Calculate machine coordinates of all components:
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                // build a point from CAD data values
                double.TryParse(Row.Cells["X_nominal"].Value.ToString().Replace(',', '.'), out Loc.X);
                double.TryParse(Row.Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out Loc.Y);
                Loc.W = 1;
                // transform it
                Loc = transform.TransformPoint(Loc);
                Loc = Loc.NormalizeHomogeneous();
                // store calculated location values
                Row.Cells["X_machine"].Value = Loc.X.ToString("0.000", CultureInfo.InvariantCulture);
                Row.Cells["Y_machine"].Value = Loc.Y.ToString("0.000", CultureInfo.InvariantCulture);
                // handle rotation
                double rot;
                double.TryParse(Row.Cells["Rotation"].Value.ToString().Replace(',', '.'), out rot);
                rot += angle;
                while (rot > 360.0)
                {
                    rot -= 360.0;
                }
                while (rot < 0.0)
                {
                    rot += 360.0;
                }
                Row.Cells["Rotation_machine"].Value = rot.ToString("0.0000", CultureInfo.InvariantCulture);

            }
            // Refresh UI:
            Update_GridView(CadData_GridView);

            // For debug, compare fiducials true measured locations and the locations.
            // Also, if a fiducials moves more than 0.5mm, something is off (maybe there
            // was a via too close to a fid, and we picked that for a measurement). Warn the user!
            bool DataOk = true;
            double dx, dy;
            for (int i = 0; i < Fiducials.Length; i++)
            {
                Loc.X = Fiducials[i].X_nominal;
                Loc.Y = Fiducials[i].Y_nominal;
                Loc.W = 1.0;
                Loc = transform.TransformPoint(Loc);
                Loc = Loc.NormalizeHomogeneous();
                dx = Math.Abs(Loc.X - Fiducials[i].X_machine);
                dy = Math.Abs(Loc.Y - Fiducials[i].Y_machine);
                DisplayText(Fiducials[i].Designator +
                    ": x_meas= " + Fiducials[i].X_machine.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", x_calc= " + Loc.X.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", dx= " + dx.ToString("0.000", CultureInfo.InvariantCulture) +
                    ": y_meas= " + Fiducials[i].Y_machine.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", y_calc= " + Loc.Y.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", dy= " + dy.ToString("0.000", CultureInfo.InvariantCulture));
                if ((Math.Abs(dx) > 0.4) || (Math.Abs(dy) > 0.4))
                {
                    DataOk = false;
                }
            };
            if (!DataOk)
            {
                DisplayText(" ** A fiducial moved more than 0.4mm from its measured location");
                DisplayText(" ** when applied the same calculations than regular componets.");
                DisplayText(" ** (Maybe the camera picked a via instead of a fiducial?)");
                DisplayText(" ** Placement data is likely not good.");
                DialogResult dialogResult = ShowMessageBox(
                    "Nominal to machine trasnformation seems to be off. (See log window)",
                    "Cancel operation?",
                    MessageBoxButtons.OKCancel
                );
                if (dialogResult == DialogResult.Cancel)
                {
                    return false;
                }
            }
            // Done! 
            ValidMeasurement_checkBox.Checked = true;
            return true;
        }// end BuildMachineCoordinateData_m

        // =================================================================================
        // BuildMachineCoordinateData_m functions end
        // =================================================================================


        // =================================================================================
        private void PausePlacement_button_Click(object sender, EventArgs e)
        {
            DialogResult dialogResult = ShowMessageBox(
                "Placement Paused. Continue? (Cancel aborts)",
                "Placement Paused",
                MessageBoxButtons.OKCancel
            );
            if (dialogResult == DialogResult.Cancel)
            {
                Machine.Move.AbortPlacement = true;
                Machine.Move.AbortPlacementShown = true;
            }

        }

        private async void ManualNozzleChange_button_Click(object sender, EventArgs e)
        {
            await ChangeNozzleManually_mAsync();
        }


        // =================================================================================
        private void AbortPlacement_button_Click(object sender, EventArgs e)
        {
            Machine.Move.AbortPlacement = true;
            Machine.Move.AbortPlacementShown = false;
        }


        // =================================================================================
        private void JobOffsetX_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(JobOffsetX_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.Job_Xoffset = val;
                }
            }
        }

        private void JobOffsetX_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JobOffsetX_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Job_Xoffset = val;
            }
        }

        // =================================================================================
        private void JobOffsetY_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(JobOffsetY_textBox.Text.Replace(',', '.'), out val))
                {
                    Setting.Job_Yoffset = val;
                }
            }
        }

        private void JobOffsetY_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(JobOffsetY_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Job_Yoffset = val;
            }
        }

        // =================================================================================
        private async void ShowNominal_button_Click(object sender, EventArgs e)
        {
            double X;
            double Y;
            double A;

            DataGridViewCell cell = CadData_GridView.CurrentCell;
            if (cell == null)
            {
                return;  // no component selected
            }
            if (cell.OwningRow.Index < 0)
            {
                return;  // header row
            }
            if (!double.TryParse(cell.OwningRow.Cells["X_nominal"].Value.ToString().Replace(',', '.'), out X))
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Bad data at X_nominal",
                    "Bad data",
                    MessageBoxButtons.OK);
                return;
            }

            if (!double.TryParse(cell.OwningRow.Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out Y))
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Bad data at Y_nominal",
                    "Bad data",
                    MessageBoxButtons.OK);
                return;
            }

            if (!double.TryParse(cell.OwningRow.Cells["Rotation"].Value.ToString().Replace(',', '.'), out A))
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Bad data at Rotation",
                    "Bad data",
                    MessageBoxButtons.OK);
                return;
            }

            await Machine.Move.MoveXYSafeAsync(X + Setting.Job_Xoffset + Setting.General_JigOffsetX,
                Y + Setting.Job_Yoffset + Setting.General_JigOffsetY);
            Machine.DownCamera.ArrowAngle = A;
            Machine.DownCamera.DrawArrow = true;

            //ShowMessageBox(
            //    "This is " + cell.OwningRow.Cells["Component"].Value.ToString() + " approximate (nominal) location",
            //    "Locate Component",
            //    MessageBoxButtons.OK);
        }

        private void ShowNominal_button_Leave(object sender, EventArgs e)
        {
            Machine.DownCamera.DrawArrow = false;
        }


        // =================================================================================
        // Checks what is needed to check before doing something for a single component selected at "CAD data" table. 
        // If succesful, sets X, Y to component machine coordinates.
        private async Task<Tuple<bool, double, double>> PrepareSingleComponentOperationAsync()
        {
            double X = 0.0;
            double Y = 0.0;

            DataGridViewCell cell = CadData_GridView.CurrentCell;
            if (cell == null)
            {
                return new Tuple<bool, double, double>(false, X, Y);  // no component selected
            }
            if (cell.OwningRow.Index < 0)
            {
                return new Tuple<bool, double, double>(false, X, Y);  // header row
            }
            if (cell.OwningRow.Cells["X_machine"].Value.ToString() == "Nan")
            {
                DialogResult dialogResult = ShowMessageBox(
                    "Component locations not yet measured. Measure now?",
                    "Measure now?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    return new Tuple<bool, double, double>(false, X, Y);
                }
                if (!await BuildMachineCoordinateData_mAsync())
                {
                    return new Tuple<bool, double, double>(false, X, Y);
                }
            }

            if (!double.TryParse(cell.OwningRow.Cells["X_machine"].Value.ToString().Replace(',', '.'), out X))
            {
                ShowMessageBox(
                    "Bad data at X_machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return new Tuple<bool, double, double>(false, X, Y);
            }

            if (!double.TryParse(cell.OwningRow.Cells["Y_machine"].Value.ToString().Replace(',', '.'), out Y))
            {
                ShowMessageBox(
                    "Bad data at Y_machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return new Tuple<bool, double, double>(false, X, Y);
            }
            return new Tuple<bool, double, double>(true, X, Y);
        }

        // =================================================================================
        private async void ShowMachine_button_Click(object sender, EventArgs e)
        {
            double X;
            double Y;
            double A;

            var result = await PrepareSingleComponentOperationAsync();
            X = result.Item2;
            Y = result.Item3;

            if (!result.Item1)
            {
                return;
            }
            await Machine.Move.MoveXYSafeAsync(X, Y);
            if (!double.TryParse(CadData_GridView.CurrentCell.OwningRow.Cells["Rotation_machine"].Value.ToString().Replace(',', '.'), out A))
            {
                ShowMessageBox(
                    "Bad data at Rotation_machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return;
            }
            Machine.DownCamera.ArrowAngle = A;
            Machine.DownCamera.DrawArrow = true;

            //bool KnownComponent = ShowFootPrint_m(cell.OwningRow.Index);
            //ShowMessageBox(
            //    "This is " + cell.OwningRow.Cells["Component"].Value.ToString() + " location",
            //    "Locate Component",
            //    MessageBoxButtons.OK);
            //if (KnownComponent)
            //{
            //    Machine.DownCamera.DrawBox = false;
            //}
        }

        private void ShowMachine_button_Leave(object sender, EventArgs e)
        {
            Machine.DownCamera.DrawArrow = false;
        }


        // =================================================================================
        private async Task<Tuple<bool, double, double>> MeasurePositionErrorByFiducial_mAsync(double X, double Y)
        {

            // find nearest fiducial
            int FiducialsRow = 0;
            double errX = 0;
            double errY = 0;
            foreach (DataGridViewRow Row in JobData_GridView.Rows)
            {
                if (Row.Cells["GroupMethod"].Value.ToString() == "Fiducials")
                {
                    FiducialsRow = Row.Index;
                    break;
                }
            }
            if (FiducialsRow == 0)
            {
                ShowMessageBox(
                    "ResetPositionByFiducial: Fiducials not indicated",
                    "Missing data",
                    MessageBoxButtons.OK);
                return new Tuple<bool, double, double>(false, X, Y);
            }
            string[] FiducialDesignators = JobData_GridView.Rows[FiducialsRow].Cells["ComponentList"].Value.ToString().Split(',');
            double ShortestDistance = 10000;
            double X_fid = Double.NaN;
            double Y_fid = Double.NaN;
            double X_shortest = Double.NaN;
            double Y_shortest = Double.NaN;
            int i;
            for (i = 0; i < FiducialDesignators.Count(); i++)
            {
                foreach (DataGridViewRow Row in CadData_GridView.Rows)
                {
                    if (Row.Cells["Component"].Value.ToString() == FiducialDesignators[i])
                    {
                        if (!double.TryParse(Row.Cells["X_Machine"].Value.ToString().Replace(',', '.'), out X_fid))
                        {
                            ShowMessageBox(
                                "Problem with " + FiducialDesignators[i] + "X machine coordinate data",
                                "Bad data",
                                MessageBoxButtons.OK);
                            return new Tuple<bool, double, double>(false, X, Y);
                        };
                        if (!double.TryParse(Row.Cells["Y_Machine"].Value.ToString().Replace(',', '.'), out Y_fid))
                        {
                            ShowMessageBox(
                                "Problem with " + FiducialDesignators[i] + "Y machine coordinate data",
                                "Bad data",
                                MessageBoxButtons.OK);
                            return new Tuple<bool, double, double>(false, X, Y);
                        };
                        break;
                    }  // end "if this is the row of the current fiducial, ...
                }
                // This is the fid we want. It is measured to be at X_fid, Y_fid
                if (double.IsNaN(X_fid) || double.IsNaN(Y_fid))
                {
                    ShowMessageBox(
                        "Machine coord data for fiducial " + FiducialDesignators[i] + "not found",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return new Tuple<bool, double, double>(false, X, Y);
                }
                double dX = X_fid - X;
                double dY = Y_fid - Y;
                double Distance = Math.Sqrt(Math.Pow(dX, 2) + Math.Pow(dY, 2));

                if (Distance < ShortestDistance)
                {
                    X_shortest = X_fid;
                    Y_shortest = Y_fid;
                    ShortestDistance = Distance;
                }
            }
            if (double.IsNaN(X_shortest) || double.IsNaN(Y_shortest))
            {
                ShowMessageBox(
                    "Problem finding nearest fiducial",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return new Tuple<bool, double, double>(false, X, Y);
            };

            // go there
            await Machine.Move.MoveXYSafeAsync(X_shortest, Y_shortest);
            SetFiducialsMeasurement();

            for (int tries = 0; tries < 5; tries++)
            {
                // 3mm max. error
                int res = Machine.DownCamera.GetClosestCircle(out errX, out errY, 3.0 / Setting.DownCam_XmmPerPixel);
                if (res != 0)
                {
                    break;
                }
                if (tries >= 4)
                {
                    ShowMessageBox(
                        "Finding fiducial: Can't regognize fiducial " + FiducialDesignators[i],
                        "No Circle found",
                        MessageBoxButtons.OK);
                    return new Tuple<bool, double, double>(false, X, Y);
                }
            }
            errX = errX * Setting.DownCam_XmmPerPixel;
            errY = -errY * Setting.DownCam_YmmPerPixel;
            // and err_ now tell how much we are off.
            return new Tuple<bool, double, double>(true, X, Y);
        }

        // =================================================================================
        private async void ReMeasure_button_Click(object sender, EventArgs e)
        {
            ValidMeasurement_checkBox.Checked = false;
            ValidMeasurement_checkBox.Checked = await BuildMachineCoordinateData_mAsync();
            // CNC_Park();
        }

        private async void TestNozzleRecognition_button_Click(object sender, EventArgs e)
        {
            double X = Machine.Position.CurrentX;
            double Y = Machine.Position.CurrentY;
            await CalibrateNozzle_mAsync();
            await Machine.Move.MoveXYASafeAsync(X, Y, 0.0);
        }


        // =================================================================================
        private bool ShowFootPrint_m(int Row)
        {
            // Turns on drawing a box of component outline to Downcamera image, if component size is known
            string FootPrint = CadData_GridView.Rows[Row].Cells["Value_Footprint"].Value.ToString();
            double sizeX = 0.0;
            double sizeY = 0.0;
            bool found = false;

            foreach (DataGridViewRow SizeRow in ComponentData_dataGridView.Rows)
            {
                if (SizeRow.Cells["PartialName_column"].Value != null)
                {
                    if (FootPrint.Contains(SizeRow.Cells["PartialName_column"].Value.ToString()))
                    {
                        if (!double.TryParse(SizeRow.Cells["SizeX_column"].Value.ToString().Replace(',', '.'), out sizeX))
                        {
                            ShowMessageBox(
                                "Bad data at " + SizeRow.Cells["PartialName_column"].Value.ToString() + ", SizeX",
                                "Sloppy programmer error",
                                MessageBoxButtons.OK);
                            return false;
                        }
                        if (!double.TryParse(SizeRow.Cells["SizeY_column"].Value.ToString().Replace(',', '.'), out sizeY))
                        {
                            ShowMessageBox(
                                "Bad data at " + SizeRow.Cells["PartialName_column"].Value.ToString() + ", SizeY",
                                "Sloppy programmer error",
                                MessageBoxButtons.OK);
                            return false;
                        }
                        found = true;
                        break;
                    }
                }
            }
            if (!found)
            {
                return true;  // ok if there is no data on component
            }

            double rot;
            if (!double.TryParse(CadData_GridView.Rows[Row].Cells["Rotation_machine"].Value.ToString().Replace(',', '.'), out rot))
            {
                ShowMessageBox(
                    "Bad data at Rotation, machine",
                    "Sloppy programmer error",
                    MessageBoxButtons.OK);
                return false;
            }

            Machine.DownCamera.BoxSizeX = (int)Math.Round((sizeX) / Setting.DownCam_XmmPerPixel);
            Machine.DownCamera.BoxSizeY = (int)Math.Round((sizeY) / Setting.DownCam_YmmPerPixel);
            Machine.DownCamera.BoxRotationDeg = rot;
            Machine.DownCamera.DrawBox = true;
            return true;
        }

        private async void Demo_button_Click(object sender, EventArgs e)
        {
            DemoThread = new Thread(() => DemoWork());
            DemoRunning = true;
            await Machine.Move.MoveZSafeAsync(0.0);
            DemoThread.IsBackground = true;
            DemoThread.Start();
        }

        private void StopDemo_button_Click(object sender, EventArgs e)
        {
            DemoRunning = false;
        }

        private bool DemoRunning = false;
        private Thread DemoThread;

        private async void DemoWork()
        {
            /*
            double PCB_X = Setting.General_JigOffsetX + Setting.DownCam_NozzleOffsetX;
            double PCB_Y = Setting.General_JigOffsetY + Setting.DownCam_NozzleOffsetY;
            double HoleX;
            double HoleY;
            double PartX;
            double PartY;
            Random rnd = new Random();
            int a;
            int b;
            NumberStyles style = NumberStyles.AllowDecimalPoint;
            CultureInfo culture = CultureInfo.InvariantCulture;
            string s = Tapes_dataGridView.Rows[0].Cells["FirstX_Column"].Value.ToString();
            if (!double.TryParse(s, style, culture, out HoleX))
            {
                ShowMessageBox(
                    "Bad X data at Tape 1",
                    "Tape data error",
                    MessageBoxButtons.OK
                );
                return;
            }
            s = Tapes_dataGridView.Rows[0].Cells["FirstY_Column"].Value.ToString();
            if (!double.TryParse(s, style, culture, out HoleY))
            {
                ShowMessageBox(
                    "Bad Y data at Tape 1",
                    "Tape data error",
                    MessageBoxButtons.OK
                );
                return;
            }
            */
            // vacuum off, no UI update because of threading
            Machine.CommsProcessor.SendCommand("{\"gc\":\"M09\"}");
            Thread.Sleep(Setting.General_PickupReleaseTime);

            // PumpOn, off main thread
            Machine.CommsProcessor.SendCommand("{\"gc\":\"M03\"}");
            Thread.Sleep(500);  // this much to develop vacuum

            // BugWorkaround();
            while (DemoRunning)
            {
                /*
                // Simulate fast measurement. Last hole:
                if (!CNC_XY_m(HoleX + 6 * 4.0, HoleY)) goto err;
                Thread.Sleep(400);
                if (!DemoRunning) return;
                // First hole:
                if (!CNC_XY_m(HoleX, HoleY)) goto err;
                Thread.Sleep(400);
                if (!DemoRunning) return;
                // components
                for (int i = 0; i < 6; i++)
                {
                    // Nozzle to part:
                    PartX = HoleX + i * 4 - 2 + Setting.DownCam_NozzleOffsetX;
                    PartY = HoleY + 3.5 + Setting.DownCam_NozzleOffsetY;
                    if (!CNC_XY_m(PartX, PartY)) goto err;
                    if (!DemoRunning) return;
                    // Pick up
                    if (!CNC_Z_m(Setting.General_ZtoPCB)) goto err;
                    Thread.Sleep(Setting.General_PickupVacuumTime);
                    if (!CNC_Z_m(0.0)) goto err;
                    if (!DemoRunning) return;
                    // goto position
                    a = rnd.Next(10, 60);
                    b = rnd.Next(10, 60);
                    if (!CNC_XY_m(PCB_X + a, PCB_Y + b)) goto err;
                    if (!DemoRunning) return;
                    // place
                    if (!CNC_Z_m(Setting.General_ZtoPCB - 0.5)) goto err;
                    Thread.Sleep(Setting.General_PickupReleaseTime);
                    if (!CNC_Z_m(0.0)) goto err;
                    if (!DemoRunning) return;
                }
                */
                if (!DemoRunning) goto demoend;
                if (!await Demo_PickupAsync(100, 100, 0, 20)) goto demoend;
                if (!DemoRunning) goto demoend;
                if (!await Demo_PlaceAsync(200, 200, 0, 20)) goto demoend;
                if (!DemoRunning) goto demoend;
            }
        demoend:
            DemoRunning = false;
            // vacuum off, no UI update because of threading
            Machine.CommsProcessor.SendCommand("{\"gc\":\"M09\"}");
            Thread.Sleep(Setting.General_PickupReleaseTime);
            // pump off, off thread
            Machine.CommsProcessor.SendCommand("{\"gc\":\"M05\"}");
            Thread.Sleep(50);
        }


        private async Task<bool> Demo_PickupAsync(double X, double Y, double A, double Z)
        {
            if (!Nozzle.Move_m(X, Y, A)) return false;
            if (!await Machine.Move.MoveZSafeAsync(Z)) return false;
            // vacuum on, no UI update because of threading
            Machine.CommsProcessor.SendCommand("{\"gc\":\"M08\"}");
            Thread.Sleep(Setting.General_PickupVacuumTime);
            if (!await Machine.Move.MoveZSafeAsync(0)) return false;
            return true;
        }

        private async Task<bool> Demo_PlaceAsync(double X, double Y, double A, double Z)
        {
            if (!Nozzle.Move_m(X, Y, A)) return false;
            if (!await Machine.Move.MoveZSafeAsync(Z)) return false;
            // vacuum off, no UI update because of threading
            Machine.CommsProcessor.SendCommand("{\"gc\":\"M09\"}");
            Thread.Sleep(Setting.General_PickupReleaseTime);
            if (!await Machine.Move.MoveZSafeAsync(0)) return false;
            return true;
        }

        // =================================================================================
        // Panelizing
        // =================================================================================
        private void Panelize_button_Click(object sender, EventArgs e)
        {
            PanelizeForm PanelizeDialog = new PanelizeForm();
            PanelizeDialog.LoadDataGridEvent += LoadDataGrid;
            PanelizeDialog.SaveDataGridEvent += SaveDataGrid;
            PanelizeDialog.DataGridViewCopyEvent += DataGridViewCopy;
            PanelizeDialog.FillJobData_GridViewEvent += FillJobData_GridView;
            PanelizeDialog.FindFiducials_mEvent += FindFiducials_m;

            PanelizeDialog.CadData = CadData_GridView;
            PanelizeDialog.JobData = JobData_GridView;
            PanelizeDialog.ShowDialog(this);
            if (PanelizeDialog.OK == true)
            {
                DisplayText("Panelize ok.");
                Update_GridView(CadData_GridView);
                Update_GridView(JobData_GridView);
            }
        }

        // =================================================================================

        private void OmitNozzleCalibration_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_OmitNozzleCalibration = OmitNozzleCalibration_checkBox.Checked;

        }

        private void SkipMeasurements_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_SkipMeasurements = SkipMeasurements_checkBox.Checked;
        }


        #endregion Job page functions

        // =================================================================================
        //CAD data reading functions: Tries to understand different pick and place file formats
        // =================================================================================
        #region CAD data reading functions

        // =================================================================================
        // CADdataToMMs_m(): Data was in inches, convert to mms

        private bool CADdataToMMs_m()
        {
            double val;
            foreach (DataGridViewRow Row in CadData_GridView.Rows)
            {
                if (!double.TryParse(Row.Cells["X_nominal"].Value.ToString().Replace(',', '.'), out val))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["Component"].Value.ToString() + " X coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                Row.Cells["X_nominal"].Value = Math.Round((val * 25.4), 3).ToString();
                if (!double.TryParse(Row.Cells["Y_nominal"].Value.ToString().Replace(',', '.'), out val))
                {
                    ShowMessageBox(
                        "Problem with " + Row.Cells["Component"].Value.ToString() + " Y coordinate data",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                };
                Row.Cells["Y_nominal"].Value = Math.Round((val * 25.4), 3).ToString();
            }
            return true;
        }

        // =================================================================================
        // ParseKiCadData_m()
        // =================================================================================
        private bool ParseKiCadData_m(String[] AllLines)
        {
            // Convert KiCad data to regular CSV
            int i = 0;
            bool inches = false;
            // Skip headers until find one starting with "## "
            while (!(AllLines[i].StartsWith("## ")))
            {
                i++;
            };

            // inches vs mms
            if (AllLines[i++].Contains("inches"))  
            {
                inches = true;
            }
            i++; // skip the "Side" line
            List<string> KiCadLines = new List<string>();
            KiCadLines.Add(AllLines[i++].Substring(2));  // add header, skip the "# " start
            // add rest of the lines
            while (!(AllLines[i]).StartsWith("## End"))
            {
                KiCadLines.Add(AllLines[i++]);
            };
            // parse the data
            string[] KicadArr = KiCadLines.ToArray();
            if (!ParseCadData_m(KicadArr, true))
            {
                return false;
            };
            // convert to mm'f if needed
            if (inches)
            {
                return (CADdataToMMs_m());
            }
            else
            {
                return true;
            }
        }

        // =================================================================================
        // ParseCadData_m(): main function called from file open
        // =================================================================================

        // =================================================================================
        // FindDelimiter_m(): Tries to find the difference with comma and semicolon separated files  
        bool FindDelimiter_m(String Line, out char delimiter)
        {
            int commas = 0;
            foreach (char c in Line)
            {
                if (c == ',')
                {
                    commas++;
                }
            };
            int semicolons = 0;
            foreach (char c in Line)
            {
                if (c == ';')
                {
                    semicolons++;
                }
            };
            if ((commas == 0) && (semicolons > 4))
            {
                delimiter = ';';
                return true;
            };
            if ((semicolons == 0) && (commas > 4))
            {
                delimiter = ',';
                return true;
            };

            ShowMessageBox(
                "FileName header parse fail",
                "Data format error",
                MessageBoxButtons.OK
            );
            delimiter = ',';
            return false;
        }

        private bool ParseCadData_m(String[] AllLines, bool KiCad)
        {
            int PlacedIndex;
            bool PlacedDataPresent;
            int ComponentIndex;
            int ValueIndex;
            int FootPrintIndex;
            int X_Nominal_Index;
            int Y_Nominal_Index;
            int RotationIndex;
            int LayerIndex = -1;
            bool LayerDataPresent = false;
            int LineIndex = 0;
            int i;

            // Parse header. 
            string FirstLine = AllLines[0];
            if(FirstLine== "Altium Designer Pick and Place Locations")
            {
                // Altium17 file
                for (int ind = 0; ind < 11; ind++)
                {
                    AllLines[ind] = "";   // so these will get skipped in next step
                }
            }

            foreach (string s in AllLines)
            {
                // Skip empty lines and comment lines(starting with # or "//")
                if (s == "")
                {
                    LineIndex++;
                    continue;
                }
                if (s[0] == '#')
                {
                    LineIndex++;
                    continue;
                };
                if ((s.Length > 1) && (s[0] == '/') && (s[1] == '/'))
                {
                    LineIndex++;
                    continue;
                };
                break;
            };

            char delimiter;
            if (KiCad)
            {
                delimiter = ' ';
            }
            else
            {
                if (!FindDelimiter_m(AllLines[LineIndex], out delimiter))
                {
                    return false;
                };
            }

            List<String> Headers;

            if (KiCad)
            {
                Headers = SplitKiCadLine(AllLines[LineIndex++]);
            }
            else
            {
                 Headers = SplitCSV(AllLines[LineIndex++], delimiter);
           }

            for (i = 0; i < Headers.Count; i++)
            {
                if (Headers[i] == "Placed")
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                PlacedDataPresent= false;
            }
            else
            {
                PlacedDataPresent= true;
            }
            PlacedIndex = i;

            List<string> DesignatorList = new List<string> { "designator", "part", "ref", "refdes", "component" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (DesignatorList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component/Designator/Name not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            ComponentIndex = i;

            List<string> ValueList = new List<string> { "value", "val", "comment"};
            for (i = 0; i < Headers.Count; i++)
            {
                if (ValueList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component value/comment not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            ValueIndex = i;

            List<string> PackageList = new List<string> { "footprint", "package", "pattern" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (PackageList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component footprint/pattern not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            FootPrintIndex = i;

            List<string> XList = new List<string> { "x", "x (mm)", "Center-X(mm)", "PosX", "mid x" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (XList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component X not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            X_Nominal_Index = i;

            List<string> YList = new List<string> { "y", "y (mm)", "Center-y(mm)", "Posy", "mid y" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (YList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component Y not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            Y_Nominal_Index = i;

            List<string> RotationList = new List<string> { "rotation", "rot", "rotate" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (RotationList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            if (i >= Headers.Count)
            {
                ShowMessageBox("Component rotation not found in header line", "Syntax error", MessageBoxButtons.OK);
                return false;
            }
            RotationIndex = i;

            List<string> LayerList = new List<string> { "layer", "side", "tb" };
            for (i = 0; i < Headers.Count; i++)
            {
                if (LayerList.Contains(Headers[i], StringComparer.OrdinalIgnoreCase))
                {
                    LayerIndex = i;
                    LayerDataPresent = true;
                    break;
                }
            }

            // clear and rebuild the data tables
            CadData_GridView.Rows.Clear();
            JobData_GridView.Rows.Clear();
            foreach (DataGridViewColumn column in JobData_GridView.Columns)
            {
                if (column.HeaderText!="Nozzle")
                {
                    column.SortMode = DataGridViewColumnSortMode.NotSortable;   // disable manual sort
                }
            }

            // Parse data
            List<String> NewLine;
            List<List<string>> DataLines = new List<List<string>>();

            for (i = LineIndex; i < AllLines.Count(); i++)   // for each component
            {
                // Skip: empty lines and comment lines (starting with # or "//")
                if (
                        (AllLines[i] == "")  // empty lines
                        ||
                        (AllLines[i] == "\"\"")  // empty lines ("")
                        ||
                        (AllLines[i][0] == '#')  // comment lines starting with #
                        ||
                        ((AllLines[i].Length > 1) && (AllLines[i][0] == '/') && (AllLines[i][1] == '/'))  // // comment lines starting with //
                    )
                {
                    continue;
                }

                if (KiCad)
                {
                    NewLine = SplitKiCadLine(AllLines[i]);
                }
                else
                {
                    NewLine = SplitCSV(AllLines[i], delimiter);
                }
                DataLines.Add(NewLine);
            }

            // DataLines now contain the splitted data from the original CSV file, with header and empty lines removed.

            HandleDuplicates(ref DataLines, ComponentIndex);    

            foreach (var Line in DataLines)
            {

                // Line = SplitCSV(AllLines[i], delimiter);
                // If layer is indicated and the component is not on this layer, skip it
                // TODO: Notify user if component is not on either layer (unknown data), once only and continue.
                // TODO: Fix bug: If component is not on either layer, skip it.
                // TODO: Use list of strings for layers and other fields, add “TopLayer” and “BottomLayer” for AD17.

                if (LayerDataPresent)
                {
                    List<string> TopList = new List<string> { "top", "t", "F.Cu" };
                    List<string> BottomList = new List<string> { "bottom", "b", "B.Cu", "bot" };
                    if (Bottom_checkBox.Checked)
                    {
                        if (TopList.Contains(Line[LayerIndex], StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (BottomList.Contains(Line[LayerIndex], StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                }
                CadData_GridView.Rows.Add();
                int Last = CadData_GridView.RowCount - 1;
                CadData_GridView.Rows[Last].Cells["Component"].Value = Line[ComponentIndex];
                CadData_GridView.Rows[Last].Cells["Value_Footprint"].Value = Line[ValueIndex] + "  |  " + Line[FootPrintIndex];
                CadData_GridView.Rows[Last].Cells["Rotation"].Value = Line[RotationIndex];

                if (PlacedDataPresent)
	            {
                    if ((Line[PlacedIndex]=="True")||(Line[PlacedIndex]=="true"))
                    {
                        CadData_GridView.Rows[Last].Cells["Placed_column"].Value = true;
                    }
		            else
	                {
                        CadData_GridView.Rows[Last].Cells["Placed_column"].Value = false;
	                }
	            }
		        else
	            {
                    CadData_GridView.Rows[Last].Cells["Placed_column"].Value = false;
	            }

                if (LayerDataPresent)
                {
                    if (Bottom_checkBox.Checked)
                    {
                        if (Line[X_Nominal_Index].StartsWith("-"))
                        {
                            CadData_GridView.Rows[Last].Cells["X_nominal"].Value = Line[X_Nominal_Index].Replace("mm", "").Replace("-", "");
                        }
                        else
                        {
                            CadData_GridView.Rows[Last].Cells["X_nominal"].Value = "-" + Line[X_Nominal_Index].Replace("mm", "");
                        }
                        double rot;
                        if (!double.TryParse(CadData_GridView.Rows[Last].Cells["Rotation"].Value.ToString().Replace(',', '.'), out rot))
                        {
                            DialogResult dialogResult = ShowMessageBox(
                                "Bad data at Rotation",
                                "Bad data",
                                MessageBoxButtons.OK);
                            return false;
                        }
                        rot = -rot + 180;
                        CadData_GridView.Rows[Last].Cells["Rotation"].Value= rot.ToString();
                    }
                    else
                    {
                        CadData_GridView.Rows[Last].Cells["X_nominal"].Value = Line[X_Nominal_Index].Replace("mm", "");
                    }
                }
                else
                {
                    CadData_GridView.Rows[Last].Cells["X_nominal"].Value = Line[X_Nominal_Index].Replace("mm", "");
                }
                CadData_GridView.Rows[Last].Cells["Y_nominal"].Value = Line[Y_Nominal_Index].Replace("mm", "");
                CadData_GridView.Rows[Last].Cells["X_nominal"].Value = CadData_GridView.Rows[Last].Cells["X_nominal"].Value.ToString().Replace(",", ".");
                CadData_GridView.Rows[Last].Cells["Y_nominal"].Value = CadData_GridView.Rows[Last].Cells["Y_nominal"].Value.ToString().Replace(",", ".");
                CadData_GridView.Rows[Last].Cells["X_Machine"].Value = "Nan";   // will be set later 
                CadData_GridView.Rows[Last].Cells["Y_Machine"].Value = "Nan";
                CadData_GridView.Rows[Last].Cells["Rotation_machine"].Value = "Nan";
            }   // end "for each component..."

            // Disable manual sorting
            foreach (DataGridViewColumn column in CadData_GridView.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            CadData_GridView.ClearSelection();
            // Check, that our data is good:
            if (!ValidateCADdata_m())
            {
                return false;
            }

            return true;
        }   // end ParseCadData

        // =================================================================================
        // HandleDuplicates():
        // If the inout data has duplicate designators, like R1, R1, R1, this routine 
        // replaces them with R1_1, R1_2, R1_3 etc.

        public void HandleDuplicates(ref List<List<string>> DataLines, int ComponentIndex)
        {
            string Designator = "";
            int Count;
            bool DuplicateFound;

            // For each component,
            for (int i = 0; i < DataLines.Count; i++)
            {
                Designator = DataLines[i][ComponentIndex];  // get the designator
                DuplicateFound = false;
                Count = 2;
                // and check, if there are duplicates
                for (int j = i+1; j < DataLines.Count; j++)
                {
                    if (DataLines[j][ComponentIndex]==Designator)
                    {
                        DuplicateFound = true;
                        DataLines[j][ComponentIndex] = Designator + "_" + Count.ToString();
                        Count++;
                    }
                }
                // if there were, all others are now renamed but the first one
                if (DuplicateFound)
                {
                    DataLines[i][ComponentIndex] = Designator + "_1";
                }
            }
        }


        // =================================================================================
        // Helper function for ParseCadData() (and some others, hence public)

        public List<String> SplitCSV(string InputLine, char delimiter)
        {
            // input lines can be "xxx","xxxx","xx"; output is array: xxx  xxxxx  xx
            // or xxx,xxxx,xx; output is array: xxx  xxxx  xx
            // or xxx,"xx,xx",xxxx; output is array: xxx  xx,xx  xxxx

            List<String> Tokens = new List<string>();
            string Line = InputLine;
            while (Line != "")
            {
                // skip the delimiter(s)
                while (Line[0] == delimiter)
                {
                    if (Line.Length < 2)
                    {
                        ShowMessageBox(
                           "Unexpected end of line on " + InputLine,
                           "Line parsing failed",
                           MessageBoxButtons.OK);
                        return (Tokens);
                    }
                    ShowMessageBox(
                       "Warning: empty field on line " + InputLine,
                       "Empty field",
                       MessageBoxButtons.OK);
                    Tokens.Add("");
                    Line = Line.Substring(1);
                };
                // add token
                if (Line[0] == '"')
                {
                    if (Line.Length < 2)
                    {
                        ShowMessageBox(
                           "Unexpected end of line on " + InputLine,
                           "Line parsing failed",
                           MessageBoxButtons.OK);
                        return (Tokens);
                    }
                    // token is "xxx"
                    Line = Line.Substring(1);   // skip the first "
                    Tokens.Add(Line.Substring(0, Line.IndexOf('"')));
                    if ( (Line.IndexOf('"') + 1) == Line.Length)
                    {
                        Line = "";
                    }
                    else
                    {
                        Line = Line.Substring(Line.IndexOf('"') + 2);  // skip the " and the delimiter
                    }
                }
                else
                {
                    // token does not have "" 's
                    if (Line.IndexOf(delimiter) < 0)
                    {
                        Tokens.Add(Line);   // last element
                        Line = "";
                    }
                    else
                    {
                        Tokens.Add(Line.Substring(0, Line.IndexOf(delimiter)));
                        Line = Line.Substring(Line.IndexOf(delimiter)+1);
                    }
                }
            }
            // remove leading spaces
            for (int i = 0; i < Tokens.Count; i++)
            {
                Tokens[i] = Tokens[i].Trim();
            }
            return (Tokens);
        }

        private List<String> SplitKiCadLine(string InputLine)
        {
            // input line is space formatted:
            // C123     0,1uF/50V        SM0603              1.6025     2.0964     180.0    top
            // tokens may have "" around them

            List<String> Tokens = new List<string>();
            string Line = InputLine;
            while (Line != "")
            {
                // skip leading spaces
                Line = Line.Trim(' ');       

                // add token
                if (Line[0] == '"')
                {
                    if (Line.Length < 2)
                    {
                        ShowMessageBox(
                           "Unexpected end of line on " + InputLine,
                           "Line parsing failed",
                           MessageBoxButtons.OK);
                        return (Tokens);
                    }
                    // token is "xxx"
                    Line = Line.Substring(1);   // skip the first "
                    Tokens.Add(Line.Substring(0, Line.IndexOf('"')));
                    if ((Line.IndexOf('"') + 1) == Line.Length)
                    {
                        Line = "";
                    }
                    else
                    {
                        Line = Line.Substring(Line.IndexOf('"') + 2);  // skip the " and the delimiter
                    }
                }
                else
                {
                    // token does not have "" 's
                    if (Line.IndexOf(' ') < 0)
                    {
                        Tokens.Add(Line);   // last element
                        Line = "";
                    }
                    else
                    {
                        Tokens.Add(Line.Substring(0, Line.IndexOf(' ')));
                        Line = Line.Substring(Line.IndexOf(' ') + 1);
                    }
                }
            }
            // remove leading spaces
            return (Tokens);
        }

        #endregion  CAD data reading functions

        // =================================================================================
        // Tape Positions page functions
        // =================================================================================
        #region Tape Positions page functions

        // This is set up at Form1_Load()
        void Tapes_dataGridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // Ugly, but MS raises this error when programmatically changing a combobox cell value
        }

        private void Tapes_tabPage_Begin()
        {
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                row.HeaderCell.Value = row.Index.ToString();
                row.Cells["SelectButton_Column"].Value = "Reset";
            }
            SetDownCameraDefaults();
            SelectCamera(Machine.DownCamera);
            Machine.DownCamera.ImageBox = Tapes_pictureBox;
        }

        private void Tapes_tabPage_End()
        {
            Machine.Move.ZGuardOn();
        }

        // =================================================================================
        public void TapeWidthStringToValues(string WidthStr, out double Xoff, out double Yoff, out double pitch)
        {
            switch (WidthStr)
            {
                case "8/2mm":
                    pitch = 2.0;
                    Xoff = 3.5;
                    Yoff = 2.0;
                    break;
                case "8/4mm":
                    pitch = 4.0;
                    Xoff = 3.50;
                    Yoff = 2.0;
                    break;
                case "12/4mm":
                    pitch = 4.0;
                    Xoff = 5.50;
                    Yoff = 2.0;
                    break;
                case "12/8mm":
                    pitch = 8.0;
                    Xoff = 5.50;
                    Yoff = 2.0;
                    break;
                case "16/4mm":
                    pitch = 4.0;
                    Xoff = 7.50;
                    Yoff = 2.0;
                    break;
                case "16/8mm":
                    pitch = 8.0;
                    Xoff = 7.50;
                    Yoff = 2.0;
                    break;
                case "16/12mm":
                    pitch = 12.0;
                    Xoff = 7.50;
                    Yoff = 2.0;
                    break;
                case "24/4mm":
                    pitch = 4.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/8mm":
                    pitch = 8.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/12mm":
                    pitch = 12.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/16mm":
                    pitch = 16.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "24/20mm":
                    pitch = 20.0;
                    Xoff = 11.50;
                    Yoff = 2.0;
                    break;
                case "32/4mm":
                    pitch = 4.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/8mm":
                    pitch = 8.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/12mm":
                    pitch = 12.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/16mm":
                    pitch = 16.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/20mm":
                    pitch = 20.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/24mm":
                    pitch = 24.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/28mm":
                    pitch = 28.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                case "32/32mm":
                    pitch = 32.0;
                    Xoff = 14.20;
                    Yoff = 2.0;
                    break;
                default:
                    pitch = 0.0;
                    Xoff = 0.0;
                    Yoff = 2.0;
                    break;
            }
        }

        // =================================================================================
        // tape parameters edit dialog

        private int TapesGridEditRow = 0;
        private int TapesGridEnterRow = 0;

        private void Tapes_dataGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            TapesGridEnterRow = e.RowIndex;
        }

        private void Tapes_dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                TapesGridEditRow = TapesGridEnterRow;
            }
        }

        private void Invoke_TapeEditDialog(int row)
        {
            DisplayText("Open edit tape dialog", KnownColor.DarkGreen);
            TapeEditForm TapeEditDialog = new TapeEditForm(Machine.DownCamera);
            TapeEditDialog.MainForm = this;
            TapeEditDialog.TapeRowNo = TapesGridEditRow;
            TapeEditDialog.TapesDataGrid = Tapes_dataGridView;
            TapeEditDialog.Row = Tapes_dataGridView.Rows[row];
            AttachButtonLogging(TapeEditDialog.Controls);
            TapeEditDialog.Show(this);
        }

        private void EditTape_MenuItemClick(object sender, EventArgs e)
        {
            if (TapesGridEditRow<0)
            {
                return; // user clicked header or empty space
            }
            Invoke_TapeEditDialog(TapesGridEditRow);
        }

        // end edit dialog stuff
        // =================================================================================


        private void AddTape_button_Click(object sender, EventArgs e)
        {
            DataGridViewSelectedRowCollection SelectedRows = Tapes_dataGridView.SelectedRows;
            int index = 0;
            if (SelectedRows.Count == 0)
            {
                // add to the end
                Tapes_dataGridView.Rows.Insert(Tapes_dataGridView.Rows.Count);
                index = Tapes_dataGridView.Rows.Count - 1;
            }
            else
            {
                // replace current
                index = Tapes_dataGridView.SelectedRows[0].Index;
                Tapes_dataGridView.Rows.RemoveAt(index);
                Tapes_dataGridView.Rows.Insert(index);
            };
            // Add data
            // SelectButton_Column: On main form, resets tape to position 1.
            // The gridView is moved to selection dialog on job run time. There the SelectButton selects that tape.
            Tapes_dataGridView.Rows[index].Cells["SelectButton_Column"].Value = "Reset";
            // Id_Column: User settable name for the tape
            Tapes_dataGridView.Rows[index].Cells["Id_Column"].Value = index.ToString();
            // FirstX_Column, FirstY_Column: Originally set approximate location for the first hole
            Tapes_dataGridView.Rows[index].Cells["FirstX_Column"].Value = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["FirstY_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            // Orientation_Column: Which way the tape is set. It is the direction to go for next part
            Tapes_dataGridView.Rows[index].Cells["Orientation_Column"].Value = "+X";
            // Rotation_Column: Which way the parts are rotated on the tape. if 0, parts form +Y oriented tape
            // correspont to 0deg. on the PCB, tape.e. the placement operation does not rotate them.
            Tapes_dataGridView.Rows[index].Cells["Rotation_Column"].Value = "0deg.";
            // WidthColumn: sets the width of the tape and the distance from one part to next. 
            // From EIA-481, we get the part location from the hole location.
            Tapes_dataGridView.Rows[index].Cells["Width_Column"].Value = "8/4mm";
            // Type_Column: used in hole recognition
            Tapes_dataGridView.Rows[index].Cells["Type_Column"].Value = "Paper (White)";
            // NextPart_Column tells the part number of next part. 
            // NextX, NextY tell the approximate hole location for the next part. Incremented when a part is picked up.
            Tapes_dataGridView.Rows[index].Cells["NextPart_Column"].Value = "1";
            Tapes_dataGridView.Rows[index].Cells["Next_X_Column"].Value = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["Next_Y_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            // Z_Pickup_Column, Z_Place_Column: The Z values are measured when first part is placed. Picking up and
            // placing the next parts will then be faster.
            Tapes_dataGridView.Rows[index].Cells["Z_Pickup_Column"].Value = "--";
            Tapes_dataGridView.Rows[index].Cells["Z_Place_Column"].Value = "--";
            Tapes_dataGridView.Rows[index].Cells["TrayID_Column"].Value = "--";
            // Coordinates for parts: If set, optical system is not used, the coordinates are used directly
            // FirstXY sets the location of the first part. If LastXY is set (!=0, != first; there are more than one part on a tape),
            // The next part is <pitch> mm to the direction from first to last. If LastXY is not set (individual pickup location,
            // automatic feeder), orientation does not apply, rotation is used and manually set A correction is also used (it is
            // almost impossible to mount feeders or part holders exactly perpedicular).
            Tapes_dataGridView.Rows[index].Cells["CoordinatesForParts_Column"].Value = false;
            Tapes_dataGridView.Rows[index].Cells["LastX_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["LastY_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[index].Cells["RotationDirect_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            TapesGridEditRow = index;
            Invoke_TapeEditDialog(index);
        }

        private void TapeUp_button_Click(object sender, EventArgs e)
        {
            DataGrid_Up_button(Tapes_dataGridView);
        }

        private void TapeDown_button_Click(object sender, EventArgs e)
        {
            DataGrid_Down_button(Tapes_dataGridView);
        }

        private void DeleteTape_button_Click(object sender, EventArgs e)
        {
            if (Tapes_dataGridView.RowCount > 0)
            {
                Tapes_dataGridView.Rows.RemoveAt(Tapes_dataGridView.CurrentCell.RowIndex);
            }
        }

        private async void TapeGoTo_button_Click(object sender, EventArgs e)
        {
            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                return;
            };
            double X;
            double Y;
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["FirstX_Column"].Value.ToString().Replace(',', '.'), out X))
            {
                return;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["FirstY_Column"].Value.ToString().Replace(',', '.'), out Y))
            {
                return;
            }
            await Machine.Move.MoveXYSafeAsync(X, Y);
        }

        private void TapeSet1_button_Click(object sender, EventArgs e)
        {
            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                return;
            };
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            Tapes_dataGridView.Rows[row].Cells["FirstX_Column"].Value = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[row].Cells["FirstY_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
            // fix #22 update next coordinates when setting hole 1
            Tapes_dataGridView.Rows[row].Cells["NextPart_Column"].Value = "1";
            Tapes_dataGridView.Rows[row].Cells["Next_X_Column"].Value = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
            Tapes_dataGridView.Rows[row].Cells["Next_Y_Column"].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);

        }


        // ==========================================================================================================
        // Tapes_dataGridView_CellClick(): 
        // If the click is on a button column, resets the tape. 
        private void Tapes_dataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore clicks that are not on button cell  Id_Column
            if ((e.RowIndex < 0) || (e.ColumnIndex != Tapes_dataGridView.Columns["SelectButton_Column"].Index))
            {
                return;
            }
            Tapes.Reset(e.RowIndex);
            Update_GridView(Tapes_dataGridView);
        }

        // ==========================================================================================================
        private void Tapes_pictureBox_MouseClick(object sender, MouseEventArgs e)
        {
            General_pictureBox_MouseClick(Tapes_pictureBox, e.X, e.Y);
        }


        // ==========================================================================================================
        // SelectTape(): 
        // Displays a dialog with buttons for all defined tapes, returns the ID of the tape.
        // used in placement when the user selects the tape to be used in runtime.
        // In this case, we remove the regular Tapes_dataGridView_CellClick() handler; the TapeDialog
        // changes the button text to "select" (and back to "reset" on return) and uses its own handler.
        private string SelectTape(string header)
        {
            System.Drawing.Point loc = Tapes_dataGridView.Location;
            Size size = Tapes_dataGridView.Size;
            DataGridView Grid = Tapes_dataGridView;
            TapeSelectionForm TapeDialog = new TapeSelectionForm(Grid);
            TapeDialog.HeaderString = header;
            Tapes_dataGridView.CellClick -= new DataGridViewCellEventHandler(Tapes_dataGridView_CellClick);
            this.Controls.Remove(Tapes_dataGridView);

            TapeDialog.ShowDialog(this);

            string ID = TapeDialog.ID;  // get the result
            DisplayText("Selected tape: " + ID);

            Tapes_tabPage.Controls.Add(Tapes_dataGridView);
            Tapes_dataGridView = Grid;
            Tapes_dataGridView.Location = loc;
            Tapes_dataGridView.Size = size;

            TapeDialog.Dispose();
            Tapes_dataGridView.CellClick += new DataGridViewCellEventHandler(Tapes_dataGridView_CellClick);
            return ID;
        }

        private void ResetOneTape_button_Click(object sender, EventArgs e)
        {
            for (int CurrentRow = 0; CurrentRow < JobData_GridView.RowCount; CurrentRow++)
            {
                DataGridViewRow Row = JobData_GridView.Rows[CurrentRow];
                bool DoRow = false;
                foreach (DataGridViewCell oneCell in Row.Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }

                if (!DoRow)
                {
                    continue;
                };
                // Reset this component's tape:
                string ID = JobData_GridView.Rows[CurrentRow].Cells[3].Value.ToString();
                int TapeNo = 0;
                if (Tapes.IdValidates_m(ID, out TapeNo))
                {
                    // fix #22 reset next coordinates
                    Tapes.Reset(TapeNo);
                }
            }
        }

        private void ResetAllTapes_button_Click(object sender, EventArgs e)
        {
            Tapes.ClearAll();
        }

        private void SetPartNo_button_Click(object sender, EventArgs e)
        {
            int no = 0;
            if (!int.TryParse(NextPart_TextBox.Text, out no))
            {
                return;
            }
            DataGridViewRow Row = Tapes_dataGridView.Rows[Tapes_dataGridView.CurrentCell.RowIndex];
            Row.Cells["NextPart_Column"].Value = no.ToString();
            Row.Cells["Next_X_Column"].Value = Machine.Position.CurrentX.ToString();
            Row.Cells["Next_Y_Column"].Value = Machine.Position.CurrentY.ToString();
        }

        private async void Tape_GoToNext_button_Click(object sender, EventArgs e)
        {
            if (Tapes_dataGridView.SelectedCells.Count != 1)
            {
                return;
            };
            double X;
            double Y;
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["Next_X_Column"].Value.ToString().Replace(',', '.'), out X))
            {
                return;
            }
            if (!double.TryParse(Tapes_dataGridView.Rows[row].Cells["Next_Y_Column"].Value.ToString().Replace(',', '.'), out Y))
            {
                return;
            }
            await Machine.Move.MoveXYSafeAsync(X, Y);
        }

        private void Tape_resetZs_button_Click(object sender, EventArgs e)
        {
            for (int tape = 0; tape < Tapes_dataGridView.Rows.Count; tape++)
            {
                Tapes_dataGridView.Rows[tape].Cells["Z_Pickup_Column"].Value = "--";
                Tapes_dataGridView.Rows[tape].Cells["Z_Place_Column"].Value = "--";
            }
        }

        private void ResetPlaceZ_button_Click(object sender, EventArgs e)
        {
            for (int tape = 0; tape < Tapes_dataGridView.Rows.Count; tape++)
            {
                Tapes_dataGridView.Rows[tape].Cells["Z_Place_Column"].Value = "--";
            }
        }

        private void ResetSelectedZs(bool both)
        {
            bool DoRow = false;
            for (int tape = 0; tape < Tapes_dataGridView.Rows.Count; tape++)
            {
                foreach (DataGridViewCell oneCell in Tapes_dataGridView.Rows[tape].Cells)
                {
                    if (oneCell.Selected)
                    {
                        DoRow = true;
                        break;
                    }
                }
                if (DoRow)
                {
                    Tapes_dataGridView.Rows[tape].Cells["Z_Place_Column"].Value = "--";
                    if (both)
                    {
                        Tapes_dataGridView.Rows[tape].Cells["Z_Pickup_Column"].Value = "--";
                    }
                }
            }
        }

        private void ResetSelectedZs_button_Click(object sender, EventArgs e)
        {
            ResetSelectedZs(true);
        }

        private void ResetSelectedPlaceZs_button_Click(object sender, EventArgs e)
        {
            ResetSelectedZs(false);
        }

        private void SaveAllTapes_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SaveDataGrid(TapesAll_saveFileDialog.FileName, Tapes_dataGridView);
            }
        }

        private void LoadAllTapes_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                LoadTapesTable(TapesAll_openFileDialog.FileName);
                // LoadDataGrid(TapesAll_openFileDialog.FileName, Tapes_dataGridView, DataTableType.Tapes);
            }
        }

        private async void HoleTest_button_Click(object sender, EventArgs e)
        {
            int PartNum = 0;
            int TapeNum = 0;
            if (UseCoordinatesDirectly(Tapes_dataGridView.CurrentCell.RowIndex))
            {
                DisplayText("\"Coordinates for parts\" is checked, hole location does not apply.");
                return;
            }

            DataGridViewRow Row = Tapes_dataGridView.Rows[Tapes_dataGridView.CurrentCell.RowIndex];
            if (!int.TryParse(HoleTest_maskedTextBox.Text, out PartNum))
            {
                if (!int.TryParse(Row.Cells["NextPart_Column"].Value.ToString(), out PartNum))
                {
                    return;
                }
            }
            string Id = Row.Cells["Id_Column"].Value.ToString();
            double X = 0.0;
            double Y = 0.0;
            if (!Tapes.IdValidates_m(Id, out TapeNum))
            {
                return;
            }

            var result = await Tapes.GetPartHole_m(TapeNum, PartNum);
            X = result.Item2;
            Y = result.Item3;

            if (result.Item1)
            {
                await Machine.Move.MoveXYSafeAsync(X, Y);
            }
        }

        private async Task ShowPartByCoordinates_mAsync()
        {
            double X;
            double Y;
            double A;
            bool increment;
            if (!FindPartWithDirectCoordinates_m(Tapes_dataGridView.CurrentCell.RowIndex, out X, out Y, out A, out increment))
            {
                return;
            }
            await Machine.Move.MoveXYSafeAsync(X, Y);
            Machine.DownCamera.ArrowAngle = A;
            Machine.DownCamera.DrawArrow = true;
        }

        private async void ShowPart_button_Click(object sender, EventArgs e)
        {
            int PartNum = 0;
            int TapeNum = 0;

            DataGridViewRow Row = Tapes_dataGridView.Rows[Tapes_dataGridView.CurrentCell.RowIndex];
            if (!int.TryParse(HoleTest_maskedTextBox.Text, out PartNum))
            {
                DisplayText("Bad data in part # ");
                return;
            }
            // Tapes.GetPartLocationFromHolePosition_m() and ShowPartByCoordinates_m() use the next column from Tapes_dataGridView.
            // Set it temporarily, but remember what was there:
            string temp = Row.Cells["NextPart_Column"].Value.ToString();
            Row.Cells["NextPart_Column"].Value = PartNum.ToString();

            if (UseCoordinatesDirectly(Tapes_dataGridView.CurrentCell.RowIndex))
            {
                await ShowPartByCoordinates_mAsync();
                Row.Cells["NextPart_Column"].Value = temp.ToString();
                return;
            }

            string Id = Row.Cells["Id_Column"].Value.ToString();
            double X = 0.0;
            double Y = 0.0;
            if (!Tapes.IdValidates_m(Id, out TapeNum))
            {
                Row.Cells["NextPart_Column"].Value = temp.ToString();
                return;
            }

            var result = await Tapes.GetPartHole_m(TapeNum, PartNum);
            X = result.Item2;
            Y = result.Item3;

            if (!result.Item1)
            {
                Row.Cells["NextPart_Column"].Value = temp.ToString();
                return;
            }
            double pX = 0.0;
            double pY = 0.0;
            double A = 0.0;
            if (Tapes.GetPartLocationFromHolePosition_m(TapeNum, X, Y, out pX, out pY, out A))
            {
                await Machine.Move.MoveXYSafeAsync(pX, pY);
            }
            Machine.DownCamera.ArrowAngle = A;
            Machine.DownCamera.DrawArrow = true;

            Row.Cells["NextPart_Column"].Value = temp.ToString();
        }

        private void ShowPart_button_Leave(object sender, EventArgs e)
        {
            Machine.DownCamera.DrawArrow = false;
        }

        private void ShowPart_button_MouseLeave(object sender, EventArgs e)
        {
            Machine.DownCamera.DrawArrow = false;
        }

        // =================================================================================
        // even handlers for Tapes_dataGridView
        // see http://stackoverflow.com/questions/5652957/what-event-catches-a-change-of-value-in-a-combobox-in-a-datagridviewcell


        private void Tapes_dataGridView_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            // see http://stackoverflow.com/questions/4284370/datagridview-keydown-event-not-working-in-c-sharp
            if (e.Control is DataGridViewTextBoxEditingControl)
            {
                DataGridViewTextBoxEditingControl tb = e.Control as DataGridViewTextBoxEditingControl;
                tb.KeyDown += new KeyEventHandler(Tapes_dataGridView_MyKeyDown);
            }
        }

        private void Tapes_dataGridView_MyKeyDown(object sender, KeyEventArgs e)
        {
            int row = Tapes_dataGridView.CurrentCell.RowIndex;
            int col = Tapes_dataGridView.CurrentCell.ColumnIndex;
            // for pitch and offset columns, set width to custom
            if ((Tapes_dataGridView.Columns[col].Name == "Pitch_Column") ||
                (Tapes_dataGridView.Columns[col].Name == "OffsetX_Column") ||
                (Tapes_dataGridView.Columns[col].Name == "OffsetY_Column"))
            {
                Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value = "custom";
                return;
            }
        }

        private void Tapes_dataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // This event handler manually raises the CellValueChanged event 
            // by calling the CommitEdit method. 
            if (Tapes_dataGridView.IsCurrentCellDirty)
            {
                // This fires the cell value changed handler below
                Tapes_dataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Tapes_dataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            int row = e.RowIndex;
            int col = e.ColumnIndex;
            if (StartingUp || LoadingDataGrid)
            {
                return;
            }
            if ((col < 0) || (col > Tapes_dataGridView.Columns.Count))
            {
                return;
            }
            if (row < 0)
            {
                return;
            }

            // if width dropdown, fill values automatically
            if (((Tapes_dataGridView.Columns[col].HeaderText == "Width") && (row >= 0)))
            {
                double X;
                double Y;
                double pitch;
                if (Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value != null)
                {
                    if (Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value.ToString() != "custom")
                    {
                        TapeWidthStringToValues(Tapes_dataGridView.Rows[row].Cells["Width_Column"].Value.ToString(), out X, out Y, out pitch);
                        Tapes_dataGridView.Rows[row].Cells["Pitch_Column"].Value = pitch.ToString();
                        Tapes_dataGridView.Rows[row].Cells["OffsetX_Column"].Value = X.ToString();
                        Tapes_dataGridView.Rows[row].Cells["OffsetY_Column"].Value = Y.ToString();
                    }
                    return;
                }
            }

            // commented out until I find how to make it work at the keystroke (MS insists on end edit, too late)
            //double val;
            //if (((Tapes_dataGridView.CurrentCell.OwningColumn.HeaderText == "Pitch") &&
            //     (Tapes_dataGridView.CurrentCell.RowIndex >= 0)))
            //{
            //    if (Tapes_dataGridView.CurrentCell.Value != null)
            //    {
            //        if (double.TryParse(Tapes_dataGridView.CurrentCell.Value.ToString(), out val))
            //        {
            //            Tapes_dataGridView.CurrentCell.Style.ForeColor = Color.Black;
            //        }
            //        else
            //        {
            //            Tapes_dataGridView.CurrentCell.Style.ForeColor = Color.Red;
            //        }
            //    }
            //}

            // fix #22 calculate new next coordinates if column was changed
            // only update next coordinates if corresponding column has been changed and rowIndex > 0
            if ((Tapes_dataGridView.Columns[e.ColumnIndex].HeaderText == "Next") && (e.RowIndex >= 0))
            {
                int NextNo = 1;

                if (Tapes_dataGridView.Rows[row].Cells["NextPart_Column"].Value == null)
                {
                    ShowMessageBox(
                        "Bad data in Next",
                        "Data error",
                        MessageBoxButtons.OK);
                    return;
                }

                if (!int.TryParse(Tapes_dataGridView.Rows[row].Cells["NextPart_Column"].Value.ToString(), out NextNo))
                {
                    ShowMessageBox(
                        "Bad data in Next",
                        "Data error",
                        MessageBoxButtons.OK);
                    return;
                }

                if (Tapes != null)
                {
                    Tapes.UpdateNextCoordinates(row, NextNo);
                }
                return;
            }
        }


        // =================================================================================
        // Trays:

        private void SaveTray_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            // Get current tray ID
            int CurrRow = Tapes_dataGridView.CurrentCell.RowIndex;
            string TrayID = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].Value.ToString();

            // Copy the tapes with this ID to Clipboard datagridview:
            DataGridView ClipBoard_dgw = new DataGridView();
            ClipBoard_dgw.AllowUserToAddRows = false;  // this prevents an empty row in the end
            foreach (DataGridViewColumn col in Tapes_dataGridView.Columns)
            {
                ClipBoard_dgw.Columns.Add(col.Clone() as DataGridViewColumn);
            }
            DataGridViewRow NewRow = new DataGridViewRow();
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                if (row.Cells["TrayID_Column"].Value.ToString() == TrayID)
                {
                    NewRow = (DataGridViewRow)row.Clone();
                    int intColIndex = 0;
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        NewRow.Cells[intColIndex].Value = cell.Value;
                        intColIndex++;
                    }
                    ClipBoard_dgw.Rows.Add(NewRow);
                }
            }

            // Save the Clipboard
            SaveDataGrid(TapesAll_saveFileDialog.FileName, ClipBoard_dgw);
        }

        private void LoadTrayFromFile(string FileName)
        {
            // from: http://stackoverflow.com/questions/6336239/copy-datagridviews-rows-into-another-datagridview  
            DataGridView ClipBoard_dgw = new DataGridView();
            ClipBoard_dgw.AllowUserToAddRows = false;  // this prevents an empty row in the end
            foreach (DataGridViewColumn col in Tapes_dataGridView.Columns)
            {
                ClipBoard_dgw.Columns.Add(col.Clone() as DataGridViewColumn);
            }
            LoadDataGrid(FileName, ClipBoard_dgw, DataTableType.Tapes);
            DataGridViewRow NewRow = new DataGridViewRow();
            foreach (DataGridViewRow row in ClipBoard_dgw.Rows)
            {
                NewRow = (DataGridViewRow)row.Clone();
                int intColIndex = 0;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    NewRow.Cells[intColIndex].Value = cell.Value;
                    intColIndex++;
                }
                Tapes_dataGridView.Rows.Add(NewRow);
            }
            Update_GridView(Tapes_dataGridView);
        }

        private void LoadTray_button_Click(object sender, EventArgs e)
        {
            if (TapesAll_openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            LoadTrayFromFile(TapesAll_openFileDialog.FileName);
        }

        private void DeleteTray(string TrayID, int col_index)
        {
            // removes a tray from Tapes_dataGridView
            // Can't modify the gridview and iterate through it at the same time, so:

            // Copy current to clipboard
            DataGridView ClipBoard_dgw = new DataGridView();
            ClipBoard_dgw.AllowUserToAddRows = false;  // this prevents an empty row in the end
            // create columns
            foreach (DataGridViewColumn col in Tapes_dataGridView.Columns)
            {
                ClipBoard_dgw.Columns.Add(new DataGridViewColumn(col.CellTemplate));
            }
            // copy rows
            DataGridViewRow NewRow = new DataGridViewRow();
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                NewRow = (DataGridViewRow)row.Clone();
                int intColIndex = 0;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    NewRow.Cells[intColIndex].Value = cell.Value;
                    intColIndex++;
                }
                ClipBoard_dgw.Rows.Add(NewRow);
            }

            Tapes_dataGridView.Rows.Clear();   // Clear existing
            // Copy back if needed
            foreach (DataGridViewRow row in ClipBoard_dgw.Rows)
            {
                NewRow = (DataGridViewRow)row.Clone();
                int intColIndex = 0;
                if (row.Cells[col_index].Value.ToString() != TrayID)
                {
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        NewRow.Cells[intColIndex].Value = cell.Value;
                        intColIndex++;
                    }
                    Tapes_dataGridView.Rows.Add(NewRow);
                }
            }
        }

        private void ReplaceTray_button_Click(object sender, EventArgs e)
        {
            TapesAll_openFileDialog.Filter = "LitePlacer Tapes files (*.tapes)|*.tapes|All files (*.*)|*.*";

            if (TapesAll_openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            // Get current tray ID
            int CurrRow = Tapes_dataGridView.CurrentCell.RowIndex;
            string TrayID = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].Value.ToString();
            int col = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].ColumnIndex;
            DeleteTray(TrayID, col);
            LoadTrayFromFile(TapesAll_openFileDialog.FileName);
        }

        private void ReloadTray_button_Click(object sender, EventArgs e)
        {
            // Get current tray ID
            int CurrRow = Tapes_dataGridView.CurrentCell.RowIndex;
            string TrayID = Tapes_dataGridView.Rows[CurrRow].Cells["TrayID_Column"].Value.ToString();
            foreach (DataGridViewRow row in Tapes_dataGridView.Rows)
            {
                if (row.Cells["TrayID_Column"].Value.ToString() == TrayID)
                {
                    row.Cells["NextPart_Column"].Value = 1;
                }
            }
        }

 
        #endregion  TapeNumber Positions page functions

        // =================================================================================
        // Test functions
        // =================================================================================
        #region test functions

        // =================================================================================
        private void LabelTestButtons()
        {
            Test1_button.Text = "Pickup this";
            Test2_button.Text = "Place here";
            Test3_button.Text = "Probe (n.c.)";
            Test4_button.Text = "Nozzle to cam";
            Test5_button.Text = "Probe down";
            Test6_button.Text = "Nozzle up";
        }

        // test 1

        private async void Test1_button_Click(object sender, EventArgs e)
        {
            double Xmark = Machine.Position.CurrentX;
            double Ymark = Machine.Position.CurrentY;
            DisplayText("test 1: Pick up this (probing)");
            Machine.Pump.PumpOn();
            Machine.Vacuum.VacuumOff();
            if (!Nozzle.Move_m(Machine.Position.CurrentX, Machine.Position.CurrentY, Machine.Position.CurrentA))
            {
                Machine.Pump.PumpOff_NoWorkaround();
                return;
            }
            if (!await Nozzle_ProbeDown_mAsync())
            {
                return;
            }
            Machine.Vacuum.VacuumOn();
            await Machine.Move.MoveZSafeAsync(0);  // pick up
            await Machine.Move.MoveXYSafeAsync(Xmark, Ymark);
        }

        // =================================================================================
        // test 2

        // static int test2_state = 0;
        private async void Test2_button_Click(object sender, EventArgs e)
        {
            double Xmark = Machine.Position.CurrentX;
            double Ymark = Machine.Position.CurrentY;
            DisplayText("test 2: Place here (probing)");
            if (!Nozzle.Move_m(Machine.Position.CurrentX, Machine.Position.CurrentY, Machine.Position.CurrentA))
            {
                return;
            }
            await Nozzle_ProbeDown_mAsync();
            Machine.Vacuum.VacuumOff();
            await Machine.Move.MoveZSafeAsync(0);  // back up
            await Machine.Move.MoveXYSafeAsync(Xmark, Ymark);  // show results
        }

        // =================================================================================
        // test 3 "Probe (n.c.)"

        private async void Test3_button_Click(object sender, EventArgs e)
        {
            Xmark = Machine.Position.CurrentX;
            Ymark = Machine.Position.CurrentY;
            await Machine.Move.MoveXYSafeAsync((Machine.Position.CurrentX + Setting.DownCam_NozzleOffsetX), (Machine.Position.CurrentY + Setting.DownCam_NozzleOffsetY));
            await Nozzle_ProbeDown_mAsync();
        }


        // =================================================================================
        // test 4 "Nozzle to cam"

        private void Test4_button_Click(object sender, EventArgs e)
        {
            double xp = Setting.UpCam_PositionX;
            double xo = Setting.DownCam_NozzleOffsetX;
            double yp = Setting.UpCam_PositionY;
            double yo = Setting.DownCam_NozzleOffsetY;
            Nozzle.Move_m(xp - xo, yp - yo, Machine.Position.CurrentA);
        }

        // =================================================================================
        // test 5 "Probe down";

        private double Xmark;
        private double Ymark;
        private async void Test5_button_Click(object sender, EventArgs e)
        {
            Xmark = Machine.Position.CurrentX;
            Ymark = Machine.Position.CurrentY;
            if (!Nozzle.Move_m(Machine.Position.CurrentX, Machine.Position.CurrentY, Machine.Position.CurrentA))
            {
                return;
            }
            Machine.Move.ProbingMode(true);
            await Nozzle_ProbeDown_mAsync();
        }

        // =================================================================================
        // test 6
        private async void Test6_button_Click(object sender, EventArgs e)
        {
            DisplayText("test 6: Nozzle up");
            await Machine.Move.MoveZSafeAsync(0);  // go up
            await Machine.Move.MoveXYSafeAsync(Xmark, Ymark);
        }


        // ==========================================================================================================
        // I tried automatic measurement of mm/pixel, but results were not great: repeatable, but not accurate. ??
        // I'll hide the button, but keep the code, just in case.

        private async void MeasureDownCam_button_Click(object sender, EventArgs e)
        {
            if (!Machine.DownCamera.IsRunning())
            {
                ShowMessageBox(
                    "Downcamera not running.",
                    "Measurement failed.",
                    MessageBoxButtons.OK);
                return;
            }
            SetHomingMeasurement();
            const double dist = 1.0;
            double X1, Y1;
            if (Machine.DownCamera.GetClosestCircle(out X1, out Y1, 50.0) <= 0)
            {
                ShowMessageBox(
                    "To use: Set homing parameters, then place a single homing mark close to camera center.",
                    "Measurement failed.",
                    MessageBoxButtons.OK);
                return;
            }
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX - (dist / 2.0), Machine.Position.CurrentY - (dist / 2.0));
            Thread.Sleep(500);
            if (Machine.DownCamera.GetClosestCircle(out X1, out Y1, 250.0) <= 0)
            {
                ShowMessageBox(
                    "Measurement failed.",
                    "Measurement failed.",
                    MessageBoxButtons.OK);
                return;
            }
            double X2, Y2;
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX + dist, Machine.Position.CurrentY + dist);
            Thread.Sleep(500);
            if (Machine.DownCamera.GetClosestCircle(out X2, out Y2, 250.0) <= 0)
            {
                ShowMessageBox(
                    "Measurement failed.",
                    "Measurement failed.",
                    MessageBoxButtons.OK);
                return;
            }
            // sanity check
            double X3, Y3;
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX - (dist / 2.0), Machine.Position.CurrentY - (dist / 2.0));
            Thread.Sleep(500);
            if (Machine.DownCamera.GetClosestCircle(out X3, out Y3, 10.0) <= 0)
            {
                ShowMessageBox(
                    "Measurement failed.",
                    "Measurement failed.",
                    MessageBoxButtons.OK);
                return;
            }
            Setting.DownCam_XmmPerPixel = dist / (X1 - X2);
            DownCameraBoxXmmPerPixel_label.Text = "(" + Setting.DownCam_XmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            double BoxX = Setting.DownCam_XmmPerPixel * Machine.DownCamera.BoxSizeX;
            DownCameraBoxX_textBox.Text = BoxX.ToString("0.000", CultureInfo.InvariantCulture);

            Setting.DownCam_YmmPerPixel = dist / (Y2 - Y1);
            DownCameraBoxYmmPerPixel_label.Text = "(" + Setting.DownCam_YmmPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + "mm/pixel)";
            double BoxY = Setting.DownCam_YmmPerPixel * Machine.DownCamera.BoxSizeY;
            DownCameraBoxY_textBox.Text = BoxY.ToString("0.000", CultureInfo.InvariantCulture);
        }

        #endregion test functions

        // ==========================================================================================================
        // Measurement boxes (Homing, Nozzle, OriginalFiducials, Tapes etc)
        // ==========================================================================================================
        #region Measurementboxes

        // ==========================================================================================================
        // Some helpers:

        public void DataGridViewCopy(DataGridView FromGr, ref DataGridView ToGr, bool print = true)
        {
            DataGridViewRow NewRow;
            ToGr.Rows.Clear();

            foreach (DataGridViewRow Row in FromGr.Rows)
            {
                NewRow = (DataGridViewRow)Row.Clone();
                for (int i = 0; i < Row.Cells.Count; i++)
                {
                    NewRow.Cells[i].Value = Row.Cells[i].Value;
                }
                ToGr.Rows.Add(NewRow);
            }
            if (print)
            {
                // Print results:
                string DebugStr = FromGr.Name + " => " + ToGr.Name + ":";
                DebugStr = DebugStr.Replace("_dataGridView", "");
                if (ToGr.Rows.Count == 0)
                {
                    DisplayText(DebugStr + "( )");
                }
                else
                {
                    foreach (DataGridViewRow Row in ToGr.Rows)
                    {
                        DebugStr += "( ";
                        for (int i = 0; i < Row.Cells.Count; i++)
                        {
                            if (Row.Cells[i].Value == null)
                            {
                                DebugStr += "--, ";
                            }
                            else
                            {
                                DebugStr += Row.Cells[i].Value.ToString() + ", ";
                            }
                        }
                        DebugStr += ")\n";
                        DebugStr = DebugStr.Replace(", )\n", " )");
                        DisplayText(DebugStr);
                    }
                }
                ClearEditTargets();
            }
        }

        // ==========================================================================================================
        // DownCam:


        // ==========================================================================================================
        
         private void DebugRegtanclesDownCamera(double Tolerance)
        {
            double X, Y;
            if (Machine.DownCamera.GetClosestRectangle(out X, out Y, Tolerance) > 0)
            {
                X = X * Setting.DownCam_XmmPerPixel;
                Y = -Y * Setting.DownCam_YmmPerPixel;
                DisplayText("X: " + X.ToString("0.000", CultureInfo.InvariantCulture));
                DisplayText("Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture));
            }
            else
            {
                DisplayText("No results.");
            }
        }

        private void DebugCirclesDownCamera(double Tolerance)
        {
            double X, Y;
            if (Machine.DownCamera.GetClosestCircle(out X, out Y, Tolerance) > 0)
            {
                X = X * Setting.DownCam_XmmPerPixel;
                Y = -Y * Setting.DownCam_YmmPerPixel;
                DisplayText("X: " + X.ToString("0.000", CultureInfo.InvariantCulture));
                DisplayText("Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture));
            }
            else
            {
                DisplayText("No results.");
            }
        }

        // ==========================================================================================================
        // Snapshot:
        private void UpCam_SnapshotToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref UpcamSnapshot_dataGridView);
        }

        private void UpCam_SnapshotToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(UpcamSnapshot_dataGridView, ref Display_dataGridView);
            Machine.UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void UpCam_TakeSnapshot_button_Click(object sender, EventArgs e)
        {
            UpCam_TakeSnapshot();
        }

        private void UpCam_TakeSnapshot()
        {
            SelectCamera(Machine.UpCamera);
            DisplayText("UpCam_TakeSnapshot()");
            Machine.UpCamera.SnapshotRotation = Machine.Position.CurrentA;
            Machine.UpCamera.BuildMeasurementFunctionsList(UpcamSnapshot_dataGridView);
            Machine.UpCamera.TakeSnapshot();

            Machine.DownCamera.SnapshotOriginalImage = new Bitmap(Machine.UpCamera.SnapshotImage);
            Machine.DownCamera.SnapshotImage = new Bitmap(Machine.UpCamera.SnapshotImage);

            // We need a copy of the snapshot to scale it, in 24bpp format. See http://stackoverflow.com/questions/2016406/converting-bitmap-pixelformats-in-c-sharp
            Bitmap Snapshot24bpp = new Bitmap(640, 480, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (Graphics gr = Graphics.FromImage(Snapshot24bpp))
            {
                gr.DrawImage(Machine.UpCamera.SnapshotOriginalImage, new Rectangle(0, 0, 640, 480));
            }
            // scale:
            double Xscale = Setting.UpCam_XmmPerPixel / Setting.DownCam_XmmPerPixel;
            double Yscale = Setting.UpCam_YmmPerPixel / Setting.DownCam_YmmPerPixel;
            double zoom = Machine.UpCamera.GetMeasurementZoom();
            Xscale = Xscale / zoom;
            Yscale = Yscale / zoom;
            int SnapshotSizeX = (int)(Xscale * 640);
            int SnapshotSizeY = (int)(Yscale * 480);
            // SnapshotSize is the size (in pxls) of upcam snapshot, scaled so that it draws in correct size on downcam image.
            ResizeNearestNeighbor RezFilter = new ResizeNearestNeighbor(SnapshotSizeX, SnapshotSizeY);
            Bitmap ScaledShot = RezFilter.Apply(Snapshot24bpp);  // and this is the scaled image
            // Mirror:
            Mirror MirrFilter = new Mirror(false, true);
            MirrFilter.ApplyInPlace(ScaledShot);

            // Clear DownCam image
            Graphics DownCamGr = Graphics.FromImage(Machine.DownCamera.SnapshotImage);
            DownCamGr.Clear(Color.Black);
            // Embed the ScaledShot to it. Upper left corner of the embedding is:
            int X = 320 - SnapshotSizeX / 2;
            int Y = 240 - SnapshotSizeY / 2;
            DownCamGr.DrawImage(ScaledShot, X, Y, SnapshotSizeX, SnapshotSizeY);
            Machine.DownCamera.SnapshotImage.MakeTransparent(Color.Black);
            // DownCam Snapshot is ok, copy it to original too
            Machine.DownCamera.SnapshotOriginalImage = new Bitmap(Machine.DownCamera.SnapshotImage);

            Machine.DownCamera.SnapshotRotation = Machine.Position.CurrentA;
        }

        private void UpcamSnapshot_ColorBox_MouseClick(object sender, MouseEventArgs e)
        {
            // Show the color dialog.
            DialogResult result = colorDialog1.ShowDialog();
            // See if user pressed ok.
            if (result == DialogResult.OK)
            {
                // Set form background to the selected color.
                UpcamSnapshot_ColorBox.BackColor = colorDialog1.Color;
                Setting.UpCam_SnapshotColor = colorDialog1.Color;
                Machine.UpCamera.SnapshotColor = colorDialog1.Color;
            }
        }


        private void DownCam_SnapshotToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref DowncamSnapshot_dataGridView);
        }

        private void DownCam_SnapshotToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(DowncamSnapshot_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void DownCam_TakeSnapshot_button_Click(object sender, EventArgs e)
        {
            Machine.DownCamera.SnapshotRotation = Machine.Position.CurrentA;
            Machine.DownCamera.BuildMeasurementFunctionsList(DowncamSnapshot_dataGridView);
            Machine.DownCamera.TakeSnapshot();
        }

        private void DowncamSnapshot_ColorBox_MouseClick(object sender, MouseEventArgs e)
        {
            // Show the color dialog.
            DialogResult result = colorDialog1.ShowDialog();
            // See if user pressed ok.
            if (result == DialogResult.OK)
            {
                // Set form background to the selected color.
                DowncamSnapshot_ColorBox.BackColor = colorDialog1.Color;
                Setting.DownCam_SnapshotColor = colorDialog1.Color;
                Machine.DownCamera.SnapshotColor = colorDialog1.Color;
            }
        }


        // ==========================================================================================================
        // Homing:
        private void HomingToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref Homing_dataGridView);
        }

        private void HomingToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Homing_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void HomingMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.DownCamera.IsRunning())
            {
                SetHomingMeasurement();
                // Big tolerance for manual debug
                DebugCirclesDownCamera(20.0 / Setting.DownCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Down camera is not running.");
            }
        }

        private void SetHomingMeasurement()
        {
            Machine.DownCamera.BuildMeasurementFunctionsList(Homing_dataGridView);
        }

        // ==========================================================================================================
        // OriginalFiducials:
        private void FiducialsToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref Fiducials_dataGridView);
        }

        private void FiducialsToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Fiducials_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void SetFiducialsMeasurement()
        {
            Machine.DownCamera.BuildMeasurementFunctionsList(Fiducials_dataGridView);
            Thread.Sleep(100);   // for automatic camera gain to have an effect
        }

        private void FiducialsMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.DownCamera.IsRunning())
            {
                SetFiducialsMeasurement();
                DisplayText("Circles:");
                DebugCirclesDownCamera(20.0 / Setting.DownCam_XmmPerPixel);
                DisplayText("Rectangles:");
                DebugRegtanclesDownCamera(20.0 / Setting.DownCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Down camera is not running.");
            }
        }

        // ==========================================================================================================
        // Components:

        private void DebugComponents_Camera(double Tolerance, ICamera Cam, double mmPerPixel)
        {
            double X = 0;
            double Y = 0;
            double A = 0.0;
            int count = MeasureClosestComponentInPx(out X, out Y, out A, Cam, Tolerance, 5);
            if (count == 0)
            {
                DisplayText("No results");
                return;
            }
            X = X * mmPerPixel;
            Y = -Y * mmPerPixel;
            DisplayText("Component measurement:");
            DisplayText("X: " + X.ToString("0.000", CultureInfo.InvariantCulture) + " (" + count.ToString() + " results out of 5)");
            DisplayText("Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture));
            DisplayText("A: " + A.ToString("0.000", CultureInfo.InvariantCulture));
        }


        private void ComponentsToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref Components_dataGridView);
        }

        private void ComponentsToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Components_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }


        private void SetComponentsMeasurement()
        {
            Machine.DownCamera.BuildMeasurementFunctionsList(Components_dataGridView);
        }

        private void ComponentsMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.DownCamera.IsRunning())
            {
                SetComponentsMeasurement();
                DebugComponents_Camera(
                    10.0 / Setting.DownCam_XmmPerPixel,
                    Machine.DownCamera,
                    Setting.DownCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Down camera is not running.");
            }
        }


        // ==========================================================================================================
        // Paper TapeNumber:
        private void PaperTapeToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref PaperTape_dataGridView);
        }

        private void PaperTapeToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(PaperTape_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        public void SetPaperTapeMeasurement()
        {
            Machine.DownCamera.BuildMeasurementFunctionsList(PaperTape_dataGridView);
        }

        private void PaperTapeMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.DownCamera.IsRunning())
            {
                SetPaperTapeMeasurement();
                DebugCirclesDownCamera(20.0 / Setting.DownCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Down camera is not running.");
            }
        }

        // ==========================================================================================================
        // Clear Plastic TapeNumber:
        private void ClearTapeToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref ClearTape_dataGridView);
        }

        private void ClearTapeToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(ClearTape_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        public void SetClearTapeMeasurement()
        {
            Machine.DownCamera.BuildMeasurementFunctionsList(ClearTape_dataGridView);
        }

        private void ClearTapeMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.DownCamera.IsRunning())
            {
                SetClearTapeMeasurement();
                DebugCirclesDownCamera(20.0 / Setting.DownCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Down camera is not running.");
            }
        }

        // ==========================================================================================================
        // Black Plastic TapeNumber:
        private void BlackTapeToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref BlackTape_dataGridView);
        }

        private void BlackTapeToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(BlackTape_dataGridView, ref Display_dataGridView);
            Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        public void SetBlackTapeMeasurement()
        {
            Machine.DownCamera.BuildMeasurementFunctionsList(BlackTape_dataGridView);
        }

        private void BlackTapeMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.DownCamera.IsRunning())
            {
                SetBlackTapeMeasurement();
                DebugCirclesDownCamera(20.0 / Setting.DownCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Down camera is not running.");
            }
        }

        // ==========================================================================================================
        // UpCam:
        // Nozzle:
        private void NozzleToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref Nozzle_dataGridView);
        }

        private void NozzleToHere2_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref Nozzle2_dataGridView);
        }

        private void NozzleToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Nozzle_dataGridView, ref Display_dataGridView);
            Machine.UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

         private void NozzleToDisplay2_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Nozzle2_dataGridView, ref Display_dataGridView);
            Machine.UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        //private void NozzleMeasure()
        //{
        //    if (Machine.UpCamera.IsRunning())
        //    {
        //        SetNozzleMeasurement();
        //        DebugNozzle();
        //    }
        //    else
        //    {
        //        DisplayText("Up camera is not running.");
        //    }
        //}

        private void NozzleMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.UpCamera.IsRunning())
            {
                Machine.UpCamera.BuildMeasurementFunctionsList(Nozzle_dataGridView);
                DebugNozzle();
            }
            else
            {
                DisplayText("Up camera is not running.");
            }
        }

        private void NozzleMeasure2_button_Click(object sender, EventArgs e)
        {
            if (Machine.UpCamera.IsRunning())
            {
                Machine.UpCamera.BuildMeasurementFunctionsList(Nozzle2_dataGridView);
                DebugNozzle();
            }
            else
            {
                DisplayText("Up camera is not running.");
            }
        }

        private void SetNozzleMeasurement()
        {
            if (NozzleUseTable2())
            {
                DisplayText("Using alternative table");
                Machine.UpCamera.BuildMeasurementFunctionsList(Nozzle2_dataGridView);
            }
            else
            {
                DisplayText("Using regular table");
                Machine.UpCamera.BuildMeasurementFunctionsList(Nozzle_dataGridView);
            }
        }

        //private void DebugCirclesUpCamera(double Tolerance)
        //{
        //    double X, Y;
        //    double Xpx, Ypx;
        //    if (Machine.UpCamera.GetClosestCircle(out X, out Y, Tolerance) > 0)
        //    {
        //        Xpx = X * Machine.UpCamera.GetMeasurementZoom();
        //        Ypx = Y * Machine.UpCamera.GetMeasurementZoom();
        //        DisplayText("X: " + Xpx.ToString() + "pixels, Y: " + Ypx.ToString() + "pixels");
        //        X = X * Setting.UpCam_XmmPerPixel;
        //        Y = -Y * Setting.UpCam_YmmPerPixel;
        //        DisplayText("X: " + X.ToString("0.000", CultureInfo.InvariantCulture));
        //        DisplayText("Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture));
        //    }
        //    else
        //    {
        //        DisplayText("No results.");
        //    }
        //}

        private void DebugNozzle()
        {
            double X = 0;
            double Y = 0;
            double radius = 0;
            Machine.UpCamera.MaxSize = Setting.Nozzles_CalibrationMaxSize / Setting.UpCam_XmmPerPixel;
            Machine.UpCamera.MinSize = Setting.Nozzles_CalibrationMinSize / Setting.UpCam_XmmPerPixel;
            double Maxdistance = Setting.Nozzles_CalibrationDistance / Setting.UpCam_XmmPerPixel;
            double Xpx, Ypx;
            double db1 = Setting.Nozzles_CalibrationMinSize;
            double db2 = Setting.Nozzles_CalibrationMaxSize;
            double db3 = Setting.UpCam_XmmPerPixel;
            int res = 0;
            Machine.UpCamera.SizeLimited = true;
            for (int tries = 0; tries < 10; tries++)
            {
                res = Machine.UpCamera.GetSmallestCircle(out X, out Y, out radius, Maxdistance);
                if (res != 0)
                {
                    break;
                }
                Thread.Sleep(100);

                if (tries >= 9)
                {
                    DisplayText("Can't see Nozzle, no results.");
                    Machine.UpCamera.SizeLimited = false;
                    return;
                }
            }
            Xpx = X * Machine.UpCamera.GetMeasurementZoom();
            Ypx = Y * Machine.UpCamera.GetMeasurementZoom();
            DisplayText(res.ToString() + " candidates, smallest: ");
            DisplayText("radius: " + radius.ToString("0.000", CultureInfo.InvariantCulture) + " pixels, " 
                + (radius * Setting.UpCam_XmmPerPixel).ToString("0.000", CultureInfo.InvariantCulture) + "mm");
            DisplayText("X: " + Xpx.ToString() + " pixels, Y: " + Ypx.ToString() + " pixels");
            X = X * Setting.UpCam_XmmPerPixel;
            Y = -Y * Setting.UpCam_YmmPerPixel;
            DisplayText("X: " + X.ToString("0.000", CultureInfo.InvariantCulture));
            DisplayText("Y: " + Y.ToString("0.000", CultureInfo.InvariantCulture));
            Machine.UpCamera.SizeLimited = false;
        }


        // ==========================================================================================================
        // Components:
        private void UpCamComponentsToHere_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(Display_dataGridView, ref UpCamComponents_dataGridView);
        }

        private void UpCamComponentsToDisplay_button_Click(object sender, EventArgs e)
        {
            DataGridViewCopy(UpCamComponents_dataGridView, ref Display_dataGridView);
            Machine.UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
        }

        private void SetUpCamComponentsMeasurement()
        {
            Machine.UpCamera.BuildMeasurementFunctionsList(UpCamComponents_dataGridView);
        }

        private void UpCamComponentsMeasure_button_Click(object sender, EventArgs e)
        {
            if (Machine.UpCamera.IsRunning())
            {
                SetUpCamComponentsMeasurement();
                DebugComponents_Camera(
                    10.0 / Setting.UpCam_XmmPerPixel,
                    Machine.UpCamera,
                    Setting.UpCam_XmmPerPixel);
            }
            else
            {
                DisplayText("Up camera is not running.");
            }
        }


        #endregion  Measurementboxes

        // ==========================================================================================================
        // Video processing functions lists control
        // ==========================================================================================================
        #region VideoProcessingFunctionsLists

        private void UpdateDisplayFunctions()
        {
            if (Machine.DownCamera.IsRunning())
            {

                Machine.DownCamera.BuildDisplayFunctionsList(Display_dataGridView);
            }
            if (Machine.UpCamera.IsRunning())
            {

                Machine.UpCamera.BuildDisplayFunctionsList(Display_dataGridView);
            }
        }

        // ==========================================================================================================
        // Add, delete and move rows:

        private void AddCamFunction_button_Click(object sender, EventArgs e)
        {
            DataGridViewSelectedRowCollection SelectedRows = Display_dataGridView.SelectedRows;
            int index = 0;
            if (Display_dataGridView.Rows.Count == 0)
            {
                // grid is empty:
                Display_dataGridView.Rows.Insert(0);
            }
            else
            {
                // insert at end
                Display_dataGridView.Rows.Insert(Display_dataGridView.Rows.Count);
                index = Display_dataGridView.Rows.Count - 1;
            };
            Display_dataGridView.Rows[index].Cells[1].Value = "false";	// This will be tested later, and can't be null
        }

        private void DeleteCamFunction_button_Click(object sender, EventArgs e)
        {
            int DoomedRow = Display_dataGridView.CurrentCell.RowIndex;
            Display_dataGridView.ClearSelection();
            Display_dataGridView.Rows.RemoveAt(DoomedRow);
            UpdateDisplayFunctions();
        }

        private void CamFunctionUp_button_Click(object sender, EventArgs e)
        {
            DataGrid_Up_button(Display_dataGridView);
        }

        private void CamFunctionDown_button_Click(object sender, EventArgs e)
        {
            DataGrid_Down_button(Display_dataGridView);
        }

        private void CamFunctionsClear_button_Click(object sender, EventArgs e)
        {
            Display_dataGridView.Rows.Clear();
            UpdateDisplayFunctions();
            ClearEditTargets();
        }

        // ==========================================================================================================
        // Display_dataGridView functions:
        enum Display_dataGridViewColumns { Function, Active, Int, Double, R, G, B };
        // NOTE: a verbatim copy is at camera.cs. If you edit this, edit camera.cs as well!

        private void Display_dataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            int FunctionCol = (int)Display_dataGridViewColumns.Function;
            int ActiveCol = (int)Display_dataGridViewColumns.Active;
            if ((Display_dataGridView.CurrentCell.ColumnIndex != FunctionCol)
                &&
                (Display_dataGridView.CurrentCell.ColumnIndex != ActiveCol))
            {
                return;  // parameter changes hadled by the parameter edit widgets
            };

            // Return if not dirty; otherwise the stuff is executed twice (once when it changes, once when it becomes clean)
            if (!Display_dataGridView.IsCurrentCellDirty)
            {
                return;
            }
            if (SetupCamerasPageVisible)
            {
                // react immediately to changes
                Update_GridView(Display_dataGridView);
                if (Display_dataGridView.CurrentRow.Cells[ActiveCol].Value.ToString() == "True")
                {
                    SetEditTargets();
                }
                else
                {
                    ClearEditTargets();
                }
                UpdateDisplayFunctions();
            }
        }

        private void Display_dataGridView_CurrentCellChanged(object sender, EventArgs e)
        {
            if (Display_dataGridView.CurrentCell != null)
            {
                SetEditTargets();
            }
        }

        // ==========================================================================================================
        // Parameter labels and control widgets:
        // ==========================================================================================================
        // Sharing the labels and some controls so I don't need to duplicate so much code:
        private void UpdateCameraCameraStatus_labelThreadSafe()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    UpdateCameraCameraStatus_label();
                }));
            }
            else
            {
                UpdateCameraCameraStatus_label();
            }
        }

        private void UpdateCameraCameraStatus_label()
        {
            if (tabControlPages.SelectedTab.Name!= "tabPageSetupCameras")
            {
                return;
            }
            ICamera cam= Machine.DownCamera;
            switch (CamerasSetUp_tabControl.SelectedTab.Name)
            {
                case "DownCamera_tabPage":
                    cam = Machine.DownCamera;
                    break;
                case "UpCamera_tabPage":
                    cam = Machine.UpCamera;
                    break;
            }
            if (cam.Active)
            {
                CameraStatus_label.Text = "Active";
            }
            else
            {
                CameraStatus_label.Text = "Not Active";
            }
        }


        private void CamerasSetUp_tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            // move labels and value setting widgets to current page
            TabPage Page = DownCamera_tabPage;
            switch (CamerasSetUp_tabControl.SelectedTab.Name)
            {
                case "DownCamera_tabPage":
                    Page = DownCamera_tabPage;
                    break;
                case "UpCamera_tabPage":
                    Page = UpCamera_tabPage;
                    break;
            }
            Display_dataGridView.Parent = Page;
            Parameter_int_label.Parent = Page;
            Parameter_Int_numericUpDown.Parent = Page;
            Parameter_double_label.Parent = Page;
            Parameter_double_textBox.Parent = Page;
            AddCamFunction_button.Parent = Page;
            DeleteCamFunction_button.Parent = Page;
            CamFunctionUp_button.Parent = Page;
            CamFunctionDown_button.Parent = Page;
            CamFunctionsClear_button.Parent = Page;
            Color_label.Parent = Page;
            Color_Box.Parent = Page;
            R_label.Parent = Page;
            R_numericUpDown.Parent = Page;
            G_label.Parent = Page;
            G_numericUpDown.Parent = Page;
            B_label.Parent = Page;
            B_numericUpDown.Parent = Page;
            ColorHelp_label.Parent = Page;
            RobustFast_checkBox.Parent = Page;
            KeepActive_checkBox.Parent = Page;
            ListResolutions_button.Parent = Page;
            CamShowPixels_checkBox.Parent = Page;
            CameraStatus_label.Parent = Page;
            UpdateCameraCameraStatus_labelThreadSafe();
        }

        private void ClearEditTargets()
        {
            Parameter_int_label.Text = "--";
            Parameter_Int_numericUpDown.Enabled = false;
            Parameter_Int_numericUpDown.Value = 0;
            Parameter_double_label.Text = "--";
            Parameter_double_textBox.Enabled = false;
            Color_label.Text = "--";
            Color_Box.Enabled = false;
            R_label.Text = "--";
            R_numericUpDown.Enabled = false;
            G_label.Text = "--";
            G_numericUpDown.Enabled = false;
            B_label.Text = "--";
            B_numericUpDown.Enabled = false;
            ColorHelp_label.Visible = false;
        }

        private void ClearParameterValuesExcept(int row, int keep)
        {
            for (int i = 2; i < Display_dataGridView.Columns.Count; i++)
            {
                if (i != keep)
                {
                    Display_dataGridView.Rows[row].Cells[i].Value = null;
                }
            }
        }

        private void DoRGBparameters(int row)
        {
            Parameter_int_label.Text = "Range";
            Parameter_Int_numericUpDown.Enabled = true;
            Parameter_double_textBox.Enabled = false;
            int DoubleCol = (int)Display_dataGridViewColumns.Double;
            Display_dataGridView.Rows[row].Cells[DoubleCol].Value = null;
            R_label.Text = "R";
            R_numericUpDown.Enabled = true;
            G_label.Text = "G";
            G_numericUpDown.Enabled = true;
            B_label.Text = "B";
            B_numericUpDown.Enabled = true;
            ColorHelp_label.Visible = true;

            int par_i;
            int IntCol = (int)Display_dataGridViewColumns.Int;
            if (Display_dataGridView.Rows[row].Cells[IntCol].Value == null)
            {
                Parameter_Int_numericUpDown.Value = 10;
                Display_dataGridView.Rows[row].Cells[IntCol].Value = "10";
            }
            else if (!int.TryParse(Display_dataGridView.Rows[row].Cells[IntCol].Value.ToString(), out par_i))
            {
                Parameter_Int_numericUpDown.Value = 10;
                Display_dataGridView.Rows[row].Cells[IntCol].Value = "10";
            }
            else
            {
                Parameter_Int_numericUpDown.Value = par_i;
            }

            SetColorBoxColor(row);


        }

        private void SetGridRowColor(int row)
        {
            // Didn't find good color definition from dataviewgrid, so we'll copy the box color to grid.
            int R_col = (int)Display_dataGridViewColumns.R;
            int G_col = (int)Display_dataGridViewColumns.G;
            int B_col = (int)Display_dataGridViewColumns.B;

            Color BoxColor = Color_Box.BackColor;
            Display_dataGridView.Rows[row].Cells[R_col].Value = BoxColor.R;
            Display_dataGridView.Rows[row].Cells[G_col].Value = BoxColor.G;
            Display_dataGridView.Rows[row].Cells[B_col].Value = BoxColor.B;
        }

        private void SetColorBoxColor(int row)
        {
            byte R = 128;
            byte G = 128;
            byte B = 128;
            byte temp;
            int R_col = (int)Display_dataGridViewColumns.R;
            int G_col = (int)Display_dataGridViewColumns.G;
            int B_col = (int)Display_dataGridViewColumns.B;

            if (Display_dataGridView.Rows[row].Cells[R_col].Value == null)
            {
                SetGridRowColor(row);
                return;
            }
            else
            {
                if (byte.TryParse(Display_dataGridView.Rows[row].Cells[R_col].Value.ToString(), out temp))
                {
                    R = temp;
                }
                else
                {
                    SetGridRowColor(row);
                    return;
                }
            };

            if (Display_dataGridView.Rows[row].Cells[G_col].Value == null)
            {
                SetGridRowColor(row);
                return;
            }
            else
            {
                if (byte.TryParse(Display_dataGridView.Rows[row].Cells[G_col].Value.ToString(), out temp))
                {
                    G = temp;
                }
                else
                {
                    SetGridRowColor(row);
                    return;
                }
            };

            if (Display_dataGridView.Rows[row].Cells[B_col].Value == null)
            {
                SetGridRowColor(row);
                return;
            }
            else
            {
                if (byte.TryParse(Display_dataGridView.Rows[row].Cells[B_col].Value.ToString(), out temp))
                {
                    B = temp;
                }
                else
                {
                    SetGridRowColor(row);
                    return;
                }
            };
            // have not returned, so set box color:
            Color_Box.BackColor = Color.FromArgb(R, G, B);
        }

        private void SetProcessingFunctions(DataGridView Grid)
        {
            // To add a video processing fuction:
            // Add the name to here.
            // Add the name to ICamera.cs, BuildFunctionsList()
            // Add the parameter handling to SetEditTargets() below
            // Add the actual function (take example of any function already referred from BuildFunctionsList()

            DataGridViewComboBoxColumn comboboxColumn =
                (DataGridViewComboBoxColumn)Grid.Columns[(int)Display_dataGridViewColumns.Function];
            comboboxColumn.Items.Clear();
            comboboxColumn.Items.AddRange("Threshold", "Histogram", "Grayscale", "Invert", "Edge detect",
                "Noise reduction", "Kill color", "Keep color", "Blur", "Gaussian blur", "Meas. zoom");
        }

        private void SetEditTargets()
        {
            int row = Display_dataGridView.CurrentCell.RowIndex;
            int col = Display_dataGridView.CurrentCell.ColumnIndex;
            int FunctCol = (int)Display_dataGridViewColumns.Function;
            int ActiveCol = (int)Display_dataGridViewColumns.Active;
            int par_i;
            int IntCol = (int)Display_dataGridViewColumns.Int;
            double par_d;
            int DoubleCol = (int)Display_dataGridViewColumns.Double;

            ClearEditTargets();
            if (Display_dataGridView.Rows[row].Cells[FunctCol].Value == null)
            {
                return;
            };
            if (Display_dataGridView.Rows[row].Cells[ActiveCol].Value == null)
            {
                return;
            };
            if (Display_dataGridView.Rows[row].Cells[ActiveCol].Value.ToString() == "False")
            {
                return;
            };
            switch (Display_dataGridView.Rows[row].Cells[FunctCol].Value.ToString())
            {
                // switch by the selected algorithm:  
                case "Blur":
                    ClearParameterValuesExcept(row, -1);
                    return;		// no parameters

                case "Histogram":
                    ClearParameterValuesExcept(row, -1);
                    return;		// no parameters

                case "Grayscale":
                    ClearParameterValuesExcept(row, -1);
                    return;		// no parameters

                case "Invert":
                    ClearParameterValuesExcept(row, -1);
                    return;		// no parameters

                case "Edge detect":
                    ClearParameterValuesExcept(row, 2);
                    if (Display_dataGridView.Rows[row].Cells[IntCol].Value == null)
                    {
                        Parameter_Int_numericUpDown.Value = 1;
                        Display_dataGridView.Rows[row].Cells[IntCol].Value = "1";
                    }
                    else if (!int.TryParse(Display_dataGridView.Rows[row].Cells[2].Value.ToString(), out par_i))
                    {
                        Parameter_Int_numericUpDown.Value = 1;
                        Display_dataGridView.Rows[row].Cells[IntCol].Value = "1";
                    }
                    else
                    {
                        Parameter_Int_numericUpDown.Value = par_i;
                    }
                    Parameter_Int_numericUpDown.Enabled = true;
                    Parameter_int_label.Text = "Type (1..4)";
                    UpdateDisplayFunctions();
                    return;		// no parameters

                case "Noise reduction":
                    ClearParameterValuesExcept(row, 2);
                    if (Display_dataGridView.Rows[row].Cells[IntCol].Value == null)
                    {
                        Parameter_Int_numericUpDown.Value = 1;
                        Display_dataGridView.Rows[row].Cells[IntCol].Value = "1";
                    }
                    else if (!int.TryParse(Display_dataGridView.Rows[row].Cells[2].Value.ToString(), out par_i))
                    {
                        Parameter_Int_numericUpDown.Value = 1;
                        Display_dataGridView.Rows[row].Cells[IntCol].Value = "1";
                    }
                    else
                    {
                        Parameter_Int_numericUpDown.Value = par_i;
                    }
                    Parameter_Int_numericUpDown.Enabled = true;
                    Parameter_int_label.Text = "Type (1..3)";
                    UpdateDisplayFunctions();
                    return;		// no parameters

                case "Kill color":
                    DoRGBparameters(row);
                    Color_label.Text = "Color to kill:";
                    UpdateDisplayFunctions();
                    return;

                case "Keep color":
                    Color_label.Text = "Color to keep:";
                    DoRGBparameters(row);
                    UpdateDisplayFunctions();
                    return;

                case "Meas. zoom":
                    // one double parameter
                    ClearParameterValuesExcept(row, 3);
                    if (Display_dataGridView.Rows[row].Cells[DoubleCol].Value == null)
                    {
                        Parameter_double_textBox.Text = "2.0";
                        Display_dataGridView.Rows[row].Cells[DoubleCol].Value = "2.0";
                    }
                    else if (!double.TryParse(Display_dataGridView.Rows[row].Cells[3].Value.ToString().Replace(',', '.'), out par_d))
                    {
                        Parameter_double_textBox.Text = "2.0";
                        Display_dataGridView.Rows[row].Cells[DoubleCol].Value = "2.0";
                    }
                    else
                    {
                        Parameter_double_textBox.Text = par_d.ToString("0.0");
                    }
                    Parameter_double_textBox.Enabled = true;
                    Parameter_double_label.Text = "Zoom factor";
                    UpdateDisplayFunctions();
                    break;

                case "Gaussian blur":
                    // one double parameter
                    ClearParameterValuesExcept(row, 3);
                    if (Display_dataGridView.Rows[row].Cells[DoubleCol].Value == null)
                    {
                        Parameter_double_textBox.Text = "2.0";
                        Display_dataGridView.Rows[row].Cells[DoubleCol].Value = "2.0";
                    }
                    else if (!double.TryParse(Display_dataGridView.Rows[row].Cells[3].Value.ToString().Replace(',', '.'), out par_d))
                    {
                        Parameter_double_textBox.Text = "2.0";
                        Display_dataGridView.Rows[row].Cells[DoubleCol].Value = "2.0";
                    }
                    else
                    {
                        Parameter_double_textBox.Text = par_d.ToString("0.00");
                    }
                    Parameter_double_textBox.Enabled = true;
                    Parameter_double_label.Text = "Sigma, 0.01 to 5.0";
                    UpdateDisplayFunctions();
                    break;

                case "Threshold":
                    // one int parameter
                    ClearParameterValuesExcept(row, 2);

                    if (Display_dataGridView.Rows[row].Cells[IntCol].Value == null)
                    {
                        Parameter_Int_numericUpDown.Value = 100;
                        Display_dataGridView.Rows[row].Cells[IntCol].Value = "100";
                    }
                    else if (!int.TryParse(Display_dataGridView.Rows[row].Cells[2].Value.ToString(), out par_i))
                    {
                        Parameter_Int_numericUpDown.Value = 100;
                        Display_dataGridView.Rows[row].Cells[IntCol].Value = "100";
                    }
                    else
                    {
                        Parameter_Int_numericUpDown.Value = par_i;
                    }
                    Parameter_Int_numericUpDown.Enabled = true;
                    Parameter_int_label.Text = "Threshold";
                    UpdateDisplayFunctions();
                    break;

                default:
                    return;
                // break;
            }
        }

        private void Parameter_Int_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (!Parameter_Int_numericUpDown.Enabled)
            {
                return;		// so that it can be cleared safely
            }

            int row = Display_dataGridView.CurrentCell.RowIndex;
            int IntCol = (int)Display_dataGridViewColumns.Int;
            Display_dataGridView.Rows[row].Cells[IntCol].Value = Parameter_Int_numericUpDown.Value.ToString();
            UpdateDisplayFunctions();
        }

        private void Parameter_double_textBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            double val;
            int DoubleCol = (int)Display_dataGridViewColumns.Double;
            int row = Display_dataGridView.CurrentCell.RowIndex;
            if (e.KeyChar == '\r')
            {
                if (double.TryParse(Parameter_double_textBox.Text.Replace(',', '.'), out val))
                {
                    Display_dataGridView.Rows[row].Cells[DoubleCol].Value = Parameter_double_textBox.Text;
                    UpdateDisplayFunctions();
                }
            }

        }

        private void Parameter_double_textBox_Leave(object sender, EventArgs e)
        {
            double val;
            int DoubleCol = (int)Display_dataGridViewColumns.Double;
            int row = Display_dataGridView.CurrentCell.RowIndex;
            if (double.TryParse(Parameter_double_textBox.Text.Replace(',', '.'), out val))
            {
                Display_dataGridView.Rows[row].Cells[DoubleCol].Value = Parameter_double_textBox.Text;
                UpdateDisplayFunctions();
            }

        }

        private void R_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (!R_numericUpDown.Enabled)
            {
                return;
            }
            int row = Display_dataGridView.CurrentCell.RowIndex;
            int R_col = (int)Display_dataGridViewColumns.R;
            Display_dataGridView.Rows[row].Cells[R_col].Value = R_numericUpDown.Value.ToString();
            SetColorBoxColor(row);
            UpdateDisplayFunctions();
        }

        private void G_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (!G_numericUpDown.Enabled)
            {
                return;
            }
            int row = Display_dataGridView.CurrentCell.RowIndex;
            int G_col = (int)Display_dataGridViewColumns.G;
            Display_dataGridView.Rows[row].Cells[G_col].Value = G_numericUpDown.Value.ToString();
            SetColorBoxColor(row);
            UpdateDisplayFunctions();
        }

        private void B_numericUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (!B_numericUpDown.Enabled)
            {
                return;
            }
            int row = Display_dataGridView.CurrentCell.RowIndex;
            int B_col = (int)Display_dataGridViewColumns.B;
            Display_dataGridView.Rows[row].Cells[B_col].Value = B_numericUpDown.Value.ToString();
            SetColorBoxColor(row);
            UpdateDisplayFunctions();
        }


        private void PickColor(int X, int Y)
        {
            if (X < 2)
                X = 2;
            if (X > (Cam_pictureBox.Width - 2))
                X = Cam_pictureBox.Width - 2;
            if (Y < 2)
                Y = 2;
            if (Y > (Cam_pictureBox.Height - 2))
                X = Cam_pictureBox.Height - 2;

            int Rsum = 0;
            int Gsum = 0;
            int Bsum = 0;
            byte R = 0;
            byte G = 0;
            byte B = 0;
            Color pixelColor;
            int deb = 0;
            Bitmap img = (Bitmap)Cam_pictureBox.Image;
            if (img!=null)
            {
                for (int ix = X - 2; ix <= X + 2; ix++)
                {
                    for (int iy = Y - 2; iy <= Y + 2; iy++)
                    {
                        pixelColor = img.GetPixel(ix, iy);
                        Rsum += pixelColor.R;
                        Gsum += pixelColor.G;
                        Bsum += pixelColor.B;
                        deb++;
                    }
                }
                R = (byte)(Rsum / 25);
                G = (byte)(Gsum / 25);
                B = (byte)(Bsum / 25);
                img.Dispose();
           }
            R_numericUpDown.Value = R;
            G_numericUpDown.Value = G;
            B_numericUpDown.Value = B;
            Color_Box.BackColor = Color.FromArgb(R, G, B);
        }

        private void Display_dataGridView_DataError(object sender, DataGridViewDataErrorEventArgs anError)
        {
            DisplayText("PaperTape_dataGridView values:");
            string msg = "";
            foreach (DataGridViewRow Row in PaperTape_dataGridView.Rows)
            {
                for (int i = 0; i < Row.Cells.Count; i++)
                {
                    if (Row.Cells[i].Value == null)
                    {
                        msg += "null:";
                    }
                    else
                    {
                        msg += Row.Cells[i].Value.ToString() + ":";
                    }
                }
                DisplayText(msg);
            }
            DisplayText("Display_dataGridView values:");
            msg = "";
            foreach (DataGridViewRow Row in Display_dataGridView.Rows)
            {
                for (int i = 0; i < Row.Cells.Count; i++)
                {
                    if (Row.Cells[i].Value == null)
                    {
                        msg += "null:";
                    }
                    else
                    {
                        msg += Row.Cells[i].Value.ToString() + ":";
                    }
                }
                DisplayText("msg");
            }

        }
        #endregion

        // ==========================================================================================================
        // Nozzles
        // ==========================================================================================================
        #region Nozzles

        // Note: Datagrid rows go from 0 to n, we show nozzles from 1 to n.
        // When referring to nozzle, think nozzle no, which is datagridrow + 1 (and vice versa).

        // datagrid column numbers
        const int Nozzledata_NozzleNoColumn = 0;
        const int Nozzledata_StartXColumn = 1;
        const int Nozzledata_StartYColumn = 2;
        const int Nozzledata_StartZColumn = 3;
        // Rest are move 1 axis, move 1 amount, move 2 axis, ...
        // == Nozzledata_StartZColumn+(moveno-1)*2+1 for axis, Nozzledata_StartZColumn+(moveno-1)*2+2 for amount.

        const int NoOfNozzleMoves = 8;

        // ==========================================================================================================
        // Context menus:

        // The load and unload datagrids have context menu. We want to know which nozzle the user right-clicked:
        private int ContextmenuLoadNozzle = 1;
        private int ContextmenuUnloadNozzle = 1;

        // Doh! mouseclick right button event does not fire if context menu is set! So, we need to keep track of the cell
        // the mouse is over and if right button goes down, make a note of the position.
        private int LoadMouseEnterRow;
        private int UnloadMouseEnterRow;

        private void NozzlesLoad_dataGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            LoadMouseEnterRow = e.RowIndex;
        }

        private void NozzlesLoad_dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ContextmenuLoadNozzle = LoadMouseEnterRow + 1;
            }
        }

        private void NozzlesUnload_dataGridView_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            UnloadMouseEnterRow = e.RowIndex;
        }

        private void NozzlesUnload_dataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ContextmenuUnloadNozzle = UnloadMouseEnterRow + 1;
            }
        }


        // ==========================================================================================================
        // Context (right click) menu items

        private async void gotoStartPositionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ContextmenuLoadNozzle==0)
            {
                DisplayText("Goto load start - heaqder click, ignored", KnownColor.DarkGreen);
                return;
            }
            DisplayText("Goto load start", KnownColor.DarkGreen);
            await m_NozzleGotoStartAsync(NozzlesLoad_dataGridView, ContextmenuLoadNozzle);
        }

        private async void gotoUnloadStartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ContextmenuUnloadNozzle == 0)
            {
                DisplayText("Goto unload start - heaqder click, ignored", KnownColor.DarkGreen);
                return;
            }
            DisplayText("Goto unload start", KnownColor.DarkGreen);
            await m_NozzleGotoStartAsync(NozzlesUnload_dataGridView, ContextmenuUnloadNozzle);
        }

        private void CopyUnloadStartPositionsFromLoadEndPositions()
        {
            double X;
            double Y;
            double Z;
            if (NozzlesLoad_dataGridView.RowCount==0)
            {
                return;
            }
            NozzlesLoad_dataGridView.CurrentCell = NozzlesLoad_dataGridView[0, 0];   // If cursor is on an editable cell, old value may be used.
            NozzlesUnload_dataGridView.CurrentCell = NozzlesUnload_dataGridView[0, 0];

            for (int nozzle = 1; nozzle <= Setting.Nozzles_count; nozzle++)
            {
                if (GetLoadresult(nozzle, out X, out Y, out Z))
                {
                    NozzlesUnload_dataGridView.Rows[nozzle - 1].Cells[Nozzledata_StartXColumn].Value = X.ToString("0.000", CultureInfo.InvariantCulture);
                    NozzlesUnload_dataGridView.Rows[nozzle - 1].Cells[Nozzledata_StartYColumn].Value = Y.ToString("0.000", CultureInfo.InvariantCulture);
                    NozzlesUnload_dataGridView.Rows[nozzle - 1].Cells[Nozzledata_StartZColumn].Value = Z.ToString("0.000", CultureInfo.InvariantCulture);

                }
            }
        }

        private void copyUnloadStartPositionsFromLoadEndPositionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Get unload start positions from load end positions", KnownColor.DarkGreen);
            CopyUnloadStartPositionsFromLoadEndPositions();
        }


        // ===============================
        // Helper functions for getUnloadMovesFromLoadMovesToolStripMenuItem_Click
        
        private bool FindLastMove_m(int row, out double Z)
        {
            int AxisInd;
            int ValInd;
            Z = 0;
            for (int i = NoOfNozzleMoves; i > 1; i--)
            {
                AxisInd = Nozzledata_StartZColumn + (i - 1) * 2 + 1;
                ValInd= AxisInd+1;
                if (NozzlesLoad_dataGridView.Rows[row].Cells[AxisInd].Value!=null)
                {
                    if (NozzlesLoad_dataGridView.Rows[row].Cells[AxisInd].Value.ToString() != "--")
                    {
                        if (NozzlesLoad_dataGridView.Rows[row].Cells[AxisInd].Value.ToString() != "Z")
                        {
                            DisplayText("last load move should be Z", KnownColor.DarkRed);
                            return false;
                        }
                        if (NozzlesLoad_dataGridView.Rows[row].Cells[ValInd].Value == null)
                        {
                            DisplayText("last load move amount not set", KnownColor.DarkRed);
                            return false;
                        }
                        if (!double.TryParse(NozzlesLoad_dataGridView.Rows[row].Cells[ValInd].Value.ToString().Replace(',', '.'), out Z))
                        {
                            DisplayText("Bad data: nozzle #" + (row + 1).ToString() + ", move " + i.ToString(), KnownColor.DarkRed);
                            return false;
                        }
                        return true;
                    }
                }
            }
            DisplayText("Moves to load nozzle "+(row+1).ToString()+" not set", KnownColor.DarkRed);
            return false;
        }


        private void getUnloadMovesFromLoadMovesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Get unload moves from load moves", KnownColor.DarkGreen);
            CopyUnloadStartPositionsFromLoadEndPositions();
            for (int i = 0; i < Setting.Nozzles_count; i++)
            {
                NozzlesUnload_dataGridView.Rows[i].Cells[4].Value = "Z";    // first move axis
                double Z;
                if (!FindLastMove_m(i, out Z))
                {
                    return;
                }
                NozzlesUnload_dataGridView.Rows[i].Cells[5].Value = (-Z).ToString("0.000", CultureInfo.InvariantCulture);

                double Yload;
                double Yunload;
                double Xload;
                double Xunload;
                if (!double.TryParse(NozzlesLoad_dataGridView.Rows[i].Cells[Nozzledata_StartYColumn].Value.ToString().Replace(',', '.'), out Yload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString() + ", Load start Y", KnownColor.DarkRed);
                    return;
                }
                if (!double.TryParse(NozzlesUnload_dataGridView.Rows[i].Cells[Nozzledata_StartYColumn].Value.ToString().Replace(',', '.'), out Yunload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString() + ", Unload start Y", KnownColor.DarkRed);
                    return;
                }
                if (!double.TryParse(NozzlesLoad_dataGridView.Rows[i].Cells[Nozzledata_StartXColumn].Value.ToString().Replace(',', '.'), out Xload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString() + ", Load start X", KnownColor.DarkRed);
                    return;
                }
                if (!double.TryParse(NozzlesUnload_dataGridView.Rows[i].Cells[Nozzledata_StartXColumn].Value.ToString().Replace(',', '.'), out Xunload))
                {
                    DisplayText("Bad data: nozzle #" + (i + 1).ToString() + ", Unload start X", KnownColor.DarkRed);
                    return;
                }
                if ((Math.Abs(Yload - Yunload)>0.1)&& (Math.Abs(Xload - Xunload) > 0.1))
                {
                    DisplayText("Both X and Y changed on load sequence; too complex to figure out unload", KnownColor.DarkRed);
                    return;
                }
                if ((Math.Abs(Yload - Yunload) > 0.1))
                {
                    NozzlesUnload_dataGridView.Rows[i].Cells[6].Value = "Y";
                    NozzlesUnload_dataGridView.Rows[i].Cells[7].Value = (Yload - Yunload).ToString("0.000", CultureInfo.InvariantCulture);
                }
                else
                {
                    NozzlesUnload_dataGridView.Rows[i].Cells[6].Value = "X";
                    NozzlesUnload_dataGridView.Rows[i].Cells[7].Value = (Xload - Xunload).ToString("0.000", CultureInfo.InvariantCulture);
                }
                NozzlesUnload_dataGridView.Rows[i].Cells[8].Value = "Z";
                NozzlesUnload_dataGridView.Rows[i].Cells[9].Value = Z.ToString("0.000", CultureInfo.InvariantCulture);
            }
        }

    // ==========================================================================================================
    private void copyLoadMovesFromNozzle1_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Copy load moves from nozzle 1", KnownColor.DarkGreen);
            copyMovesFromNozzle1(NozzlesLoad_dataGridView);
        }

        private void copyUnloadMovesFromNozzle1_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayText("Copy unload moves from nozzle 1", KnownColor.DarkGreen);
            copyMovesFromNozzle1(NozzlesUnload_dataGridView);
        }

        private void copyMovesFromNozzle1(DataGridView grid)
        {
            if (grid.RowCount==0)
            {
                return;
            }
            grid.CurrentCell = grid[0, 0];
            for (int nozzle = 1; nozzle < Setting.Nozzles_count; nozzle++)
            {
                for (int i = Nozzledata_StartZColumn+1; i < grid.ColumnCount; i++)
                {
                    grid.Rows[nozzle].Cells[i].Value = grid.Rows[0].Cells[i].Value;
                }
            }
        }

        // ==========================================================================================================
        private bool GetLoadresult(int nozzle, out double X, out double Y, out double Z)
        {
            X = 0.0;
            Y = 0.0;
            Z = 0.0;
            if (!m_GetNozzleStartCoordinates(NozzlesLoad_dataGridView, nozzle, out X, out Y, out Z))
            {
                return false;
            }
            int Move = 1;
            bool AllDone = false;
            double dX;
            double dY;
            double dZ;
            while (!AllDone)
            {
                if (!m_getNozzleMove(NozzlesLoad_dataGridView, nozzle, Move++, out dX, out dY, out dZ, out AllDone))
                {
                    return false;
                }
                X = X + dX;
                Y = Y + dY;
                Z = Z + dZ;
            }
            return true;
        }

        private bool m_getNozzleMove(DataGridView grid, int nozzle, int move, out double dX, out double dY, out double dZ, out bool AllDone)
        {
            dX = 0.0;
            dY = 0.0;
            dZ = 0.0;
            AllDone = false;
            if (move > NoOfNozzleMoves)
            {
                AllDone = true;
                return false;
            };
            // is direction set? If not, all done.
            int DirCol = Nozzledata_StartZColumn + (move - 1) * 2 + 1;
            if (grid.Rows[nozzle - 1].Cells[DirCol].Value == null)
            {
                AllDone = true;
                return true;
            }
            if (grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString() == "--")
            {
                AllDone = true;
                return true;
            }
            double val;
            string op= "undefined";
            if (grid == NozzlesLoad_dataGridView)
            {
                op = "load ";
            }
            if (grid == NozzlesUnload_dataGridView)
            {
                op = "unload ";
            }
            if (!NozzleDataCheck(grid, nozzle, DirCol + 1, out val))
            {
                DisplayText("Bad data: " + op + "nozzle #" + nozzle + ", move " + (DirCol + 1).ToString(), KnownColor.DarkRed);
            }
            switch (grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString())
            {
                case "X":
                    dX = val;
                    DisplayText(op + " nozzle " + nozzle.ToString() + ", move " + move.ToString() + ": X" + val.ToString());
                    break;

                case "Y":
                    dY = val;
                    DisplayText(op + " nozzle " + nozzle.ToString() + ", move " + move.ToString() + ": Y" + val.ToString());
                    break;

                case "Z":
                    dZ = val;
                    DisplayText(op + " nozzle " + nozzle.ToString() + ", move " + move.ToString() + ": Z" + val.ToString());
                    break;

                default:
                    DisplayText("m_getNozzleMove: " + op + " nozzle" + nozzle.ToString() + ", move " + move.ToString() + "?");
                    return false;
                    //break;
            }
            return true;
        }

        // ==========================================================================================================
        private void BuildNozzleTable(DataGridView Grid)
        {
            for (int i = 1; i <= NoOfNozzleMoves; i++)
            {
                DataGridViewComboBoxColumn ComboCol = new DataGridViewComboBoxColumn();
                ComboCol.Items.Add("X");
                ComboCol.Items.Add("Y");
                ComboCol.Items.Add("Z");
                ComboCol.Items.Add("--");
                ComboCol.HeaderText = "move" + i.ToString() + " axis";
                ComboCol.Width = 44;
                ComboCol.Name = "MoveNumber" + i.ToString() + "axis_Column";
                Grid.Columns.Add(ComboCol);
                DataGridViewTextBoxColumn TextCol = new DataGridViewTextBoxColumn();
                TextCol.HeaderText="move" + i.ToString() + " amount";
                TextCol.Width = 48;
                TextCol.Name = "MoveNumber" + i.ToString() + "amount_Column";
                Grid.Columns.Add(TextCol);
            }
            foreach (DataGridViewColumn column in Grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

        }

        private void Nozzles_initialize()
        {
            DisplayText("Loading nozzles data");
            // build tables
            BuildNozzleTable(NozzlesLoad_dataGridView);
            BuildNozzleTable(NozzlesUnload_dataGridView);
            for (int i = 0; i < Setting.Nozzles_count; i++)
            {
                AddNozzle(false);
            }
            NoOfNozzles_UpDown.Value = Setting.Nozzles_count;
            // fill values
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int ind = path.LastIndexOf('\\');
            path = path.Remove(ind + 1);
            LoadDataGrid(path + "LitePlacer.NozzlesLoadData_v2", NozzlesLoad_dataGridView, DataTableType.Nozzles);
            LoadDataGrid(path + "LitePlacer.NozzlesUnLoadData_v2", NozzlesUnload_dataGridView, DataTableType.Nozzles);
            LoadDataGrid(path + "LitePlacer.NozzlesParameters_v2", NozzlesParameters_dataGridView, DataTableType.Nozzles);
            if ((Setting.Nozzles_current != 0) && Setting.Nozzles_Enabled)
            {
                Nozzle.UseCalibration(Setting.Nozzles_current);
            }
        }

        // ==========================================================================================================
        private void ResizeNozzleTables()
        {
            int height = 2 * SystemInformation.BorderSize.Height;
            foreach (DataGridViewRow row in NozzlesLoad_dataGridView.Rows)
            {
                height += row.Height;
            }

            System.Drawing.Size size= NozzlesLoad_dataGridView.Size;
            size.Height = height + NozzlesLoad_dataGridView.ColumnHeadersHeight;
            NozzlesLoad_dataGridView.Size = size;

            size = NozzlesUnload_dataGridView.Size;
            size.Height = height + NozzlesLoad_dataGridView.ColumnHeadersHeight + SystemInformation.VerticalScrollBarWidth;
            NozzlesUnload_dataGridView.Size = size;

            size = NozzlesParameters_dataGridView.Size;
            size.Height = height + NozzlesParameters_dataGridView.ColumnHeadersHeight;
            NozzlesParameters_dataGridView.Size = size;
        }

        // ==========================================================================================================
        // tab page enter/leave

        private bool NozzletabStore_slowXY = false;
        private bool NozzletabStore_slowZ = false;
        private bool NozzletabStore_slowA = false;
        private double NozzletabStore_XYspeed = 500.0;
        private double NozzletabStore_Zspeed = 500.0;
        private double NozzletabStore_Aspeed = 500.0;
        private int NozzletabStore_timeout= 10;
        private bool NozzletabStore_slack = false;
        private bool NozzletabStore_slackA = false;

        private bool AtNozzlesTab = false;

        private void Nozzles_tabPage_Begin()
        {
            if (NozzleZGuard_checkBox.Checked)
            {
                Machine.Move.ZGuardOff();
            }
            else
            {
                Machine.Move.ZGuardOn();
            }
            // disable z switches, otherwise you can't do setup 
            Machine.Move.ZGuardOff();
            Machine.CommsProcessor.SendCommand("{\"zsn\":0}");
            Thread.Sleep(50);
            Machine.CommsProcessor.SendCommand("{\"zsx\":0}");
            Thread.Sleep(50);

            NozzleChangeEnable_checkBox.Checked = Setting.Nozzles_Enabled;
            NozzleXYspeed_textBox.Text = Setting.Nozzles_XYspeed.ToString();
            NozzleZspeed_textBox.Text = Setting.Nozzles_Zspeed.ToString();
            NozzleAspeed_textBox.Text = Setting.Nozzles_Aspeed.ToString();
            NozzleTimeout_textBox.Text = Setting.Nozzles_Timeout.ToString();
            NozzleXYFullSpeed_checkBox.Checked = Setting.Nozzles_XYfullSpeed;
            NozzleZFullSpeed_checkBox.Checked = Setting.Nozzles_ZfullSpeed;
            NozzleAFullSpeed_checkBox.Checked = Setting.Nozzles_AfullSpeed;
            FirstMoveFullSpeed_checkBox.Checked = Setting.Nozzles_FirstMoveFullSpeed;
            Nozzle1stMoveSlackComp_checkBox.Checked = Setting.Nozzles_FirstMoveSlackCompensation;
            LastMoveFullSpeed_checkBox.Checked = Setting.Nozzles_LastMoveFullSpeed;
            ForceNozzle_numericUpDown.Maximum = Setting.Nozzles_count;
            NozzleWarning_textBox.Text = Setting.Nozzles_WarningTreshold.ToString("0.00", CultureInfo.InvariantCulture);


            // For setup and testing, we want all operations (including jog and go button) to obey speed settings.
            // We don't want to disturb other operations, so we'll store the current state at page enter and restore at page leave.
            // store cnc speed settings
            NozzletabStore_slowXY = Machine.Move.SlowXY;
            NozzletabStore_slowZ = Machine.Move.SlowZ;
            NozzletabStore_slowA = Machine.Move.SlowA;
            NozzletabStore_XYspeed = Machine.Move.SlowSpeedXY;
            NozzletabStore_Zspeed = Machine.Move.SlowSpeedZ;
            NozzletabStore_Aspeed = Machine.Move.SlowSpeedA;
            NozzletabStore_timeout = Machine.Move.CNC_timeout;
            NozzletabStore_slack = Machine.Move.SlackCompensation;
            NozzletabStore_slackA = Machine.Move.SlackCompensationA;

            // replace with nozzle speed settings
            Machine.Move.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Machine.Move.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Machine.Move.SlowA = !Setting.Nozzles_AfullSpeed;
            Machine.Move.SlowSpeedXY = Setting.Nozzles_XYspeed;
            Machine.Move.SlowSpeedZ = Setting.Nozzles_Zspeed;
            Machine.Move.SlowSpeedA = Setting.Nozzles_Aspeed;
            Machine.Move.CNC_timeout = Setting.Nozzles_Timeout;
            Machine.Move.SlackCompensation = false;  // nozzle changes without slack compensation
            Machine.Move.SlackCompensationA = false;

            ResizeNozzleTables();
            AtNozzlesTab = true;
        }

        private void Nozzles_tabPage_End()
        {
            Machine.Move.ZGuardOn();
            // enable switches
            Machine.CommsProcessor.SendCommand("{\"zsn\":3}");
            Thread.Sleep(50);
            Machine.CommsProcessor.SendCommand("{\"zsx\":2}");
            Thread.Sleep(50);
            // restore settings
            Machine.Move.SlowXY = NozzletabStore_slowXY;
            Machine.Move.SlowZ = NozzletabStore_slowZ;
            Machine.Move.SlowA = NozzletabStore_slowA;
            Machine.Move.SlowSpeedXY = NozzletabStore_XYspeed;
            Machine.Move.SlowSpeedZ = NozzletabStore_Zspeed;
            Machine.Move.SlowSpeedA = NozzletabStore_Aspeed;
            Machine.Move.CNC_timeout = NozzletabStore_timeout;
            Machine.Move.SlackCompensation = NozzletabStore_slack;
            Machine.Move.SlackCompensationA = NozzletabStore_slackA;


            AtNozzlesTab = false;
        }

        // ==========================================================================================================
        private void AddNozzle(bool ResizeNeeded)
        {
            int RowNo = NozzlesLoad_dataGridView.Rows.Count;
            NozzlesLoad_dataGridView.Rows.Insert(RowNo);
            NozzlesUnload_dataGridView.Rows.Insert(RowNo);
            NozzlesParameters_dataGridView.Rows.Insert(RowNo);
            RowNo++;
            NozzlesLoad_dataGridView.Rows[RowNo-1].Cells[Nozzledata_NozzleNoColumn].Value = RowNo.ToString();
            NozzlesUnload_dataGridView.Rows[RowNo - 1].Cells[Nozzledata_NozzleNoColumn].Value = RowNo.ToString();
            NozzlesParameters_dataGridView.Rows[RowNo - 1].Cells[Nozzledata_NozzleNoColumn].Value = RowNo.ToString();
            if (ResizeNeeded)
            {
                ResizeNozzleTables();
            }
        }

        private void RemoveNozzle()
        {
            NozzlesLoad_dataGridView.Rows.RemoveAt(NozzlesLoad_dataGridView.Rows.Count-1);
            NozzlesUnload_dataGridView.Rows.RemoveAt(NozzlesUnload_dataGridView.Rows.Count - 1);
            NozzlesParameters_dataGridView.Rows.RemoveAt(NozzlesParameters_dataGridView.Rows.Count - 1);
            ResizeNozzleTables();
        }


        // ==========================================================================================================
        private bool NozzleDataCheck(DataGridView grid, int nozzle, int col, out double value)
        {
            value = 0.0;
            if (grid.RowCount==0)
            {
                return false;
            }
            if (grid.Rows[nozzle-1].Cells[col].Value == null)
            {
                return false;
            }
            if (double.TryParse(grid.Rows[nozzle - 1].Cells[col].Value.ToString().Replace(',', '.'), out value))
            {
                return true;
            }
            return false;
        }

        private bool m_GetNozzleStartCoordinates(DataGridView grid, int nozzle, out double X, out double Y, out double Z)
        {
            X = 0.0;
            Y = 0.0;
            Z = 0.0;
            string op = "move not set";
            if (grid == NozzlesLoad_dataGridView)
            {
                op = "load";
            }
            if (grid == NozzlesUnload_dataGridView)
            {
                op = "unload";
            }
            if (!NozzleDataCheck(grid, nozzle, Nozzledata_StartXColumn, out X))
            {
                DisplayText("Bad data, Start X, " + op + " nozzle #" + nozzle.ToString());
                return false;
            }
            if (!NozzleDataCheck(grid, nozzle, Nozzledata_StartYColumn, out Y))
            {
                DisplayText("Bad data, Start Y, " + op + " nozzle #" + nozzle.ToString());
                return false;
            }
            if (!NozzleDataCheck(grid, nozzle, Nozzledata_StartZColumn, out Z))
            {
                DisplayText("Bad data, Start Z, " + op + " nozzle #" + nozzle.ToString());
                return false;
            }
            return true;
        }
        
        private void GetCoordinates_button_Click(DataGridView grid)
        {
            // Use story: The user is setting up the nozles. Jog nozzle holder to start position,
            // click get, start position is automatically filled and the next step box is selected.
            // Jog the next step and click get, the axis and move size are automatically filled
            if (grid.RowCount==0)
            {
                return;
            }
            int row = grid.CurrentCell.RowIndex;
            int col = grid.CurrentCell.ColumnIndex;
            DisplayText("Current Cell: " + row.ToString() + ", " + col.ToString());

            if (col <= Nozzledata_StartZColumn)
            {
                grid.Rows[row].Cells[Nozzledata_StartXColumn].Value = Machine.Position.CurrentX.ToString("0.000", CultureInfo.InvariantCulture);
                grid.Rows[row].Cells[Nozzledata_StartYColumn].Value = Machine.Position.CurrentY.ToString("0.000", CultureInfo.InvariantCulture);
                grid.Rows[row].Cells[Nozzledata_StartZColumn].Value = Machine.Position.CurrentZ.ToString("0.000", CultureInfo.InvariantCulture);
                grid.CurrentCell = grid.Rows[row].Cells[Nozzledata_StartZColumn + 2];
            }
            else
            {
                int MoveNo = (col - Nozzledata_StartZColumn) / 2;
                DisplayText("move no " + MoveNo.ToString());
                // Get coordinates until the move
                // Start position
                double X; 
                double Y;
                double Z;
                if (!m_GetNozzleStartCoordinates(grid, row+1, out X, out Y, out Z))
                {
                    return;
                }
                // Follow the moves until before the current move
                for (int move = 1; move < MoveNo; move++)
                {
                    double amount;
                    int amountCol = 2 * move + Nozzledata_StartZColumn;
                    int dirCol = 2 * move + Nozzledata_StartZColumn-1;
                    if (!NozzleDataCheck(grid, row+1, amountCol, out amount))
                    {
                        DisplayText("Bad data, move " + move.ToString() + " amount", KnownColor.DarkRed);
                        return;
                    }
                    // direction
                    if (grid.Rows[row].Cells[dirCol].Value == null)
                    {
                        DisplayText("Direction not set at move " + MoveNo.ToString(), KnownColor.DarkRed);
                        return;
                    }
                    else if (grid.Rows[row].Cells[dirCol].Value.ToString() == "--")
                    {
                        DisplayText("Direction not set at move " + MoveNo.ToString(), KnownColor.DarkRed);
                        return;
                    }
                    else
                    {
                        if (grid.Rows[row].Cells[dirCol].Value.ToString() == "X")
                        {
                            X += amount;
                        }
                        else if (grid.Rows[row].Cells[dirCol].Value.ToString() == "Y")
                        {
                            Y += amount;
                        }
                        else
                        {
                            Z += amount;
                        }
                    }
                }
                DisplayText("Position until here: X= " + X.ToString() + ", Y= " + Y.ToString() + ", Z= " + Z.ToString(), KnownColor.Blue);
                // get current position
                // check that one but only one coordinate has changed
                int count = 0;
                if (Math.Abs(X - Machine.Position.CurrentX) > 0.01)
                {
                    DisplayText("X changed");
                    count++;
                }
                if (Math.Abs(Y - Machine.Position.CurrentY) > 0.01)
                {
                    DisplayText("Y changed");
                    count++;
                }
                if (Math.Abs(Z - Machine.Position.CurrentZ) > 0.01)
                {
                    DisplayText("Z changed");
                    count++;
                }
                if (count == 0)
                {
                    DisplayText("No moves, previous steps would take the machine here.", KnownColor.DarkRed);
                    return;
                }
                if (count != 1)
                {
                    DisplayText("More than one coordinate changed from where previous steps would take the machine.", KnownColor.DarkRed);
                    return;
                }
                // All ok, set direction and value
                int amCol = 2 * MoveNo + Nozzledata_StartZColumn;
                int dCol = 2 * MoveNo + Nozzledata_StartZColumn-1;
                int nextcol=2 * MoveNo + Nozzledata_StartZColumn + 2;
                if (nextcol<grid.ColumnCount)
                {
                    grid.CurrentCell = grid.Rows[row].Cells[2 * MoveNo + Nozzledata_StartZColumn + 2];
                }
                if (Math.Abs(X - Machine.Position.CurrentX) > 0.01)
                {
                    grid.Rows[row].Cells[dCol].Value = "X";
                    grid.Rows[row].Cells[amCol].Value = (Machine.Position.CurrentX - X).ToString();
                    return;
                }
                if (Math.Abs(Y - Machine.Position.CurrentY) > 0.01)
                {
                    grid.Rows[row].Cells[dCol].Value = "Y";
                    grid.Rows[row].Cells[amCol].Value = (Machine.Position.CurrentY - Y).ToString();
                    return;
                }
                grid.Rows[row].Cells[dCol].Value = "Z";
                grid.Rows[row].Cells[amCol].Value = (Machine.Position.CurrentZ - Z).ToString();
                return;
            }
        }

        private void GetLoadCoordinates_button_Click(object sender, EventArgs e)
        {
            GetCoordinates_button_Click(NozzlesLoad_dataGridView);
        }

        private void GetUnloadCoordinates_button_Click(object sender, EventArgs e)
        {
            GetCoordinates_button_Click(NozzlesUnload_dataGridView);
        }

        // ==========================================================================================================
        // load / unload nozzles, button handlers
        // ==========================================================================================================
        private void SetDefaultNozzle_button_Click(object sender, EventArgs e)
        {
            Setting.Nozzles_default = (int)ForceNozzle_numericUpDown.Value;
            DefaultNozzle_label.Text = Setting.Nozzles_default.ToString();
        }

        private void ForceNozzleStatus_button_Click(object sender, EventArgs e)
        {
            if (ForceNozzle_numericUpDown.Value==0)
            {
                NozzleNo_textBox.Text = "--";
            }
            else
            {
                NozzleNo_textBox.Text = ForceNozzle_numericUpDown.Value.ToString();
            }
            Setting.Nozzles_current = (int)ForceNozzle_numericUpDown.Value;
            if (Setting.Nozzles_current != 0)
            {
                Nozzle.UseCalibration(Setting.Nozzles_current);
            }
        }

        private async Task<bool> m_UnloadNozzleAsync(int Nozzle)
        {
            DisplayText("Unload nozzle #" + Nozzle.ToString(), KnownColor.Blue);
            if (await m_DoNozzleSequenceAsync(NozzlesUnload_dataGridView, Nozzle))
            {
                NozzleNo_textBox.Text = "--";
                Setting.Nozzles_current = 0;
                return true;
            }
            else
            {
                return false;
            }
        }

        private async Task<bool> m_LoadNozzleAsync(int NozzleNo)
        {
            DisplayText("Load nozzle #" + NozzleNo.ToString(), KnownColor.Blue);
            if (await m_DoNozzleSequenceAsync(NozzlesLoad_dataGridView, NozzleNo))
            {
                NozzleNo_textBox.Text = NozzleNo.ToString();
                Setting.Nozzles_current = NozzleNo;
                Nozzle.UseCalibration(NozzleNo);
                return true;
            }
            else
            {
                return false;
            }
        }

        private async void ChangeNozzle_button_Click(object sender, EventArgs e)
        {
            await ChangeNozzle_mAsync((int)ForceNozzle_numericUpDown.Value);
        }

        // ==========================================================================================================
        // Change nozzle: This routine does the magic
        // ==========================================================================================================
        public async Task<bool> ChangeNozzle_mAsync(int Nozzle)
        {
            double MinSize=0;
            double MaxSize=10;

            Nozzles_Stop = false;
            if (Nozzle == Setting.Nozzles_current)
            {
                DisplayText("Wanted nozzle (#" + Nozzle.ToString() + ") already loaded");
                return true;
            };
            /*
            if (Nozzle>0)
            {
                if (NozzlesParameters_dataGridView.Rows[Nozzle - 1].Cells[1].Value == null)
                {
                    ShowMessageBox(
                        "Bad data at Nozzles vision parameters table, nozzle " + Nozzle.ToString() + ", min. size",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                }

                if (NozzlesParameters_dataGridView.Rows[Nozzle - 1].Cells[2].Value == null)
                {
                    ShowMessageBox(
                        "Bad data at Nozzles vision parameters table, nozzle " + Nozzle.ToString() + ", max. size",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                }

                if (!double.TryParse(NozzlesParameters_dataGridView.Rows[Nozzle - 1].Cells[1].Value.ToString().Replace(',', '.'), out MinSize))
                {
                    ShowMessageBox(
                        "Bad data at Nozzles vision parameters table, nozzle " + Nozzle.ToString() + ", min. size",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                }
                if (!double.TryParse(NozzlesParameters_dataGridView.Rows[Nozzle - 1].Cells[2].Value.ToString().Replace(',', '.'), out MaxSize))
                {
                    ShowMessageBox(
                        "Bad data at Nozzles vision parameters table, nozzle " + Nozzle.ToString() + ", max. size",
                        "Bad data",
                        MessageBoxButtons.OK);
                    return false;
                }
            }
            */

            // store cnc speed settings
            bool slowXY = Machine.Move.SlowXY;
            bool slowZ = Machine.Move.SlowZ;
            bool slowA = Machine.Move.SlowA;
            double XYspeed = Machine.Move.SlowSpeedXY;
            double Zspeed = Machine.Move.SlowSpeedZ;
            double Aspeed = Machine.Move.SlowSpeedA;
            int timeout = Machine.Move.CNC_timeout;

            // replace with nozzle speed settings
            Machine.Move.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Machine.Move.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Machine.Move.SlowA = !Setting.Nozzles_AfullSpeed;
            Machine.Move.SlowSpeedXY = Setting.Nozzles_XYspeed;
            Machine.Move.SlowSpeedZ = Setting.Nozzles_Zspeed;
            Machine.Move.SlowSpeedA = Setting.Nozzles_Aspeed;
            Machine.Move.CNC_timeout = Setting.Nozzles_Timeout;

            bool ok = true;
            // disable z switches 
            Machine.Move.ZGuardOff();
            Machine.CommsProcessor.SendCommand("{\"zsn\":0}");
            Thread.Sleep(50);
            Machine.CommsProcessor.SendCommand("{\"zsx\":0}");
            Thread.Sleep(50);

            // Unload if needed
            int dbg = Setting.Nozzles_current;
            if (Setting.Nozzles_current != 0)
            {
                if (!await m_UnloadNozzleAsync(Setting.Nozzles_current))
                {
                    ShowMessageBox(
                        "Nozzle unload failed, check situation and log window.",
                        "Nozzle unload failed",
                        MessageBoxButtons.OK);
                    ok = false;
                }
            }
            // Load if needed
            if ((Nozzle != 0) && ok)
            {
                if (!await m_LoadNozzleAsync(Nozzle))
                {
                    ShowMessageBox(
                        "Nozzle load failed, check situation and log window.",
                        "Nozzle load failed",
                        MessageBoxButtons.OK);
                    ok = false;
                }
            }

            if (!AtNozzlesTab)
            {
                // enable switches
                Machine.Move.ZGuardOn();
                Machine.CommsProcessor.SendCommand("{\"zsn\":3}");
                Thread.Sleep(50);
                Machine.CommsProcessor.SendCommand("{\"zsx\":2}");
                Thread.Sleep(50);
            }

            // restore cnc speed settings
            Machine.Move.SlowXY = slowXY;
            Machine.Move.SlowZ = slowZ;
            Machine.Move.SlowA = slowA;
            Machine.Move.SlowSpeedXY = XYspeed;
            Machine.Move.SlowSpeedZ = Zspeed;
            Machine.Move.SlowSpeedA = Aspeed;
            Machine.Move.CNC_timeout = timeout;

            if (Nozzle > 0)
            {
                Machine.UpCamera.MinSize = MinSize;
                Machine.UpCamera.MaxSize = MaxSize;
            }
            return ok;
        }


        // ==========================================================================================================
        // actual moves
        private async void GotoZ0_button_Click(object sender, EventArgs e)
        {
            await Machine.Move.MoveZSafeAsync(0.0);
        }


        private async Task<bool> m_NozzleGotoStartAsync(DataGridView grid, int nozzle)
        {
            double X;
            double Y;
            double Z;

            if (!m_GetNozzleStartCoordinates(grid, nozzle, out X, out Y, out Z))
            {
                return false;
            }
            if (Setting.Nozzles_FirstMoveFullSpeed)
            {
                Machine.Move.SlowXY = false;
                Machine.Move.SlowZ = false;
                Machine.Move.SlowA = false;
            }
            await Machine.Move.MoveZSafeAsync(0.0);
            if (Setting.Nozzles_FirstMoveSlackCompensation)
            {
                await Machine.Move.MoveXYASafeAsync(X-1, Y-1, -5.0);
            }
            await Machine.Move.MoveXYASafeAsync(X, Y, 0.0);
            await Machine.Move.MoveZSafeAsync(Z);
            Machine.Move.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Machine.Move.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Machine.Move.SlowA = !Setting.Nozzles_AfullSpeed;

            return true;
        }

        private async Task<bool> m_DoNozzleSequenceAsync(DataGridView grid, int Nozzle)
        {
            bool slack = Machine.Move.SlackCompensation;  // this routine can be called outside nozzle tab, so we need to store slack values, ...
            bool slackA = Machine.Move.SlackCompensationA;
            Machine.Move.SlackCompensation = false;      // .. but we don't want slack compensation moves on nozzle load/unload
            Machine.Move.SlackCompensationA = false;

            if (Nozzles_Stop)
            {
                return false;
            }
            await m_NozzleGotoStartAsync(grid, Nozzle);

            int Move = 1;
            bool AllDone = false;
            while (!AllDone)
            {
                if (Nozzles_Stop)
                {
                    Machine.Move.SlackCompensation = slack;
                    Machine.Move.SlackCompensationA = slackA;
                    return false;
                }
                var result = await m_DoNozzleMoveAsync(grid, Nozzle, Move++);

                if (!result.Item1)
                {
                    Machine.Move.SlackCompensation = slack;
                    Machine.Move.SlackCompensationA = slackA;
                    return false;
                }
            }
            Machine.Move.SlackCompensation = slack;
            Machine.Move.SlackCompensationA = slackA;
            return true;
        }

        private async Task<Tuple<bool, bool>> m_DoNozzleMoveAsync(DataGridView grid, int nozzle, int MoveNumber)
        {
            bool AllDone = false;

            if (MoveNumber> NoOfNozzleMoves+1)
            {
                DisplayText("attempting move #" + MoveNumber.ToString(), KnownColor.DarkRed);
                AllDone = true;
                return new Tuple<bool, bool>(false, AllDone);
            }
            AllDone = false;
            if (grid.RowCount==0)
            {
                return new Tuple<bool, bool>(false, AllDone);
            }
            // is direction set? If not, all done.
            int DirCol = Nozzledata_StartZColumn + (MoveNumber-1) * 2+1;
            if (grid.Rows[nozzle - 1].Cells[DirCol].Value == null)
            {
                AllDone = true;
                return new Tuple<bool, bool>(true, AllDone);
            }
            if (grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString() == "--")
            {
                AllDone = true;
                return new Tuple<bool, bool>(true, AllDone);
            }

            bool LastMove = false;
            if (MoveNumber == NoOfNozzleMoves)
            {
                LastMove = true;
            }
            else if (grid.Rows[nozzle - 1].Cells[DirCol+2].Value == null)
            {
                LastMove = true;
            }
            else if(grid.Rows[nozzle - 1].Cells[DirCol + 2].Value.ToString() == "--")
            {
                LastMove = true;
            }

            double val;
            if (!NozzleDataCheck(grid, nozzle, DirCol + 1, out val))
            {
                string op = "Undefined ";
                if (grid == NozzlesLoad_dataGridView)
                {
                    op = "load ";
                }
                if (grid == NozzlesUnload_dataGridView)
                {
                    op = "unload ";
                }
                DisplayText("Bad data: " + op + "nozzle #" + nozzle + ", move " + MoveNumber.ToString(), KnownColor.DarkRed);
            }
            string axis=grid.Rows[nozzle - 1].Cells[DirCol].Value.ToString();

            if (LastMove && Setting.Nozzles_LastMoveFullSpeed)
            {
                Machine.Move.SlowXY = false;
                Machine.Move.SlowZ = false;
                Machine.Move.SlowA = false;
            }

            if (axis=="Z")
            {
                if (!await Machine.Move.MoveZSafeAsync(Machine.Position.CurrentZ + val))
                {
                    return new Tuple<bool, bool>(false, AllDone);
                }
            }
            else
            {
                double X = Machine.Position.CurrentX;
                double Y = Machine.Position.CurrentY;
                switch (axis)
                {
                    case "X":
                        X = X + val;
                        break;

                    case "Y":
                        Y = Y + val;
                        break;

                    default:
                        DisplayText("m_DoNozzleMove: nozzle #" + nozzle + ", move " + MoveNumber.ToString() + ", axis?", KnownColor.DarkRed);
                        Machine.Move.SlowXY = !Setting.Nozzles_XYfullSpeed;
                        Machine.Move.SlowZ = !Setting.Nozzles_ZfullSpeed;
                        Machine.Move.SlowA = !Setting.Nozzles_AfullSpeed;
                        return new Tuple<bool, bool>(false, AllDone);
                        //break;
                }
                if (!await Machine.Move.MoveXYSafeAsync(X, Y))
                {
                    return new Tuple<bool, bool>(false, AllDone);
                }
            }
            Machine.Move.SlowXY = !Setting.Nozzles_XYfullSpeed;
            Machine.Move.SlowZ = !Setting.Nozzles_ZfullSpeed;
            Machine.Move.SlowA = !Setting.Nozzles_AfullSpeed;
            return new Tuple<bool, bool>(true, AllDone);
        }


        private void NozzleZGuard_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if ( NozzleZGuard_checkBox.Checked)
            {
                Machine.Move.ZGuardOff();
            }
            else
            {
                Machine.Move.ZGuardOn();
            }
        }

        // ==========================================================================================================
        private void NozzleWarning_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleWarning_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_WarningTreshold = val;
                NozzleWarning_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleWarning_textBox.ForeColor = Color.Red;
            }
        }

        // ==========================================================================================================
        // Speed controls

        private void NozzleXYspeed_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleXYspeed_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_XYspeed = val;
                Machine.Move.SlowSpeedXY = val;
                NozzleXYspeed_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleXYspeed_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleZspeed_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleZspeed_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_Zspeed = val;
                Machine.Move.SlowSpeedZ = val;
                NozzleZspeed_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleZspeed_textBox.ForeColor = Color.Red;
            }
        }
        private void NozzleAspeed_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(NozzleAspeed_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_Aspeed = val;
                Machine.Move.SlowSpeedA = val;
                NozzleAspeed_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleAspeed_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleTimeout_textBox_TextChanged(object sender, EventArgs e)
        {
            int val;
            if (int.TryParse(NozzleTimeout_textBox.Text, out val))
            {
                Setting.Nozzles_Timeout = val;
                Machine.Move.CNC_timeout = val;
                NozzleTimeout_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleTimeout_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleXYFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_XYfullSpeed = NozzleXYFullSpeed_checkBox.Checked;
            Machine.Move.SlowXY = !NozzleXYFullSpeed_checkBox.Checked;
        }

        private void NozzleZFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_ZfullSpeed = NozzleZFullSpeed_checkBox.Checked;
            Machine.Move.SlowZ = !NozzleZFullSpeed_checkBox.Checked;
        }

        private void NozzleAFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_ZfullSpeed = NozzleAFullSpeed_checkBox.Checked;
            Machine.Move.SlowA = !NozzleAFullSpeed_checkBox.Checked;
        }

        private void Nozzle1stMoveSlackComp_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_FirstMoveSlackCompensation = Nozzle1stMoveSlackComp_checkBox.Checked;
        }

        private void FirstMoveFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_FirstMoveFullSpeed = FirstMoveFullSpeed_checkBox.Checked;
        }

        private void LastMoveFullSpeed_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Nozzles_LastMoveFullSpeed = LastMoveFullSpeed_checkBox.Checked;
        }

        private void NozzleChangeEnable_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (Setting.Nozzles_count == 0)
            {
                NozzleChangeEnable_checkBox.Checked = false;
            }
            Setting.Nozzles_Enabled = NozzleChangeEnable_checkBox.Checked;
        }

        private void NoOfNozzles_UpDown_ValueChanged(object sender, EventArgs e)
        {
            if (StartingUp)
            {
                return;
            }

            if (NoOfNozzles_UpDown.Value > Setting.Nozzles_maximum)
            {
                NoOfNozzles_UpDown.Value = Setting.Nozzles_maximum;
            }
            while (NoOfNozzles_UpDown.Value > NozzlesLoad_dataGridView.RowCount)
            {
                AddNozzle(true);
            }
            while (NoOfNozzles_UpDown.Value < NozzlesLoad_dataGridView.RowCount)
            {
                if (NozzlesLoad_dataGridView.RowCount > 0)
                {
                    RemoveNozzle();
                }
            }

            Setting.Nozzles_count = (int)NoOfNozzles_UpDown.Value;
            ForceNozzle_numericUpDown.Maximum = Setting.Nozzles_count;
            if (Setting.Nozzles_count == 0)
            {
                NozzleChangeEnable_checkBox.Checked = false;
            }
        }

        private async void CalibrateNozzles_button_Click(object sender, EventArgs e)
        {
            // this is only called from nozzle tab page, so we want to leave with slack compensation off
            // We want to do moves to camera with slack compensatoin, if he user has it on
            Machine.Move.SlackCompensation = Setting.CNC_SlackCompensation;  

            Nozzles_Stop = false;
            /*
            if (Setting.Placement_OmitNozzleCalibration)
            {
                if (Setting.Placement_OmitNozzleCalibration)
                {
                    DialogResult dialogResult = ShowMessageBox(
                        "Don't use Nozzle correction checked (at run job tab). Uncheck?",
                        "Nozzle calibration disabled", MessageBoxButtons.YesNo);
                    if (dialogResult == DialogResult.No)
                    {
                        Machine.Move.SlackCompensation = false;
                        return;
                    };
                    OmitNozzleCalibration_checkBox.Checked = false;
                    Setting.Placement_OmitNozzleCalibration = false;
                }
            }
            */
            for (int nozzle = 1; nozzle <= Setting.Nozzles_count; nozzle++)
            {
                if (!await ChangeNozzle_mAsync(nozzle))
                {
                    Machine.Move.SlackCompensation = false;
                    return;
                }
                if (Nozzles_Stop)
                {
                    Machine.Move.SlackCompensation = false;
                    return;
                }
                if (!await CalibrateNozzle_mAsync())
                {
                    Machine.Move.SlackCompensation = false;
                    return;
                }
                Nozzle.Store(nozzle);
            }
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);
            Nozzle.SaveCalibration(path + "LitePlacer.NozzlesCalibrationData");
            Nozzle.UseCalibration(Setting.Nozzles_count);
            for (int nozzle = 1; nozzle <= Setting.Nozzles_count; nozzle++)
            {
                CheckCalibrationErrors(nozzle);
            }
            Machine.Move.SlackCompensation = false;
        }

        private void NozzlesSave_button_Click(object sender, EventArgs e)
        {
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);
            SaveDataGrid(path + "LitePlacer.NozzlesLoadData_v2", NozzlesLoad_dataGridView);
            SaveDataGrid(path + "LitePlacer.NozzlesUnLoadData_v2", NozzlesUnload_dataGridView);
            SaveDataGrid(path + "LitePlacer.NozzlesParameters_v2", NozzlesParameters_dataGridView);
        }

        private bool Nozzles_Stop = false;
        private void NozzlesStop_button_Click(object sender, EventArgs e)
        {
            Nozzles_Stop = true;
        }

        private void NozzlesParameters_dataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
        }

        private void NozzleDistance_textBox_KeyUp(object sender, KeyEventArgs e)
        {
            double val;
            if (double.TryParse(NozzleDistance_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_CalibrationDistance = val;
                NozzleDistance_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleDistance_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleMinSize_textBox_KeyUp(object sender, KeyEventArgs e)
        {
            double val;
            if (double.TryParse(NozzleMinSize_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_CalibrationMinSize = val;
                NozzleMinSize_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleMinSize_textBox.ForeColor = Color.Red;
            }
        }

        private void NozzleMaxSize_textBox_KeyUp(object sender, KeyEventArgs e)
        {
            double val;
            if (double.TryParse(NozzleMaxSize_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Nozzles_CalibrationMaxSize = val;
                NozzleMaxSize_textBox.ForeColor = Color.Black;
            }
            else
            {
                NozzleMaxSize_textBox.ForeColor = Color.Red;
            }
        }

        private bool NozzleUseTable2()
        {
            if (!Setting.Nozzles_Enabled)
            {
                return false;
            }
            DataGridViewCheckBoxCell cell = NozzlesParameters_dataGridView.Rows[Setting.Nozzles_current-1].Cells["NozzleAlternative_column"] as DataGridViewCheckBoxCell;
            if (cell.Value==null)
            {
                return false;
            }
            return (cell.Value.ToString() == "True");
        }


        private void CalData_button_Click(object sender, EventArgs e)
        {
            DisplayText("Nozzles calibration data:");
            for (int i = 1; i <= 6; i++)
            {
                if (Nozzle.CalibratedArr[i])
                {
                    DisplayText("Nozzle " + i.ToString() + " is calibrated:");
                }
                else
                {
                    DisplayText("Nozzle " + i.ToString() + " is not calibrated:");
                }
                if ( Nozzle.CalibrationPointsArr[i]==null)
                {
                    DisplayText("No calibration data");
                }
                else
                {
                    foreach (NozzleClass.NozzlePoint p in Nozzle.CalibrationPointsArr[i])
                    {
                        DisplayText("A: " + p.Angle.ToString("0.000") + ", X: " + p.X.ToString("0.000") + ", Y: " + p.Y.ToString("0.000"));
                    }
                }
            }
            DisplayText("Currently used:");
            foreach (NozzleClass.NozzlePoint p in Nozzle.CalibrationPoints)
            {
                DisplayText("A: " + p.Angle.ToString("0.000") + ", X: " + p.X.ToString("0.000") + ", Y: " + p.Y.ToString("0.000"));
            }
        }

        private async void CalibrateThis_button_Click(object sender, EventArgs e)
        {
            await CalibrateNozzle_mAsync();
            CheckCalibrationErrors(Setting.Nozzles_current);
        }

        private void CheckCalibrationErrors(int nozzle)
        {
            double val;
            if (!double.TryParse(NozzleWarning_textBox.Text.Replace(',', '.'), out val))
            {
                DisplayText("Bad data in warning treshold");
                return;
            }
            foreach (NozzleClass.NozzlePoint p in Nozzle.CalibrationPoints)
            {
                if ((Math.Abs(p.X) > Math.Abs(val)) || (Math.Abs(p.Y) > Math.Abs(val)))
                {
                    DisplayText("WARNING: Calibration value over threshold: ");
                    DisplayText("Nozzle " + nozzle.ToString() + ", A: " + p.Angle.ToString("0.000") + ", X: " + p.X.ToString("0.000") + ", Y: " + p.Y.ToString("0.000"));
                }
            }
        }


        #endregion
        public bool DownCameraRotationFollowsA = false;
        private void apos_textBox_TextChanged(object sender, EventArgs e)
        {
            if (DownCameraRotationFollowsA)
            {
                Machine.DownCamera.BoxRotationDeg = Machine.Position.CurrentA;
            }
        }


        private void FiducialsTolerance_textBox_TextChanged(object sender, EventArgs e)
        {
            double val;
            if (double.TryParse(FiducialsTolerance_textBox.Text.Replace(',', '.'), out val))
            {
                Setting.Placement_FiducialTolerance = val;
            }
        }

        private void RoundFiducial_radioButton_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_FiducialsType = 0;
        }

        private void RectangularFiducial_radioButton_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_FiducialsType = 1;
        }

        private void AutoFiducial_radioButton_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_FiducialsType = 2;
        }

        private void FiducialManConfirmation_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.Placement_FiducialConfirmation = FiducialManConfirmation_checkBox.Checked;
        }

        private void AppSettingsSave_button_Click(object sender, EventArgs e)
        {
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);

            AppSettings_saveFileDialog.Filter = "All files (*.*)|*.*";
            AppSettings_saveFileDialog.FileName = "LitePlacer.Appsettings";
            AppSettings_saveFileDialog.InitialDirectory = path;

            if (AppSettings_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                SettingsOps.Save(Setting, AppSettings_saveFileDialog.FileName);
            }

        }

        private void AppSettingsLoad_button_Click(object sender, EventArgs e)
        {
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);

            AppSettings_openFileDialog.Filter = "All files (*.*)|*.*";
            AppSettings_openFileDialog.FileName = "LitePlacer.Appsettings";
            AppSettings_openFileDialog.InitialDirectory = path;

            if (AppSettings_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                Setting = SettingsOps.Load(AppSettings_openFileDialog.FileName);
                Application.Restart();
            }
        }

        private void AppBuiltInSettings_button_Click(object sender, EventArgs e)
        {
            DialogResult dialogResult = ShowMessageBox(
                "Reset application settings top built in defaults?",
                "Confirm Loading Built-In settings", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.No)
            {
                return;
            };
            Setting = new MySettings();
            Application.Restart();
        }

        private void BoardSettingsSave_button_Click(object sender, EventArgs e)
        {
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);

            AppSettings_saveFileDialog.Filter = "All files (*.*)|*.*";
            AppSettings_saveFileDialog.FileName = "LitePlacer.BoardSettings";
            AppSettings_saveFileDialog.InitialDirectory = path;

            if (AppSettings_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                BoardSettings.Save(TinyGBoard, qQuinticBoard, AppSettings_saveFileDialog.FileName);
            }
        }

        // ===================================================================================
        private void BoardSettingsLoad_button_Click(object sender, EventArgs e)
        {
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
            int i = path.LastIndexOf('\\');
            path = path.Remove(i + 1);

            AppSettings_openFileDialog.Filter = "All files (*.*)|*.*";
            AppSettings_openFileDialog.FileName = "LitePlacer.BoardSettings";
            AppSettings_openFileDialog.InitialDirectory = path;

            if (AppSettings_openFileDialog.ShowDialog() == DialogResult.OK)
            {
                if(!BoardSettings.Load(ref TinyGBoard, ref qQuinticBoard, AppSettings_openFileDialog.FileName))
                {
                    return;
                }
                WriteAllBoardSettings_m();
            }
        }

        private void BoardBuiltInSettings_button_Click(object sender, EventArgs e)
        {
            TinyGBoard = new BoardSettings.TinyG();
            qQuinticBoard = new BoardSettings.qQuintic();
            WriteAllBoardSettings_m();
        }

        private void WriteAllBoardSettings_m()
        {
            bool res = true;
            DialogResult dialogResult;
            //if (Cnc.Controlboard == ControlBoardType.TinyG)
            //{
                dialogResult = ShowMessageBox(
                   "Settings currently stored on board of your TinyG will be permanently lost,\n" +
                   "if you haven't stored a backup copy.\n" +
                   "Continue?",
                   "Overwrite current settings?", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No)
                {
                    return;
                }
                res = WriteTinyGSettings();
            /*}
            else
            {
                //sres = WriteqQuinticSettings();
            }*/
            if (!res)
            {
                DisplayText("Writing settings failed.");
                ShowMessageBox(
                    "Problem writing board settings. Board is in undefined state, fix the problem before continuing!",
                    "Settings not loaded",
                    MessageBoxButtons.OK);
            }
        }

        // =============
        private bool WriteSetting(string setting, string value, bool delay)
        {
            string dbg = "{\"" + setting + "\":" + value + "}";
            //string dbg = "$" + setting + "=" + value;
            DisplayText("write: " + dbg);
            try
            {
                Machine.CommsProcessor.SendCommand(dbg);
            }
            catch
            {
                return false;
            }

            if (delay)
            {
                Thread.Sleep(50);
            }
/*            dbg = "{sr:n}";
            DisplayText("write: " + dbg);
            if (!CNC_Write_m(dbg))
            {
                return false;
            };
            if (delay)
            {
                Thread.Sleep(50);
            }
*/
            return true;
        }


        private bool WriteTinyGSettings()
        {
            DisplayText("Writing settings to TinyG board.");
            if (!WriteSetting("st", TinyGBoard.st, true)) return false;
            if (!WriteSetting("mt", TinyGBoard.mt, true)) return false;
            if (!WriteSetting("jv", TinyGBoard.jv, true)) return false;
            if (!WriteSetting("js", TinyGBoard.js, true)) return false;
            if (!WriteSetting("tv", TinyGBoard.tv, true)) return false;
            if (!WriteSetting("qv", TinyGBoard.qv, true)) return false;
            if (!WriteSetting("sv", TinyGBoard.sv, true)) return false;
            if (!WriteSetting("si", TinyGBoard.si, true)) return false;
            if (!WriteSetting("gun", TinyGBoard.gun, true)) return false;
            if (!WriteSetting("1ma", TinyGBoard.motor1ma, true)) return false;
            if (!WriteSetting("1sa", TinyGBoard.motor1sa, true)) return false;
            if (!WriteSetting("1tr", TinyGBoard.motor1tr, true)) return false;
            if (!WriteSetting("1mi", TinyGBoard.motor1mi, true)) return false;
            if (!WriteSetting("1po", TinyGBoard.motor1po, true)) return false;
            if (!WriteSetting("1pm", TinyGBoard.motor1pm, true)) return false;
            if (!WriteSetting("2ma", TinyGBoard.motor2ma, true)) return false;
            if (!WriteSetting("2sa", TinyGBoard.motor2sa, true)) return false;
            if (!WriteSetting("2tr", TinyGBoard.motor2tr, true)) return false;
            if (!WriteSetting("2mi", TinyGBoard.motor2mi, true)) return false;
            if (!WriteSetting("2po", TinyGBoard.motor2po, true)) return false;
            if (!WriteSetting("2pm", TinyGBoard.motor2pm, true)) return false;
            if (!WriteSetting("3ma", TinyGBoard.motor3ma, true)) return false;
            if (!WriteSetting("3sa", TinyGBoard.motor3sa, true)) return false;
            if (!WriteSetting("3tr", TinyGBoard.motor3tr, true)) return false;
            if (!WriteSetting("3mi", TinyGBoard.motor3mi, true)) return false;
            if (!WriteSetting("3po", TinyGBoard.motor3po, true)) return false;
            if (!WriteSetting("3pm", TinyGBoard.motor3pm, true)) return false;
            if (!WriteSetting("4ma", TinyGBoard.motor4ma, true)) return false;
            if (!WriteSetting("4sa", TinyGBoard.motor4sa, true)) return false;
            if (!WriteSetting("4tr", TinyGBoard.motor4tr, true)) return false;
            if (!WriteSetting("4mi", TinyGBoard.motor4mi, true)) return false;
            if (!WriteSetting("4po", TinyGBoard.motor4po, true)) return false;
            if (!WriteSetting("4pm", TinyGBoard.motor4pm, true)) return false;
            if (!WriteSetting("xam", TinyGBoard.xam, true)) return false;
            if (!WriteSetting("xvm", TinyGBoard.xvm, true)) return false;
            if (!WriteSetting("xfr", TinyGBoard.xfr, true)) return false;
            if (!WriteSetting("xtn", TinyGBoard.xtn, true)) return false;
            if (!WriteSetting("xtm", TinyGBoard.xtm, true)) return false;
            if (!WriteSetting("xjm", TinyGBoard.xjm, true)) return false;
            if (!WriteSetting("xjh", TinyGBoard.xjh, true)) return false;
            if (!WriteSetting("xsv", TinyGBoard.xsv, true)) return false;
            if (!WriteSetting("xlv", TinyGBoard.xlv, true)) return false;
            if (!WriteSetting("xlb", TinyGBoard.xlb, true)) return false;
            if (!WriteSetting("xzb", TinyGBoard.xzb, true)) return false;
            if (!WriteSetting("yam", TinyGBoard.yam, true)) return false;
            if (!WriteSetting("yvm", TinyGBoard.yvm, true)) return false;
            if (!WriteSetting("yfr", TinyGBoard.yfr, true)) return false;
            if (!WriteSetting("ytn", TinyGBoard.ytn, true)) return false;
            if (!WriteSetting("ytm", TinyGBoard.ytm, true)) return false;
            if (!WriteSetting("yjm", TinyGBoard.yjm, true)) return false;
            if (!WriteSetting("yjh", TinyGBoard.yjh, true)) return false;
            if (!WriteSetting("ysv", TinyGBoard.ysv, true)) return false;
            if (!WriteSetting("ylv", TinyGBoard.ylv, true)) return false;
            if (!WriteSetting("ylb", TinyGBoard.ylb, true)) return false;
            if (!WriteSetting("yzb", TinyGBoard.yzb, true)) return false;
            if (!WriteSetting("zam", TinyGBoard.zam, true)) return false;
            if (!WriteSetting("zvm", TinyGBoard.zvm, true)) return false;
            if (!WriteSetting("zfr", TinyGBoard.zfr, true)) return false;
            if (!WriteSetting("ztn", TinyGBoard.ztn, true)) return false;
            if (!WriteSetting("ztm", TinyGBoard.ztm, true)) return false;
            if (!WriteSetting("zjm", TinyGBoard.zjm, true)) return false;
            if (!WriteSetting("zjh", TinyGBoard.zjh, true)) return false;
            if (!WriteSetting("zsv", TinyGBoard.zsv, true)) return false;
            if (!WriteSetting("zlv", TinyGBoard.zlv, true)) return false;
            if (!WriteSetting("zlb", TinyGBoard.zlb, true)) return false;
            if (!WriteSetting("zzb", TinyGBoard.zzb, true)) return false;
            if (!WriteSetting("aam", TinyGBoard.aam, true)) return false;
            if (!WriteSetting("avm", TinyGBoard.avm, true)) return false;
            if (!WriteSetting("afr", TinyGBoard.afr, true)) return false;
            if (!WriteSetting("atn", TinyGBoard.atn, true)) return false;
            if (!WriteSetting("atm", TinyGBoard.atm, true)) return false;
            if (!WriteSetting("ajm", TinyGBoard.ajm, true)) return false;
            if (!WriteSetting("ajh", TinyGBoard.ajh, true)) return false;
            if (!WriteSetting("asv", TinyGBoard.asv, true)) return false;
            if (!WriteSetting("ec", TinyGBoard.ec, true)) return false;
            if (!WriteSetting("ee", TinyGBoard.ee, true)) return false;
            if (!WriteSetting("ex", TinyGBoard.ex, true)) return false;
            if (!WriteSetting("xsn", TinyGBoard.xsn, true)) return false;
            if (!WriteSetting("xsx", TinyGBoard.xsx, true)) return false;
            if (!WriteSetting("ysn", TinyGBoard.ysn, true)) return false;
            if (!WriteSetting("ysx", TinyGBoard.ysx, true)) return false;
            if (!WriteSetting("zsn", TinyGBoard.zsn, true)) return false;
            if (!WriteSetting("zsx", TinyGBoard.zsx, true)) return false;
            if (!WriteSetting("asn", TinyGBoard.asn, true)) return false;
            if (!WriteSetting("asx", TinyGBoard.asx, true)) return false;
            return true;
        }


        private void ListResolutions_button_Click(object sender, EventArgs e)
        {
            List<string> Monikers;
            ComboBox Box;
            string MonikerStr;
            ICamera Cam;
            if (CamerasSetUp_tabControl.SelectedTab.Name== "DownCamera_tabPage")
            {
                Monikers = Machine.DownCamera.GetMonikerStrings();
                Box = DownCam_comboBox;
                Cam = Machine.DownCamera;
            }
            else if (CamerasSetUp_tabControl.SelectedTab.Name == "UpCamera_tabPage")
            {
                Monikers = Machine.UpCamera.GetMonikerStrings();
                Box = UpCam_comboBox;
                Cam = Machine.UpCamera;
            }
            else
            {
                ShowMessageBox(
                    "Bad tab name",
                    "Programmer error",
                    MessageBoxButtons.OK);
                return;
            }
            if (Monikers.Count == 0)
            {
                DisplayText("No camera");
                return;
            }
            if (Box.SelectedIndex> Monikers.Count)
            {
                DisplayText("Select camera");
                return;
            }
            MonikerStr = Monikers[Box.SelectedIndex];
            Cam.ListResolutions(MonikerStr);

        }

        private void DownCameraDesiredX_textBox_TextChanged(object sender, EventArgs e)
        {
            int res;
            if (int.TryParse(DownCameraDesiredX_textBox.Text, out res))
            {
                DownCameraDesiredX_textBox.ForeColor = Color.Black;
                Setting.DownCam_DesiredX = res;
                Machine.DownCamera.DesiredX = res;
            }
            else
            {
                DownCameraDesiredX_textBox.ForeColor = Color.Red;
            }
        }

        private void DownCameraDesiredY_textBox_TextChanged(object sender, EventArgs e)
        {
            int res;
            if (int.TryParse(DownCameraDesiredY_textBox.Text, out res))
            {
                DownCameraDesiredY_textBox.ForeColor = Color.Black;
                Setting.DownCam_DesiredY = res;
                Machine.DownCamera.DesiredY = res;
            }
            else
            {
                DownCameraDesiredY_textBox.ForeColor = Color.Red;
            }
        }

        private void UpCameraDesiredX_textBox_TextChanged(object sender, EventArgs e)
        {
            int res;
            if (int.TryParse(UpCameraDesiredX_textBox.Text, out res))
            {
                UpCameraDesiredX_textBox.ForeColor = Color.Black;
                Setting.UpCam_DesiredX = res;
                Machine.UpCamera.DesiredX = res;
            }
            else
            {
                UpCameraDesiredX_textBox.ForeColor = Color.Red;
            }
        }

        private void UpCameraDesiredY_textBox_TextChanged(object sender, EventArgs e)
        {
            int res;
            if (int.TryParse(UpCameraDesiredY_textBox.Text, out res))
            {
                UpCameraDesiredY_textBox.ForeColor = Color.Black;
                Setting.UpCam_DesiredY = res;
                Machine.UpCamera.DesiredY = res;
            }
            else
            {
                UpCameraDesiredY_textBox.ForeColor = Color.Red;
            }
        }

        private void CamShowPixels_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (CamShowPixels_checkBox.Checked)
            {
                Cam_pictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
            }
            else
            {
                Cam_pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            }
        }

        private void VigorousHoming_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            Setting.General_VigorousHoming = VigorousHoming_checkBox.Checked;
        }

        private void Ato0_button_Click(object sender, EventArgs e)
        {
            Machine.CommsProcessor.SendCommand("{\"gc\":\"G28.3 A0\"}");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Nozzle.Move_m(Machine.Position.CurrentX, Machine.Position.CurrentY, Machine.Position.CurrentA);
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            double xo = Setting.DownCam_NozzleOffsetX;
            double yo = Setting.DownCam_NozzleOffsetY;
            await Machine.Move.MoveXYSafeAsync(Machine.Position.CurrentX - xo, Machine.Position.CurrentY - yo);
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            await CameraToFirstComponent(0);
        }

        public void LoadCalibrationSettingsToUI()
        {
            topLeft_XPos_textBox.Text = Setting.Calibration_A_Marker_X.ToString();
            topLeft_YPos_textBox.Text = Setting.Calibration_A_Marker_Y.ToString();
            topRight_XPos_textBox.Text = Setting.Calibration_B_Marker_X.ToString();
            topRight_YPos_textBox.Text = Setting.Calibration_B_Marker_Y.ToString();
            bottomLeft_XPos_textBox.Text = Setting.Calibration_C_Marker_X.ToString();
            bottomLeft_YPos_textBox.Text = Setting.Calibration_C_Marker_Y.ToString();
            bottomRight_XPos_textBox.Text = Setting.Calibration_D_Marker_X.ToString();
            bottomRight_YPos_textBox.Text = Setting.Calibration_D_Marker_Y.ToString();
            centre_XPos_textBox.Text = Setting.Calibration_E_Marker_X.ToString();
            centre_YPos_textBox.Text = Setting.Calibration_E_Marker_Y.ToString();
            xQuantaStep_textBox.Text = Setting.Calibration_X_Quanta_Step_Size.ToString();
            yQuantaStep_textBox.Text = Setting.Calibration_Y_Quanta_Step_Size.ToString();
        }

        private void PositionSetupEnable_checkBox_CheckedChanged(object sender, EventArgs e)
        {
            topLeft_Set_button.Enabled = PositionSetupEnable_checkBox.Checked;
            topRight_Set_button.Enabled = PositionSetupEnable_checkBox.Checked;
            bottomLeft_Set_button.Enabled = PositionSetupEnable_checkBox.Checked;
            bottomRight_Set_button.Enabled = PositionSetupEnable_checkBox.Checked;
            centre_Set_button.Enabled = PositionSetupEnable_checkBox.Checked;
        }


        private void setQuantaSteps_button_Click(object sender, EventArgs e)
        {
            if (double.TryParse(xQuantaStep_textBox.Text, out double xQuantaStep))
            {
                Setting.Calibration_X_Quanta_Step_Size = xQuantaStep;
            }

            if (double.TryParse(yQuantaStep_textBox.Text, out double yQuantaStep))
            {
                Setting.Calibration_Y_Quanta_Step_Size = yQuantaStep;
            }
        }

        private void popCam_button_Click(object sender, EventArgs e)
        {
            OpenSecondaryDownCameraForm();
        }

        private void OpenSecondaryDownCameraForm()
        {
        }

        private void popMultiCam_button_Click(object sender, EventArgs e)
        {
            OpenSecondaryMultiDownCameraForm();
        }

        private void OpenSecondaryMultiDownCameraForm()
        {
        }
    }   // end of: 	public partial class FormMain : Form


    public static class RichTextBoxExtensions
    {
        public static void AppendText(this RichTextBox box, string text, Color color)
        {
            if (color != box.ForeColor)
            {
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = color;
                box.AppendText(text);
                box.SelectionColor = box.ForeColor;
            }
            else
            {
                box.AppendText(text);
            }
        }
    }

    // ===================================================================================
    // allows addition of color info to displayText 
    /*public static class RichTextBoxExtensions
    {
        public static void AppendText(this RichTextBox box, string text, Color color)
        {
            if (color != box.ForeColor)
            {
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = color;
                box.AppendText(text);
                box.SelectionColor = box.ForeColor;
            }
            else
            {
                box.AppendText(text);
            }
        }
    }*/

    // ===================================================================================
    // A message box that is centered on the main form.
    // From: http://www.jasoncarr.com/technology/centering-a-message-box-on-the-active-window-in-csharp

    internal static class CenteredMessageBox2
    {
        internal static void PrepToCenterMessageBoxOnForm(UserControl userControl)
        {
            MessageBoxCenterHelper helper = new MessageBoxCenterHelper();
            helper.Prep(userControl);
        }

        private class MessageBoxCenterHelper
        {
            private int messageHook;
            private IntPtr parentFormHandle;

            public void Prep(UserControl form)
            {
                NativeMethods.CenterMessageCallBackDelegate callBackDelegate = new NativeMethods.CenterMessageCallBackDelegate(CenterMessageCallBack);
                GCHandle.Alloc(callBackDelegate);

                parentFormHandle = form.Handle;
                messageHook = NativeMethods.SetWindowsHookEx(5, callBackDelegate, new IntPtr(NativeMethods.GetWindowLong(parentFormHandle, -6)), NativeMethods.GetCurrentThreadId()).ToInt32();
            }

            private int CenterMessageCallBack(int message, int wParam, int lParam)
            {
                NativeMethods.RECT formRect;
                NativeMethods.RECT messageBoxRect;
                int xPos;
                int yPos;

                if (message == 5)
                {
                    NativeMethods.GetWindowRect(parentFormHandle, out formRect);
                    NativeMethods.GetWindowRect(new IntPtr(wParam), out messageBoxRect);

                    xPos = (int)((formRect.Left + (formRect.Right - formRect.Left) / 2) - ((messageBoxRect.Right - messageBoxRect.Left) / 2));
                    yPos = (int)((formRect.Top + (formRect.Bottom - formRect.Top) / 2) - ((messageBoxRect.Bottom - messageBoxRect.Top) / 2));

                    NativeMethods.SetWindowPos(wParam, 0, xPos, yPos, 0, 0, 0x1 | 0x4 | 0x10);
                    NativeMethods.UnhookWindowsHookEx(messageHook);
                }

                return 0;
            }
        }

        private static class NativeMethods
        {
            internal struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            internal delegate int CenterMessageCallBackDelegate(int message, int wParam, int lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool UnhookWindowsHookEx(int hhk);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("kernel32.dll")]
            internal static extern int GetCurrentThreadId();

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetWindowsHookEx(int hook, CenterMessageCallBackDelegate callback, IntPtr hMod, int dwThreadId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetWindowPos(int hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        }
    }


    }	// end of: namespace LitePlacer