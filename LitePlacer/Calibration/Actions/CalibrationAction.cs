using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitePlacer.Calibration.Actions
{
    public abstract class CalibrationAction
    {
        public static Func<double, double, double, bool> CNC_XYZ_m;
    }
}
