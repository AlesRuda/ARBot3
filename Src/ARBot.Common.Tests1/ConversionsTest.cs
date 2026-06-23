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
using System.Windows.Media.Media3D;

namespace UnitTests
{
    [TestClass]
    public class ConversionsTest
    {
        [TestMethod]
        public void Rad2Deg()
        {
            Assert.AreEqual(0, Conversions.Rad2Deg(0));
            Assert.AreEqual(90, Conversions.Rad2Deg(Math.PI / 2));
            Assert.AreEqual(-90, Conversions.Rad2Deg(-Math.PI / 2));
        }
        [TestMethod]
        public void Deg2Rad()
        {
            Assert.AreEqual(0, Conversions.Deg2Rad(0));
            Assert.AreEqual(Math.PI / 2, Conversions.Deg2Rad(90));
            Assert.AreEqual(-Math.PI / 2, Conversions.Deg2Rad(-90));
        }
        [TestMethod]
        public void NormalizeOrientation()
        {
            Assert.AreEqual(0, Conversions.NormalizeOrientation(0));
            Assert.AreEqual(Math.PI / 2, Conversions.NormalizeOrientation(Math.PI / 2));
            Assert.AreEqual(0, Conversions.NormalizeOrientation(2 * Math.PI));
            Assert.AreEqual(Math.PI / 2, Conversions.NormalizeOrientation(2 * Math.PI + Math.PI / 2));
            Assert.AreEqual(-Math.PI / 2, Conversions.NormalizeOrientation(-Math.PI / 2));
            Assert.AreEqual(0, Conversions.NormalizeOrientation(-2 * Math.PI));
            Assert.AreEqual(-Math.PI / 2, Conversions.NormalizeOrientation(-2 * Math.PI - Math.PI / 2));
        }
        [TestMethod]
        public void NormalizeAzimut()
        {
            Assert.AreEqual(0, Conversions.NormalizeAzimut(0));
            Assert.AreEqual(180, Conversions.NormalizeAzimut(180));
            Assert.AreEqual(-179, Conversions.NormalizeAzimut(-179));
            Assert.AreEqual(180, Conversions.NormalizeAzimut(-180));
            Assert.AreEqual(-179, Conversions.NormalizeAzimut(181));
        }
        [TestMethod]
        public void NormalizeHalfOrientation()
        {
            Assert.AreEqual(0, Conversions.NormalizeHalfOrientation(0));
            Assert.AreEqual(0, Conversions.NormalizeHalfOrientation(Math.PI));
            Assert.AreEqual(Math.PI / 4, Conversions.NormalizeHalfOrientation(Math.PI / 4));
            Assert.AreEqual(-Math.PI / 4, Conversions.NormalizeHalfOrientation(-Math.PI / 4));
            Assert.AreEqual(Math.PI / 4, Conversions.NormalizeHalfOrientation(Math.PI + Math.PI / 4));
            Assert.AreEqual(-Math.PI / 4, Conversions.NormalizeHalfOrientation(Math.PI - Math.PI / 4));
        }
        [TestMethod]
        public void NormalizePrimaryOrientation()
        {
            Assert.AreEqual(0, Conversions.NormalizePrimaryOrientation(0, 0));
            Assert.AreEqual(0, Conversions.NormalizePrimaryOrientation(0, Math.PI));
            Assert.AreEqual(Math.PI / 10, Conversions.NormalizePrimaryOrientation(0, Math.PI / 10));
            Assert.AreEqual(Math.PI / 10, Conversions.NormalizePrimaryOrientation(0, Math.PI + Math.PI / 10));
            Assert.AreEqual(-Math.PI + Math.PI / 10, Conversions.NormalizePrimaryOrientation(Math.PI, Math.PI / 10));
            Assert.AreEqual(-Math.PI + Math.PI / 10, Conversions.NormalizePrimaryOrientation(Math.PI, Math.PI + Math.PI / 10));
            Assert.AreEqual(Math.PI / 10, Conversions.NormalizePrimaryOrientation(Math.PI / 4, Math.PI / 10));
            Assert.AreEqual(Math.PI / 10, Conversions.NormalizePrimaryOrientation(Math.PI / 4, Math.PI + Math.PI / 10));
        }
        [TestMethod]
        public void Azimut2Orientation()
        {
            Assert.AreEqual(Math.PI / 2, Conversions.Azimut2Orientation(0));
            Assert.AreEqual(0, Conversions.Azimut2Orientation(Math.PI / 2));
            Assert.AreEqual(Math.PI, Conversions.Azimut2Orientation(-Math.PI / 2));
        }
        [TestMethod]
        public void Orientation2Azimut()
        {
            Assert.AreEqual(Math.PI / 2, Conversions.Orientation2Azimut(0));
            Assert.AreEqual(0, Conversions.Orientation2Azimut(Math.PI / 2));
            Assert.AreEqual(Math.PI, Conversions.Orientation2Azimut(-Math.PI / 2));
        }

        void VectoToVectorTest(Vector3D f, Vector3D t, Func<Vector3D, Vector3D, Matrix3D> func)
        {
            f.Normalize();
            t.Normalize();
            var r = func(f, t);

            var x = r.Transform(f);
            Assert.AreEqual(t.X, x.X, 0.00001);
            Assert.AreEqual(t.Y, x.Y, 0.00001);
            Assert.AreEqual(t.Z, x.Z, 0.00001);
        }

        [TestMethod]
        public void VectoToVector()
        {
            var l = new (Vector3D f, Vector3D t)[]
            {
                (new Vector3D(1, 0, 0), new Vector3D(1, 0, 0)),
                (new Vector3D(0, 1, 0), new Vector3D(1, 0, 0)),
                (new Vector3D(0, 0, 1), new Vector3D(1, 0, 0)),
                (new Vector3D(1, 0, 0), new Vector3D(0, 1, 0)),
                (new Vector3D(0, 1, 0), new Vector3D(0, 1, 0)),
                (new Vector3D(0, 0, 1), new Vector3D(0, 1, 0)),
                (new Vector3D(1, 0, 0), new Vector3D(0, 0, 1)),
                (new Vector3D(0, 1, 0), new Vector3D(0, 0, 1)),
                (new Vector3D(0, 0, 1), new Vector3D(0, 0, 1)),
                (new Vector3D(1, 0, 0), new Vector3D(1, 1, 0)),
                (new Vector3D(0, 1, 0), new Vector3D(1, 1, 0)),
                (new Vector3D(0, 0, 1), new Vector3D(1, 1, 0)),
                (new Vector3D(1, 0, 0), new Vector3D(1, 1, 1)),
                (new Vector3D(0, 1, 0), new Vector3D(1, 1, 1)),
                (new Vector3D(0, 0, 1), new Vector3D(1, 1, 1)),
            };

            foreach ((Vector3D f, Vector3D t) in l)
                VectoToVectorTest(f, t, Conversions.VectoToVector);
            foreach ((Vector3D f, Vector3D t) in l)
                VectoToVectorTest(f, t, Conversions.VectoToVectorRodrigues);
        }

        void WordToWordTransform(YawPitchRoll ypr)
        {
            var t = Conversions.WordToWordTransform(ypr.Yaw, ypr.Pitch, ypr.Roll, new Vector3D(0, 0, 0));
            var ypr1 = new YawPitchRoll(t);

            Assert.AreEqual(ypr.Yaw, ypr1.Yaw, 0.00001);
            Assert.AreEqual(ypr.Pitch, ypr1.Pitch, 0.00001);
            Assert.AreEqual(ypr.Roll, ypr1.Roll, 0.00001);
        }

        [TestMethod]
        public void WordToWordTransform()
        {
            WordToWordTransform(new YawPitchRoll(1, 0, 0));
            WordToWordTransform(new YawPitchRoll(0, 1, 0));
            WordToWordTransform(new YawPitchRoll(0, 0, 1));

            WordToWordTransform(new YawPitchRoll(1, 1, 0));
            WordToWordTransform(new YawPitchRoll(0, 1, 1));
            WordToWordTransform(new YawPitchRoll(1, 0, 1));

            WordToWordTransform(new YawPitchRoll(1, 1, 1));
        }
    }
}
