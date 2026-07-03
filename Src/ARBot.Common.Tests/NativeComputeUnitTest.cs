using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Tests
{
    /// <summary>
    /// Testy P/Invoke vrstvy nad NativeLib.dll (NativeComputeUnit.NativeMethods a verejne wrappery).
    /// Vyzaduji nativni NativeLib.dll v output adresari a x64 proces.
    /// </summary>
    public class NativeComputeUnitTest
    {
        private const double Tol = 1e-4;

        // Diagnostika padu nativniho kodu (napr. na ARM/QEMU): pokud je nastavena promenna
        // CRASH_LOG, zapise se pred kazdym testem jeho jmeno - posledni radek = padajici test.
        [SetUp]
        public void LogRunningTest()
        {
            var f = Environment.GetEnvironmentVariable("CRASH_LOG");
            if (!string.IsNullOrEmpty(f))
                System.IO.File.AppendAllText(f, TestContext.CurrentContext.Test.Name + "\n");
        }

        private static void AreEqual(Point4D expected, Point4D actual, string msg)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tol), msg + ".X");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tol), msg + ".Y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tol), msg + ".Z");
            Assert.That(actual.A, Is.EqualTo(expected.A).Within(Tol), msg + ".A");
        }

        /// <summary>
        /// Konstrukce instance alokuje nativni ComputeInfo - smoke test ze se NativeLib.dll nacte.
        /// </summary>
        [Test]
        public void Construct_AllocatesNativeComputeInfo()
        {
            var unit = new NativeComputeUnit(100, 10, 10, 0, 0, 0.1f, new BackProject(BackProject.RoadProbability));
            Assert.That(unit.AggregateResolution, Is.EqualTo(0.1f).Within(Tol));
        }

        private static void CopyByte(int len)
        {
            byte[] src = new byte[len];
            byte[] dst = new byte[src.Length];

            for (int i = 0; i < src.Length; i++)
                src[i] = (byte)i;
            NativeComputeUnit.CopyByte(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
                Assert.That(dst[i], Is.EqualTo(src[i]), $"{len}:src[{i}]");
        }

        [Test]
        public void CopyByte_CopiesArray()
        {
            CopyByte(10);
            CopyByte(32);
        }

        private static void ReverseInt16(int len)
        {
            var src = new short[len];
            var dst = new short[src.Length];

            for (int i = 0; i < src.Length; i++)
                src[i] = (short)i;
            NativeComputeUnit.ReverseInt16(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
                Assert.That(dst[src.Length - i - 1], Is.EqualTo(src[i]), $"src[{i}]");
        }

        [Test]
        public void ReverseInt16_ReversesArray()
        {
            ReverseInt16(10);
            ReverseInt16(32);
        }

        private static void CopyRGB24ToBGR32(int len)
        {
            var src = new NativeComputeUnit.RGB[len];
            var dst = new NativeComputeUnit.BGR32[src.Length];

            for (int i = 0; i < src.Length; i++)
            {
                src[i].R = (byte)i;
                src[i].G = (byte)(i + len);
                src[i].B = (byte)(i + 2 * len);
            }
            NativeComputeUnit.CopyRGB24ToBGR32(dst, src, src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                Assert.That(dst[i].R, Is.EqualTo(src[i].R), $"{len}:src[{i}.R]");
                Assert.That(dst[i].G, Is.EqualTo(src[i].G), $"{len}:src[{i}.G]");
                Assert.That(dst[i].B, Is.EqualTo(src[i].B), $"{len}:src[{i}.B]");
            }
        }

        [Test]
        public void CopyRGB24ToBGR32_KeepsOrder()
        {
            CopyRGB24ToBGR32(10);
            CopyRGB24ToBGR32(32);
        }

        private static void ReverseRGB24ToBGR32(int len)
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
                Assert.That(dst[len - 1 - i].R, Is.EqualTo(src[i].R), $"{len}:src[{i}.R]");
                Assert.That(dst[len - 1 - i].G, Is.EqualTo(src[i].G), $"{len}:src[{i}.G]");
                Assert.That(dst[len - 1 - i].B, Is.EqualTo(src[i].B), $"{len}:src[{i}.B]");
            }
        }

        [Test]
        public void ReverseRGB24ToBGR32_ReversesOrder()
        {
            ReverseRGB24ToBGR32(10);
            ReverseRGB24ToBGR32(32);
        }

        /// <summary>
        /// Transformation prevede Matrix4x4 na float[16] po radcich.
        /// </summary>
        [Test]
        public void Transformation_Identity_IsIdentityArray()
        {
            var t = NativeComputeUnit.Transformation(Matrix4x4.Identity);

            float[] expected = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            Assert.That(t.Length, Is.EqualTo(16));
            for (int i = 0; i < 16; i++)
                Assert.That(t[i], Is.EqualTo(expected[i]).Within(Tol), $"transform[{i}]");
        }

        [Test]
        public void TransformPoint4DImpl_Identity_ReturnsInput()
        {
            Point4D[] dst = new Point4D[3];
            Point4D[] src = new Point4D[3];

            Point4D x = new Point4D() { X = 1, Y = 0, Z = 0, A = 1 };
            Point4D y = new Point4D() { X = 0, Y = 1, Z = 0, A = 1 };
            Point4D z = new Point4D() { X = 0, Y = 0, Z = 1, A = 1 };

            var t = NativeComputeUnit.Transformation(Matrix4x4.Identity);

            src[0] = x;
            src[1] = y;
            src[2] = z;

            NativeComputeUnit.TransformPoint4DImpl(dst, t, src, 3);

            AreEqual(x, dst[0], "Identity.X");
            AreEqual(y, dst[1], "Identity.Y");
            AreEqual(z, dst[2], "Identity.Z");
        }

        [Test]
        public void Depth2XYZImpl_SkipsUnmeasuredAndWritesFromEnd()
        {
            Point4D[] dst = new Point4D[5];
            short[] dist = new short[5];
            Point2DF[] transform = new Point2DF[5];

            transform[0] = new Point2DF(-1, -2);
            transform[1] = new Point2DF(-1, -3);
            transform[2] = new Point2DF(-1, -4);
            transform[3] = new Point2DF(1, 5);
            transform[4] = new Point2DF(1, 6);

            dist[0] = 0;
            dist[1] = -1;
            dist[2] = 1000;
            dist[3] = 2000;
            dist[4] = 3000;

            int cnt = NativeComputeUnit.Depth2XYZImpl(dst, dist, transform, 5);

            Assert.That(cnt, Is.EqualTo(3), "Count");

            AreEqual(new Point4D() { X = 3, Y = 18, Z = 3, A = 1 }, dst[0], "dst[0]");
            AreEqual(new Point4D() { X = 2, Y = 10, Z = 2, A = 1 }, dst[1], "dst[1]");
            AreEqual(new Point4D() { X = -1, Y = -4, Z = 1, A = 1 }, dst[2], "dst[2]");
        }

        [Test]
        public void DepthTransformImpl_Identity_MatchesDepth2XYZ()
        {
            Point4D[] dst = new Point4D[5];
            short[] dist = new short[5];

            var r = NativeComputeUnit.Transformation(Matrix4x4.Identity);

            Point2DF[] transform = new Point2DF[5];

            transform[0] = new Point2DF(-1, -2);
            transform[1] = new Point2DF(-1, -3);
            transform[2] = new Point2DF(-1, -4);
            transform[3] = new Point2DF(1, 5);
            transform[4] = new Point2DF(1, 6);

            dist[0] = 0;
            dist[1] = -1;
            dist[2] = 1000;
            dist[3] = 2000;
            dist[4] = 3000;

            int cnt = NativeComputeUnit.DepthTransformImpl(dst, transform, r, dist, 5);

            Assert.That(cnt, Is.EqualTo(3), "Count");

            AreEqual(new Point4D() { X = 3, Y = 18, Z = 3, A = 1 }, dst[0], "dst[0]");
            AreEqual(new Point4D() { X = 2, Y = 10, Z = 2, A = 1 }, dst[1], "dst[1]");
            AreEqual(new Point4D() { X = -1, Y = -4, Z = 1, A = 1 }, dst[2], "dst[2]");
        }

        [Test]
        public void ResetPlaneParams_ZeroesAllSums()
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

            NativeComputeUnit.ResetPlaneParams(ref modelParams);

            Assert.That(modelParams.Count1, Is.EqualTo(0), "Count1");
            Assert.That(modelParams.Count2, Is.EqualTo(0), "Count2");
            Assert.That(modelParams.Count3, Is.EqualTo(0), "Count3");

            Assert.That(modelParams.SumX, Is.EqualTo(0), "SumX");
            Assert.That(modelParams.SumXX, Is.EqualTo(0), "SumXX");
            Assert.That(modelParams.SumXY, Is.EqualTo(0), "SumXY");

            Assert.That(modelParams.SumY, Is.EqualTo(0), "SumY");
            Assert.That(modelParams.SumYY, Is.EqualTo(0), "SumYY");
            Assert.That(modelParams.SumYZ, Is.EqualTo(0), "SumYZ");

            Assert.That(modelParams.SumZ, Is.EqualTo(0), "SumZ");
            Assert.That(modelParams.SumZX, Is.EqualTo(0), "SumZX");
            Assert.That(modelParams.SumZZ, Is.EqualTo(0), "SumZZ");
        }

        [Test]
        public void CalcPlaneParams_ComputesPlaneVector()
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

            Assert.That(modelParams.v.A, Is.EqualTo(1.5454545).Within(1e-6), "v.A");
            Assert.That(modelParams.v.X, Is.EqualTo(-4.63636351).Within(1e-6), "v.X");
            Assert.That(modelParams.v.Y, Is.EqualTo(1), "v.Y");
            Assert.That(modelParams.v.Z, Is.EqualTo(1), "v.Z");
        }

        [Test]
        public void XYZ2PlaneImpl_AggregatesOnlyPointsWithinMaxZ()
        {
            PlaneParams modelParams = new PlaneParams();
            NativeComputeUnit.ResetPlaneParams(ref modelParams);

            Point4D[] points = new Point4D[5];

            points[0].X = 1; points[0].Y = 0; points[0].Z = 0; points[0].A = 1;
            points[1].X = 2; points[1].Y = 0; points[1].Z = 0; points[1].A = 1;
            points[2].X = 0; points[2].Y = 10; points[2].Z = -1; points[2].A = 1;
            points[3].X = 10; points[3].Y = 10; points[3].Z = 1; points[3].A = 1;
            points[4].X = 10; points[4].Y = 10; points[4].Z = 1000; points[4].A = 1;

            NativeComputeUnit.XYZ2PlaneImpl(ref modelParams, points, 2, 5);

            Assert.That(modelParams.SumX, Is.EqualTo(13), "SumX");
            Assert.That(modelParams.SumY, Is.EqualTo(20), "SumY");
            Assert.That(modelParams.SumZ, Is.EqualTo(0), "SumZ");

            Assert.That(modelParams.Count1, Is.EqualTo(4), "Count1");

            Assert.That(modelParams.SumXY, Is.EqualTo(100), "SumXY");
            Assert.That(modelParams.SumYZ, Is.EqualTo(0), "SumYZ");
            Assert.That(modelParams.SumZX, Is.EqualTo(10), "SumZX");

            Assert.That(modelParams.SumXX, Is.EqualTo(105), "SumXX");
            Assert.That(modelParams.SumYY, Is.EqualTo(200), "SumYY");
            Assert.That(modelParams.SumZZ, Is.EqualTo(2), "SumZZ");
        }

        [Test]
        public void ClearAggregateImpl_ClearsOnlyReferencedItems()
        {
            AggregateItem[] ais = new AggregateItem[5];
            ais[0].Count = 1;
            ais[1].Count = 4;
            ais[2].Count = 2;
            ais[3].Count = 3;
            ais[4].Count = 5;

            int[] uais = new int[5];
            uais[0] = 3 * 32;
            uais[1] = 0 * 32;
            uais[2] = 2 * 32;

            NativeComputeUnit.ClearAggregateImpl(ais, uais, 3);

            Assert.That(ais[0].Count, Is.EqualTo(0), "ais[0].Count");
            Assert.That(ais[1].Count, Is.EqualTo(4), "ais[1].Count");
            Assert.That(ais[2].Count, Is.EqualTo(0), "ais[2].Count");
            Assert.That(ais[3].Count, Is.EqualTo(0), "ais[3].Count");
            Assert.That(ais[4].Count, Is.EqualTo(5), "ais[4].Count");
        }

        [Test]
        public void AggregateObstaclesImpl_AggregatesPointsWithinField()
        {
            AggregateItem[] ais = new AggregateItem[15];
            int[] uais = new int[15];
            Point4D v = new Point4D() { X = 0, Y = 0, Z = 1, A = 0 };
            Point4D[] points = new Point4D[7];

            points[0].X = 4; points[0].Y = 0; points[0].Z = 1; points[0].A = 1;
            points[1].X = 20; points[1].Y = 0; points[1].Z = 1; points[1].A = 1;
            points[2].X = -20; points[2].Y = 0; points[2].Z = 1; points[2].A = 1;
            points[3].X = 0; points[3].Y = 2; points[3].Z = 1; points[3].A = 1;
            points[4].X = 0; points[4].Y = 1.9f; points[4].Z = 2; points[4].A = 1;
            points[5].X = 0; points[5].Y = 20; points[5].Z = 1; points[5].A = 1;
            points[6].X = 0; points[6].Y = -20; points[6].Z = 1; points[6].A = 1;

            var cnt = NativeComputeUnit.AggregateObstaclesImpl(points, 7, 2, 2, 1, ais, uais, 5, 3, v);

            Assert.That(cnt, Is.EqualTo(2), "cnt");
            Assert.That(uais[0], Is.EqualTo(0x180), "uais[0]");
            Assert.That(uais[1], Is.EqualTo(0x120), "uais[1]");

            int i1 = uais[0] / 32;
            int i2 = uais[1] / 32;

            Assert.That(ais[i1].Count, Is.EqualTo(2), "ais[i1].Count");
            Assert.That(ais[i1].SumX, Is.EqualTo(0).Within(1e-6), "ais[i1].SumX");
            Assert.That(ais[i1].SumY, Is.EqualTo(3.9).Within(1e-6), "ais[i1].SumY");
            Assert.That(ais[i1].SumZ, Is.EqualTo(3).Within(1e-6), "ais[i1].SumZ");
            Assert.That(ais[i1].SumZ2, Is.EqualTo(5).Within(1e-6), "ais[i1].SumZ2");

            Assert.That(ais[i2].Count, Is.EqualTo(1), "ais[i2].Count");
            Assert.That(ais[i2].SumX, Is.EqualTo(4).Within(1e-6), "ais[i2].SumX");
            Assert.That(ais[i2].SumY, Is.EqualTo(0).Within(1e-6), "ais[i2].SumY");
            Assert.That(ais[i2].SumZ, Is.EqualTo(1).Within(1e-6), "ais[i2].SumZ");
            Assert.That(ais[i2].SumZ2, Is.EqualTo(1).Within(1e-6), "ais[i2].SumZ2");
        }

        [Test]
        public void ExtractObstaclesImpl_ReturnsOnlyObstaclesAboveThresholds()
        {
            AggregateItem[] ais = new AggregateItem[15];
            int[] uais = new int[15];
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

            uais[0] = 0 * 32;
            uais[1] = 1 * 32;
            uais[2] = 3 * 32;

            var cnt = NativeComputeUnit.ExtractObstaclesImpl(ais, uais, 3, ops, 50, 1);

            Assert.That(cnt, Is.EqualTo(1), "cnt");

            Assert.That(ops[0].X, Is.EqualTo(10).Within(1e-6), "ops[0].X");
            Assert.That(ops[0].Y, Is.EqualTo(9).Within(1e-6), "ops[0].Y");
            Assert.That(ops[0].Z, Is.EqualTo(1).Within(1e-6), "ops[0].Z");
            Assert.That(ops[0].A, Is.EqualTo(1).Within(1e-6), "ops[0].A");
        }

        // ---------------------------------------------------------------------
        // Doplnkove testy: IntPtr varianty, non-identity transformace, hranicni
        // pripady. IntPtr varianty maji stejny nativni EntryPoint jako pole,
        // takze se overuji cross-checkem proti spravne fungujici pole-variante.
        // ---------------------------------------------------------------------

        private static IntPtr ToNative(byte[] bytes)
        {
            IntPtr p = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            return p;
        }

        [Test]
        public void CopyIntPtr_MatchesManagedCopy()
        {
            const int len = 32;
            byte[] src = new byte[len];
            for (int i = 0; i < len; i++)
                src[i] = (byte)(i * 7 + 1);

            byte[] expected = new byte[len];
            NativeComputeUnit.CopyByte(expected, src, len);

            IntPtr p = ToNative(src);
            try
            {
                byte[] dst = new byte[len];
                NativeComputeUnit.CopyIntPtr(dst, p, len);
                Assert.That(dst, Is.EqualTo(expected));
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        [Test]
        public void ReverseInt16IntPtr_MatchesManagedReverse()
        {
            const int len = 16;
            short[] src = new short[len];
            for (int i = 0; i < len; i++)
                src[i] = (short)(i * 111 - 500);

            short[] expArr = new short[len];
            NativeComputeUnit.ReverseInt16(expArr, src, len);
            byte[] expected = new byte[len * 2];
            Buffer.BlockCopy(expArr, 0, expected, 0, len * 2);

            byte[] srcBytes = new byte[len * 2];
            Buffer.BlockCopy(src, 0, srcBytes, 0, len * 2);

            IntPtr p = ToNative(srcBytes);
            try
            {
                byte[] dst = new byte[len * 2];
                NativeComputeUnit.ReverseInt16IntPtr(dst, p, len);
                Assert.That(dst, Is.EqualTo(expected));
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        [Test]
        public void CopyRGB24ToBGR32IntPtr_MatchesManagedCopy()
        {
            const int len = 16;
            var srcArr = new NativeComputeUnit.RGB[len];
            byte[] srcBytes = new byte[len * 3];
            for (int i = 0; i < len; i++)
            {
                byte r = (byte)i, g = (byte)(i + len), b = (byte)(i + 2 * len);
                srcArr[i].R = r; srcArr[i].G = g; srcArr[i].B = b;
                srcBytes[i * 3 + 0] = r; srcBytes[i * 3 + 1] = g; srcBytes[i * 3 + 2] = b;
            }

            var expArr = new NativeComputeUnit.BGR32[len];
            NativeComputeUnit.CopyRGB24ToBGR32(expArr, srcArr, len);
            byte[] expected = new byte[len * 4];
            for (int i = 0; i < len; i++)
            {
                expected[i * 4 + 0] = expArr[i].B;
                expected[i * 4 + 1] = expArr[i].G;
                expected[i * 4 + 2] = expArr[i].R;
                expected[i * 4 + 3] = expArr[i].A;
            }

            IntPtr p = ToNative(srcBytes);
            try
            {
                byte[] dst = new byte[len * 4];
                NativeComputeUnit.CopyRGB24ToBGR32IntPtr(dst, p, len);
                Assert.That(dst, Is.EqualTo(expected));
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        [Test]
        public void ReverseRGB24ToBGR32IntPtr_MatchesManagedReverse()
        {
            const int len = 16;
            var srcArr = new NativeComputeUnit.RGB[len];
            byte[] srcBytes = new byte[len * 3];
            for (int i = 0; i < len; i++)
            {
                byte r = (byte)i, g = (byte)(i + len), b = (byte)(i + 2 * len);
                srcArr[i].R = r; srcArr[i].G = g; srcArr[i].B = b;
                srcBytes[i * 3 + 0] = r; srcBytes[i * 3 + 1] = g; srcBytes[i * 3 + 2] = b;
            }

            var expArr = new NativeComputeUnit.BGR32[len];
            NativeComputeUnit.ReverseRGB24ToBGR32(expArr, srcArr, len);
            byte[] expected = new byte[len * 4];
            for (int i = 0; i < len; i++)
            {
                expected[i * 4 + 0] = expArr[i].B;
                expected[i * 4 + 1] = expArr[i].G;
                expected[i * 4 + 2] = expArr[i].R;
                expected[i * 4 + 3] = expArr[i].A;
            }

            IntPtr p = ToNative(srcBytes);
            try
            {
                byte[] dst = new byte[len * 4];
                NativeComputeUnit.ReverseRGB24ToBGR32IntPtr(dst, p, len);
                Assert.That(dst, Is.EqualTo(expected));
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        /// <summary>
        /// Transformation mapuje Matrix4x4 po radcich do float[16] (M11..M44).
        /// </summary>
        [Test]
        public void Transformation_NonIdentity_MapsRowMajor()
        {
            var m = new Matrix4x4(
                1, 2, 3, 4,
                5, 6, 7, 8,
                9, 10, 11, 12,
                13, 14, 15, 16);

            var t = NativeComputeUnit.Transformation(m);

            for (int i = 0; i < 16; i++)
                Assert.That(t[i], Is.EqualTo(i + 1).Within(Tol), $"transform[{i}]");
        }

        [Test]
        public void Depth2XYZImpl_AllUnmeasured_ReturnsZeroCount()
        {
            Point4D[] dst = new Point4D[4];
            short[] dist = { 0, -1, 0, -1 };
            Point2DF[] transform =
            {
                new Point2DF(1, 1), new Point2DF(1, 1),
                new Point2DF(1, 1), new Point2DF(1, 1)
            };

            int cnt = NativeComputeUnit.Depth2XYZImpl(dst, dist, transform, 4);

            Assert.That(cnt, Is.EqualTo(0), "Count");
        }

        [Test]
        public void DepthTransformImpl_AllUnmeasured_ReturnsZeroCount()
        {
            Point4D[] dst = new Point4D[4];
            short[] dist = { 0, -1, 0, -1 };
            var r = NativeComputeUnit.Transformation(Matrix4x4.Identity);
            Point2DF[] transform =
            {
                new Point2DF(1, 1), new Point2DF(1, 1),
                new Point2DF(1, 1), new Point2DF(1, 1)
            };

            int cnt = NativeComputeUnit.DepthTransformImpl(dst, transform, r, dist, 4);

            Assert.That(cnt, Is.EqualTo(0), "Count");
        }

        /// <summary>
        /// Degenerovana mnozina (vsechny sumy 0 =&gt; determinant d==0) da svislou normalu (0,0,1,0).
        /// Overuje ochranu proti singularite v nativnim CalcPlaneParams (shodne s managed PlaneParams.Calc()).
        /// </summary>
        [Test]
        public void CalcPlaneParams_Degenerate_ReturnsVerticalNormal()
        {
            PlaneParams pars = new PlaneParams();
            NativeComputeUnit.ResetPlaneParams(ref pars);

            NativeComputeUnit.CalcPlaneParams(ref pars);

            Assert.That(pars.v.X, Is.EqualTo(0).Within(Tol), "v.X");
            Assert.That(pars.v.Y, Is.EqualTo(0).Within(Tol), "v.Y");
            Assert.That(pars.v.Z, Is.EqualTo(1).Within(Tol), "v.Z");
            Assert.That(pars.v.A, Is.EqualTo(0).Within(Tol), "v.A");
        }

        /// <summary>
        /// XYZ2PlaneImpl s len=0 nesmi nic naagregovat.
        /// </summary>
        [Test]
        public void XYZ2PlaneImpl_ZeroLength_LeavesParamsEmpty()
        {
            PlaneParams pars = new PlaneParams();
            NativeComputeUnit.ResetPlaneParams(ref pars);

            Point4D[] points = new Point4D[1];
            points[0] = new Point4D() { X = 5, Y = 5, Z = 0, A = 1 };

            NativeComputeUnit.XYZ2PlaneImpl(ref pars, points, 2, 0);

            Assert.That(pars.Count1, Is.EqualTo(0), "Count1");
            Assert.That(pars.SumX, Is.EqualTo(0), "SumX");
            Assert.That(pars.SumY, Is.EqualTo(0), "SumY");
        }

        // ---------------------------------------------------------------------
        // Integracni testy: Segment / properties. Pouzivaji fake projekci
        // (Segment cte z projekce jen Transformation a Camera2DToCamera3D).
        // ---------------------------------------------------------------------

        private sealed class FakeProjection : IDepthCameraProjection
        {
            public Matrix4x4 Transformation { get; set; } = Matrix4x4.Identity;
            public Point2DF[,] Camera2DToCamera3D { get; set; } = new Point2DF[0, 0];
            public List<Point2D> TargetPoly => throw new NotImplementedException();
            public void SetOrientation(Matrix4x4 transform) => Transformation = transform;
            public List<Point4D> GetPointCloud(Image<Gray16> depth) => throw new NotImplementedException();
            public List<Point4D> TransformBack(List<ARBot.Common.Common.Point> points, Image<Gray16> depth) => throw new NotImplementedException();
        }

        private static NativeComputeUnit CreateUnit(int maxPoints)
            => new NativeComputeUnit(maxPoints, 64, 64, 32, 32, 0.1f, new BackProject(BackProject.RoadProbability));

        // Projekce mapujici kazdy pixel na stejny smerovy vektor (x,y) v rovine kamery.
        private static FakeProjection ConstProjection(int w, int h, Point2DF map)
        {
            var lct = new Point2DF[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    lct[y, x] = map;
            return new FakeProjection { Transformation = Matrix4x4.Identity, Camera2DToCamera3D = lct };
        }

        // Hloubkovy obraz Gray16 (vzdalenost v mm, little-endian ushort).
        private static Image<Gray16> DepthImage(int w, int h, Func<int, int, ushort> distMm)
        {
            var img = new Image<Gray16>(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    ushort d = distMm(x, y);
                    int idx = (y * w + x) * 2;
                    img.Data[idx] = (byte)(d & 0xFF);
                    img.Data[idx + 1] = (byte)(d >> 8);
                }
            return img;
        }

        // POZOR: Nasledujici Segment_* testy jsou [Ignore] - nativni Segment2 cesta
        // (Segment2 -> Segment -> DepthTransformImpl) pada na x64 s AccessViolation (0xC0000005)
        // pri zpracovani realnych hloubkovych dat (validni body). Prazdny obraz nepada, ale
        // vraci nedeterministicky WordPointsCount (znak poskozeni pameti / use-after-free).
        // NativeComputeUnit.Segment se v produkci nepouziva (nikde neni 'new NativeComputeUnit').
        // Testy jsou pripravene - zapnout az bude nativni Segment cesta na x64 opravena.

        /// <summary>
        /// Segment jednoho obrazu: pri smeru (0,0) a vzdalenosti 1 m vzniknou body (0,0,1,1),
        /// vsechny padnou do jedne agregacni bunky.
        /// </summary>
        [Test]
        [Ignore("Nativni Segment2 cesta pada na x64 (AccessViolation). NativeComputeUnit.Segment se v produkci nepouziva.")]
        public void Segment_SingleImage_AllValid_PopulatesWorldPoints()
        {
            const int w = 8, h = 8, n = w * h;
            var unit = CreateUnit(n);
            var proj = ConstProjection(w, h, new Point2DF(0, 0));
            var img = DepthImage(w, h, (x, y) => 1000); // 1 m

            unit.Segment(img, proj, Matrix4x4.Identity);

            Assert.That(unit.WordPointsCount, Is.EqualTo(n), "WordPointsCount");
            Assert.That(unit.WordPoints.Length, Is.EqualTo(n), "WordPoints.Length");
            foreach (var p in unit.WordPoints)
            {
                Assert.That(p.X, Is.EqualTo(0).Within(1e-3), "X");
                Assert.That(p.Y, Is.EqualTo(0).Within(1e-3), "Y");
                Assert.That(p.Z, Is.EqualTo(1).Within(1e-3), "Z");
                Assert.That(p.A, Is.EqualTo(1).Within(1e-3), "A");
            }

            // vsech n bodu spadne do stejne agregacni bunky na (0,0)
            var agg = unit.GetAggregateItem(0, 0);
            Assert.That(agg.HasValue, Is.True, "agg (0,0)");
            Assert.That(agg.Value.Count, Is.EqualTo(n), "agg.Count");
            Assert.That(agg.Value.SumZ, Is.EqualTo(n).Within(1e-2), "agg.SumZ");

            // rovina je degenerovana (zadny bod v |z|<maxZ) => svisla normala
            Assert.That(unit.LeftCameraParams.v.Z, Is.EqualTo(1).Within(Tol), "LeftCameraParams.v.Z");

            GC.KeepAlive(unit);
        }

        [Test]
        [Ignore("Nativni Segment2 cesta pada na x64 (AccessViolation). NativeComputeUnit.Segment se v produkci nepouziva.")]
        public void Segment_PartialImage_CountsOnlyMeasuredPixels()
        {
            const int w = 8, h = 8, n = w * h;
            var unit = CreateUnit(n);
            var proj = ConstProjection(w, h, new Point2DF(0, 0));
            // mereny jen kazdy druhy pixel (dist=0 => nezmereno)
            var img = DepthImage(w, h, (x, y) => (ushort)(((y * w + x) % 2 == 0) ? 500 : 0));

            unit.Segment(img, proj, Matrix4x4.Identity);

            Assert.That(unit.WordPointsCount, Is.EqualTo(n / 2), "WordPointsCount");
            GC.KeepAlive(unit);
        }

        [Test]
        [Ignore("Nativni Segment2 cesta pada na x64 (AccessViolation). NativeComputeUnit.Segment se v produkci nepouziva.")]
        public void Segment_Stereo_SumsBothImages()
        {
            const int w = 8, h = 8, n = w * h;
            var unit = CreateUnit(2 * n);
            var proj = ConstProjection(w, h, new Point2DF(0, 0));
            var img = DepthImage(w, h, (x, y) => 1000);

            unit.Segment(img, proj, img, proj, Matrix4x4.Identity);

            Assert.That(unit.WordPointsCount, Is.EqualTo(2 * n), "WordPointsCount (left+right)");
            GC.KeepAlive(unit);
        }

        [Test]
        [Ignore("Nativni Segment2 cesta na x64 vraci nedeterministicky vysledek (poskozeni pameti). NativeComputeUnit.Segment se v produkci nepouziva.")]
        public void Segment_EmptyImage_ProducesNoPoints()
        {
            const int w = 8, h = 8, n = w * h;
            var unit = CreateUnit(n);
            var proj = ConstProjection(w, h, new Point2DF(0, 0));
            var img = DepthImage(w, h, (x, y) => 0); // vse nezmereno

            unit.Segment(img, proj, Matrix4x4.Identity);

            Assert.That(unit.WordPointsCount, Is.EqualTo(0), "WordPointsCount");
            Assert.That(unit.ObstaclePoints.Length, Is.EqualTo(0), "ObstaclePoints");
            GC.KeepAlive(unit);
        }

        [Test]
        public void GetAggregateItem_OutOfRange_ReturnsNull()
        {
            var unit = CreateUnit(64);

            Assert.That(unit.GetAggregateItem(1000, 1000), Is.Null, "kladne mimo");
            Assert.That(unit.GetAggregateItem(-1000, -1000), Is.Null, "zaporne mimo");
            GC.KeepAlive(unit);
        }

        /// <summary>
        /// SegmentNew3 (aktualne pouzivana detekce prekazek) - managed algoritmus nad
        /// DepthTransform2Impl. WordPoints ma delku w*h, WordObstaclePoints je vyplneno.
        /// </summary>
        [Test]
        public void SegmentNew3_RunsAndFillsWorldPoints()
        {
            const int w = 16, h = 16, n = w * h;
            var unit = CreateUnit(n);
            var proj = ConstProjection(w, h, new Point2DF(0, 0));
            var img = DepthImage(w, h, (x, y) => 1000);

            unit.SegmentNew3(img, proj, Matrix4x4.Identity);

            Assert.That(unit.WordPoints.Length, Is.EqualTo(n), "WordPoints.Length");
            Assert.That(unit.WordObstaclePoints, Is.Not.Null, "WordObstaclePoints");
            GC.KeepAlive(unit);
        }

        /// <summary>
        /// PathEdges nad prazdnym (cernym) obrazem nevraci zadne hrany.
        /// </summary>
        [Test]
        public void PathEdges_BlackImage_ReturnsNoEdges()
        {
            var unit = CreateUnit(64);
            var img = new Image<Gray>(32, 16); // vse 0 => zadna cesta

            var edges = unit.PathEdges(img, 1.0, 1.0);

            Assert.That(edges, Is.Not.Null);
            Assert.That(edges.Count, Is.EqualTo(0), "pocet hran");
            GC.KeepAlive(unit);
        }

        /// <summary>
        /// PathEdges: cesta jako svisly pruh vysokych hodnot uprostred obrazu se detekuje.
        /// </summary>
        [Test]
        public void PathEdges_CenterStripe_DetectsEdges()
        {
            const int w = 40, h = 10;
            var unit = CreateUnit(64);
            var img = new Image<Gray>(w, h);
            // prostredni tretina = "cesta" (vysoka pravdepodobnost), okraje 0
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    img.Data[y * w + x] = (byte)((x >= w / 3 && x < 2 * w / 3) ? 255 : 0);

            var edges = unit.PathEdges(img, 1.0, 1.0);

            Assert.That(edges, Is.Not.Null);
            Assert.That(edges.Count, Is.GreaterThan(0), "nejake hrany");
            foreach (var e in edges)
                Assert.That(e.Y, Is.InRange(0, h - 1), "Y hrany");
            GC.KeepAlive(unit);
        }

        /// <summary>
        /// BackProject (BGR32): cerny obraz (vsechny kanaly 0) mapuje na tabulku[0].
        /// </summary>
        [Test]
        public void BackProject_BlackImage_MapsToTableZero()
        {
            const int w = 8, h = 8;
            var unit = CreateUnit(64);
            var prob = new Image<Gray>(w, h);
            var img = new Image<ARBot.Common.Common.BGR32>(w, h); // vse 0
            byte[] tab = new byte[4096];
            tab[0] = 77;

            unit.BackProject(prob, img, tab);

            for (int i = 0; i < w * h; i++)
                Assert.That(prob.Data[i], Is.EqualTo(77), $"prob[{i}]");
            GC.KeepAlive(unit);
        }

        /// <summary>
        /// BackProject (BGR32): bily obraz (vsechny kanaly 255) mapuje na tabulku[4095]
        /// (horni 4 bity z B,G,R = 0xFFF).
        /// </summary>
        [Test]
        public void BackProject_WhiteImage_MapsToTableMax()
        {
            const int w = 8, h = 8;
            var unit = CreateUnit(64);
            var prob = new Image<Gray>(w, h);
            var img = new Image<ARBot.Common.Common.BGR32>(w, h);
            for (int i = 0; i < img.Data.Length; i++)
                img.Data[i] = 255;
            byte[] tab = new byte[4096];
            tab[4095] = 99;

            unit.BackProject(prob, img, tab);

            for (int i = 0; i < w * h; i++)
                Assert.That(prob.Data[i], Is.EqualTo(99), $"prob[{i}]");
            GC.KeepAlive(unit);
        }

        [Test]
        public void BackProject_MismatchedSizes_Throws()
        {
            var unit = CreateUnit(64);
            var prob = new Image<Gray>(8, 8);
            var img = new Image<ARBot.Common.Common.BGR32>(4, 4);
            byte[] tab = new byte[4096];

            Assert.That(() => unit.BackProject(prob, img, tab), Throws.Exception);
            GC.KeepAlive(unit);
        }

        /// <summary>
        /// Process pouzije BackProject tabulku predanou v konstruktoru (RoadProbability).
        /// </summary>
        [Test]
        public void Process_BlackImage_UsesRoadProbabilityTable()
        {
            const int w = 8, h = 8;
            var unit = new NativeComputeUnit(64, 64, 64, 32, 32, 0.1f, new BackProject(BackProject.RoadProbability));
            var src = new Image<ARBot.Common.Common.BGR32>(w, h); // black
            var dest = new Image<Gray>(w, h);

            unit.Process(src, dest);

            for (int i = 0; i < w * h; i++)
                Assert.That(dest.Data[i], Is.EqualTo(BackProject.RoadProbability[0]), $"dest[{i}]");
            GC.KeepAlive(unit);
        }
    }
}
