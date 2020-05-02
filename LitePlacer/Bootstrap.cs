using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terpsichore.Common;

namespace LitePlacer
{
    static public class Bootstrap
    {
        static public void Initialise()
        {
            //DIBindings.CreateSingletonBinding<IMySettings, MySettings>();

            Terpsichore.Common.Bootstrap.Initialise();
            Terpsichore.Machine.Bootstrap.Initialise();
        }
    }
}
