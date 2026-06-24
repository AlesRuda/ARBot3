using System;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Tests
{
    public class TransformationTest
    {
        private const double Tol = 1e-9;

        private static void AssertEcef(ECEF expected, ECEF actual, string message = null)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tol), message);
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tol), message);
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Tol), message);
        }

        /// <summary>
        /// Vychozi transformace ma zvetseni 1 a je identita.
        /// </summary>
        [Test]
        public void Transform_Default_IsIdentityWithUnitScale()
        {
            var t = new Transformation();
            Assert.That(t.Scale, Is.EqualTo(1).Within(Tol));

            AssertEcef(new ECEF { X = 1 }, t.Transform(new ECEF { X = 1 }));
            AssertEcef(new ECEF { Y = 1 }, t.Transform(new ECEF { Y = 1 }));
            AssertEcef(new ECEF { Z = 1 }, t.Transform(new ECEF { Z = 1 }));
        }

        /// <summary>
        /// Nastaveni zvetseni na 2 zdvojnasobi delku vektoru ve vsech osach.
        /// </summary>
        [Test]
        public void Transform_ScaleTwo_DoublesVectors()
        {
            var t = new Transformation { };
            t.Scale = 2;
            Assert.That(t.Scale, Is.EqualTo(2).Within(Tol));

            AssertEcef(new ECEF { X = 2 }, t.Transform(new ECEF { X = 1 }));
            AssertEcef(new ECEF { Y = 2 }, t.Transform(new ECEF { Y = 1 }));
            AssertEcef(new ECEF { Z = 2 }, t.Transform(new ECEF { Z = 1 }));
        }

        /// <summary>
        /// Move postupne posouva pocatek v jednotlivych osach.
        /// </summary>
        [Test]
        public void Transform_Move_TranslatesVectors()
        {
            var t = new Transformation();

            t.Move(1, 0, 0);
            AssertEcef(new ECEF { X = 2 }, t.Transform(new ECEF { X = 1 }));

            t.Move(-1, 1, 0);
            AssertEcef(new ECEF { Y = 2 }, t.Transform(new ECEF { Y = 1 }));

            t.Move(0, -1, 1);
            AssertEcef(new ECEF { Z = 2 }, t.Transform(new ECEF { Z = 1 }));
        }

        /// <summary>
        /// Rotace o 90 stupnu kolem osy Z: X -> Y, Y -> -X, Z beze zmeny.
        /// </summary>
        [Test]
        public void Transform_RotateZ90_RotatesAxes()
        {
            var t = new Transformation();
            t.RotateZ(Math.PI / 2);

            AssertEcef(new ECEF { Y = 1 }, t.Transform(new ECEF { X = 1 }), "X -> Y");
            AssertEcef(new ECEF { X = -1 }, t.Transform(new ECEF { Y = 1 }), "Y -> -X");
            AssertEcef(new ECEF { Z = 1 }, t.Transform(new ECEF { Z = 1 }), "Z -> Z");
        }

        /// <summary>
        /// Rotace o 90 stupnu kolem osy X: X beze zmeny, Y -> Z, Z -> -Y.
        /// </summary>
        [Test]
        public void Transform_RotateX90_RotatesAxes()
        {
            var t = new Transformation();
            t.RotateX(Math.PI / 2);

            AssertEcef(new ECEF { X = 1 }, t.Transform(new ECEF { X = 1 }), "X -> X");
            AssertEcef(new ECEF { Z = 1 }, t.Transform(new ECEF { Y = 1 }), "Y -> Z");
            AssertEcef(new ECEF { Y = -1 }, t.Transform(new ECEF { Z = 1 }), "Z -> -Y");
        }

        /// <summary>
        /// Rotace o 90 stupnu kolem osy Y: X -> -Z, Y beze zmeny, Z -> X.
        /// </summary>
        [Test]
        public void Transform_RotateY90_RotatesAxes()
        {
            var t = new Transformation();
            t.RotateY(Math.PI / 2);

            AssertEcef(new ECEF { Z = -1 }, t.Transform(new ECEF { X = 1 }), "X -> -Z");
            AssertEcef(new ECEF { Y = 1 }, t.Transform(new ECEF { Y = 1 }), "Y -> Y");
            AssertEcef(new ECEF { X = 1 }, t.Transform(new ECEF { Z = 1 }), "Z -> X");
        }

        /// <summary>
        /// Rotate(ecef) zarovna dany bod do osy X (a zpetna varianta osu X do bodu).
        /// </summary>
        [Test]
        public void Transform_RotateByEcef_AlignsPointWithXAxis()
        {
            var x1 = new ECEF { X = 1 };

            var e = new ECEF { X = 1, Y = 1, Z = 0.5 };
            var t = new Transformation();
            t.Rotate(e, false);
            var unit = e * (1 / e.Radius);
            AssertEcef(x1, t.Transform(unit), "bod -> osa X");

            e = new ECEF { X = 1, Y = 1, Z = 0.5 };
            t = new Transformation();
            t.Rotate(e, true);
            unit = e * (1 / e.Radius);
            AssertEcef(unit, t.Transform(x1), "osa X -> bod");
        }

        /// <summary>
        /// Transformace z LLA: identita a pootoceni doprava/doleva o 90 stupnu delky.
        /// </summary>
        [Test]
        public void Transform_FromLLA_RotatesByLongitude()
        {
            var x1 = new ECEF { X = 1 };
            var y1 = new ECEF { Y = 1 };

            var t = new Transformation(new LLA(0, 0), true);
            AssertEcef(x1, t.Transform(x1), "LLA identita");

            t = new Transformation(new LLA(0, 90.0 / 180.0 * Math.PI), true);
            AssertEcef(y1, t.Transform(x1), "LLA pootoceni doprava");

            t = new Transformation(new LLA(0, 90.0 / 180.0 * Math.PI), false);
            AssertEcef(x1, t.Transform(y1), "LLA pootoceni doleva");
        }

        /// <summary>
        /// Prevod LLA -> ECEF -> LLA zachova souradnice.
        /// </summary>
        [Test]
        public void Coordinates_LlaEcefRoundTrip()
        {
            var lla = new LLA(Conversions.Deg2Rad(50), Conversions.Deg2Rad(15), 100);
            var e = new ECEF(Ellipsoid.Sphere, lla);
            var lla1 = new LLA(Ellipsoid.Sphere, e);

            Assert.That(lla1.Latitude, Is.EqualTo(lla.Latitude).Within(1e-6));
            Assert.That(lla1.Longitude, Is.EqualTo(lla.Longitude).Within(1e-6));
            Assert.That(lla1.Altitude, Is.EqualTo(lla.Altitude).Within(1e-3));
        }
    }
}
