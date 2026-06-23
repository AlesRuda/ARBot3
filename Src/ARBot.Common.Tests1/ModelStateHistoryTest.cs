using System;
using System.Linq;
using AForge.Math;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.LocalMaps;
using ARBot.Common.Maps;
using ARBot.Common.Navigations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ARBot.Common.Logs;
using System.IO;
using System.Windows.Media.Imaging;
using ARBot2;
using HAL;
using System.Diagnostics;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.KDTree;
using System.Collections.Generic;
using ARBot.Common;
using ARBot.Common.Algorithms.ComputeUnit;
using HALWindows;
using ARBot.Common.Configuration;

namespace UnitTests
{
    [TestClass]
    public class ModelStateHistoryTest
    {
        [TestMethod]
        public void Speed()
        {
            var h = new ModelStateHistory(100);
            DateTime dt = new DateTime(2000, 1, 1);

            var ms = new ModelState(0.2);

            for(int i=0;i<100;i++)
            {
                var m = ms.Clone();
                m.TimeStamp = dt.AddSeconds(i);
                h.Add(m);
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            for(int i=0;i<100;i++)
            {
                var m=h[dt.AddSeconds(50.5)];
            }

            sw.Stop();
        }
    }
}
