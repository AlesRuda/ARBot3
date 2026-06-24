using System;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Tests
{
    public class Vector2DTest
    {
        /// <summary>
        /// Konstruktor (x, y) ulozi slozky do X a Y.
        /// </summary>
        [Test]
        public void Constructor_FromXY_SetsComponents()
        {
            var v = new Vector2D(3, 4);
            Assert.That(v.X, Is.EqualTo(3.0));
            Assert.That(v.Y, Is.EqualTo(4.0));
        }

        /// <summary>
        /// Bezparametrovy konstruktor vytvori nulovy vektor.
        /// </summary>
        [Test]
        public void Constructor_Default_IsZeroVector()
        {
            var v = new Vector2D();
            Assert.That(v.X, Is.EqualTo(0.0));
            Assert.That(v.Y, Is.EqualTo(0.0));
        }

        /// <summary>
        /// Konstruktor z uhlu da jednotkovy vektor (cos, sin).
        /// </summary>
        [Test]
        public void Constructor_FromAngle_GivesUnitVector()
        {
            var v = new Vector2D(Math.PI / 4);
            Assert.That(v.X, Is.EqualTo(Math.Sqrt(2) / 2).Within(1e-9));
            Assert.That(v.Y, Is.EqualTo(Math.Sqrt(2) / 2).Within(1e-9));
            Assert.That(v.Length, Is.EqualTo(1.0).Within(1e-9));
        }

        /// <summary>
        /// Konstruktor z ECEF vezme X z ecef.Y (vychod) a Y z ecef.Z (sever).
        /// </summary>
        [Test]
        public void Constructor_FromEcef_TakesEastNorth()
        {
            var ecef = new ECEF { Y = 3, Z = 4 };
            var v = new Vector2D(ecef);
            Assert.That(v.X, Is.EqualTo(3.0));
            Assert.That(v.Y, Is.EqualTo(4.0));
        }

        /// <summary>
        /// Length vraci delku vektoru (3,4 -> 5).
        /// </summary>
        [Test]
        public void Length_ReturnsMagnitude()
        {
            Assert.That(new Vector2D(3, 4).Length, Is.EqualTo(5.0).Within(1e-9));
        }

        /// <summary>
        /// LengthSquerd vraci kvadrat delky (3,4 -> 25).
        /// </summary>
        [Test]
        public void LengthSquared_ReturnsSquaredMagnitude()
        {
            Assert.That(new Vector2D(3, 4).LengthSquerd, Is.EqualTo(25.0).Within(1e-9));
        }

        /// <summary>
        /// Angle vraci uhel vektoru v matematickem smyslu (atan2).
        /// </summary>
        [Test]
        public void Angle_ReturnsMathematicalAngle()
        {
            Assert.That(new Vector2D(1, 1).Angle, Is.EqualTo(Math.PI / 4).Within(1e-9));
            Assert.That(new Vector2D(0, 1).Angle, Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(new Vector2D(-1, 0).Angle, Is.EqualTo(Math.PI).Within(1e-9));
        }

        /// <summary>
        /// AngleBetween vraci orientovany uhel od this k cilovemu vektoru.
        /// </summary>
        [Test]
        public void AngleBetween_ReturnsSignedAngle()
        {
            Assert.That(new Vector2D(1, 0).AngleBetween(new Vector2D(0, 1)), Is.EqualTo(Math.PI / 2).Within(1e-9));
            Assert.That(new Vector2D(1, 0).AngleBetween(new Vector2D(1, 0)), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(new Vector2D(0, 1).AngleBetween(new Vector2D(1, 0)), Is.EqualTo(-Math.PI / 2).Within(1e-9));
        }

        /// <summary>
        /// Normal vraci levou normalu (-Y, X).
        /// </summary>
        [Test]
        public void Normal_ReturnsLeftNormal()
        {
            var n = new Vector2D(1, 0).Normal;
            Assert.That(n.X, Is.EqualTo(0.0));
            Assert.That(n.Y, Is.EqualTo(1.0));
        }

        /// <summary>
        /// ToString formatuje vektor jako "[X, Y]" v invariantni kulture (desetinna tecka).
        /// </summary>
        [Test]
        public void ToString_UsesInvariantCulture()
        {
            Assert.That(new Vector2D(1.5, 2.5).ToString(), Is.EqualTo("[1.5, 2.5]"));
        }

        /// <summary>
        /// Operator + scita dva vektory po slozkach.
        /// </summary>
        [Test]
        public void OperatorAdd_TwoVectors_AddsComponents()
        {
            var r = new Vector2D(1, 2) + new Vector2D(3, 4);
            Assert.That(r.X, Is.EqualTo(4.0));
            Assert.That(r.Y, Is.EqualTo(6.0));
        }

        /// <summary>
        /// Operator - odecita dva vektory po slozkach.
        /// </summary>
        [Test]
        public void OperatorSubtract_TwoVectors_SubtractsComponents()
        {
            var r = new Vector2D(5, 7) - new Vector2D(1, 2);
            Assert.That(r.X, Is.EqualTo(4.0));
            Assert.That(r.Y, Is.EqualTo(5.0));
        }

        /// <summary>
        /// Operator + (vektor + bod) i (bod + vektor) vraci posunuty Point2D.
        /// </summary>
        [Test]
        public void OperatorAdd_VectorAndPoint_ReturnsTranslatedPoint()
        {
            var fromVectorPoint = new Vector2D(1, 2) + new Point2D(3, 4);
            Assert.That(fromVectorPoint.X, Is.EqualTo(4f));
            Assert.That(fromVectorPoint.Y, Is.EqualTo(6f));

            var fromPointVector = new Point2D(3, 4) + new Vector2D(1, 2);
            Assert.That(fromPointVector.X, Is.EqualTo(4f));
            Assert.That(fromPointVector.Y, Is.EqualTo(6f));
        }

        /// <summary>
        /// Operator - (bod - vektor) vraci posunuty Point2D.
        /// </summary>
        [Test]
        public void OperatorSubtract_PointAndVector_ReturnsTranslatedPoint()
        {
            var r = new Point2D(5, 7) - new Vector2D(1, 2);
            Assert.That(r.X, Is.EqualTo(4f));
            Assert.That(r.Y, Is.EqualTo(5f));
        }

        /// <summary>
        /// Operator * mezi dvema vektory je skalarni soucin.
        /// </summary>
        [Test]
        public void OperatorMultiply_TwoVectors_ReturnsDotProduct()
        {
            double dot = new Vector2D(1, 2) * new Vector2D(3, 4);
            Assert.That(dot, Is.EqualTo(11.0).Within(1e-9));
        }

        /// <summary>
        /// Operator * se skalarem (z obou stran) nasobi obe slozky.
        /// </summary>
        [Test]
        public void OperatorMultiply_ByScalar_ScalesComponents()
        {
            var left = 2.0 * new Vector2D(1, 2);
            Assert.That(left.X, Is.EqualTo(2.0));
            Assert.That(left.Y, Is.EqualTo(4.0));

            var right = new Vector2D(1, 2) * 2.0;
            Assert.That(right.X, Is.EqualTo(2.0));
            Assert.That(right.Y, Is.EqualTo(4.0));
        }

        /// <summary>
        /// Operator / skalarem deli obe slozky.
        /// </summary>
        [Test]
        public void OperatorDivide_ByScalar_DividesComponents()
        {
            var r = new Vector2D(4, 6) / 2.0;
            Assert.That(r.X, Is.EqualTo(2.0));
            Assert.That(r.Y, Is.EqualTo(3.0));
        }

        /// <summary>
        /// Explicitni konverze na MathNet Matrix da sloupcovy vektor 2x1.
        /// </summary>
        [Test]
        public void ExplicitToMathNetMatrix_ReturnsColumnVector()
        {
            var m = (Matrix<double>)new Vector2D(3, 4);
            Assert.That(m.RowCount, Is.EqualTo(2));
            Assert.That(m.ColumnCount, Is.EqualTo(1));
            Assert.That(m[0, 0], Is.EqualTo(3.0));
            Assert.That(m[1, 0], Is.EqualTo(4.0));
        }
    }
}
