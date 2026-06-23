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
using System.Windows.Media.Media3D;

namespace UnitTests
{
    [TestClass]
    public class ProjectionTest
    {
        [TestMethod]
        public void Test1()
        {
            var i = Intrinsics.TestDepth;

            var ii = Intrinsics.TestDepth.Inverse();

            var p = new CameraProjection(i, ii, Matrix3D.Identity, Matrix3D.Identity);
            p.SetOrientation(Profile.LeftCameraTransform*Conversions.WordToWordTransform(1.57, 0, 0, new System.Windows.Media.Media3D.Vector3D(0, 0, 0)));


            var pol = p.TargetPoly;
/*
            Assert.AreEqual(0, l.A);
            Assert.AreEqual(10, l.B);
            Assert.AreEqual(0, l.Angle, 0.000001);*/
        }

    }
}
