using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    [TestFixture]
    public class GeoReferenceTests
    {
        // Praha-ish pocatek
        private static GeoReference Ref() => GeoReference.FromDegrees(50.0, 14.0);

        [Test]
        public void Origin_MapsToZero()
        {
            var r = Ref();
            var p = r.ToLocal(r.Origin);
            Assert.That(p.X, Is.EqualTo(0.0).Within(1e-6));
            Assert.That(p.Y, Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void PointToNorth_HasPositiveYZeroX()
        {
            var r = Ref();
            var p = r.ToLocal(new LLA(Conversions.Deg2Rad(50.001), Conversions.Deg2Rad(14.0)));
            Assert.That(p.Y, Is.GreaterThan(0));
            Assert.That(p.X, Is.EqualTo(0.0).Within(0.01));
            // 0.001 deg zem. sirky ~ 111 m
            Assert.That(p.Y, Is.EqualTo(111.0).Within(2.0));
        }

        [Test]
        public void PointToEast_HasPositiveXZeroY()
        {
            var r = Ref();
            var p = r.ToLocal(new LLA(Conversions.Deg2Rad(50.0), Conversions.Deg2Rad(14.001)));
            Assert.That(p.X, Is.GreaterThan(0));
            Assert.That(p.Y, Is.EqualTo(0.0).Within(0.01));
        }

        [Test]
        public void RoundTrip_LocalToLlaAndBack()
        {
            var r = Ref();
            var orig = new LLA(Conversions.Deg2Rad(50.0005), Conversions.Deg2Rad(14.0007));
            var local = r.ToLocal(orig);
            var back = r.ToLLA(local.X, local.Y);
            Assert.That(back.Latitude, Is.EqualTo(orig.Latitude).Within(1e-9));
            Assert.That(back.Longitude, Is.EqualTo(orig.Longitude).Within(1e-9));
        }

        [Test]
        public void LocalDistance_MatchesGreatCircle()
        {
            var r = Ref();
            var p = new LLA(Conversions.Deg2Rad(50.002), Conversions.Deg2Rad(14.003));
            var local = r.ToLocal(p);
            double planar = local.Distance;                         // sqrt(X^2+Y^2)
            double gc = r.Origin.Distance(Ellipsoid.Wgs84, p);      // po povrchu
            // Obojí uz pocita na WGS84 (GeoReference pres ECEF, Distance pres geodetiku),
            // takze se musi shodnout radove na milimetry - ne jen "do promile" jako drive,
            // kdy Distance jela na kouli o rovnikovem polomeru. Viz GreatCircle.
            Assert.That(planar, Is.EqualTo(gc).Within(0.001));
        }
    }
}
