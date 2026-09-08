using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// ⚠️ System.Numerics se ZAMERNE neimportuje celý: `Vector<>` v nem koliduje s MathNet
// `Vector<T>` (CS0104) v ostatnich souborech teto slozky; drzime jednotny styl.
using Vector3 = System.Numerics.Vector3;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Pokryti mericich smeru</b> — kolik azimutu a naklonu uz robot pri otaceni prosel.
    ///
    /// <para><b>Nacpak.</b> Podminenost prolozeni (<see cref="MagCalFit"/>) rekne, ze soustava
    /// jeste neni urcena, ale nerekne <b>co s tim</b>. Kose to rekly: „chybi azimuty 120–165°",
    /// „podloz robota na druhou stranu". Obsluha stoji u robota a potrebuje pokyn, ne diagnozu.
    /// A na rozdil od podminenosti je pokryti kriterium <b>geometricke</b>, tedy nezavisle na
    /// sumu v datech.</para>
    ///
    /// <para>⚠️ <b><paramref name="yawRad"/> je INTEGROVANE GYRO</b>, ne yaw ze senzoru — yaw je
    /// prave ta vada, kterou merime. Gyro je ciste (klidovy bias −4,6 °/h, tedy ~0,15° za dve
    /// minuty otaceni) a na pokryti staci relativni uhel: nepotrebujeme vedet, kde je sever,
    /// jen ze jsme se otocili dokola.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class MagCalCoverage
    {
        /// <summary>Sirka naklonoveho kose podle velikosti odklonu [deg].</summary>
        private const double TiltBinDeg = 10.0;

        /// <summary>Sirka sektoru podle SMERU naklonu [deg].</summary>
        private const double TiltDirSectorDeg = 90.0;

        /// <summary>
        /// O kolik se musi lisit smery dvou naklonu, aby se pocitaly za „na druhou stranu" [deg].
        /// </summary>
        private const double OppositeTiltMinDeg = 90.0;

        /// <summary>Klic naklonove skupiny: velikost odklonu a jeho SMER.</summary>
        private readonly struct TiltKey : IEquatable<TiltKey>
        {
            public TiltKey(int magBin, int dirSector) { MagBin = magBin; DirSector = dirSector; }

            /// <summary>Kos podle velikosti odklonu (nasobek <see cref="TiltBinDeg"/>).</summary>
            public int MagBin { get; }

            /// <summary>Sektor podle smeru naklonu; <c>-1</c> = na rovine (smer nema vyznam).</summary>
            public int DirSector { get; }

            public bool Equals(TiltKey o) => MagBin == o.MagBin && DirSector == o.DirSector;
            public override bool Equals(object o) => o is TiltKey k && Equals(k);
            public override int GetHashCode() => MagBin * 397 ^ DirSector;
        }

        private readonly int[] azimuth = new int[MagCalThresholds.AzimuthBins];
        // Klic = naklonova skupina, hodnota = pocty po azimutech v te skupine.
        private readonly Dictionary<TiltKey, int[]> tilt = new();
        private readonly List<Vector3> mag = new();
        private readonly List<Vector3> acc = new();

        /// <summary>Nasbirana surova mereni pole [G] — vstup pro <see cref="MagCalFit"/>.</summary>
        public IReadOnlyList<Vector3> Mag => mag;

        /// <summary>Gravitace ke kazdemu vzorku, ve STEJNEM poradi jako <see cref="Mag"/>.</summary>
        public IReadOnlyList<Vector3> Acc => acc;

        /// <summary>Pocty vzorku po azimutovych kosich.</summary>
        public int[] AzimuthCounts => (int[])azimuth.Clone();

        public void Add(double yawRad, Vector3 magSample, Vector3 accSample)
        {
            int ai = AzimuthBin(yawRad);
            azimuth[ai]++;

            var key = Key(accSample);
            if (!tilt.TryGetValue(key, out var po))
                tilt[key] = po = new int[MagCalThresholds.AzimuthBins];
            po[ai]++;

            mag.Add(magSample);
            acc.Add(accSample);
        }

        /// <summary>Kolik azimutovych kosu ma dost vzorku.</summary>
        public int FilledAzimuthBins => azimuth.Count(c => c >= MagCalThresholds.MinPerAzimuthBin);

        /// <summary>Naklonove skupiny s dostatecnym azimutovym pokrytim.</summary>
        public int TiltGroups => tilt.Count(kv => Dostatecna(kv.Value));

        /// <summary>Z nich ty odklonene aspon <see cref="MagCalThresholds.MinTiltDeg"/>.</summary>
        public int TiltedGroups => Naklonene().Count;

        /// <summary>
        /// <b>Je robot naklonen na dve RUZNE strany?</b>
        ///
        /// <para>⚠️ Nestaci pocet naklonu — <b>zmereno</b> (viz <see cref="MagCalThresholds.MaxCondition"/>),
        /// ze dva naklony na tutéz stranu jsou skoro tak degenerovane jako rovina
        /// (podminenost 4,7 × 10⁷ proti 2,0 × 10⁸), kdezto par +/− ji srazi na 434. Teprve
        /// protilehle naklony zlomi symetrii, ktera drzi slozku <c>z</c> neurcenou.</para>
        /// </summary>
        public bool HasOppositeTilts
        {
            get
            {
                var n = Naklonene();
                for (int i = 0; i < n.Count; i++)
                    for (int j = i + 1; j < n.Count; j++)
                        if (RozdilSmeru(n[i].DirSector, n[j].DirSector) >= OppositeTiltMinDeg)
                            return true;
                return false;
            }
        }

        /// <summary>Je pokryti hotove, tedy da se z nej poctive prolozit?</summary>
        public bool Complete
            => FilledAzimuthBins == MagCalThresholds.AzimuthBins
               && TiltGroups >= MagCalThresholds.MinTiltGroups
               && TiltedGroups >= MagCalThresholds.MinTiltedGroups
               && HasOppositeTilts;

        /// <summary>
        /// Co jeste chybi, <b>pro cloveka a jako pokyn</b>. Prazdny retezec = nic.
        /// </summary>
        public string MissingText()
        {
            var s = new List<string>();

            var chybi = ChybejiciAzimuty();
            if (chybi.Count > 0) s.Add("chybi azimuty " + PopisRozsahu(chybi));

            int naklonene = TiltedGroups;
            if (naklonene < MagCalThresholds.MinTiltedGroups)
                s.Add(string.Format(CultureInfo.InvariantCulture,
                    "chybi naklon (mam {0} z {1}, podloz robota aspon o {2:F0} stupnu)",
                    naklonene, MagCalThresholds.MinTiltedGroups, MagCalThresholds.MinTiltDeg));
            else if (!HasOppositeTilts)
                s.Add("naklony jsou jen na jednu stranu - podloz robota na DRUHOU stranu");
            else if (TiltGroups < MagCalThresholds.MinTiltGroups)
                s.Add($"chybi naklonova skupina ({TiltGroups} z {MagCalThresholds.MinTiltGroups})");

            return string.Join("; ", s);
        }

        /// <summary>Naklonene skupiny s dostatecnym azimutovym pokrytim.</summary>
        private List<TiltKey> Naklonene()
            => tilt.Where(kv => Dostatecna(kv.Value)
                                && kv.Key.MagBin * TiltBinDeg >= MagCalThresholds.MinTiltDeg)
                   .Select(kv => kv.Key)
                   .ToList();

        /// <summary>
        /// Ma skupina dost azimutu? Pulka kosu staci — pri naklonu se robotem otaci rukou
        /// a cekat plny obrat v kazdem naklonu je nad lidske sily.
        /// </summary>
        private static bool Dostatecna(int[] po)
            => po.Count(c => c >= MagCalThresholds.MinPerAzimuthBin)
               >= MagCalThresholds.AzimuthBins / 2;

        private static double RozdilSmeru(int a, int b)
        {
            double d = Math.Abs(a - b) * TiltDirSectorDeg;
            d %= 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        private List<int> ChybejiciAzimuty()
        {
            var r = new List<int>();
            for (int i = 0; i < azimuth.Length; i++)
                if (azimuth[i] < MagCalThresholds.MinPerAzimuthBin) r.Add(i);
            return r;
        }

        /// <summary>Souvisle useky kosu jako rozsahy ve stupnich (140-180°), ne vypis cisel.</summary>
        private static string PopisRozsahu(List<int> kose)
        {
            double sirka = 360.0 / MagCalThresholds.AzimuthBins;
            var casti = new List<string>();
            int i = 0;
            while (i < kose.Count)
            {
                int j = i;
                while (j + 1 < kose.Count && kose[j + 1] == kose[j] + 1) j++;
                casti.Add(string.Format(CultureInfo.InvariantCulture, "{0:F0}-{1:F0}°",
                                        kose[i] * sirka, (kose[j] + 1) * sirka));
                i = j + 1;
            }
            return string.Join(", ", casti);
        }

        private static int AzimuthBin(double yawRad)
        {
            double f = yawRad % (2 * Math.PI);
            if (f < 0) f += 2 * Math.PI;
            int i = (int)(f / (2 * Math.PI) * MagCalThresholds.AzimuthBins);
            return Math.Min(i, MagCalThresholds.AzimuthBins - 1);
        }

        /// <summary>
        /// Klic skupiny z gravitace: velikost odklonu od svislice a jeho smer.
        ///
        /// <para>⚠️ <b>Smer je podstatny, ne detail</b> — bez nej by +20° a −20° spadly do tehoz
        /// kose a jednostranne naklonení by proslo jako hotove, ackoli je skoro tak degenerovane
        /// jako rovina.</para>
        /// </summary>
        private static TiltKey Key(Vector3 acc)
        {
            double vodorovne = Math.Sqrt(acc.X * acc.X + acc.Y * acc.Y);
            double odklon = Math.Atan2(vodorovne, Math.Abs(acc.Z)) * 180.0 / Math.PI;

            // Floor, ne Round: kos k pak znamena odklon v [k·10°, (k+1)·10°), takze
            // `magBin·TiltBinDeg >= MinTiltDeg` nikdy netvrdi vic, nez jaky je SKUTECNY odklon.
            // Se zaokrouhlovanim by se 15,5° tvarilo jako 20° a kriterium by se samo zmirnilo.
            int magBin = (int)Math.Floor(odklon / TiltBinDeg);

            // Na rovine nema smer vyznam (delil by sum na ctyri skupiny) -> jedna skupina.
            if (odklon < MagCalThresholds.MinTiltDeg) return new TiltKey(magBin, -1);

            double smer = Math.Atan2(acc.Y, acc.X) * 180.0 / Math.PI;
            if (smer < 0) smer += 360.0;
            int sektor = (int)(smer / TiltDirSectorDeg) % (int)(360.0 / TiltDirSectorDeg);
            return new TiltKey(magBin, sektor);
        }
    }
}
