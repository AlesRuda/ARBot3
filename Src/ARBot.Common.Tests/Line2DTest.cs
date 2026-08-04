using System;
using System.Collections.Generic;
using ARBot.Common;
using ARBot.Common.Common;

namespace ARBot.Common.Tests
{
    public class Line2DTest
    {
        /// <summary>
        /// Vodorovna primka (0,0)->(10,0): A=0, B=10, uhel 0.
        /// </summary>
        [Test]
        public void Constructor_HorizontalLine_SetsCoefficientsAndAngle()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 0));

            Assert.That(l.A, Is.EqualTo(0));
            Assert.That(l.B, Is.EqualTo(10));
            Assert.That(l.Angle, Is.EqualTo(0).Within(1e-6));
        }

        /// <summary>
        /// Svisla primka smerem nahoru (0,0)->(0,10): A=-10, B=0, uhel PI/2.
        /// </summary>
        [Test]
        public void Constructor_VerticalLineUp_SetsCoefficientsAndAngle()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, 10));

            Assert.That(l.A, Is.EqualTo(-10));
            Assert.That(l.B, Is.EqualTo(0));
            Assert.That(l.Angle, Is.EqualTo(Math.PI / 2).Within(1e-6));
        }

        /// <summary>
        /// Svisla primka smerem dolu (0,0)->(0,-10): A=10, B=0, uhel -PI/2.
        /// </summary>
        [Test]
        public void Constructor_VerticalLineDown_SetsCoefficientsAndAngle()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, -10));

            Assert.That(l.A, Is.EqualTo(10));
            Assert.That(l.B, Is.EqualTo(0));
            Assert.That(l.Angle, Is.EqualTo(-Math.PI / 2).Within(1e-6));
        }

        /// <summary>
        /// Sikma primka (0,0)->(5,10): A=-10, B=5, uhel cca 64 stupnu.
        /// </summary>
        [Test]
        public void Constructor_DiagonalLine_SetsCoefficientsAndAngle()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(5, 10));

            Assert.That(l.A, Is.EqualTo(-10));
            Assert.That(l.B, Is.EqualTo(5));
            Assert.That(l.Angle, Is.EqualTo(Conversions.Deg2Rad(64)).Within(0.1));
        }

        /// <summary>
        /// Primka jdouci stredem kruznice ji protina v bode na kruznici (45 stupnu, r=1).
        /// </summary>
        [Test]
        public void CircleIntersect_LineThroughCenter_ReturnsPointOnCircle()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(10, 10));

            var p = l1.CircleIntersect(new Point2D(0, 0), 1);
            Assert.That(p[0].X, Is.EqualTo(Math.Sqrt(2) / 2).Within(1e-4));
            Assert.That(p[0].Y, Is.EqualTo(Math.Sqrt(2) / 2).Within(1e-4));
        }

        /// <summary>
        /// Secna protne kruznici ve dvou bodech (osa, r=10) -> (10,0) a (0,10).
        /// </summary>
        [Test]
        public void CircleIntersect_SecantLine_ReturnsTwoPoints()
        {
            var l1 = new Line2D(new Point2D(0, 10), new Point2D(10, 0));

            var p = l1.CircleIntersect(new Point2D(0, 0), 10);

            Assert.That(p.Length, Is.EqualTo(2));

            Assert.That(p[0].X, Is.EqualTo(10).Within(1e-3));
            Assert.That(p[0].Y, Is.EqualTo(0).Within(1e-3));

            Assert.That(p[1].X, Is.EqualTo(0).Within(1e-3));
            Assert.That(p[1].Y, Is.EqualTo(10).Within(1e-3));
        }

        /// <summary>
        /// Prusecik svisle a vodorovne primky jdoucich pocatkem je pocatek.
        /// </summary>
        [Test]
        public void Intersection_PerpendicularLinesThroughOrigin_ReturnsOrigin()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var l2 = new Line2D(new Point2D(0, 0), new Point2D(10, 0));

            var p = l1.Intersection(l2);
            Assert.That(p.X, Is.EqualTo(0).Within(1e-4));
            Assert.That(p.Y, Is.EqualTo(0).Within(1e-4));
        }

        /// <summary>
        /// Prusecik svisle primky x=0 a vodorovne primky y=2 je (0,2).
        /// </summary>
        [Test]
        public void Intersection_VerticalAndHorizontal_ReturnsCrossing()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var l2 = new Line2D(new Point2D(2, 2), new Point2D(10, 2));

            var p = l1.Intersection(l2);
            Assert.That(p.X, Is.EqualTo(0).Within(1e-4));
            Assert.That(p.Y, Is.EqualTo(2).Within(1e-4));
        }

        /// <summary>
        /// Prusecik primky x=0 s kolmici jdouci bodem (10,0) je pata kolmice (0,0).
        /// </summary>
        [Test]
        public void Intersection_WithPoint_ReturnsFootOfPerpendicular()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var p1 = new Point2D(10, 0);
            var p = l.ProjectOntoLine(p1);
            Assert.That(p.X, Is.EqualTo(0).Within(1e-4));
            Assert.That(p.Y, Is.EqualTo(0).Within(1e-4));
        }

        /// <summary>
        /// Pata kolmice z bodu (1,-1) na diagonalu y=x je pocatek.
        /// </summary>
        [Test]
        public void Intersection_WithPointOnDiagonal_ReturnsFootOfPerpendicular()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 10));
            var p1 = new Point2D(1, -1);
            var p = l.ProjectOntoLine(p1);
            Assert.That(p.X, Is.EqualTo(0).Within(1e-4));
            Assert.That(p.Y, Is.EqualTo(0).Within(1e-4));
        }

        /// <summary>
        /// Normala vodorovne primky miri vzhuru: (0,10).
        /// </summary>
        [Test]
        public void Normal_HorizontalLine_PointsUp()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 0));
            var n = l.Normal;
            Assert.That(n.X, Is.EqualTo(0).Within(1e-5));
            Assert.That(n.Y, Is.EqualTo(10).Within(1e-5));
        }

        /// <summary>
        /// Normala svisle primky miri vlevo: (-10,0).
        /// </summary>
        [Test]
        public void Normal_VerticalLine_PointsLeft()
        {
            var l = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var n = l.Normal;
            Assert.That(n.X, Is.EqualTo(-10).Within(1e-5));
            Assert.That(n.Y, Is.EqualTo(0).Within(1e-5));
        }

        /// <summary>
        /// Vzdalenost bodu (1,-1) od primky y=x je sqrt(2) bez ohledu na zpusob zadani primky.
        /// </summary>
        [Test]
        public void Distance_PointToLine_VariousConstructions_ReturnsPerpendicularDistance()
        {
            var p1 = new Point2D(1, -1);

            var l = new Line2D(new Point2D(0, 0), new Point2D(10, 10));
            Assert.That(l.Distance(p1), Is.EqualTo(Math.Sqrt(2)).Within(1e-5));

            l = new Line2D(new Point2D(0, 0), new Point2D(1, 1));
            Assert.That(l.Distance(p1), Is.EqualTo(Math.Sqrt(2)).Within(1e-5));

            l = new Line2D(1, -1, 0);
            Assert.That(l.Distance(p1), Is.EqualTo(Math.Sqrt(2)).Within(1e-5));

            l = new Line2D(10, -10, 0);
            Assert.That(l.Distance(p1), Is.EqualTo(Math.Sqrt(2)).Within(1e-5));
        }

        /// <summary>
        /// Vzdalenost dvou rovnobezek (posun o (1,0)) je sqrt(2)/2.
        /// </summary>
        [Test]
        public void Distance_BetweenParallelLines_ReturnsGap()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(10, 10));
            var l2 = new Line2D(new Point2D(1, 0), new Point2D(11, 10));
            Assert.That(l1.Distance(l2), Is.EqualTo(Math.Sqrt(2) / 2).Within(1e-5));
        }

        /// <summary>
        /// Vzdalenost pocatku od svisle primky x=10 je 10.
        /// </summary>
        [Test]
        public void Distance_PointToVerticalLine_ReturnsHorizontalGap()
        {
            var l = new Line2D(new Point2D(10, 0), new Point2D(10, 10));
            var p1 = new Point2D(0, 0);
            Assert.That(l.Distance(p1), Is.EqualTo(10).Within(1e-5));
        }

        /// <summary>
        /// Vzdalenost bodu od primky proalozene linearni regresi (cca 4.17).
        /// </summary>
        [Test]
        public void Distance_PointToRegressionLine_ReturnsApproxDistance()
        {
            var l = Line2D.LinearRegesion(new List<Point2D>
            {
                new Point2D(-6.06, 0), new Point2D(-4.2, 0.9), new Point2D(-2.1, 1.6)
            });
            var p1 = new Point2D(-6.2, 4.5);
            Assert.That(l.Distance(p1), Is.EqualTo(4.17).Within(0.1));
        }

        /// <summary>
        /// Rovnobezka zachova normalu (A, B) a je ve zadane vzdalenosti.
        /// </summary>
        [Test]
        public void Parallel_OffsetLine_KeepsDirectionAndDistance()
        {
            var l1 = new Line2D(new Point2D(0, 0), new Point2D(0, 10));
            var l2 = l1.Parallel(2);
            Assert.That(l2.A, Is.EqualTo(l1.A));
            Assert.That(l2.B, Is.EqualTo(l1.B));
            Assert.That(l1.Distance(l2), Is.EqualTo(2).Within(1e-5));
        }
    }
}
