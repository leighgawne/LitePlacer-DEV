using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitePlacer.Calibration.Actions
{
    public class HomeToCommonPosition : CalibrationAction, ICalibrationAction
    {
        public async Task Execute()
        {
            await (Task.Run(() => CNC_XYZ_m(0.0, 0.0, 0.0)));
        }
    }
}
