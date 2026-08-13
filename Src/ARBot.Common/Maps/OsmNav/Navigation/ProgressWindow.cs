using System;
using System.Collections.Generic;

namespace ARBot.Common.Maps.OsmNav.Navigation
{
    /// <summary>
    /// Klouzave okno postupu k cili: dvojice (ujeta draha, potencial φ) za poslednich
    /// <c>WindowM</c> metru jizdy.
    /// <para>
    /// Okno bezi proti <b>ujete draze</b>, ne proti casu - to je zamer: kdyz robot stoji,
    /// okno se neposouva a detektor "bloudim" se vubec neuplatni (od staní je detektor A).
    /// </para>
    /// <para>
    /// Okno je zamerne <b>nezavisle na hranach</b> - bloudeni se pozna prave pres jejich hranice.
    /// Viz doc/global-navigation-runtime.md.
    /// </para>
    /// </summary>
    public sealed class ProgressWindow
    {
        private readonly double windowM;
        private readonly Queue<(double Travelled, double Phi)> samples = new();

        /// <param name="windowM">Delka okna v ujete draze [m].</param>
        public ProgressWindow(double windowM)
        {
            if (windowM <= 0) throw new ArgumentOutOfRangeException(nameof(windowM));
            this.windowM = windowM;
        }

        /// <summary>Zahodi obsah okna (napr. pri zmene cile - stary potencial uz neplati).</summary>
        public void Reset() => samples.Clear();

        /// <summary>
        /// Prida vzorek. <paramref name="travelledM"/> je kumulativni ujeta draha, aby okno
        /// nezaviselo na tom, jak casto se vzorkuje.
        /// </summary>
        public void Add(double travelledM, double phi)
        {
            samples.Enqueue((travelledM, phi));

            // Necháme jeden vzorek starsi nez okno - z nej se pocita pokles pres cele okno.
            while (samples.Count > 2 && travelledM - Peek2ndTravelled() >= windowM)
                samples.Dequeue();
        }

        /// <summary>Ujeta draha druheho nejstarsiho vzorku (po pripadnem zahozeni nejstarsiho).</summary>
        private double Peek2ndTravelled()
        {
            int i = 0;
            foreach (var s in samples)
                if (i++ == 1) return s.Travelled;
            return double.PositiveInfinity;
        }

        /// <summary>
        /// Pokles potencialu pres okno [s]. Kladny = priblizeni k cili.
        /// </summary>
        /// <returns>false, dokud okno neni naplnene ujetou drahou.</returns>
        public bool TryGetDrop(out double dropSeconds)
        {
            dropSeconds = 0;
            if (samples.Count < 2) return false;

            (double Travelled, double Phi) oldest = default;
            (double Travelled, double Phi) newest = default;
            int i = 0;
            foreach (var s in samples)
            {
                if (i++ == 0) oldest = s;
                newest = s;
            }

            if (newest.Travelled - oldest.Travelled < windowM) return false;

            dropSeconds = oldest.Phi - newest.Phi;
            return true;
        }
    }
}
