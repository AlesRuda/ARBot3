using System;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Models;

namespace ARBot.Common.Tests
{
    public class ConversionsTest
    {
        /// <summary>
        /// Rad2Deg prevadi radiany na stupne (0, +-PI/2 -> 0, +-90).
        /// </summary>
        [Test]
        public void Rad2Deg_ConvertsRadiansToDegrees()
        {
            Assert.That(Conversions.Rad2Deg(0), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.Rad2Deg(Math.PI / 2), Is.EqualTo(90).Within(1e-9));
            Assert.That(Conversions.Rad2Deg(-Math.PI / 2), Is.EqualTo(-90).Within(1e-9));
        }

        /// <summary>
        /// Deg2Rad prevadi stupne na radiany (0, +-90 -> 0, +-PI/2).
        /// </summary>
        [Test]
        public void Deg2Rad_ConvertsDegreesToRadians()
        {
            Assert.That(Conversions.Deg2Rad(0), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.Deg2Rad(90), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(Conversions.Deg2Rad(-90), Is.EqualTo(-Math.PI / 2).Within(1e-9));
        }

        /// <summary>
        /// NormalizeOrientation zarovnava uhel do rozsahu +-PI (nasobky 2*PI se odecitaji).
        /// </summary>
        [Test]
        public void NormalizeOrientation_WrapsToPlusMinusPi()
        {
            Assert.That(Conversions.NormalizeOrientation(0), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizeOrientation(Math.PI / 2), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(Conversions.NormalizeOrientation(2 * Math.PI), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizeOrientation(2 * Math.PI + Math.PI / 2), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(Conversions.NormalizeOrientation(-Math.PI / 2), Is.EqualTo(-Math.PI / 2).Within(1e-9));
            Assert.That(Conversions.NormalizeOrientation(-2 * Math.PI), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizeOrientation(-2 * Math.PI - Math.PI / 2), Is.EqualTo(-Math.PI / 2).Within(1e-9));
        }

        /// <summary>
        /// NormalizeAzimut zarovnava azimut do rozsahu +-180 stupnu (-180 mapuje na 180).
        /// </summary>
        [Test]
        public void NormalizeAzimut_WrapsToPlusMinus180()
        {
            Assert.That(Conversions.NormalizeAzimut(0), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizeAzimut(180), Is.EqualTo(180).Within(1e-9));
            Assert.That(Conversions.NormalizeAzimut(-179), Is.EqualTo(-179).Within(1e-9));
            Assert.That(Conversions.NormalizeAzimut(-180), Is.EqualTo(180).Within(1e-9));
            Assert.That(Conversions.NormalizeAzimut(181), Is.EqualTo(-179).Within(1e-9));
        }

        /// <summary>
        /// NormalizeHalfOrientation zarovnava uhel do rozsahu +-PI/2 (smer bez orientace, PI ~ 0).
        /// </summary>
        [Test]
        public void NormalizeHalfOrientation_WrapsToPlusMinusHalfPi()
        {
            Assert.That(Conversions.NormalizeHalfOrientation(0), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizeHalfOrientation(Math.PI), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizeHalfOrientation(Math.PI / 4), Is.EqualTo(Math.PI / 4).Within(1e-9));
            Assert.That(Conversions.NormalizeHalfOrientation(-Math.PI / 4), Is.EqualTo(-Math.PI / 4).Within(1e-9));
            Assert.That(Conversions.NormalizeHalfOrientation(Math.PI + Math.PI / 4), Is.EqualTo(Math.PI / 4).Within(1e-9));
            Assert.That(Conversions.NormalizeHalfOrientation(Math.PI - Math.PI / 4), Is.EqualTo(-Math.PI / 4).Within(1e-9));
        }

        /// <summary>
        /// NormalizePrimaryOrientation otoci smer toHalf (o PI) tak, aby byl nejblize primary smeru.
        /// </summary>
        [Test]
        public void NormalizePrimaryOrientation_AlignsToPrimaryDirection()
        {
            Assert.That(Conversions.NormalizePrimaryOrientation(0, 0), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(0, Math.PI), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(0, Math.PI / 10), Is.EqualTo(Math.PI / 10).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(0, Math.PI + Math.PI / 10), Is.EqualTo(Math.PI / 10).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(Math.PI, Math.PI / 10), Is.EqualTo(-Math.PI + Math.PI / 10).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(Math.PI, Math.PI + Math.PI / 10), Is.EqualTo(-Math.PI + Math.PI / 10).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(Math.PI / 4, Math.PI / 10), Is.EqualTo(Math.PI / 10).Within(1e-9));
            Assert.That(Conversions.NormalizePrimaryOrientation(Math.PI / 4, Math.PI + Math.PI / 10), Is.EqualTo(Math.PI / 10).Within(1e-9));
        }

        /// <summary>
        /// Azimut2Orientation prevadi smer kompasu na matematicky smer (0 -> PI/2).
        /// </summary>
        [Test]
        public void Azimut2Orientation_ConvertsCompassToMath()
        {
            Assert.That(Conversions.Azimut2Orientation(0), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(Conversions.Azimut2Orientation(Math.PI / 2), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.Azimut2Orientation(-Math.PI / 2), Is.EqualTo(Math.PI).Within(1e-9));
        }

        /// <summary>
        /// Orientation2Azimut prevadi matematicky smer na smer kompasu (inverzni k Azimut2Orientation).
        /// </summary>
        [Test]
        public void Orientation2Azimut_ConvertsMathToCompass()
        {
            Assert.That(Conversions.Orientation2Azimut(0), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(Conversions.Orientation2Azimut(Math.PI / 2), Is.EqualTo(0).Within(1e-9));
            Assert.That(Conversions.Orientation2Azimut(-Math.PI / 2), Is.EqualTo(Math.PI).Within(1e-9));
        }

        /// <summary>
        /// Pomocna metoda: aplikace transformace z func na (normalizovany) from musi dat smer to.
        /// </summary>
        private static void AssertRotatesFromOntoTo(Vector3 from, Vector3 to, Func<Vector3, Vector3, Matrix4x4> func)
        {
            var f = Vector3.Normalize(from);
            var t = Vector3.Normalize(to);
            var r = func(f, t);

            var x = Vector3.Transform(f, r);
            Assert.That(x.X, Is.EqualTo(t.X).Within(1e-4));
            Assert.That(x.Y, Is.EqualTo(t.Y).Within(1e-4));
            Assert.That(x.Z, Is.EqualTo(t.Z).Within(1e-4));
        }

        /// <summary>
        /// VectoToVector i VectoToVectorRodrigues otoci vektor from do smeru to pro vsechny kombinace os.
        /// </summary>
        [Test]
        public void VectoToVector_RotatesFromOntoTo()
        {
            var cases = new (Vector3 f, Vector3 t)[]
            {
                (new Vector3(1, 0, 0), new Vector3(1, 0, 0)),
                (new Vector3(0, 1, 0), new Vector3(1, 0, 0)),
                (new Vector3(0, 0, 1), new Vector3(1, 0, 0)),
                (new Vector3(1, 0, 0), new Vector3(0, 1, 0)),
                (new Vector3(0, 1, 0), new Vector3(0, 1, 0)),
                (new Vector3(0, 0, 1), new Vector3(0, 1, 0)),
                (new Vector3(1, 0, 0), new Vector3(0, 0, 1)),
                (new Vector3(0, 1, 0), new Vector3(0, 0, 1)),
                (new Vector3(0, 0, 1), new Vector3(0, 0, 1)),
                (new Vector3(1, 0, 0), new Vector3(1, 1, 0)),
                (new Vector3(0, 1, 0), new Vector3(1, 1, 0)),
                (new Vector3(0, 0, 1), new Vector3(1, 1, 0)),
                (new Vector3(1, 0, 0), new Vector3(1, 1, 1)),
                (new Vector3(0, 1, 0), new Vector3(1, 1, 1)),
                (new Vector3(0, 0, 1), new Vector3(1, 1, 1)),
            };

            foreach (var (f, t) in cases)
                AssertRotatesFromOntoTo(f, t, Conversions.VectoToVector);
            foreach (var (f, t) in cases)
                AssertRotatesFromOntoTo(f, t, Conversions.VectoToVectorRodrigues);
        }

        /// <summary>
        /// Pomocna metoda: transformace z YawPitchRoll a zpetna rekonstrukce musi dat puvodni uhly.
        /// </summary>
        private static void AssertYprRoundTrip(float yaw, float pitch, float roll)
        {
            var ypr = new YawPitchRoll(yaw, pitch, roll);
            var t = Conversions.WorldToWorldTransform(ypr.Yaw, ypr.Pitch, ypr.Roll, new Vector3(0, 0, 0));
            var ypr1 = new YawPitchRoll(t);

            Assert.That(ypr1.Yaw, Is.EqualTo(ypr.Yaw).Within(1e-4));
            Assert.That(ypr1.Pitch, Is.EqualTo(ypr.Pitch).Within(1e-4));
            Assert.That(ypr1.Roll, Is.EqualTo(ypr.Roll).Within(1e-4));
        }

        /// <summary>
        /// WorldToWorldTransform -> YawPitchRoll(matice) je round-trip pro ruzne kombinace yaw/pitch/roll.
        /// </summary>
        [Test]
        public void WorldToWorldTransform_RoundTripsYawPitchRoll()
        {
            AssertYprRoundTrip(1, 0, 0);
            AssertYprRoundTrip(0, 1, 0);
            AssertYprRoundTrip(0, 0, 1);

            AssertYprRoundTrip(1, 1, 0);
            AssertYprRoundTrip(0, 1, 1);
            AssertYprRoundTrip(1, 0, 1);

            AssertYprRoundTrip(1, 1, 1);
        }
    }
}
