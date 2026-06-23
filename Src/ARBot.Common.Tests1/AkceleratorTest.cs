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
    public class AkceleratorTest
    {
        [TestMethod]
        public void BackProjectTest()
        {
            NativeComputeUnit a = new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability));
            BackProject bp = new BackProject(BackProject.RoadProbability);
            Random r = new Random();
            var img = new Image<BGR32>(100, 50);
            r.NextBytes(img.Data);

            var probA = new Image<Gray>(img.Width, img.Height);
            var probB = new Image<Gray>(img.Width, img.Height);

            bp.Process(img, probB);

            a.BackProject(probA, img, bp);

            for (int i = 0; i < probA.Data.Length; i++)
                Assert.AreEqual(probB.Data[i], probA.Data[i]);

        }
        [TestMethod]
        public void BackProjectSpeedTest()
        {
            NativeComputeUnit a = new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability));
            var img = new Image<BGR32>(640, 480);
            var prob = new Image<Gray>(img.Width, img.Height);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for(int i=0;i<100;i++)
            {
                a.Process(img, prob);
            }
            sw.Stop();
            var b = sw.Elapsed.TotalSeconds / 100;

        }
        [TestMethod]
        public void FindPathEdgeSpeedTest()
        {
            NativeComputeUnit a = new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability));
            var prob = new Image<Gray>(640, 480);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < 100; i++)
            {
                var ed=a.PathEdges(prob, 1, 1);
            }
            sw.Stop();
            var b = sw.Elapsed.TotalSeconds / 100;

        }
        [TestMethod]
        public void FindPathEdgeTest()
        {
            NativeComputeUnit a = new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability));
            var prob = new Image<Gray>(10, 10);
            prob.ForEach((x, y, p) =>
            {
                int w = prob.Width / 2;
                if (w > x)
                    p.Value = w - x > y ? (byte)0 : (byte)255;
                else
                    p.Value = x - w - 2 > y ? (byte)0 : (byte)255;
            });

            var ed = a.PathEdges(prob, 1, 1);

            Assert.AreEqual(4, ed.Count);

            Assert.AreEqual(0, ed[0].Y);
            Assert.AreEqual(5, ed[0].Left);
            Assert.AreEqual(8, ed[0].Right);

            Assert.AreEqual(1, ed[1].Y);
            Assert.AreEqual(4, ed[1].Left);
            Assert.AreEqual(9, ed[1].Right);

            Assert.AreEqual(2, ed[2].Y);
            Assert.AreEqual(3, ed[2].Left);
            Assert.AreEqual(null, ed[2].Right);

            Assert.AreEqual(3, ed[3].Y);
            Assert.AreEqual(2, ed[3].Left);
            Assert.AreEqual(null, ed[3].Right);
        }
        [TestMethod]
        public void CopyTest()
        {
            NativeComputeUnit a = new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability));
            a.Test();
        }
    }
}
