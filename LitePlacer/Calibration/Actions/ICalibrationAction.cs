using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LitePlacer.Calibration.Actions
{
    public interface ICalibrationAction
    {
        Task Execute();
    }
}
