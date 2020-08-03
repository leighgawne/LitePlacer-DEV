using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Terpsichore.Machine.Sensors;
using Terpsichore.Machine.Interfaces;
using Terpsichore.Machine;
using Terpsichore.Common;

namespace LitePlacer
{
    class TapesClass
	{
        private DataGridView Grid;
        private NozzleClass Nozzle;
		private ICameraLegacy DownCamera;

        private IAppLogger appLogger = DIBindings.Resolve<IAppLogger>();

        private IMachine Machine { get; } = DIBindings.Resolve<IMachine>();


        public delegate void SetPaperTapeMeasurementHandler();
        public event SetPaperTapeMeasurementHandler SetPaperTapeMeasurementEvent;

        public delegate void SetBlackTapeMeasurementHandler();
        public event SetBlackTapeMeasurementHandler SetBlackTapeMeasurementEvent;

        public delegate void SetClearTapeMeasurementHandler();
        public event SetClearTapeMeasurementHandler SetClearTapeMeasurementEvent;

        public delegate bool UseCoordinatesDirectlyHandler(int TapeNum);
        public event UseCoordinatesDirectlyHandler UseCoordinatesDirectlyEvent;

        public TapesClass(DataGridView grd, NozzleClass ndl, ICameraLegacy cam)
		{
            Grid = grd;
			Nozzle = ndl;
			DownCamera = cam;
		}

		// ========================================================================================
		// ClearAll(): Resets TapeNumber positions and pickup/place Z's.
		public void ClearAll()
		{
			for (int tape = 0; tape < Grid.Rows.Count; tape++)
			{
				Grid.Rows[tape].Cells["NextPart_Column"].Value = "1";
				Grid.Rows[tape].Cells["Z_Pickup_Column"].Value = "--";
				Grid.Rows[tape].Cells["Z_Place_Column"].Value = "--";
				Grid.Rows[tape].Cells["Next_X_Column"].Value = Grid.Rows[tape].Cells["FirstX_Column"].Value;
				Grid.Rows[tape].Cells["Next_Y_Column"].Value = Grid.Rows[tape].Cells["FirstY_Column"].Value;
			}
		}

		// ========================================================================================
		// Reset(): Resets one tape position and pickup/place Z's.
		public void Reset(int tape)
		{
			Grid.Rows[tape].Cells["NextPart_Column"].Value = "1";
			Grid.Rows[tape].Cells["Z_Pickup_Column"].Value = "--";
			Grid.Rows[tape].Cells["Z_Place_Column"].Value = "--";
            // fix #22 reset next coordinates
            Grid.Rows[tape].Cells["Next_X_Column"].Value = Grid.Rows[tape].Cells["FirstX_Column"].Value;
			Grid.Rows[tape].Cells["Next_Y_Column"].Value = Grid.Rows[tape].Cells["FirstY_Column"].Value;
		}

		// ========================================================================================
		// IdValidates_m(): Checks that tape with description of "Id" exists.
		// TapeNumber is set to the corresponding row of the grid.
		public bool IdValidates_m(string Id, out int Tape)
		{
			Tape = -1;
			foreach (DataGridViewRow Row in Grid.Rows)
			{
				Tape++;
				if (Row.Cells["Id_Column"].Value.ToString() == Id)
				{
					return true;
				}
			}
            appLogger.Error("Did not find tape " + Id.ToString());
			return false;
		}

        // ========================================================================================
        // Fast placement:
        // ========================================================================================
        // The process measures last and first hole positions for a row in job data. It keeps track about
        // the hole location in next columns, and in this process, these are measured, not approximated.
        // The part numbers are found with the GetPartLocationFromHolePosition_m() routine.

        public bool FastParametersOk { get; set; }  // if we should use fast placement in the first place
        public double FastXstep { get; set; }       // steps for hole positions
        public double FastYstep { get; set; }
        public double FastXpos { get; set; }       // we don't want to mess with tape definitions
        public double FastYpos { get; set; }

        // ========================================================================================
        // PrepareForFastPlacement_m: Called before starting fast placement

        public async Task<bool> PrepareForFastPlacement_mAsync(string TapeID, int ComponentCount)
        {
            int TapeNum;
            if (!IdValidates_m(TapeID, out TapeNum))
            {
                FastParametersOk = false;
                return false;
            }
            if (UseCoordinatesDirectlyEvent(TapeNum))
            {
                return true;
            }


            int first;
            if (!int.TryParse(Grid.Rows[TapeNum].Cells["NextPart_Column"].Value.ToString(), out first))
            {
                appLogger.Error("Bad data at next column");
                FastParametersOk = false;
                return false;
            }
            // get pitch
            double pitch = 0;
            if (!double.TryParse(Grid.Rows[TapeNum].Cells["Pitch_Column"].Value.ToString().Replace(',', '.'), out pitch))
            {
                appLogger.Error("Bad data at Pitch column, tape ID: " + Grid.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }
            int last = first + ComponentCount - 1;
            //adjust for which part to measure - make sure that the one selected is not indexed
            //from the hole at the end of the cut tape strip, as that hole is often cut in half
            if (pitch < 6) //really 4 or less
            {
                --last;
            }
            if (pitch < 3) //really 2 or less
            {
                --last;
            }
            if (last < first)
            {
                last = first;
            }
            // measure holes
            double LastX = 0.0;
            double LastY = 0.0;
            double FirstX = 0.0;
            double FirstY = 0.0;

            var result = await GetPartHole_m(TapeNum, last);
            LastX = result.Item2;
            LastY = result.Item3;

            if (!result.Item1)
            {
                FastParametersOk = false;
                return false;
            }
            if (last != first)
            {
                var result2 = await GetPartHole_m(TapeNum, first);
                FirstX = result2.Item2;
                FirstY = result2.Item3;

                if (!result.Item1)
                {
                    FastParametersOk = false;
                    return false;
                }
            }
            else
            {
                FirstX = LastX;
                FirstY = LastY;
            }

            FastXpos = FirstX;
            FastYpos = FirstY;
            //test for a minimum of 2 complete holes - ie don't try to measure hold at the end of the tape, which is likely cut in half
            //measure and divide to calculate pitch for these
            if (last > first)
            {
                // if pitch == 2
                if ((pitch < 2.01) && (pitch > 1.99))
                {
                    int starthole = (first + 1) / 2;
                    int lasthole = (last + 1) / 2;
                    int HoleIncrements = lasthole - starthole;
                    if (HoleIncrements == 0)
                    {
                        FastXstep = 0.0;
                        FastYstep = 0.0;
                    }
                    else
                    {
                        FastXstep = (LastX - FirstX) / (double)HoleIncrements;
                        FastYstep = (LastY - FirstY) / (double)HoleIncrements;
                    }
                }
                else
                {
                    // normal case
                    FastXstep = (LastX - FirstX) / (double)(last - first);
                    FastYstep = (LastY - FirstY) / (double)(last - first);
                }
            }
            //if we had more than one component but could not measure multiple holes, just use the canned values for pitch
            else if (ComponentCount > 1)
            {
                switch (Grid.Rows[TapeNum].Cells["Orientation_Column"].Value.ToString())
                {
                    case "+Y":
                        FastXstep = 0;
                        FastYstep = pitch;
                        break;

                    case "+X":
                        FastXstep = pitch;
                        FastYstep = 0;
                        break;

                    case "-Y":
                        FastXstep = 0;
                        FastYstep = -pitch;
                        break;

                    case "-X":
                        FastXstep = -pitch;
                        FastYstep = 0;
                        break;

                    default:
                    appLogger.Error("Bad data at Tape #" + TapeNum.ToString());
                        return false;
                }
            }
            else
            {
                FastXstep = 0.0;
                FastYstep = 0.0;
            }

            appLogger.Info("Fast parameters:");
            appLogger.Info("First X: " + FirstX.ToString() + ", Y: " + FirstY.ToString());
            appLogger.Info("Last X: " + LastX.ToString() + ", Y: " + LastY.ToString());
            appLogger.Info("Step X: " + FastXstep.ToString() + ", Y: " + FastYstep.ToString());

            return true;
        }

        // ========================================================================================
        // IncrementTape_Fast(): Updates count and next hole locations for a tape
        // Like IncrementTape(), but just using the fast parametersheader description
        public bool IncrementTape_Fast_m(int TapeNum)
        {
            // get current part number
            int pos;
            if (!int.TryParse(Grid.Rows[TapeNum].Cells["NextPart_Column"].Value.ToString(), out pos))
            {
                appLogger.Error("Bad data at next column");
                return false;
            }

            // get pitch
            double pitch = 0;
            if (!double.TryParse(Grid.Rows[TapeNum].Cells["Pitch_Column"].Value.ToString().Replace(',', '.'), out pitch))
            {
                appLogger.Error("Bad data at Pitch column, tape ID: " + Grid.Rows[TapeNum].Cells["Id_Column"].Value.ToString());
                return false;
            }

            // Increment hole location, except if pitch is 2 and part number is odd
            if (!(
                ((pitch < 2.01) && (pitch > 1.99)) // pitch=2
                && 
                ((pos % 2) == 1)
                ))
            {
                FastXpos += FastXstep;
                FastYpos += FastYstep;
                Grid.Rows[TapeNum].Cells["Next_X_Column"].Value = FastXpos.ToString("0.000", CultureInfo.InvariantCulture);
                Grid.Rows[TapeNum].Cells["Next_Y_Column"].Value = FastYpos.ToString("0.000", CultureInfo.InvariantCulture);
            }
            pos += 1;
            Grid.Rows[TapeNum].Cells["NextPart_Column"].Value = pos.ToString();
            return true;

    }

        // ========================================================================================
        // GetTapeParameters_m(): 
        private bool GetTapeParameters_m(int Tape, out double OffsetX, out double OffsetY, out double Pitch)
        {
            OffsetX = 0.0; 
            OffsetY = 0.0;
            Pitch = 0.0;
            // Check for values
            if (!double.TryParse(Grid.Rows[Tape].Cells["OffsetX_Column"].Value.ToString().Replace(',', '.'), out OffsetX))
            {
                appLogger.Error("Bad data at Tape " + Grid.Rows[Tape].Cells["ID_Column"].Value.ToString() + ", OffsetX");
                return false;
            }
            if (!double.TryParse(Grid.Rows[Tape].Cells["OffsetY_Column"].Value.ToString().Replace(',', '.'), out OffsetY))
            {
                appLogger.Error("Bad data at Tape " + Grid.Rows[Tape].Cells["ID_Column"].Value.ToString() + ", OffsetY");
                return false;
            }
            if (!double.TryParse(Grid.Rows[Tape].Cells["Pitch_Column"].Value.ToString().Replace(',', '.'), out Pitch))
            {
                appLogger.Error("Bad data at Tape " + Grid.Rows[Tape].Cells["ID_Column"].Value.ToString() + ", Pitch");
                return false;
            }
            return true;
        }
		// ========================================================================================

        // ========================================================================================
        // GetPartHole_m(): Measures X,Y location of the hole corresponding to part number
        public async Task<Tuple<bool, double, double>> GetPartHole_m(int TapeNum, int PartNum)
        {
            double ResultX = 0.0;
            double ResultY = 0.0;

            // Get start points
            double X = 0.0;
            double Y = 0.0;
            if (!double.TryParse(Grid.Rows[TapeNum].Cells["FirstX_Column"].Value.ToString().Replace(',', '.'), out X))
            {
                appLogger.Error("Bad data at Tape " + TapeNum.ToString() + ", X");
                return new Tuple<bool, double, double>(false, ResultX, ResultY);
            }
            if (!double.TryParse(Grid.Rows[TapeNum].Cells["FirstY_Column"].Value.ToString().Replace(',', '.'), out Y))
            {
                appLogger.Error("Bad data at Tape " + TapeNum.ToString() + ", Y");
                return new Tuple<bool, double, double>(false, ResultX, ResultY);
            }

            // Get the hole location guess
            double dW;
            double Pitch;
            double FromHole;
            if (!GetTapeParameters_m(TapeNum, out dW, out FromHole, out Pitch))
            {
                return new Tuple<bool, double, double>(false, ResultX, ResultY);
            }
            if (Math.Abs(Pitch-2.0)<0.01) // if pitch ==2
            {
                PartNum = (PartNum +1)/ 2;
                Pitch = 4.0;
            }
            double dist = (double)(PartNum-1) * Pitch; // This many mm's from start
            switch (Grid.Rows[TapeNum].Cells["Orientation_Column"].Value.ToString())
            {
                case "+Y":
                    Y = Y + dist;
                    break;

                case "+X":
                    X = X + dist;
                    break;

                case "-Y":
                    Y = Y - dist;
                    break;

                case "-X":
                    X = X - dist;
                    break;

                default:
                appLogger.Error("Bad data at Tape #" + TapeNum.ToString() + ", Orientation");
                    return new Tuple<bool, double, double>(false, ResultX, ResultY);
            }
            // X, Y now hold the first guess

            // Measuring 
            if (!SetCurrentTapeMeasurement_m(TapeNum))  // having the measurement setup here helps with the automatic gain lag
            {
                return new Tuple<bool, double, double>(false, ResultX, ResultY);
            }
            // Go there:
            if (!await Machine.Move.MoveXYSafeAsync(X, Y))
            {
                return new Tuple<bool, double, double>(false, ResultX, ResultY);
            };

            // get hole exact location:
            var result = await Machine.Move.GoToFeatureLocation_mAsync(FeatureType.Circle, 1.8, 0.1);

            if (!result.Item1)
            {
                appLogger.Error("Can't find tape hole");
                return new Tuple<bool, double, double>(false, ResultX, ResultY);
            }
            ResultX = Machine.Position.DesiredX + X;
            ResultY = Machine.Position.DesiredY + Y;
            return new Tuple<bool, double, double>(true, ResultX, ResultY);
        }

        // ========================================================================================
        // GetPartLocationFromHolePosition_m(): Returns the location and rotation of the part
        // Input is the exact (measured) location of the hole

        public bool GetPartLocationFromHolePosition_m(int Tape, double X, double Y, out double PartX, out double PartY, out double A)
        {
            PartX = 0.0;
            PartY = 0.0;
            A = 0.0;

			double dW;	// Part center pos from hole, tape width direction. Varies.
            double dL=2.0;   // Part center pos from hole, tape lenght direction. -2mm on all standard tapes
			double Pitch;  // Distance from one part to another

            if (!GetTapeParameters_m(Tape, out dW, out dL, out Pitch))
	        {
		        return false;
	        }
            int pos;
			if (!int.TryParse(Grid.Rows[Tape].Cells["NextPart_Column"].Value.ToString(), out pos))
			{
                appLogger.Error("Bad data at Tape " + Tape.ToString() + ", Next");
				return false;
			}
            // if pitch == 2 and part# is even, DL=0
            if (Math.Abs(Pitch - 2) < 0.01)
            {
                if ((pos % 2) == 0)
                {
                    dL = 0.0;
                }
            }

			// TapeNumber orientation: 
			// +Y: Holeside of tape is right, part is dW(mm) to left, dL(mm) down from hole, A= 0
			// +X: Holeside of tape is down, part is dW(mm) up, dL(mm) to left from hole, A= -90
			// -Y: Holeside of tape is left, part is dW(mm) to right, dL(mm) up from hole, A= -180
			// -X: Holeside of tape is up, part is dW(mm) down, dL(mm) to right from hole, A=-270
			switch (Grid.Rows[Tape].Cells["Orientation_Column"].Value.ToString())
			{
				case "+Y":
					PartX = X - dW;
					PartY = Y - dL;
					A = 0.0;
					break;

				case "+X":
					PartX = X - dL;
					PartY = Y + dW;
					A = -90.0;
					break;

				case "-Y":
					PartX = X + dW;
					PartY = Y + dL;
					A = -180.0;
					break;

				case "-X":
					PartX = X + dL;
					PartY = Y - dW;
					A = -270.0;
					break;

				default:
                appLogger.Error("Bad data at Tape #" + Tape.ToString() + ", Orientation");
					return false;
			}
            appLogger.Info("Part position: " + Grid.Rows[Tape].Cells["Id_Column"].Value.ToString() + ", part #" + pos.ToString()
                + ": X= " + PartX.ToString() + ", Y= " + PartY.ToString());
			// rotation:
			if (Grid.Rows[Tape].Cells["Rotation_Column"].Value == null)
			{
                appLogger.Error("Bad data at tape " + Grid.Rows[Tape].Cells["Id_Column"].Value.ToString() +" rotation");
				return false;
			}
			switch (Grid.Rows[Tape].Cells["Rotation_Column"].Value.ToString())
			{
				case "0deg.":
					break;

				case "90deg.":
					A += 90.0;
					break;

				case "180deg.":
					A += 180.0;
					break;

				case "270deg.":
					A += 270.0;
					break;

				default:
					appLogger.Error("Bad data at Tape " + Grid.Rows[Tape].Cells["Id_Column"].Value.ToString() + " rotation");
					return false;
					// break;
			};
			while (A > 360.1)
			{
				A -= 360.0;
			}
			while (A < 0.0)
			{
				A += 360.0;
			};
            return true;
        }

        // ========================================================================================
        // IncrementTape(): Updates count and next hole locations for a tape
        // The caller knows the current hole location, so we don't need to re-measure them
        public bool IncrementTape(int Tape, double HoleX, double HoleY)
        {
            double dW;	// Part center pos from hole, tape width direction. Varies.
            double dL;   // Part center pos from hole, tape lenght direction. -2mm on all standard tapes
            double Pitch;  // Distance from one part to another
            if (!GetTapeParameters_m(Tape, out dW, out dL, out Pitch))
            {
                return false;
            }

            int pos;
            if (!int.TryParse(Grid.Rows[Tape].Cells["NextPart_Column"].Value.ToString(), out pos))
			{
                appLogger.Error("Bad data at Tape " + Grid.Rows[Tape].Cells["Id_Column"].Value.ToString() + ", next");
				return false;
			}

            // Set next hole approximate location. On 2mm part pitch, increment only at even part count.
            if (Math.Abs(Pitch - 2) < 0.000001)
            {
                if ((pos % 2) != 0)
                {
                    Pitch = 0.0;
                }
                else
                {
                    Pitch = 4.0;
                }
            };
            switch (Grid.Rows[Tape].Cells["Orientation_Column"].Value.ToString())
            {
                case "+Y":
                    HoleY = HoleY + (double)Pitch;
                    break;

                case "+X":
                    HoleX = HoleX + (double)Pitch;
                    break;

                case "-Y":
                    HoleY = HoleY - (double)Pitch;
                    break;

                case "-X":
                    HoleX = HoleX - (double)Pitch;
                    break;
            };
            Grid.Rows[Tape].Cells["Next_X_Column"].Value = HoleX.ToString();
            Grid.Rows[Tape].Cells["Next_Y_Column"].Value = HoleY.ToString();
            // increment next count
            pos++;
            Grid.Rows[Tape].Cells["NextPart_Column"].Value = pos.ToString();
            return true;
        }

        // ========================================================================================
        // UpdateNextCoordinates(): Updates next coordinates for a given tape based on new next coordinate number
        public bool UpdateNextCoordinates(int Tape, int NextNo)
        {
            double dW;	// Part center pos from hole, tape width direction. Varies.
            double dL;   // Part center pos from hole, tape lenght direction. -2mm on all standard tapes
            double Pitch;  // Distance from one part to another
            if (!GetTapeParameters_m(Tape, out dW, out dL, out Pitch))
            {
                return false;
            }

            int pos = NextNo - 1;

            double offset = pos * Pitch;

            // correct offset for 2mm part pitch
            if (Math.Abs(Pitch - 2) < 0.000001)
            {
                if ((pos % 2) != 0)
                {
                    offset -= Pitch;
                }
            }

            // determine first hole coordinates
            double Hole1X;
            double Hole1Y;

            NumberStyles style = NumberStyles.AllowDecimalPoint;
            CultureInfo culture = CultureInfo.InvariantCulture;
            if (Grid.Rows[Tape].Cells["FirstX_Column"].Value == null)
            {
                return false;
            }
            string s = Grid.Rows[Tape].Cells["FirstX_Column"].Value.ToString();           
            if (!double.TryParse(s, style, culture, out Hole1X))
            {
                return false;
            }
            if (Grid.Rows[Tape].Cells["FirstY_Column"].Value == null)
            {
                return false;
            }
            s = Grid.Rows[Tape].Cells["FirstY_Column"].Value.ToString();
            if (!double.TryParse(s, style, culture, out Hole1Y))
            {
                return false;
            }

            double NextX = Hole1X;
            double NextY = Hole1Y;

            // calculate next coordinates based on tape orientation, 1st hole position and offset from above
            switch (Grid.Rows[Tape].Cells["Orientation_Column"].Value.ToString())
            {
                case "+Y":
                    NextY = Hole1Y + offset;
                    break;

                case "+X":
                    NextX = Hole1X + offset;
                    break;

                case "-Y":
                    NextY = Hole1Y - offset;
                    break;

                case "-X":
                    NextX = Hole1X - offset;
                    break;
            };

            Grid.Rows[Tape].Cells["Next_X_Column"].Value = NextX.ToString("0.000", CultureInfo.InvariantCulture);
            Grid.Rows[Tape].Cells["Next_Y_Column"].Value = NextY.ToString("0.000", CultureInfo.InvariantCulture);

            return true;
        }

        // ========================================================================================
        // GotoNextPartByMeasurement_m(): Takes Nozzle to exact location of the part, tape and part rotation taken in to account.
        // The hole position is measured on each call using tape holes and knowledge about tape width and pitch (see EIA-481 standard).
        // Id tells the tape name. 
        // The caller needs the hole coordinates and tape number later in the process, but they are measured and returned here.
        public async Task<Tuple<bool, double, double>> GotoNextPartByMeasurement_m(int TapeNumber)
		{
            double HoleX = 0;
            double HoleY = 0;

			// Go to next hole approximate location:
			if (!SetCurrentTapeMeasurement_m(TapeNumber))  // having the measurement setup here helps with the automatic gain lag
				return new Tuple<bool, double, double>(false, HoleX, HoleY);

			double NextX= 0;
            double NextY = 0;
            if (!double.TryParse(Grid.Rows[TapeNumber].Cells["Next_X_Column"].Value.ToString().Replace(',', '.'), out NextX))
			{
                appLogger.Error("Bad data at Tape " + Grid.Rows[TapeNumber].Cells["Id_Column"].Value.ToString() + ", Next X");
                return new Tuple<bool, double, double>(false, HoleX, HoleY);
            }

            if (!double.TryParse(Grid.Rows[TapeNumber].Cells["Next_Y_Column"].Value.ToString().Replace(',', '.'), out NextY))
			{
                appLogger.Error("Bad data at Tape " + Grid.Rows[TapeNumber].Cells["Id_Column"].Value.ToString() + ", Next Y");
                return new Tuple<bool, double, double>(false, HoleX, HoleY);
            }
            // Go there:
            if (!await Machine.Move.MoveXYSafeAsync(NextX, NextY))
			{
                return new Tuple<bool, double, double>(false, HoleX, HoleY);
            };

            // Get hole exact location:
            // We want to find the hole less than 2mm from where we think it should be. (Otherwise there is a risk
            // of picking a wrong hole.)
            var result = await Machine.Move.GoToFeatureLocation_mAsync(FeatureType.Circle, 1.8, 0.5);

            if (!result.Item1)
			{
                appLogger.Error("Can't find tape hole");
                return new Tuple<bool, double, double>(false, HoleX, HoleY);
            }

            HoleX = result.Item2;
            HoleY = result.Item3;

            // The hole locations are:
            HoleX = Machine.Position.DesiredX + HoleX;
            HoleY = Machine.Position.DesiredY + HoleY;

			// ==================================================
			// find the part location and go there:
            double PartX = 0.0;
            double PartY = 0.0;
			double A= 0.0;

            if (!GetPartLocationFromHolePosition_m(TapeNumber, HoleX, HoleY, out PartX, out PartY, out A))
            {
                appLogger.Error("Can't find tape hole");
            }

			// Now, PartX, PartY, A tell the position of the part. Take Nozzle there:
			if (!Nozzle.Move_m(PartX, PartY, A))
			{
                return new Tuple<bool, double, double>(false, HoleX, HoleY);
            }

            return new Tuple<bool, double, double>(true, HoleX, HoleY);
        }   // end GotoNextPartByMeasurement_m


        // ========================================================================================
        // SetCurrentTapeMeasurement_m(): sets the camera measurement parameters according to the tape type.
        private bool SetCurrentTapeMeasurement_m(int row)
		{
			switch (Grid.Rows[row].Cells["Type_Column"].Value.ToString())
			{
				case "Paper (White)":
                    SetPaperTapeMeasurementEvent();
					Thread.Sleep(200);   // for automatic camera gain to have an effect
					return true;

				case "Black Plastic":
					SetBlackTapeMeasurementEvent();
					Thread.Sleep(200);   // for automatic camera gain to have an effect
					return true;

				case "Clear Plastic":
					SetClearTapeMeasurementEvent();
					Thread.Sleep(200);   // for automatic camera gain to have an effect
					return true;

				default:
                appLogger.Error("Bad Type data on row " + row.ToString() + ": " + Grid.Rows[row].Cells["Type_Column"].Value.ToString());
					return false;
			}
		}

	}
}
