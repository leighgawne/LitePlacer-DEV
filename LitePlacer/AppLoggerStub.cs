using NLog;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terpsichore.Common;

namespace LitePlacer
{
    public class AppLoggerStub : IAppLogger
    {

        public event Action<string, LogLevel> LogEvent;

        public List<Tuple<string, LogLevel>> CloneLogs()
        {
            throw new NotImplementedException();
        }

        public void Debug(string text, params object[] args)
        {
            LogEvent(text, LogLevel.Debug);
        }

        public void Error(string text, params object[] args)
        {
            //Program.MainForm.DisplayText(text, System.Drawing.KnownColor.Red);
        }

        public void Info(string text, params object[] args)
        {
            //Program.MainForm.DisplayText(text);
        }
        
        public void Trace(string text, params object[] args)
        {
        }

        public void Warn(string text, params object[] args)
        { 
        }
    }
}
