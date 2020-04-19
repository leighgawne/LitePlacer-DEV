﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LitePlacer.Model
{
    public enum E_RPCalibrationPositionStates : int
    {
        PositionA,
        PositionB,
        PositionC,
        PositionD,
        PositionE
    }

    public enum E_RPCalibrationActionStates : int
    {
        HomeToCommonPosition,
        HomeToNominalCalibrationPosition,
        MoveFunctionOfQuantaPosX,
        MoveFunctionOfQuantaPosY,
        MoveFunctionOfQuantaNegX,
        MoveFunctionOfQuantaNegY,
        MoveFunctionOfQuantaPosXPosY,
        MoveFunctionOfQuantaNegXNegY,
        MoveFunctionOfQuantaPosXNegY,
        MoveFunctionOfQuantaNegXPosY
    }

    public class CalibrationProfile
    {
        public E_RPCalibrationPositionStates RPCalibrationPosition { get; set; }

        public List<Action<ProfileExecutor>> RPCalibrationActions { get; set; }

        public Func<double> Nominal_Pos_X;
        public Func<double> Nominal_Pos_Y;
    }

    public class CalibrationProfiles
    {
        public static FormMain FormMain { get; set; }

        public CalibrationProfile PositionA = new CalibrationProfile()
        {
            RPCalibrationPosition = E_RPCalibrationPositionStates.PositionA,
            RPCalibrationActions = new List<Action<ProfileExecutor>>()
            {
                new Action<ProfileExecutor>(ProfileExecutor.HomeToCommonPosition),
                new Action<ProfileExecutor>(ProfileExecutor.HomeToNominalCalibrationPosition),
                new Action<ProfileExecutor>(ProfileExecutor.MoveFunctionOfQuantaNegY),
                new Action<ProfileExecutor>(ProfileExecutor.MoveFunctionOfQuantaPosX),
                new Action<ProfileExecutor>(ProfileExecutor.MoveFunctionOfQuantaPosXNegY),
            },
            Nominal_Pos_X = () => { return FormMain.Setting.Calibration_A_Marker_X; },
            Nominal_Pos_Y = () => { return FormMain.Setting.Calibration_A_Marker_Y; }
        };

        public static int QuantisationStepCountX
        {
            get
            {
                double deltaX = FormMain.Setting.Calibration_B_Marker_X - FormMain.Setting.Calibration_A_Marker_X;
                return (int)((deltaX - (deltaX % FormMain.Setting.Calibration_X_Quanta_Step_Size)) / FormMain.Setting.Calibration_X_Quanta_Step_Size);
            }
        }

        public static int QuantisationStepCountY
        {
            get
            {
                double deltaY = FormMain.Setting.Calibration_A_Marker_Y - FormMain.Setting.Calibration_C_Marker_Y;
                return (int)((deltaY - (deltaY % FormMain.Setting.Calibration_Y_Quanta_Step_Size)) / FormMain.Setting.Calibration_Y_Quanta_Step_Size);
            }
        }
    }

    public class ProfileExecutor
    {
        public static FormMain FormMain { get; set; }

        public CalibrationProfile ActiveProfile { get; set; }

        public void Execute()
        {
            for (int actionIndex = 0; actionIndex < ActiveProfile.RPCalibrationActions.Count; actionIndex++)
            {
                ActiveProfile.RPCalibrationActions[actionIndex](this);
            }
        }

        public static void HomeToCommonPosition(ProfileExecutor profileExecutor)
        {
            FormMain.CNC_XY_m(
                FormMain.Setting.Calibration_Common_X,
                FormMain.Setting.Calibration_Common_Y);
        }

        public static void HomeToNominalCalibrationPosition(ProfileExecutor profileExecutor)
        {
            FormMain.CNC_XY_m(
                profileExecutor.ActiveProfile.Nominal_Pos_X(),
                profileExecutor.ActiveProfile.Nominal_Pos_Y());
        }

        public static void MoveFunctionOfQuantaNegX(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, -1, 0);
        }

        public static void MoveFunctionOfQuantaPosX(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, 1, 0);
        }

        public static void MoveFunctionOfQuantaNegY(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, 0, -1);
        }

        public static void MoveFunctionOfQuantaPosY(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, 0, 1);
        }

        public static void MoveFunctionOfQuantaPosXPosY(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, 1, 1);
        }

        public static void MoveFunctionOfQuantaPosXNegY(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, 1, -1);
        }

        public static void MoveFunctionOfQuantaNegXPosY(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, -1, 1);
        }

        public static void MoveFunctionOfQuantaNegXNegY(ProfileExecutor profileExecutor)
        {
            MoveFunctionOfQuanta(profileExecutor, -1, -1);
        }

        public static void MoveFunctionOfQuanta(ProfileExecutor profileExecutor, int directionMultiplierX, int directionMultiplierY)
        {
            int quantaCurrentStepX = 1;
            int quantaCurrentStepY = 1;

            while ((quantaCurrentStepX <= CalibrationProfiles.QuantisationStepCountX) && (quantaCurrentStepY <= CalibrationProfiles.QuantisationStepCountY))
            {
                double newX = FormMain.Cnc.CurrentX + (FormMain.Setting.Calibration_X_Quanta_Step_Size * (Math.Min(CalibrationProfiles.QuantisationStepCountX, quantaCurrentStepX)) * directionMultiplierX);
                double newY = FormMain.Cnc.CurrentY + (FormMain.Setting.Calibration_Y_Quanta_Step_Size * (Math.Min(CalibrationProfiles.QuantisationStepCountY, quantaCurrentStepY)) * directionMultiplierY);

                ExecuteMove(
                    profileExecutor,
                    newX,
                    newY);

                quantaCurrentStepX++;
                quantaCurrentStepY++;
            }
        }

        public static void ExecuteMove(ProfileExecutor profileExecutor, double posX, double posY)
        {
            FormMain.CNC_XY_m(posX, posY);
            Thread.Sleep(400);
            HomeToNominalCalibrationPosition(profileExecutor);
            Thread.Sleep(400);
        }
    }
    
    public class RepeatabilityProfilier
    {
        private static FormMain formMain;

        public static FormMain FormMain
        {
            get
            {
                return formMain;
            }
            set
            {
                formMain = value;
                CalibrationProfiles.FormMain = value;
                ProfileExecutor.FormMain = value;
            }
        }

        public void ExecuteProfiling()
        {
            CalibrationProfiles calibrationProfiles = new CalibrationProfiles();
            ProfileExecutor profileExecutor = new ProfileExecutor();
            profileExecutor.ActiveProfile = calibrationProfiles.PositionA;
            profileExecutor.Execute();
        }
    }
}
