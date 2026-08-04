using ARBot.Common.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common;
using System;

namespace UnitTests
{
    [TestClass]
    public class NativeComputeUnitTest
    {

        NativeComputeUnit Create()
        {
            return new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability)); 
        }
        public void CopyTest(int len)
        {
            byte[] src = new byte[len];
            byte[] dst = new byte[src.Length];

            for (int i = 0; i < src.Length; i++)
                src[i] = (byte)i;
            NativeComputeUnit.CopyByte(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
                Assert.AreEqual(src[i], dst[i], string.Format("{1}:src[{0}]", i, len));
        }
        [TestMethod]
        public void CopyTest()
        {
            CopyTest(10);
            CopyTest(32);
        }

        public void ReverseInt16Test(int len)
        {
            var src = new Int16[len];
            var dst = new Int16[src.Length];

            for (int i = 0; i < src.Length; i++)
                src[i] = (Int16)i;
            NativeComputeUnit.ReverseInt16(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
                Assert.AreEqual(src[i], dst[src.Length - i - 1], string.Format("src[{0}]", i));
        }
        [TestMethod]
        public void ReverseInt16Test()
        {
            ReverseInt16Test(10);
            ReverseInt16Test(32);
        }

        public void CopyRGB24ToRGB32Test(int len)
        {
            var src = new NativeComputeUnit.RGB[len];
            var dst = new NativeComputeUnit.BGR32[src.Length];

            for (int i = 0; i < src.Length; i++)
            {
                src[i].R = (byte)i;
                src[i].G = (byte)(i+len);
                src[i].B = (byte)(i+2*len);
            }
            NativeComputeUnit.CopyRGB24ToBGR32(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                Assert.AreEqual(src[i].R, dst[i].R, string.Format("{1}:src[{0}.R]", i, len));
                Assert.AreEqual(src[i].G, dst[i].G, string.Format("{1}:src[{0}.G]", i, len));
                Assert.AreEqual(src[i].B, dst[i].B, string.Format("{1}:src[{0}.B]", i, len));
            }
        }
        [TestMethod]
        public void CopyRGB24ToRGB32Test()
        {
            CopyRGB24ToRGB32Test(10);
            CopyRGB24ToRGB32Test(32);
        }

        public void ReverseRGB24ToBGR32Test(int len)
        {
            var src = new NativeComputeUnit.RGB[len];
            var dst = new NativeComputeUnit.BGR32[src.Length];

            for (int i = 0; i < src.Length; i++)
            {
                src[i].R = (byte)i;
                src[i].G = (byte)(i + len);
                src[i].B = (byte)(i + 2 * len);
            }
            NativeComputeUnit.ReverseRGB24ToBGR32(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                Assert.AreEqual(src[i].R, dst[len - 1 - i].R, string.Format("{1}:src[{0}.R]", i, len));
                Assert.AreEqual(src[i].G, dst[len - 1 - i].G, string.Format("{1}:src[{0}.G]", i, len));
                Assert.AreEqual(src[i].B, dst[len - 1 - i].B, string.Format("{1}:src[{0}.B]", i, len));
            }
        }
        [TestMethod]
        public void ReverseRGB24ToRGB32Test()
        {
            ReverseRGB24ToBGR32Test(10);
            ReverseRGB24ToBGR32Test(32);
        }





        void CmpMatrix3D(float[] transform, System.Windows.Media.Media3D.Matrix3D o)
        {
            Assert.AreEqual(o.M11, transform[0], "transform[0]");
            Assert.AreEqual(o.M12, transform[1], "transform[1]");
            Assert.AreEqual(o.M13, transform[2], "transform[2]");
            Assert.AreEqual(o.M14, transform[3], "transform[3]");
            Assert.AreEqual(o.M21, transform[4], "transform[4]");
            Assert.AreEqual(o.M22, transform[5], "transform[5]");
            Assert.AreEqual(o.M23, transform[6], "transform[6]");
            Assert.AreEqual(o.M24, transform[7], "transform[7]");
            Assert.AreEqual(o.M31, transform[8], "transform[8]");
            Assert.AreEqual(o.M32, transform[9], "transform[9]");
            Assert.AreEqual(o.M33, transform[10], "transform[10]");
            Assert.AreEqual(o.M34, transform[11], "transform[11]");
            Assert.AreEqual(o.OffsetX, transform[12], "transform[12]");
            Assert.AreEqual(o.OffsetY, transform[13], "transform[13]");
            Assert.AreEqual(o.OffsetZ, transform[14], "transform[14]");
            Assert.AreEqual(o.M44, transform[15], "transform[15]");
        }

        void AreEqual(Point4D expected, Point4D actual, string msg)
        {
            Assert.AreEqual(expected.X, actual.X, 0.0001, msg + ".X");
            Assert.AreEqual(expected.Y, actual.Y, 0.0001, msg + ".Y");
            Assert.AreEqual(expected.Z, actual.Z, 0.0001, msg + ".Z");
            Assert.AreEqual(expected.A, actual.A, 0.0001, msg + ".A");
        }

        [TestMethod]
        public void TransformationTest()
        {
            var m = System.Windows.Media.Media3D.Matrix3D.Identity;
            var t=NativeComputeUnit.Transformation(m);
            CmpMatrix3D(t, m);
        }
        [TestMethod]
        public void TransformPoint4DImplTest()
        {
            /*
            float f = 1;
            f += 1;
            NativeComputeUnit.Test2();
            */
            Point4D[] dst = new Point4D[3];
            Point4D[] src = new Point4D[3];

            Point4D x = new Point4D() { X = 1, Y = 0, Z = 0, A = 1 };
            Point4D y = new Point4D() { X = 0, Y = 1, Z = 0, A = 1 };
            Point4D z = new Point4D() { X = 0, Y = 0, Z = 1, A = 1 };

            var m = System.Windows.Media.Media3D.Matrix3D.Identity;
            var t = NativeComputeUnit.Transformation(m);

            src[0] = x;
            src[1] = y;
            src[2] = z;

            NativeComputeUnit.TransformPoint4DImpl(dst, t, src, 3);

            AreEqual(dst[0], x, "Identity.X.");
            AreEqual(dst[1], y, "Identity.Y.");
            AreEqual(dst[2], z, "Identity.Z.");
        }

        [TestMethod]
        public void Depth2XYZImplTest()
        {
            Point4D[] dst = new Point4D[5];
            short[] dist = new short[5];
            Point2D[] transform = new Point2D[5];

            transform[0] = new Point2D(-1, -2);
            transform[1] = new Point2D(-1, -3);
            transform[2] = new Point2D(-1, -4);
            transform[3] = new Point2D(1, 5);
            transform[4] = new Point2D(1, 6);

            dist[0] = 0;
            dist[1] = -1;
            dist[2] = 1000;
            dist[3] = 2000;
            dist[4] = 3000;

            int cnt=NativeComputeUnit.Depth2XYZImpl(dst, dist, transform, 5);

            Assert.AreEqual(3, cnt, "Count");

            AreEqual(new Point4D() { X = 3, Y = 18, Z = 3, A = 1 }, dst[0], "dst[0]");
            AreEqual(new Point4D() { X = 2, Y = 10, Z = 2, A = 1 }, dst[1], "dst[1]");
            AreEqual(new Point4D() { X = -1, Y = -4, Z = 1, A = 1 }, dst[2], "dst[2]");
        }

        [TestMethod]
        public void DepthTransformImplTest()
        {
            Point4D[] dst = new Point4D[5];
            short[] dist = new short[5];

            var m = System.Windows.Media.Media3D.Matrix3D.Identity;
            var r = NativeComputeUnit.Transformation(m);

            Point2D[] transform = new Point2D[5];

            transform[0] = new Point2D(-1, -2);
            transform[1] = new Point2D(-1, -3);
            transform[2] = new Point2D(-1, -4);
            transform[3] = new Point2D(1, 5);
            transform[4] = new Point2D(1, 6);

            dist[0] = 0;
            dist[1] = -1;
            dist[2] = 1000;
            dist[3] = 2000;
            dist[4] = 3000;

            int cnt = NativeComputeUnit.DepthTransformImpl(dst, transform, r, dist, 5);

            Assert.AreEqual(3, cnt, "Count");

            AreEqual(new Point4D() { X = 3, Y = 18, Z = 3, A = 1 }, dst[0], "dst[0]");
            AreEqual(new Point4D() { X = 2, Y = 10, Z = 2, A = 1 }, dst[1], "dst[1]");
            AreEqual(new Point4D() { X = -1, Y = -4, Z = 1, A = 1 }, dst[2], "dst[2]");
        }

        [TestMethod]
        public void ResetPlaneParamsTest()
        {
            PlaneParams modelParams=new PlaneParams();
            modelParams.Count1 = 1;
            modelParams.Count2 = 2;
            modelParams.Count3 = 3;
            modelParams.SumX = 4;
            modelParams.SumXX = 5;
            modelParams.SumXY = 6;
            modelParams.SumY = 7;
            modelParams.SumYY = 8;
            modelParams.SumYZ = 9;
            modelParams.SumZ = 10;
            modelParams.SumZX = 11;
            modelParams.SumZZ = 12;

            NativeComputeUnit.ResetPlaneParams(ref modelParams);

            Assert.AreEqual(0, modelParams.Count1, "Count1");
            Assert.AreEqual(0, modelParams.Count2, "Count2");
            Assert.AreEqual(0, modelParams.Count3, "Count3");

            Assert.AreEqual(0, modelParams.SumX, "SumX");
            Assert.AreEqual(0, modelParams.SumXX, "SumXX");
            Assert.AreEqual(0, modelParams.SumXY, "SumXY");

            Assert.AreEqual(0, modelParams.SumY, "SumY");
            Assert.AreEqual(0, modelParams.SumYY, "SumYY");
            Assert.AreEqual(0, modelParams.SumYZ, "SumYZ");

            Assert.AreEqual(0, modelParams.SumZ, "SumZ");
            Assert.AreEqual(0, modelParams.SumZX, "SumZX");
            Assert.AreEqual(0, modelParams.SumZZ, "SumZZ");

        }

        [TestMethod]
        public void CalcPlaneParamsTest()
        {
            PlaneParams modelParams = new PlaneParams();
            modelParams.Count1 = 1;
            modelParams.Count2 = 2;
            modelParams.Count3 = 3;
            modelParams.SumX = 4;
            modelParams.SumXX = 5;
            modelParams.SumXY = 6;
            modelParams.SumY = 7;
            modelParams.SumYY = 8;
            modelParams.SumYZ = 9;
            modelParams.SumZ = 10;
            modelParams.SumZX = 11;
            modelParams.SumZZ = 12;

            NativeComputeUnit.CalcPlaneParams(ref modelParams);

            Assert.AreEqual(1.5454545, modelParams.v.A, 0.000001, "v.A");
            Assert.AreEqual(-4.63636351, modelParams.v.X, 0.000001, "v.X");
            Assert.AreEqual(1, modelParams.v.Y, "v.Y");
            Assert.AreEqual(1, modelParams.v.Z, "v.Z");

        }

        [TestMethod]
        public void XYZ2PlaneImplTest()
        {
            PlaneParams modelParams = new PlaneParams();
            NativeComputeUnit.ResetPlaneParams(ref modelParams);

            Point4D[] points = new Point4D[5];

            points[0].X = 1;
            points[0].Y = 0;
            points[0].Z = 0;
            points[0].A = 1;
            points[1].X = 2;
            points[1].Y = 0;
            points[1].Z = 0;
            points[1].A = 1;
            points[2].X = 0;
            points[2].Y = 10;
            points[2].Z = -1;
            points[2].A = 1;
            points[3].X = 10;
            points[3].Y = 10;
            points[3].Z = 1;
            points[3].A = 1;
            points[4].X = 10;
            points[4].Y = 10;
            points[4].Z = 1000;
            points[4].A = 1;


            NativeComputeUnit.XYZ2PlaneImpl(ref modelParams, points, 2, 5);

            Assert.AreEqual(13, modelParams.SumX, "SumX");
            Assert.AreEqual(20, modelParams.SumY, "SumY");
            Assert.AreEqual(0, modelParams.SumZ, "SumZ");

            Assert.AreEqual(4, modelParams.Count1, "Count1");

            Assert.AreEqual(100, modelParams.SumXY, "SumXY");
            Assert.AreEqual(0, modelParams.SumYZ, "SumYZ");
            Assert.AreEqual(10, modelParams.SumZX, "SumZX");

            Assert.AreEqual(105, modelParams.SumXX, "SumXX");
            Assert.AreEqual(200, modelParams.SumYY, "SumYY");
            Assert.AreEqual(2, modelParams.SumZZ, "SumZZ");

        }
        [TestMethod]
        public void ClearAggregateImplTest()
        {
            AggregateItem[] ais = new AggregateItem[5];
            ais[0].Count = 1;
            ais[1].Count = 4;
            ais[2].Count = 2;
            ais[3].Count = 3;
            ais[4].Count = 5;

            Int32[] uais = new Int32[5];
            uais[0] = 3*32;
            uais[1] = 0*32;
            uais[2] = 2*32;

            NativeComputeUnit.ClearAggregateImpl(ais, uais, 3);

            Assert.AreEqual(0, ais[0].Count, "ais[0].Count");
            Assert.AreEqual(4, ais[1].Count, "ais[1].Count");
            Assert.AreEqual(0, ais[2].Count, "ais[2].Count");
            Assert.AreEqual(0, ais[3].Count, "ais[3].Count");
            Assert.AreEqual(5, ais[4].Count, "ais[4].Count");

        }
        [TestMethod]
        public void AggregateObstaclesImplTest()
        {
            AggregateItem[] ais = new AggregateItem[15];
            Int32[] uais = new Int32[15];
            Point4D v = new Point4D() { X = 0, Y = 0, Z = 1, A = 0 };
            Point4D[] points = new Point4D[7];

            points[0].X = 4;
            points[0].Y = 0;
            points[0].Z = 1;
            points[0].A = 1;

            points[1].X = 20;
            points[1].Y = 0;
            points[1].Z = 1;
            points[1].A = 1;

            points[2].X = -20;
            points[2].Y = 0;
            points[2].Z = 1;
            points[2].A = 1;

            points[3].X = 0;
            points[3].Y = 2;
            points[3].Z = 1;
            points[3].A = 1;

            points[4].X = 0;
            points[4].Y = 1.9f;
            points[4].Z = 2;
            points[4].A = 1;

            points[5].X = 0;
            points[5].Y = 20;
            points[5].Z = 1;
            points[5].A = 1;

            points[6].X = 0;
            points[6].Y = -20;
            points[6].Z = 1;
            points[6].A = 1;

            var cnt=NativeComputeUnit.AggregateObstaclesImpl(points, 7, 2, 2, 1, ais, uais, 5, 3, v);

            Assert.AreEqual(2, cnt, "cnt");
            Assert.AreEqual(0x180, uais[0], "uais[0]");
            Assert.AreEqual(0x120, uais[1], "uais[0]");

            int i1 = uais[0] / 32;
            int i2 = uais[1] / 32;

            Assert.AreEqual(2, ais[i1].Count, "ais[i1].Count");
            Assert.AreEqual(0, ais[i1].SumX, 0.000001, "ais[i1].SumX");
            Assert.AreEqual(3.9, ais[i1].SumY, 0.000001, "ais[i1].SumY");
            Assert.AreEqual(3, ais[i1].SumZ, 0.000001, "ais[i1].SumZ");
            Assert.AreEqual(5, ais[i1].SumZ2, 0.000001, "ais[i1].SumZ2");

            Assert.AreEqual(1, ais[i2].Count, "ais[i2].Count");
            Assert.AreEqual(4, ais[i2].SumX, 0.000001, "ais[i2].SumX");
            Assert.AreEqual(0, ais[i2].SumY, 0.000001, "ais[i2].SumY");
            Assert.AreEqual(1, ais[i2].SumZ, 0.000001, "ais[i2].SumZ");
            Assert.AreEqual(1, ais[i2].SumZ2, 0.000001, "ais[i2].SumZ2");

        }
        [TestMethod]
        public void ExtractObstaclesImplTest()
        {
            AggregateItem[] ais = new AggregateItem[15];
            Int32[] uais = new Int32[15];
            Point4D[] ops = new Point4D[15];

            ais[0].Count = 100;
            ais[0].SumX = 1000;
            ais[0].SumY = 900;
            ais[0].SumZ = 100;
            ais[0].SumZ2 = 100;

            ais[1].Count = 10;
            ais[1].SumX = 1000;
            ais[1].SumY = 900;
            ais[1].SumZ = 100;
            ais[1].SumZ2 = 100;

            ais[3].Count = 100;
            ais[3].SumX = 1000;
            ais[3].SumY = 900;
            ais[3].SumZ = 100;
            ais[3].SumZ2 = 201;

            uais[0] = 0*32;
            uais[1] = 1*32;
            uais[2] = 3*32;

            var cnt = NativeComputeUnit.ExtractObstaclesImpl(ais, uais, 3, ops, 50, 1);

            Assert.AreEqual(1, cnt, "cnt");

            Assert.AreEqual(10, ops[0].X, 0.000001, "ops[0].X");
            Assert.AreEqual(9, ops[0].Y, 0.000001, "ops[0].Y");
            Assert.AreEqual(1, ops[0].Z, 0.000001, "ops[0].Z");
            Assert.AreEqual(1, ops[0].A, 0.000001, "ops[0].A");
        }
    }
}
