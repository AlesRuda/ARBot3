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

namespace UnitTests
{
    [TestClass]
    public class GridNavigationTest
    {
        [TestMethod]
        public void SpeedTest1()
        {
            var gn = new GridNavigation(100, 100, 0.1, 0.5, 0.5);
            gn.Start = new GraphState2D(gn) { X = 0, Y = 0 };

            Stopwatch sw = new Stopwatch();
            sw.Start();

            List<Point2D> obstacles = new List<Point2D>();
            for(double y = -5; y<4;y+=0.2)
            {
                obstacles.Add(new Point2D(-2, y));
                obstacles.Add(new Point2D(-2.4, y+0.1));
                obstacles.Add(new Point2D(2, y));
                obstacles.Add(new Point2D(2.4, y + 0.1));
            }

            gn.Obstacles = obstacles;
            var target = gn.Start.Clone();
            target.X = 4;
            target.Y = -2;
            var ret = gn.Process(target);
            sw.Stop();

        }
    }
}
