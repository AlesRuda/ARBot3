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
    public class Line2DTest
    {
        [TestMethod]
        public void Test1()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 0));

            Assert.AreEqual(0, l.A);
            Assert.AreEqual(10, l.B);
            Assert.AreEqual(0, l.Angle, 0.000001);
        }
        [TestMethod]
        public void Test2()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, 10));

            Assert.AreEqual(-10, l.A);
            Assert.AreEqual(0, l.B);
            Assert.AreEqual(Math.PI/2, l.Angle, 0.000001);
        }
        [TestMethod]
        public void Test3()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, -10));

            Assert.AreEqual(10, l.A);
            Assert.AreEqual(0, l.B);
            Assert.AreEqual(-Math.PI / 2, l.Angle, 0.000001);
        }
        [TestMethod]
        public void Test4()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(5, 10));

            Assert.AreEqual(-10, l.A);
            Assert.AreEqual(5, l.B);
            Assert.AreEqual(Conversions.Deg2Rad(64), l.Angle, 0.1);
        }

        [TestMethod]
        public void CircleIntersection1()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(10, 10));

            var p = l1.CircleIntersect(new Point2D(0, 0), 1);
            Assert.AreEqual(Math.Sqrt(2)/2, p[0].X);
            Assert.AreEqual(Math.Sqrt(2) / 2, p[0].Y);
        }

        [TestMethod]
        public void CircleIntersection2()
        {
            var l1 = new Line2D(new Point2D(0, 10), new Point2D(10, 0));

            var p = l1.CircleIntersect(new Point2D(0, 0), 10);

            Assert.AreEqual(2, p.Length);

            Assert.AreEqual(10, p[0].X);
            Assert.AreEqual(0, p[0].Y);

            Assert.AreEqual(0, p[1].X);
            Assert.AreEqual(10, p[1].Y);
        }

        [TestMethod]
        public void Intersection1()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var l2 = new Line2D(new Point2D(0, 0), new Point2D(10, 0));

            var p=l1.Intersection(l2);
            Assert.AreEqual(0, p.X);
            Assert.AreEqual(0, p.Y);
        }

        [TestMethod]
        public void Intersection2()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var l2 = new Line2D(new Point2D(2, 2), new Point2D(10, 2));

            var p = l1.Intersection(l2);
            Assert.AreEqual(0, p.X);
            Assert.AreEqual(2, p.Y);
        }

        [TestMethod]
        public void Intersection3()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var p1 = new Point2D(10, 0);
            var p = l.Intersection(p1);
            Assert.AreEqual(0, p.X);
            Assert.AreEqual(0, p.Y);
        }

        [TestMethod]
        public void Intersection4()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 10));
            var p1 = new Point2D(1, -1);
            var p = l.Intersection(p1);
            Assert.AreEqual(0, p.X);
            Assert.AreEqual(0, p.Y);
        }

        [TestMethod]
        public void Normal1()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 0));
            var n = l.Normal;
            Assert.AreEqual(0, n.X, 0.00001);
            Assert.AreEqual(10, n.Y, 0.00001);
        }

        [TestMethod]
        public void Normal2()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var n = l.Normal;
            Assert.AreEqual(-10, n.X, 0.00001);
            Assert.AreEqual(0, n.Y, 0.00001);
        }

        [TestMethod]
        public void Distance1()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 10));
            var p1 = new Point2D(1, -1);
            var d = l.Distance(p1);
            Assert.AreEqual(Math.Sqrt(2), d, 0.00001);

            l= new Line2D(new Point2D(0, 0), new Point2D(1, 1));
            d = l.Distance(p1);
            Assert.AreEqual(Math.Sqrt(2), d, 0.00001);

            l = new Line2D(1, -1, 0);
            d = l.Distance(p1);
            Assert.AreEqual(Math.Sqrt(2), d, 0.00001);

            l = new Line2D(10, -10, 0);
            d = l.Distance(p1);
            Assert.AreEqual(Math.Sqrt(2), d, 0.00001);
        }

        [TestMethod]
        public void Distance2()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(10, 10));
            var l2 = new Line2D(new Point2D(1, 0), new Point2D(11, 10));
            var d = l1.Distance(l2);
            Assert.AreEqual(Math.Sqrt(2)/2, d, 0.00001);
        }

        [TestMethod]
        public void Distance3()
        {
            var l = new Line2D(new Point2D(10, 0), new Point2D(10, 10));
            var p1 = new Point2D(0, 0);
            var d = l.Distance(p1);
            Assert.AreEqual(10, d, 0.00001);
        }

        [TestMethod]
        public void Distance4()
        {
            var l = new List<Point2D>() { new Point2D(-6.06, 0), new Point2D(-4.2, 0.9), new Point2D(-2.1, 1.6) }.LinearRegesion();
            var p1 = new Point2D(-6.2, 4.5);
            var d = l.Distance(p1);
            Assert.AreEqual(4.17, d, 0.1);
        }

        [TestMethod]
        public void Parallel1()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var l2 = l1.Parallel(2);
            Assert.AreEqual(l1.A, l2.A);
            Assert.AreEqual(l1.B, l2.B);
            Assert.AreEqual(2, l1.Distance(l2));
        }

    }
}
