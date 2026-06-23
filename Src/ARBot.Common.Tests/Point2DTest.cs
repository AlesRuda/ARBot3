using ARBot.Common;
using ARBot.Common.Common;

namespace ARBot.Common.Tests
{
    public class Point2DTest
    {
        /// <summary>
        /// Bod vlevo od primky (zadane dvema body p1 -> p2) ma IsLeft > 0.
        /// </summary>
        [Test]
        public void IsLeft_PointLeftOfLineByTwoPoints_ReturnsPositive()
        {
            Assert.That(new Point2D(-1, 1).IsLeft(new Point2D(-1, -1), new Point2D(1, 1)) > 0, Is.True);
        }

        /// <summary>
        /// Bod vlevo od primky (zadane jako Line2D) ma IsLeft > 0.
        /// </summary>
        [Test]
        public void IsLeft_PointLeftOfLine2D_ReturnsPositive()
        {
            Assert.That(new Point2D(-1, 1).IsLeft(new Line2D(new Point2D(-1, -1), new Point2D(1, 1))) > 0, Is.True);
        }

        /// <summary>
        /// Bod vpravo od primky (zadane dvema body p1 -> p2) ma IsLeft &lt; 0.
        /// </summary>
        [Test]
        public void IsLeft_PointRightOfLineByTwoPoints_ReturnsNegative()
        {
            Assert.That(new Point2D(1, -1).IsLeft(new Point2D(-1, -1), new Point2D(1, 1)) < 0, Is.True);
        }

        /// <summary>
        /// Bod vpravo od primky (zadane jako Line2D) ma IsLeft &lt; 0.
        /// </summary>
        [Test]
        public void IsLeft_PointRightOfLine2D_ReturnsNegative()
        {
            Assert.That(new Point2D(1, -1).IsLeft(new Line2D(new Point2D(-1, -1), new Point2D(1, 1))) < 0, Is.True);
        }

        /// <summary>
        /// Otoceni orientace primky (p1 -> p2 zameneno) obraci znamenko: tentyz bod je nyni vpravo, IsLeft &lt; 0.
        /// </summary>
        [Test]
        public void IsLeft_ReversedLineByTwoPoints_FlipsSign()
        {
            Assert.That(new Point2D(-1, 1).IsLeft(new Point2D(1, 1), new Point2D(-1, -1)) < 0, Is.True);
        }

        /// <summary>
        /// Otoceni orientace primky (zadane jako Line2D) obraci znamenko: tentyz bod je nyni vpravo, IsLeft &lt; 0.
        /// </summary>
        [Test]
        public void IsLeft_ReversedLine2D_FlipsSign()
        {
            Assert.That(new Point2D(-1, 1).IsLeft(new Line2D(new Point2D(1, 1), new Point2D(-1, -1))) < 0, Is.True);
        }

        /// <summary>
        /// Konstruktor z double slozky orizne na float a ulozi do X a Y.
        /// </summary>
        [Test]
        public void Constructor_FromDouble_SetsXY()
        {
            var p = new Point2D(1.5, -2.5);
            Assert.That(p.X, Is.EqualTo(1.5f));
            Assert.That(p.Y, Is.EqualTo(-2.5f));
        }

        /// <summary>
        /// Konstruktor z float slozky ulozi primo do X a Y.
        /// </summary>
        [Test]
        public void Constructor_FromFloat_SetsXY()
        {
            var p = new Point2D(3f, 4f);
            Assert.That(p.X, Is.EqualTo(3f));
            Assert.That(p.Y, Is.EqualTo(4f));
        }

        /// <summary>
        /// Konstruktor z matice [2,1] vezme X z m[0,0] a Y z m[1,0].
        /// </summary>
        [Test]
        public void Constructor_FromMatrix_TakesFirstColumn()
        {
            var m = new Matrix(new double[,] { { 7 }, { 8 } });
            var p = new Point2D(m);
            Assert.That(p.X, Is.EqualTo(7f));
            Assert.That(p.Y, Is.EqualTo(8f));
        }

        /// <summary>
        /// FromPolar prevede delku a uhel na kartezske souradnice (uhel 0 -> kladna osa X).
        /// </summary>
        [Test]
        public void FromPolar_ZeroAngle_PointsAlongX()
        {
            var p = Point2D.FromPolar(2f, 0f);
            Assert.That(p.X, Is.EqualTo(2f).Within(1e-5));
            Assert.That(p.Y, Is.EqualTo(0f).Within(1e-5));
        }

        /// <summary>
        /// FromPolar pro uhel PI/2 smeruje na kladnou osu Y.
        /// </summary>
        [Test]
        public void FromPolar_QuarterTurn_PointsAlongY()
        {
            var p = Point2D.FromPolar(2f, (float)(Math.PI / 2));
            Assert.That(p.X, Is.EqualTo(0f).Within(1e-5));
            Assert.That(p.Y, Is.EqualTo(2f).Within(1e-5));
        }

        /// <summary>
        /// Distance vraci vzdalenost od pocatku (3,4 -> 5).
        /// </summary>
        [Test]
        public void Distance_ReturnsDistanceFromOrigin()
        {
            Assert.That(new Point2D(3, 4).Distance, Is.EqualTo(5.0).Within(1e-9));
        }

        /// <summary>
        /// ToString formatuje slozky v invariantni kulture jako "X, Y".
        /// </summary>
        [Test]
        public void ToString_UsesInvariantCulture()
        {
            Assert.That(new Point2D(1.5, 2.5).ToString(), Is.EqualTo("1.5, 2.5"));
        }

        /// <summary>
        /// Operator + scita slozky po prvcich.
        /// </summary>
        [Test]
        public void OperatorAdd_AddsComponents()
        {
            var r = new Point2D(1, 2) + new Point2D(3, 4);
            Assert.That(r.X, Is.EqualTo(4f));
            Assert.That(r.Y, Is.EqualTo(6f));
        }

        /// <summary>
        /// Unarni operator - neguje obe slozky.
        /// </summary>
        [Test]
        public void OperatorNegate_NegatesComponents()
        {
            var r = -new Point2D(1, -2);
            Assert.That(r.X, Is.EqualTo(-1f));
            Assert.That(r.Y, Is.EqualTo(2f));
        }

        /// <summary>
        /// Operator / (double) deli obe slozky skalarem.
        /// </summary>
        [Test]
        public void OperatorDivide_ByDouble_DividesComponents()
        {
            var r = new Point2D(4, 6) / 2.0;
            Assert.That(r.X, Is.EqualTo(2f));
            Assert.That(r.Y, Is.EqualTo(3f));
        }

        /// <summary>
        /// Operator / (float) deli obe slozky skalarem.
        /// </summary>
        [Test]
        public void OperatorDivide_ByFloat_DividesComponents()
        {
            var r = new Point2D(4, 6) / 2f;
            Assert.That(r.X, Is.EqualTo(2f));
            Assert.That(r.Y, Is.EqualTo(3f));
        }

        /// <summary>
        /// Operator - mezi dvema body vraci Vector2D jejich rozdilu.
        /// </summary>
        [Test]
        public void OperatorSubtract_TwoPoints_ReturnsVector()
        {
            Vector2D v = new Point2D(5, 7) - new Point2D(1, 2);
            Assert.That(v.X, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(v.Y, Is.EqualTo(5.0).Within(1e-9));
        }

        /// <summary>
        /// Nasobeni matici 2x2: rotace o 90 stupnu prevede (1,0) na (0,1).
        /// </summary>
        [Test]
        public void OperatorMultiply_MatrixTimesPoint_Rotates()
        {
            var rot = new Matrix(new double[,] { { 0, -1 }, { 1, 0 } });
            var r = rot * new Point2D(1, 0);
            Assert.That(r.X, Is.EqualTo(0f).Within(1e-5));
            Assert.That(r.Y, Is.EqualTo(1f).Within(1e-5));
        }

        /// <summary>
        /// Equals vraci true pro body se stejnymi slozkami.
        /// </summary>
        [Test]
        public void Equals_SameComponents_ReturnsTrue()
        {
            Assert.That(new Point2D(1, 2).Equals(new Point2D(1, 2)), Is.True);
        }

        /// <summary>
        /// Equals vraci false pro body s odlisnymi slozkami.
        /// </summary>
        [Test]
        public void Equals_DifferentComponents_ReturnsFalse()
        {
            Assert.That(new Point2D(1, 2).Equals(new Point2D(1, 3)), Is.False);
        }

        /// <summary>
        /// Equals vraci false pro objekt jineho typu.
        /// </summary>
        [Test]
        public void Equals_DifferentType_ReturnsFalse()
        {
            Assert.That(new Point2D(1, 2).Equals("neco"), Is.False);
        }

        /// <summary>
        /// Typove Equals(Point2D) vraci true pro shodne slozky.
        /// </summary>
        [Test]
        public void EqualsTyped_SameComponents_ReturnsTrue()
        {
            Assert.That(new Point2D(1, 2).Equals(new Point2D(1, 2)), Is.True);
        }

        /// <summary>
        /// Typove Equals(Point2D) vraci false pro odlisne slozky.
        /// </summary>
        [Test]
        public void EqualsTyped_DifferentComponents_ReturnsFalse()
        {
            Assert.That(new Point2D(1, 2).Equals(new Point2D(2, 1)), Is.False);
        }

        /// <summary>
        /// Operator == vraci true pro body se stejnymi slozkami.
        /// </summary>
        [Test]
        public void OperatorEquals_SameComponents_ReturnsTrue()
        {
            Assert.That(new Point2D(1, 2) == new Point2D(1, 2), Is.True);
        }

        /// <summary>
        /// Operator != vraci true pro body s odlisnymi slozkami.
        /// </summary>
        [Test]
        public void OperatorNotEquals_DifferentComponents_ReturnsTrue()
        {
            Assert.That(new Point2D(1, 2) != new Point2D(1, 3), Is.True);
        }

        /// <summary>
        /// Shodne body maji shodny hash kod.
        /// </summary>
        [Test]
        public void GetHashCode_EqualPoints_SameHash()
        {
            Assert.That(new Point2D(1, 2).GetHashCode(), Is.EqualTo(new Point2D(1, 2).GetHashCode()));
        }

        /// <summary>
        /// Bod uvnitr ctverce ma nenulove winding number a IsInPoly vraci true.
        /// </summary>
        [Test]
        public void PointInsidePolygon_IsInPoly_True()
        {
            var poly = new List<Point2D>
            {
                new Point2D(0, 0), new Point2D(2, 0), new Point2D(2, 2), new Point2D(0, 2)
            };
            var p = new Point2D(1, 1);
            Assert.That(p.WindingNumberPoly(poly), Is.Not.EqualTo(0));
            Assert.That(p.IsInPoly(poly), Is.True);
        }

        /// <summary>
        /// Bod mimo ctverec ma nulove winding number a IsInPoly vraci false.
        /// </summary>
        [Test]
        public void PointOutsidePolygon_IsInPoly_False()
        {
            var poly = new List<Point2D>
            {
                new Point2D(0, 0), new Point2D(2, 0), new Point2D(2, 2), new Point2D(0, 2)
            };
            var p = new Point2D(5, 5);
            Assert.That(p.WindingNumberPoly(poly), Is.EqualTo(0));
            Assert.That(p.IsInPoly(poly), Is.False);
        }
    }
}
