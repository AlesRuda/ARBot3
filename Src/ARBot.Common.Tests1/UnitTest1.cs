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
using ARBot.Common.Configuration;
using ARBot.Common.Algorithms;
using ARBot.Common.Algorithms.ComputeUnit;
using System.Windows.Media.Media3D;
using Point4D = ARBot.Common.Common.Point4D;
using MathNet.Numerics.LinearAlgebra;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void BayesPixel()
        {
            BayesPixel bp = new BayesPixel();
            Assert.AreEqual(0.5, bp.Value);
            bp.Update(0.75);
            Assert.AreEqual(0.75, bp.Value);
            bp.Update(0.75);
            Assert.AreEqual(0.9, bp.Value);
        }
        [TestMethod]
        public void LocalMap()
        {
            LocalMap lm = new LocalMap();
            lm.Center = new Point(0, 0);
            lm[0, 0].Value = 1;
            lm.Center = new Point(1, 0);
            Assert.AreEqual(1, lm[-1, 0].Value);
            lm.Center = new Point(lm.Width * 2, 0);
            Assert.AreEqual(0.5, lm[0, 0].Value);
            lm[0, 0].Value = .9;
            Assert.AreEqual(.9, lm[0, 0].Value);
            lm.Center = new Point(0, 0);
            Assert.AreEqual(1, lm[0, 0].Value);
        }
        [TestMethod]
        public void VFHPlus()
        {
            VFHPlus vfh = new VFHPlus(Math.PI, 10, 3);
            VFHPlusItem[] items = new VFHPlusItem[2];
            items[0] = new VFHPlusItem() { Beta = Math.PI / 10, Distance = 1, Coeficient = 1 };
            items[1] = new VFHPlusItem() { Beta = -Math.PI / 8, Distance = 0.8, Coeficient = 1 };

            vfh.Calc(items, 0, 0, 1, Math.PI / 2);

            Assert.AreEqual(0, vfh.Direction);
        }
        [TestMethod]
        public void Trasformation()
        {
            Transformation t = new Transformation();
            ECEF x1 = new ECEF() { X = 1 };
            ECEF y1 = new ECEF() { Y = 1 };
            ECEF z1 = new ECEF() { Z = 1 };

            ECEF x_1 = new ECEF() { X = -1 };
            ECEF y_1 = new ECEF() { Y = -1 };
            ECEF z_1 = new ECEF() { Z = -1 };

            ECEF x2 = new ECEF() { X = 2 };
            ECEF y2 = new ECEF() { Y = 2 };
            ECEF z2 = new ECEF() { Z = 2 };

            Assert.AreEqual(1, t.Scale, "Pocatecni zvetseni.");

            Assert.AreEqual(t.Transform(x1), x1, "Identita.");
            Assert.AreEqual(t.Transform(y1), y1, "Identita.");
            Assert.AreEqual(t.Transform(z1), z1, "Identita.");

            t.Scale = 2;
            Assert.AreEqual(2, t.Scale, "Nastaveni zvetseni na 2.");

            Assert.AreEqual(x2, t.Transform(x1), "Zvetseni v ose X.");
            Assert.AreEqual(y2, t.Transform(y1), "Zvetseni v ose Y.");
            Assert.AreEqual(z2, t.Transform(z1), "Zvetseni v ose Z.");

            t.Scale = 1;
            Assert.AreEqual(1, t.Scale, "Nastaveni zvetseni na 1.");

            t.Move(1, 0, 0);

            Assert.AreEqual(x2, t.Transform(x1), "Posunuti o 1 ve smeru X.");

            t.Move(-1, 1, 0);

            Assert.AreEqual(y2, t.Transform(y1), "Posunuti o 1 ve smeru Y.");

            t.Move(0, -1, 1);

            Assert.AreEqual(z2, t.Transform(z1), "Posunuti o 1 ve smeru Z.");

            t.Move(0, 0, -1);

            t.RotateZ(Math.PI / 2);

            Assert.AreEqual(y1, t.Transform(x1), "Otoceni podle osy Z o 90 stupnu vlevo.");
            Assert.AreEqual(x_1, t.Transform(y1), "Otoceni podle osy Z o 90 stupnu vlevo.");
            Assert.AreEqual(z1, t.Transform(z1), "Otoceni podle osy Z o 90 stupnu vlevo.");

            t.RotateZ(-Math.PI / 2);
            t.RotateX(Math.PI / 2);

            Assert.AreEqual(x1, t.Transform(x1), "Otoceni podle osy X o 90 stupnu vlevo.");
            Assert.AreEqual(z1, t.Transform(y1), "Otoceni podle osy X o 90 stupnu vlevo.");
            Assert.AreEqual(y_1, t.Transform(z1), "Otoceni podle osy X o 90 stupnu vlevo.");

            t.RotateX(-Math.PI / 2);
            t.RotateY(Math.PI / 2);

            Assert.AreEqual(z_1, t.Transform(x1), "Otoceni podle osy Y o 90 stupnu vlevo.");
            Assert.AreEqual(y1, t.Transform(y1), "Otoceni podle osy Y o 90 stupnu vlevo.");
            Assert.AreEqual(x1, t.Transform(z1), "Otoceni podle osy Y o 90 stupnu vlevo.");

            ECEF e = new ECEF() { X = 1, Y = 1, Z = 0.5 };

            t.Reset();
            t.Rotate(e, false);

            e = e * (1 / e.Radius);

            Assert.AreEqual(x1, t.Transform(e), "Pootoceni bodu do osy X");

            e = new ECEF() { X = 1, Y = 1, Z = 0.5 };

            t.Reset();
            t.Rotate(e, true);

            e = e * (1 / e.Radius);

            Assert.AreEqual(e, t.Transform(x1), "Pootoceni osy X do bodu");

            t = new Transformation(new LLA(0, 0), true);

            Assert.AreEqual(x1, t.Transform(x1), "Transformace z LLA - identita");

            t = new Transformation(new LLA(0, 90.0 / 180.0 * Math.PI), true);

            Assert.AreEqual(y1, t.Transform(x1), "Transformace z LLA - pootoceni doprava");

            t = new Transformation(new LLA(0, 90.0 / 180.0 * Math.PI), false);

            Assert.AreEqual(x1, t.Transform(y1), "Transformace z LLA - pootoceni doleva");


            LLA lla = new LLA(Conversions.Deg2Rad(50.0318562), Conversions.Deg2Rad(14.5200161), 0);
            e = new ECEF(Ellipsoid.Sphere, lla);
            t = new Transformation(new LLA(e), false);
            ECEF e1 = t.Transform(e);
        }


        [TestMethod]
        public void CoordinatesTest()
        {
            LLA lla = new LLA(Conversions.Deg2Rad(50), Conversions.Deg2Rad(15), 100);
            ECEF e = new ECEF(Ellipsoid.Sphere, lla);
            LLA lla1 = new LLA(Ellipsoid.Sphere, e);

            Assert.AreEqual(lla, lla1, "LLA -> ECEF -> LLA");
        }


        [TestMethod]
        public void MapCorelator()
        {
            Map m = new Map("Hviezdoslavova.osm", 5, 5, true);
            LLA reference = m.Points[0].LLA;

            Transformation t = new Transformation(reference, false);
            Transformation rt = new Transformation(reference, true);
            m.Init(t);

            m.CalculateDistances(t.Transform(new ECEF(Ellipsoid.Sphere, new LLA(Conversions.Deg2Rad(52), Conversions.Deg2Rad(15)))));

            LocalMap lm = new LocalMap();
            lm.Center = new Point(0, 10);

            MapCorelator mc = new MapCorelator(lm, m);
            mc.Process(0, 0);

            var b = mc.ToLogMessage();
            var bs = b.GetBitmapSource();
            using (var fileStream = new FileStream("bitmap.png", FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bs));
                encoder.Save(fileStream);
            }

            //            bm.Save("bitmap.bmp");
        }

        [TestMethod]
        public void MapCorelator2()
        {
            Map m = new Map("test.osm", 2, 2, true);
            LLA reference = m.Points[0].LLA;

            Transformation t = new Transformation(reference, false);
            Transformation rt = new Transformation(reference, true);
            m.Init(t);

            m.CalculateDistances(t.Transform(new ECEF(Ellipsoid.Sphere, reference)));

            LocalMap lm = new LocalMap();
            lm.Center = new Point(0, 0);

            for (int x = 0; x < lm.Width - 10; x++)
            {
                lm[x + 5, x].Update(1);
                lm[x + 6, x].Update(1);
                lm[x + 7, x].Update(1);
            }

            MapCorelator mc = new MapCorelator(lm, m);
            mc.Process(0, 0);

            var b = mc.ToLogMessage();
            var bs = b.ToBitmap();
            bs.Save("bitmap.png");

            b = mc.ToLogMessage2();
            bs = b.ToBitmap();
            bs.Save("corelation.png");


            //            bm.Save("bitmap.bmp");
        }

        [TestMethod]
        public void MapCorelator3()
        {
            Map m = new Map("test2.osm", 2, 0.2, true);
            LLA reference = m.Points[0].LLA;

            Transformation t = new Transformation(reference, false);
            Transformation rt = new Transformation(reference, true);
            m.Init(t);

            m.CalculateDistances(t.Transform(new ECEF(Ellipsoid.Sphere, reference)));

            LocalMap lm = new LocalMap();
            lm.Center = new Point(0, 0);

            lm[0, 0].Update(1);

            MapCorelator mc = new MapCorelator(lm, m);

            for (int x = -10; x < 10; x++)
            {
                for (int y = -10; y < 10; y++)
                {
                    mc.Process(x, y);
                    var b = mc.ToLogMessage();
                    var bs = b.ToBitmap();
                    bs.Save("bitmap.png");

                    b = mc.ToLogMessage2();
                    bs = b.ToBitmap();
                    bs.Save("corelation.png");

                    b = mc.ToLogMessage3();
                    bs = b.ToBitmap();
                    bs.Save("localmap.png");
                }
            }
        }
        [TestMethod]
        public void MapCorelator4()
        {
            Map m = new Map();
            LocalMap lm = new LocalMap();
            lm.Center = new Point(0, 0);

            MapCorelator mc = new MapCorelator(lm, m);

            Complex[,] localMap = new Complex[64, 64];

            /*            for (int i = 0; i < 64;i++)
                            localMap[32, i] = new Complex(1, 0);*/
            localMap[32, 32] = new Complex(1, 0);
            localMap[32, 33] = new Complex(1, 0);
            localMap[32, 34] = new Complex(1, 0);

            FourierTransform.FFT2(localMap, FourierTransform.Direction.Forward);

            for (int y = -10; y < 10; y++)
            {
                for (int x = -10; x < 10; x++)
                {
                    Complex[,] map = new Complex[64, 64];
                    map[32 + x, 32 + y] = new Complex(1, 0);

                    FourierTransform.FFT2(map, FourierTransform.Direction.Forward);

                    mc.Process(map, localMap);

                    var b = mc.ToLogMessage2();
                    var bs = b.ToBitmap();
                    bs.Save("bitmap.png");

                }
            }
        }
        /*        [TestMethod]
                public void Regulator()
                {
                    StateBase s = new StateBase(Profile.Rozchod);
                    IRegulator r = new Regulator(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed, Profile.MaxAcceleration, Profile.Rozchod);
                    IModel m = new SimpleModel(Profile.MaxAcceleration, Profile.Rozchod, Profile.DeklinaceRad, s);

                    s.Read(100);

                    while (true)
                    {
                        RegulatorResult rr = r.Control(m.PredictedState, new RegulatorWayPoint[] { new RegulatorWayPoint() { X = 0.5, Y = 0.0, Speed = 0, MaxPositionError = 0.1 } });
                        s.ReqSpeed = rr.Speed;
                        s.ReqRotationSpeed = rr.RotationSpeed;

                        s.YPR = new YawPitchRoll(m.CurrentState.Azimut, 0, 0);
                        s.Motor = new MotorStateBase(false, s.ReqLeftMotorSpeed * s.Ts, s.ReqRightMotorSpeed * s.Ts, 12, 1, 1, s.ReqLeftMotorSpeed, s.ReqRightMotorSpeed);
                        m.Update();
                        Debug.WriteLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}", m.CurrentState.X, m.CurrentState.Y, Conversions.Rad2Deg(m.CurrentState.Azimut), s.ReqSpeed, s.ReqRotationSpeed));
                    }

                }
                */
        [TestMethod]
        public void Regulator1()
        {
            var reg = new Regulator(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed, Profile.MaxAcceleration, Profile.Rozchod);
            RegulatorResult r = reg.Control(new ModelState(Profile.Rozchod) { Orientation=Conversions.Deg2Rad(100), LeftWheelVelocity=-0.01, RightWheelVelocity=0.01 }, new RegulatorWayPoint() { X = -6.1, Y = -0.005, Speed = 0, MaxPositionError = 0.1 });

            var a=Conversions.Rad2Deg(Conversions.Orientation2Azimut(Math.Atan2(0.5, -0.1)));
        }
        [TestMethod]
        public void Regulator2()
        {
            var res = ARBot.Common.Regulators.Regulator.Dist2Speed2(0.3, 1, -0.5, 2, 1, 0.1);
        }
        [TestMethod]
        public void Regulator3()
        {
            double maxAcceleration = 1;
            double rozchod = 1;
            StateBase s = new StateBase(rozchod);
            IRegulator r = new Regulator(2, 0.5, maxAcceleration, rozchod);
            IModel m = new SimpleModel(maxAcceleration, rozchod, 0);
            m.CurrentState.X = 1;
            m.CurrentState.Y = 2;
            m.CurrentState.Orientation = 0;
            var ret =r.Control(m.CurrentState, new RegulatorWayPoint() { X = 2, Y = 3 });
            Assert.IsTrue(ret.RotationSpeed > 0, "Musi rotovat doleva");
            m.CurrentState.Orientation = Math.PI;
            ret = r.Control(m.CurrentState, new RegulatorWayPoint() { X = 2, Y = 3 });
            Assert.IsTrue(ret.RotationSpeed < 0, "Musi rotovat doprava");

        }
        [TestMethod]
        public void Collider()
        {
            var c = new Collider2(4, 1, 3, 0);
            Assert.IsFalse(c.Inside(-1, 1), "Inside(-1, 1)");
            Assert.IsFalse(c.Inside(2, 3), "Inside(2, 3)");
            Assert.IsFalse(c.Inside(7, 3), "Inside(7, 3)");
            Assert.IsTrue(c.Inside(2, 1), "Inside(2, 1)");
            Assert.IsTrue(c.Inside(6, 0), "Inside(6, 0)");
            Assert.IsTrue(c.Inside(6.8, 0.1), "Inside(6.8, 0.1)");

            c = new Collider2(4, 1, 3, Math.PI);
            Assert.IsFalse(c.Inside(1, 1), "Inside(1, 1)");
            Assert.IsFalse(c.Inside(-2, 3), "Inside(-2, 3)");
            Assert.IsFalse(c.Inside(-7, 3), "Inside(-7, 3)");
            Assert.IsTrue(c.Inside(-2, 1), "Inside(-2, 1)");
            Assert.IsTrue(c.Inside(-6, 0), "Inside(-6, 0)");
            Assert.IsTrue(c.Inside(-6.8, 0.1), "Inside(-6.8, 0.1)");

            c = new Collider2(4, 1, 3, Math.PI / 2);
            Assert.IsFalse(c.Inside(1, -1), "Inside(1, -1)");
            Assert.IsFalse(c.Inside(3, 2), "Inside(3, 2)");
            Assert.IsFalse(c.Inside(3, 7), "Inside(3, 7)");
            Assert.IsTrue(c.Inside(1, 2), "Inside(1, 2)");
            Assert.IsTrue(c.Inside(0, 6), "Inside(0, 6)");
            Assert.IsTrue(c.Inside(0.1, 6.8), "Inside(0.1, 6.8)");

            c = new Collider2(0.98384063437711933, 0.3, 0.5, -2.9770525833684989);
            Assert.IsTrue(c.Inside(-1.416, -0.0699), "Inside(-1.415, -0.0599)");
        }

        [TestMethod]
        public void ColliderSpeed()
        {
            Random r = new Random();
            Stopwatch sw1 = new Stopwatch();
            Collider c1=new Collider(4, 1, 3, 0);
            sw1.Start();
            for(int i=0;i<100000000;i++)
            {
                c1.Inside((0.5 - r.NextDouble()) * 100, (0.5 - r.NextDouble()) * 100);
            }
            sw1.Stop();

            Stopwatch sw2 = new Stopwatch();
            Collider2 c2 = new Collider2(4, 1, 3, 0);
            sw2.Start();
            for (int i = 0; i < 100000000; i++)
            {
                c2.Inside((0.5-r.NextDouble()) * 100, (0.5 - r.NextDouble()) * 100);
            }
            sw2.Stop();

        }

        CameraProjection CameraProjection()
        {
            Intrinsics intrinsic = new Intrinsics(1024, 1024, 1, 1);
            return new CameraProjection(intrinsic, intrinsic.Inverse(), System.Windows.Media.Media3D.Matrix3D.Identity, System.Windows.Media.Media3D.Matrix3D.Identity);
        }

        bool eq(double v1, double v2, double maxDiff = 0.000001)
        {
            return Math.Abs(v1 - v2) < maxDiff;
        }

        [TestMethod]
        public void CameraProjection2Test()
        {
            var c = CameraProjection();

            // orientace kamery smerem dolu a horejsek na sever
            c.SetOrientation(Conversions.CameraToWordTransform(0, 0, 0, new System.Windows.Media.Media3D.Vector3D(0, 0, 0)));
            /*
            double xc = 0, yc = 0;
            bool r;
            r = c.Transform(0, 0, 1, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 100 && yc == 50, "Transform(0, 0, 1) -> [true, 100, 50]");

            r = c.Transform(0, 0, 2, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 100 && yc == 50, "Transform(0, 0, 2) -> [true, 100, 50]");

            r = c.Transform(2, 0, 1, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 102 && yc == 50, "Transform(2, 0, 1) -> [true, 102, 50]");

            r = c.Transform(2, 0, 2, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 101 && yc == 50, "Transform(2, 0, 2) -> [true, 101, 50]");

            r = c.Transform(0, 2, 1, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 100 && yc == 52, "Transform(0, 2, 1) -> [true, 100, 52]");

            r = c.Transform(0, 2, 2, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 100 && yc == 51, "Transform(0, 2, 2) -> [true, 100, 51]");

            r = c.Transform(0, 0, -1, false, ref xc, ref yc);
            Assert.IsTrue(!r, "Transform(0, 0, -1) -> [false]");

            // orientace kamery - ve smeru klesani y - na jih a vodorovne
            c.SetOrientation(Conversions.CameraToWordTransform(0, Math.PI / 2, 0, new System.Windows.Media.Media3D.Vector3D(0, 0, 0)));


            r = c.Transform(0, -1, 0, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 100 && yc == 50, "Transform(0, -1, 0) -> [true, 100, 50]");

            r = c.Transform(2, -1, 0, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 102 && yc == 50, "Transform(2, -1, 0) -> [true, 102, 50]");

            r = c.Transform(0, -1, 2, false, ref xc, ref yc);
            Assert.IsTrue(r && eq(xc, 100) && eq(yc, 52), "Transform(1, 0, 2) -> [true, 100, 52]");

            // orientace kamery ve smeru rustu y 
            c.SetOrientation(Conversions.CameraToWordTransform(0, -Math.PI / 2, 0, new System.Windows.Media.Media3D.Vector3D(0, 0, 0)));

            r = c.Transform(0, 1, 0, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 100 && yc == 50, "Transform(0, 1, 0) -> [true, 100, 50]");

            r = c.Transform(2, 1, 0, false, ref xc, ref yc);
            Assert.IsTrue(r && xc == 102 && yc == 50, "Transform(2, 1, 0) -> [true, 102, 50]");

            r = c.Transform(0, 1, 2, false, ref xc, ref yc);
            Assert.IsTrue(r && eq(xc, 100) && eq(yc, 48), "Transform(1, 0, 2) -> [true, 100, 48]");





            // orientace kamery ve smeru rustu y 
            c.SetOrientation(Conversions.CameraToWordTransform(-Math.PI / 2, 0, 0, new System.Windows.Media.Media3D.Vector3D(0, 0, 0)));

            r = c.Transform(-1, 0, 0, false, ref xc, ref yc);
            Assert.IsTrue(r && eq(xc, 100) && eq(yc, 50), "Transform(1, 0, 0) -> [true, 100, 50]");

            r = c.Transform(-1, 0, 2, false, ref xc, ref yc);
            Assert.IsTrue(r && eq(xc, 102) && eq(yc, 50), "Transform(2, -1, 0) -> [true, 102, 50]");

            r = c.Transform(-1, 2, 0, false, ref xc, ref yc);
            Assert.IsTrue(r && eq(xc, 100) && eq(yc, 52), "Transform(-1, 2, 0) -> [true, 100, 52]");

    */
            Assert.Fail("Not implemented");
        }

        void KDTreeAdd(KDTree<Point2D> t, List<Point2D> l, Point2D p)
        {
            t.AddPoint(new double[] { p.X, p.Y }, p);
            l.Add(p);
        }

        void KDTreeNearest(KDTree<Point2D> t, List<Point2D> l, double x, double y, double d)
        {
            var l1 = t.NearestNeighbors(new double[] { x, y }, l.Count, d).ToList();
            var ll = l.Select(p => new { Dist2 = Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2), Point = p }).ToList();
            var l2 = ll.Where(i => i.Dist2 < d * d).OrderBy(i => i.Dist2).Select(i => i.Point).ToList();

            Assert.IsTrue(l1.Count == l2.Count, "Diferent lengths");

            for (int i = 0; i < l1.Count; i++)
            {
                Assert.IsTrue(l1[i].X == l2[i].X && l1[i].Y == l2[i].Y, "Diferent point");
            }
        }


        [TestMethod]
        public void KDTreeTest()
        {
            Random r = new Random(1);
            KDTree<Point2D> t = new KDTree<Point2D>(2);
            List<Point2D> l = new List<Point2D>();

            for (int i = 0; i < 100000; i++)
            {
                if (i % 1000 == 0)
                {
                    t = new KDTree<Point2D>(2);
                    l = new List<Point2D>();
                }
                KDTreeAdd(t, l, new Point2D(r.NextDouble(), r.NextDouble()));
                KDTreeNearest(t, l, r.NextDouble(), r.NextDouble(), r.NextDouble());
            }
        }

        Matrix Avg(Matrix m)
        {
            Matrix a = new Matrix(1, m.NoCols);

            for(int r=0;r<m.NoRows;r++)
            {
                for (int c = 0; c < m.NoCols; c++)
                    a[0, c] += m[r, c];
            }
            for (int c = 0; c < m.NoCols; c++)
                a[0, c]/= m.NoRows;
            return a;
        }

        Matrix Diff(Matrix m, Matrix a)
        {
            Matrix ret = new Matrix(m.NoRows, m.NoCols);

            for (int r = 0; r < m.NoRows; r++)
            {
                for (int c = 0; c < m.NoCols; c++)
                    ret[r, c] = m[r,c]-a[0, c];
            }
            return ret;
        }


        Matrix<double> ToMat(Point4D p)
        {
            var ret = Matrix<double>.Build.DenseOfArray(new double[1, 3] { { p.X, p.Y, p.Z } });
            return ret;
        }



        [TestMethod]
        public void PerformanceArrayTest()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            List<double[]> l = new List<double[]>(1000000);
            for(int i=0;i<1000000;i++)
            {
                l.Add(new double[3]);
            }
            sw.Stop();
            Debug.WriteLine(sw.Elapsed);
        }


        Matrix<double> TransformPair(KabschUmeyama ku, KabschUmeyama.Pair p)
        {
            return ku.Scale * ku.Rotation * p.B + ku.Translation;
        }

        [TestMethod]
        public void KabschUmeyamaTest()
        {
            List<Point4D> a = new List<Point4D>();
            a.Add(new Point4D()
            {
                X=0,
                Y=0,
                Z=0,
                A=1
            });
            a.Add(new Point4D()
            {
                X = 1,
                Y = 0,
                Z = 0,
                A = 1
            });
            a.Add(new Point4D()
            {
                X = 1,
                Y = 1,
                Z = 0,
                A = 1
            });
            a.Add(new Point4D()
            {
                X = 0,
                Y = 1,
                Z = 0,
                A = 1
            });

            var l = a.Select(i => new KabschUmeyama.Pair() { A = ToMat(i).Transpose(), B = ToMat(i * 2 + new Point4D() { X = 1 }).Transpose() });

            var ku = new KabschUmeyama();
            ku.Process(l);

            Assert.AreEqual(.5, ku.Scale);
            Assert.AreEqual(-.5, ku.Translation[0, 0]);
            Assert.AreEqual(0, ku.Translation[1, 0]);
            Assert.AreEqual(0, ku.Translation[2, 0]);

            Assert.AreEqual(1, ku.Rotation[0, 0]);
            Assert.AreEqual(0, ku.Rotation[1, 0]);
            Assert.AreEqual(0, ku.Rotation[2, 0]);
            Assert.AreEqual(0, ku.Rotation[0, 1]);
            Assert.AreEqual(1, ku.Rotation[1, 1]);
            Assert.AreEqual(0, ku.Rotation[2, 1]);
            Assert.AreEqual(0, ku.Rotation[0, 2]);
            Assert.AreEqual(0, ku.Rotation[1, 2]);
            Assert.AreEqual(1, ku.Rotation[2, 2]);

            l = a.Select(i => new KabschUmeyama.Pair() { A = ToMat(i).Transpose(), B = ToMat(i + new Point4D() { X = 1 }).Transpose() });

            ku.Process(l);

            Assert.AreEqual(1, ku.Scale);
            Assert.AreEqual(-1, ku.Translation[0, 0]);
            Assert.AreEqual(0, ku.Translation[1, 0]);
            Assert.AreEqual(0, ku.Translation[2, 0]);

            Assert.AreEqual(1, ku.Rotation[0, 0]);
            Assert.AreEqual(0, ku.Rotation[1, 0]);
            Assert.AreEqual(0, ku.Rotation[2, 0]);
            Assert.AreEqual(0, ku.Rotation[0, 1]);
            Assert.AreEqual(1, ku.Rotation[1, 1]);
            Assert.AreEqual(0, ku.Rotation[2, 1]);
            Assert.AreEqual(0, ku.Rotation[0, 2]);
            Assert.AreEqual(0, ku.Rotation[1, 2]);
            Assert.AreEqual(1, ku.Rotation[2, 2]);

            var m = Matrix3D.Identity;
            m.Rotate(new Quaternion(new Vector3D(0, 0, 1), 90));

            l = a.Select(i => new KabschUmeyama.Pair() { A = ToMat(i).Transpose(), B = ToMat(i.Trasform(m) + new Point4D() { X = 1 }).Transpose() });

            ku.Process(l);

            foreach(var p in l)
            {
                var t = TransformPair(ku, p);
                Assert.AreEqual(p.A, t);
            }
        }
    }
}
