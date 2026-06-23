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
    public class LinearRegresionTest
    {
        [TestMethod]
        public void LRData1()
        {
            List<Point2D> l = new List<Point2D>();
            l.Add(new Point2D(0, 0));
            l.Add(new Point2D(1, 0));
            l.Add(new Point2D(2, 0));

            var r = l.LinearRegesion();
            Assert.AreEqual(RegresionMode.X, r.Mode);
            Assert.AreEqual(0, r.A);
            Assert.AreEqual(1, r.B);
            Assert.AreEqual(0, r.C);
            Assert.AreEqual(0, r.Angle, 0.000001);
        }

        [TestMethod]
        public void LRData2()
        {
            List<Point2D> l = new List<Point2D>();
            l.Add(new Point2D(0, 0));
            l.Add(new Point2D(0, 1));
            l.Add(new Point2D(0, 2));

            var r = l.LinearRegesion();
            Assert.AreEqual(RegresionMode.Y, r.Mode);
            Assert.AreEqual(-1, r.A);
            Assert.AreEqual(0, r.B);
            Assert.AreEqual(0, r.C);
            Assert.AreEqual(Math.PI / 2, r.Angle, 0.000001);
        }

        [TestMethod]
        public void LRData3()
        {
            List<Point2D> l = new List<Point2D>();
            l.Add(new Point2D(0, 0));
            l.Add(new Point2D(0.5, 1));
            l.Add(new Point2D(1, 2));

            var r = l.LinearRegesion();
            Assert.AreEqual(RegresionMode.Y, r.Mode);
            Assert.AreEqual(-1, r.A);
            Assert.AreEqual(0.5, r.B);
            Assert.AreEqual(0, r.C);
            Assert.AreEqual(Conversions.Deg2Rad(63), r.Angle, 0.1);
        }

        [TestMethod]
        public void LRData4()
        {
            List<Point2D> l = new List<Point2D>();
            l.Add(new Point2D(0, 0));
            l.Add(new Point2D(-0.5, 1));
            l.Add(new Point2D(-1, 2));

            var r = l.LinearRegesion();
            Assert.AreEqual(RegresionMode.Y, r.Mode);
            Assert.AreEqual(-1, r.A);
            Assert.AreEqual(-0.5, r.B);
            Assert.AreEqual(0, r.C);
            Assert.AreEqual(Conversions.Deg2Rad(116), r.Angle, 0.1);
        }
        [TestMethod]
        public void LRData5()
        {
            List<Point2D> l = new List<Point2D>();
            l.Add(new Point2D(0, 0));
            l.Add(new Point2D(1, 0.5));
            l.Add(new Point2D(2, 1));

            var r = l.LinearRegesion();
            Assert.AreEqual(RegresionMode.X, r.Mode);
            Assert.AreEqual(-0.5, r.A);
            Assert.AreEqual(1, r.B);
            Assert.AreEqual(0, r.C);
            Assert.AreEqual(Conversions.Deg2Rad(26), r.Angle, 0.1);
        }
    }
}
