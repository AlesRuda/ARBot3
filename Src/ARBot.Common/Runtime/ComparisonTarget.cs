using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Cil pro porovnani prehraneho behu s referencnim zaznamem. Konzumuje
    /// <see cref="RobotStateMsg"/> a per takt je porovnava s referenci s toleranci.
    /// Hlasi <see cref="MaxStateError"/> a prvni odchylku (<see cref="FirstDivergence"/>)
    /// pro lokalizaci rozdilu.
    /// </summary>
    public sealed class ComparisonTarget : MessageTarget
    {
        /// <summary>Popis prvni odchylky nad toleranci.</summary>
        public struct Divergence
        {
            public int Index;
            public DateTime Time;
            public string Field;
            public double Expected;
            public double Actual;
            public double Error;

            public override string ToString()
                => $"[{Index}] {Time:HH:mm:ss.fff} {Field}: ref={Expected:G6} act={Actual:G6} |err|={Error:G6}";
        }

        private readonly IReadOnlyList<RobotStateMsg> reference;
        private readonly double tol;
        private int idx;

        /// <param name="reference">Referencni sekvence RobotStateMsg (v poradi taktu).</param>
        /// <param name="tolerance">Tolerance na jednu velicinu.</param>
        public ComparisonTarget(IReadOnlyList<RobotStateMsg> reference, double tolerance = 1e-6)
            : base(OverflowPolicy.Block)
        {
            this.reference = reference ?? throw new ArgumentNullException(nameof(reference));
            tol = tolerance;
        }

        /// <summary>Maximalni odchylka jedne veliciny napric vsemi takty.</summary>
        public double MaxStateError { get; private set; }

        /// <summary>Prvni takt s odchylkou nad toleranci (null = zadna).</summary>
        public Divergence? FirstDivergence { get; private set; }

        /// <summary>Pocet porovnanych taktu.</summary>
        public int Compared => idx;

        /// <summary>Pocet referencnich taktu.</summary>
        public int ReferenceCount => reference.Count;

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (msg is not RobotStateMsg a) return;

            if (idx >= reference.Count)
            {
                // vic taktu nez v referenci
                Consider(idx, a.TimeStamp, "count", reference.Count, idx + 1, double.PositiveInfinity);
                idx++;
                return;
            }

            var r = reference[idx];
            CompareField(idx, a.TimeStamp, "X", r.X, a.X);
            CompareField(idx, a.TimeStamp, "Y", r.Y, a.Y);
            CompareField(idx, a.TimeStamp, "Theta", 0, Conversions.NormalizeOrientation(a.Theta - r.Theta), r.Theta, a.Theta);
            CompareField(idx, a.TimeStamp, "V", r.V, a.V);
            CompareField(idx, a.TimeStamp, "Omega", r.Omega, a.Omega);
            idx++;
        }

        private void CompareField(int i, DateTime t, string field, double expected, double actual)
        {
            double err = Math.Abs(actual - expected);
            Consider(i, t, field, expected, actual, err);
        }

        // varianta pro uhel: err uz spocteny (zabaleny), expected/actual jen pro report
        private void CompareField(int i, DateTime t, string field, double _, double wrappedErr, double expected, double actual)
        {
            double err = Math.Abs(wrappedErr);
            Consider(i, t, field, expected, actual, err);
        }

        private void Consider(int i, DateTime t, string field, double expected, double actual, double err)
        {
            if (err > MaxStateError) MaxStateError = err;
            if (err > tol && FirstDivergence == null)
            {
                FirstDivergence = new Divergence
                {
                    Index = i,
                    Time = t,
                    Field = field,
                    Expected = expected,
                    Actual = actual,
                    Error = err
                };
            }
        }
    }
}
