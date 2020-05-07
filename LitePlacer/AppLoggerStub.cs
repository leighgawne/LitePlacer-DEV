using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terpsichore.Common;

namespace LitePlacer
{
    public class AppLoggerStub : IAppLogger
    {
        public void Debug(string text, params object[] args)
        {
        }

        public void Error(string text, params object[] args)
        {
        }

        public void Info(string text, params object[] args)
        {
        }
        
        public void Trace(string text, params object[] args)
        {
        }

        public void Warn(string text, params object[] args)
        { 
        }
    }
}
