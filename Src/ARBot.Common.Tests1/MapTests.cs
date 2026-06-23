using System;
using System.Resources;
using AForge.Math;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTests
{
    [TestClass]
    public class MapTests
    {
        [TestMethod]
        public void MapWayTest()
        {
            MapWay mw = new MapWay();
            mw.Start = new MapPoint() { Position = new ECEF() { X = 2, Y = 0, Z = 0 } };
            mw.End = new MapPoint() { Position = new ECEF() { X = 10, Y = 0, Z = 0 } };

            double pos;
            ECEF i = mw.Intersect(new ECEF() { X = 5, Y = 1, Z = 0 }, out pos);

            Assert.AreEqual(new ECEF() { X = 5, Y = 0, Z = 0 }, i, "MapWay.Intersect");
            Assert.AreEqual(0.375, pos, "MapWay.Intersect.Pos");
        }
        [TestMethod]
        public void MapTest()
        {
            Map m = new Map("test.osm", 10000, 10000, true);
            LLA reference = m.Points.Min;
            Transformation t = new Transformation(reference, false);
            Transformation rt = new Transformation(reference, true);
            m.Init(t);

            m.CalculateDistances(t.Transform(new ECEF(Ellipsoid.Sphere, new LLA(Conversions.Deg2Rad(52), Conversions.Deg2Rad(15)))));

            LLA r_lla = new LLA(Conversions.Deg2Rad(49.5), Conversions.Deg2Rad(15.2));
            ECEF r = t.Transform(new ECEF(Ellipsoid.Sphere, r_lla));
            ECEF r1 = m.GetNearestPoint(r, false);
            LLA r1_LLA = new LLA(Ellipsoid.Sphere, rt.Transform(r1));

            MapWay mw = null;
            MapPoint mp = null;
            double? pd = null, wd = null;
            m.GetMapNextPoint(r, ref mp, ref mw, ref pd, ref wd);

            LLA r2_lla = new LLA(Conversions.Deg2Rad(50), Conversions.Deg2Rad(14.6));
            ECEF r2 = t.Transform(new ECEF(Ellipsoid.Sphere, r2_lla));
            m.GetMapNextPoint(r2, ref mp, ref mw, ref pd, ref wd);


            Complex[,] c = new Complex[10, 10];

            DrawEngine de = new DrawEngine() { XMin = 0, YMin = 0, XMax = 9, YMax = 9, Clipping = true };

            de.PixelSetter = (x, y) =>
            {
                c[x, y].Re = 1;
            };
            m.Draw(de, -20000, 0, 10000);

        }
        [TestMethod]
        public void MapTest2()
        {
            Map m = new Map("test.osm", 10, 10, true);
            LLA reference = m.Points.Min;
            Transformation t = new Transformation(reference, false);
            m.Init(t);

            m.CalculateDistances(t.Transform(new ECEF(Ellipsoid.Sphere, new LLA(Conversions.Deg2Rad(51.5), Conversions.Deg2Rad(14.6)))));
            var cnt1 = m.Points.Count;
            m.CalculateDistances(t.Transform(new ECEF(Ellipsoid.Sphere, new LLA(Conversions.Deg2Rad(51.5), Conversions.Deg2Rad(14.6)))));
            var cnt2 = m.Points.Count;

            Assert.AreEqual(cnt1, cnt2, "Pokud dvatkat necham spocitat CalculateDistances ke stejnemu bodu uprosted cesty tak se docany bod prida jen jednou.");

        }
        [TestMethod]
        public void MapTest3()
        {
            Map m = new Map("modrany_small.osm", 10, 10, true);
            LLA reference = new LLA(Conversions.Deg2Rad(50.0233405), Conversions.Deg2Rad(14.4015709));
            Transformation t = new Transformation(reference, false);
            m.Init(t);

            ECEF p1;
            m.CalculateDistances(p1=t.Transform(new ECEF(Ellipsoid.Sphere, new LLA(Conversions.Deg2Rad(50.0185250491), Conversions.Deg2Rad(14.3999031186)))));

            var p=new ECEF() { X = Ellipsoid.Sphere.SemiMajorAxis, Y = -136, Z = -664 };

            MapPoint mp = null;
            MapWay mw = null;
            double? npDist=0, nwDist=0;

            var target = m.GetMapNextPoint(p1, ref mp, ref mw, ref npDist, ref nwDist);
            target =m.GetMapNextPoint(p, ref mp, ref mw, ref npDist, ref nwDist);
            
            m.CalculateDistances(p1=t.Transform(new ECEF(Ellipsoid.Sphere, new LLA(Conversions.Deg2Rad(50.0140920281), Conversions.Deg2Rad(14.3977788091)))));
            mp = null;
            mw = null;
            target = m.GetMapNextPoint(p1, ref mp, ref mw, ref npDist, ref nwDist);
            target = m.GetMapNextPoint(p, ref mp, ref mw, ref npDist, ref nwDist);

        }
    }
}
