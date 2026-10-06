using System;

namespace ARBot.Common.Diagnostics
{
    /// <summary>Kolik robot ujel a jak dlouho byl v pohybu za nejaky usek casu.</summary>
    public readonly struct OdometerReading
    {
        public OdometerReading(double distanceM, double movingSec, bool truncated)
        {
            DistanceM = distanceM; MovingSec = movingSec; Truncated = truncated;
        }

        /// <summary>Ujeta draha [m].</summary>
        public double DistanceM { get; }

        /// <summary>Cas, kdy robot jel (|v| nad prahem) [s].</summary>
        public double MovingSec { get; }

        /// <summary>
        /// Zacatek useku je starsi nez nejstarsi kontrolni bod, ktery v historii ZBYL (starsi uz
        /// byly zahozeny) — draha je pak <b>podhodnocena</b>. Dotaz pred uplne prvni vzorek
        /// podhodnoceny neni: pred nim robot nic neujel.
        /// </summary>
        public bool Truncated { get; }
    }

    /// <summary>
    /// <b>Pocitadlo ujete drahy</b> pro webovy nahled: kolik metru robot v misi ujel, za jak dlouho
    /// v pohybu, a z toho prumerna rychlost. Vzniklo 6. 10. 2026 na prani autora — stranka do te
    /// doby ukazovala jen okamzitou rychlost v tabulce dole.
    ///
    /// <para><b>Draha je z ODOMETRICKE pozy</b> (<c>RobotStateMsg.OdomX/OdomY</c>, integral
    /// fuzovanych <c>v</c>, <c>ω</c>), ne z fuzovane polohy: ta pri korekci z GPS nebo z koridoru
    /// skace o metry (Robotour 19. 9. 2026: skoky 0,6–4 m) a kazdy skok by se pricetl jako ujeta
    /// draha. Odometricka poza korekce nevidi, takze pocita jen to, co robot opravdu ujel.</para>
    ///
    /// <para><b>Krok pod <see cref="MinStepM"/> se nepricita hned</b>, ale az se od posledniho
    /// zapocteneho bodu nastrada — jinak by se pri stani sumou nasobil sum rychlosti. Nespojitost
    /// (krok rychlejsi nez <see cref="MaxSpeed"/>, treba po restartu fuze) se nepocita vubec.</para>
    ///
    /// <para><b>Dráha „od zacatku mise"</b>: mise hlasi jen <c>Elapsed</c>, takze se drahu musi dat
    /// zjistit i zpetne, od casu <c>posledni − Elapsed</c>. Na to je <b>historie kontrolnich bodu</b>
    /// (kumulativni draha a cas v pohybu po <see cref="CheckpointSec"/>) s linearni interpolaci mezi
    /// nimi. Nezavisi to na tom, jestli se na stranku nekdo diva — zacatek mise se nemusi „chytit".</para>
    ///
    /// <para>⚠️ <b>Neni vlaknove bezpecne</b> — vola se pod zamkem vlastnika (<c>WebStatus.gate</c>).
    /// Cas je razitko zprav (<c>TimeBase</c>), tedy tytez hodiny jako <c>Elapsed</c> mise.</para>
    /// </summary>
    public sealed class Odometer
    {
        /// <summary>Kratsi posun od posledniho zapocteneho bodu se zatim nepricita [m].</summary>
        public const double MinStepM = 0.05;

        /// <summary>Od teto |v| se robot povazuje za jedouci [m/s].</summary>
        public const double MovingSpeed = 0.05;

        /// <summary>Rychlejsi posun nez tohle je nespojitost, ne jizda [m/s].</summary>
        public const double MaxSpeed = 5.0;

        /// <summary>Delsi mezera mezi vzorky se do casu v pohybu nepocita [s].</summary>
        public const double MaxGapSec = 1.0;

        /// <summary>Rozestup kontrolnich bodu historie [s].</summary>
        public const double CheckpointSec = 2.0;

        /// <summary>Kapacita historie — 21 600 bodu po 2 s = 12 h.</summary>
        public const int DefaultCapacity = 21600;

        private readonly struct Bod
        {
            public Bod(DateTime t, double d, double m) { T = t; D = d; M = m; }
            public readonly DateTime T;
            public readonly double D;
            public readonly double M;
        }

        private readonly Bod[] ring;
        private int start, count;

        /// <summary>Zahodila uz historie nejstarsi bod? Jen pak je dotaz do minulosti podhodnoceny.</summary>
        private bool dropped;

        private bool any;
        private DateTime lastT;
        private double lastX, lastY;      // posledni vzorek (kvuli nespojitosti)
        private double anchorX, anchorY;  // posledni zapocteny bod
        private double distance, moving;

        public Odometer(int capacity = DefaultCapacity)
        {
            ring = new Bod[Math.Max(2, capacity)];
        }

        /// <summary>Celkova ujeta draha od prvniho vzorku [m].</summary>
        public double TotalDistanceM => distance;

        /// <summary>Celkovy cas v pohybu [s].</summary>
        public double TotalMovingSec => moving;

        /// <summary>Cas posledniho vzorku; <c>default</c>, dokud zadny neprisel.</summary>
        public DateTime LastTime => any ? lastT : default;

        /// <summary>Prida vzorek: cas, poloha [m] (odometricka soustava) a rychlost [m/s].</summary>
        public void Add(DateTime t, double x, double y, double v)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            if (!any)
            {
                any = true;
                lastT = t; lastX = anchorX = x; lastY = anchorY = y;
                Checkpoint(t, force: true);
                return;
            }
            if (t <= lastT) return;   // stary nebo opakovany vzorek

            double dt = (t - lastT).TotalSeconds;
            double sx = x - lastX, sy = y - lastY;
            double krok = Math.Sqrt(sx * sx + sy * sy);

            if (krok > MinStepM && krok > MaxSpeed * dt)
            {
                // Nespojitost: novy zacatek, nic se nepricita.
                anchorX = x; anchorY = y;
            }
            else
            {
                double ax = x - anchorX, ay = y - anchorY;
                double d = Math.Sqrt(ax * ax + ay * ay);
                if (d >= MinStepM)
                {
                    distance += d;
                    anchorX = x; anchorY = y;
                }
                if (dt <= MaxGapSec && double.IsFinite(v) && Math.Abs(v) >= MovingSpeed)
                    moving += dt;
            }

            lastT = t; lastX = x; lastY = y;
            Checkpoint(t, force: false);
        }

        private void Checkpoint(DateTime t, bool force)
        {
            if (!force && count > 0 && (t - Posledni().T).TotalSeconds < CheckpointSec) return;
            var b = new Bod(t, distance, moving);
            if (count < ring.Length) { ring[(start + count) % ring.Length] = b; count++; }
            else { ring[start] = b; start = (start + 1) % ring.Length; dropped = true; }
        }

        private Bod Posledni() => ring[(start + count - 1) % ring.Length];
        private Bod Na(int i) => ring[(start + i) % ring.Length];

        /// <summary>
        /// Draha a cas v pohybu <b>od casu <paramref name="from"/></b> do posledniho vzorku.
        /// Mezi kontrolnimi body se interpoluje linearne.
        /// </summary>
        public OdometerReading Since(DateTime from)
        {
            if (!any || count == 0) return new OdometerReading(0, 0, false);

            // Dotaz pred prvni bod: bez zahozene historie to znamena „pred prvnim vzorkem", tedy
            // nic neujeto (mise FreeRun meri cas od prvniho snimku, ktery prijde driv nez prvni
            // stav fuze) - podhodnoceni to neni. Nalezeno pri overeni v simulaci 6. 10. 2026.
            var prvni = Na(0);
            if (from <= prvni.T)
                return new OdometerReading(distance - prvni.D, moving - prvni.M, dropped && from < prvni.T);
            if (from >= lastT) return new OdometerReading(0, 0, false);

            // Posledni kontrolni bod s T <= from (binarni hledani).
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Na(mid).T <= from) lo = mid; else hi = mid - 1;
            }
            var a = Na(lo);
            // Za poslednim kontrolnim bodem je jeste soucasny stav (lastT, distance, moving).
            var b = lo + 1 < count ? Na(lo + 1) : new Bod(lastT, distance, moving);

            double d0 = a.D, m0 = a.M;
            double span = (b.T - a.T).TotalSeconds;
            if (span > 0)
            {
                double f = (from - a.T).TotalSeconds / span;
                d0 += f * (b.D - a.D);
                m0 += f * (b.M - a.M);
            }
            return new OdometerReading(Math.Max(0, distance - d0), Math.Max(0, moving - m0), false);
        }
    }
}
