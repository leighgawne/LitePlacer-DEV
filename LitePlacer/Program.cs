﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Terpsichore.Common;

namespace LitePlacer
{
    static class Program
    {
        //public static FormMain MainForm { get; set; }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Terpsichore.Common.DIBindings.CreateSingletonBinding<IAppLogger, AppLoggerStub>();
            Bootstrap.Initialise();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            //MainForm = new FormMain();
            //Application.Run(MainForm);
        }
    }
}
