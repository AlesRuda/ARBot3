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
    /// Jeden radek mrizky pokryti — <b>jedna poloha robota</b> a jak jsou v ni pokryte azimuty.
    /// </summary>
    public readonly struct MagCalCoverageRow
    {
        public MagCalCoverageRow(int index, string label, int[] counts, bool sufficient, bool tilted)
        {
            Index = index; Label = label; Counts = counts;
            Sufficient = sufficient; Tilted = tilted;
        }

        /// <summary>Poradi radku; <c>0</c> = na rovine.</summary>
        public int Index { get; }

        /// <summary>Popis pro cloveka (napr. „zvednuty predek").</summary>
        public string Label { get; }

        /// <summary>Pocty vzorku po azimutovych kosich.</summary>
        public int[] Counts { get; }

        /// <summary>Ma radek dost azimutu, aby se pocital jako naklonova skupina?</summary>
        public bool Sufficient { get; }

        /// <summary>Je to radek s odklonem nad <see cref="MagCalThresholds.MinTiltDeg"/>?</summary>
        public bool Tilted { get; }
    }

    /// <summary>
    /// <b>Pokryti mericich smeru</b> — kolik azimutu a naklonu uz robot pri otaceni prosel.
    ///
    /// <para><b>Nacpak.</b> Podminenost prolozeni (<see cref="MagCalFit"/>) rekne, ze soustava
    /// jeste neni urcena, ale nerekne <b>co s tim</b>. Kose to rekly: „chybi azimuty 120–165°",
    /// „podloz robota na druhou stranu". Obsluha stoji u robota a potrebuje pokyn, ne diagnozu.
    /// A na rozdil od podminenosti je pokryti kriterium <b>geometricke</b>, tedy nezavisle na
    /// sumu v datech.</para>
    ///
    /// <para>⚠️ <b>Yaw je INTEGROVANE GYRO</b>, ne yaw ze senzoru — yaw je prave ta vada, kterou
    /// merime. Gyro je ciste (klidovy bias −4,6 °/h, tedy ~0,15° za dve minuty otaceni) a na
    /// pokryti staci relativni uhel: nepotrebujeme vedet, kde je sever, jen ze jsme se otocili
    /// dokola.</para>
    ///
    /// <para><b>Struktura je MRIZKA s pevnym poctem radku</b> (<see cref="TiltRows"/>): rovina
    /// a ctyri smery podlozeni. Presne tuhle mrizku kresli stranka, takze kriterium a to, co
    /// vidi obsluha, je <b>jedna a tataz vec</b> — druhy seznam by se casem rozesel.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class MagCalCoverage
    {
        /// <summary>Pocet radku mrizky: rovina + ctyri smery podlozeni.</summary>
        public const int TiltRows = 5;

        /// <summary>Sirka sektoru podle SMERU naklonu [deg].</summary>
        private const double TiltDirSectorDeg = 90.0;

        /// <summary>
        /// O kolik se musi lisit smery dvou naklonu, aby se pocitaly za „na druhou stranu" [deg].
        /// </summary>
        private const double OppositeTiltMinDeg = 90.0;

        /// <summary>
        /// Popisy radku pro cloveka; poradi odpovida sektorum, viz <see cref="Radek"/>.
        ///
        /// <para>Verejne proto, aby je <b>stranka nemusela opisovat</b> — druhy seznam popisu
        /// by se pri zmene sektoru tise rozesel s tim, co se opravdu meri.</para>
        ///
        /// <para>⚠️ <b>Bez diakritiky zamerne</b>: tytez popisy se skladaji do
        /// <see cref="MissingText"/>, tedy do verdiktu, a ten jde krome stranky i do
        /// <c>Trace</c> a do zaznamu — kde je zbytek textu taky bez diakritiky. Michat obojí
        /// v jedne vete („mas jen jednu stranu (zvednutý předek)") vypada jako chyba.</para>
        /// </summary>
        public static readonly string[] RowLabels =
            { "na rovine", "zvednuty predek", "zvednuta leva", "zvednuta zad", "zvednuta prava" };

        private static string[] Popisy => RowLabels;

        private readonly int[] azimuth = new int[MagCalThresholds.AzimuthBins];
        private readonly int[][] rows = Enumerable.Range(0, TiltRows)
            .Select(_ => new int[MagCalThresholds.AzimuthBins]).ToArray();
        private readonly List<Vector3> mag = new();
        private readonly List<Vector3> acc = new();

        /// <summary>Nasbirana surova mereni pole [G] — vstup pro <see cref="MagCalFit"/>.</summary>
        public IReadOnlyList<Vector3> Mag => mag;

        /// <summary>Gravitace ke kazdemu vzorku, ve STEJNEM poradi jako <see cref="Mag"/>.</summary>
        public IReadOnlyList<Vector3> Acc => acc;

        /// <summary>Pocty vzorku po azimutovych kosich.</summary>
        public int[] AzimuthCounts => (int[])azimuth.Clone();

        /// <summary>Radek mrizky, ve kterem robot prave je; <c>-1</c>, dokud neprisel vzorek.</summary>
        public int CurrentRow { get; private set; } = -1;

        /// <summary>Azimutovy kos, ve kterem robot prave je; <c>-1</c>, dokud neprisel vzorek.</summary>
        public int CurrentAzimuthBin { get; private set; } = -1;

        /// <summary>
        /// Aktualni odklon od svislice [deg]; <see cref="double.NaN"/> pred prvnim vzorkem.
        ///
        /// <para>Nese se zvlast proto, ze <b>slouceni velikosti odklonu do jednoho radku ji
        /// z mrizky odstranilo</b> — a obsluha potrebuje videt, ze podklada dost.</para>
        /// </summary>
        public double CurrentTiltDeg { get; private set; } = double.NaN;

        public void Add(double yawRad, Vector3 magSample, Vector3 accSample)
        {
            int ai = AzimuthBin(yawRad);
            azimuth[ai]++;

            int r = Radek(accSample, out double odklon);
            rows[r][ai]++;

            CurrentRow = r;
            CurrentAzimuthBin = ai;
            CurrentTiltDeg = odklon;

            mag.Add(magSample);
            acc.Add(accSample);
        }

        /// <summary>Kolik azimutovych kosu ma dost vzorku.</summary>
        public int FilledAzimuthBins => azimuth.Count(c => c >= MagCalThresholds.MinPerAzimuthBin);

        /// <summary>Naklonove skupiny s dostatecnym azimutovym pokrytim.</summary>
        public int TiltGroups => rows.Count(Dostatecna);

        /// <summary>Z nich ty odklonene aspon <see cref="MagCalThresholds.MinTiltDeg"/>.</summary>
        public int TiltedGroups => Naklonene().Count;

        /// <summary>
        /// <b>Mrizka pro stranku</b> — pevny pocet radku, kazdy s pocty po azimutech.
        ///
        /// <para>Kresli se z ni to, co obsluha vidi: cervena = zadny vzorek, zluta = malo,
        /// zelena = dost. Rika <b>totez, co kriterium</b>, protoze z nej pochazi.</para>
        /// </summary>
        public IReadOnlyList<MagCalCoverageRow> Grid()
            => Enumerable.Range(0, TiltRows)
                .Select(i => new MagCalCoverageRow(i, Popisy[i], (int[])rows[i].Clone(),
                                                   Dostatecna(rows[i]), i > 0))
                .ToList();

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
                        if (RozdilSmeru(n[i], n[j]) >= OppositeTiltMinDeg)
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

            // ⚠️ Poradi vetvi je zamerne a jedna z nich je oprava vady z pole: kdyz uz obsluha
            // JEDEN naklon ma, nesmi ji pokyn poslat „podloz aspon o 15 stupnu" — clovek,
            // ktery robota drzi naklonený o 34°, z toho nepozna, co ma zmenit. Musi se
            // pojmenovat STRANA.
            var naklonene = Naklonene();
            if (naklonene.Count == 0)
                s.Add(string.Format(CultureInfo.InvariantCulture,
                    "chybi naklon - podloz robota aspon o {0:F0} stupnu a otoc ho dokola",
                    MagCalThresholds.MinTiltDeg));
            else if (!HasOppositeTilts)
                s.Add($"mas jen jednu stranu ({Popisy[naklonene[0]]}) - podloz robota"
                      + $" na DRUHOU stranu ({Popisy[Protejsi(naklonene[0])]}) a otoc ho dokola");
            else if (TiltGroups < MagCalThresholds.MinTiltGroups)
                s.Add($"chybi naklonova skupina ({TiltGroups} z {MagCalThresholds.MinTiltGroups})"
                      + " - dotoc chybejici azimuty na rovine");

            return string.Join("; ", s);
        }

        /// <summary>Radek na protejsi strane (predek↔zad, leva↔prava).</summary>
        private static int Protejsi(int radek) => (radek - 1 + 2) % 4 + 1;

        /// <summary>Indexy naklonenych radku s dostatecnym azimutovym pokrytim.</summary>
        private List<int> Naklonene()
            => Enumerable.Range(1, TiltRows - 1).Where(i => Dostatecna(rows[i])).ToList();

        /// <summary>
        /// Ma radek dost azimutu? Pulka kosu staci — pri naklonu se robotem otaci rukou
        /// a cekat plny obrat v kazdem naklonu je nad lidske sily.
        /// </summary>
        private static bool Dostatecna(int[] po)
            => po.Count(c => c >= MagCalThresholds.MinPerAzimuthBin)
               >= MagCalThresholds.AzimuthBins / 2;

        /// <summary>Rozdil smeru dvou naklonovych radku [deg]; radky 1..4 jsou sektory po 90°.</summary>
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
        /// Radek mrizky z gravitace: <c>0</c> na rovine, jinak sektor podle toho, ktera strana
        /// robota je <b>zvednuta</b>.
        ///
        /// <para>⚠️ <b>Velikost odklonu v klici NENI</b>, a je to oprava vady z pole
        /// (10. 9. 2026). Kdyz se klicovalo i velikosti po 10°, rozpadl se rucni naklon —
        /// ktery prirozene kolisa — do tri poloprazdnych skupin a zadna nedosahla poloviny
        /// azimutu; obsluze zmizela hlaska o naklonech a presto se nic nehnulo. Velikost se
        /// misto toho hlida prahem <see cref="MagCalThresholds.MinTiltDeg"/> a ukazuje se
        /// zvlast jako <see cref="CurrentTiltDeg"/>.</para>
        ///
        /// <para>⚠️ <b>Sektory jsou POSUNUTE o pul sirky</b>, aby osy robota lezely v jejich
        /// STREDU. S hranici na 0° by se podlozeni presne zepredu rozpadlo mezi dva sektory —
        /// tataz trida chyby jako vys, jen o osu jinde.</para>
        ///
        /// <para>Vodorovna slozka akcelerometru miri k <b>zvednute</b> strane: pri klopeni
        /// predku dolu ma gravitace v telese slozku +X, takze <c>acc = −g</c> ma −X, tedy
        /// smer dozadu — a zvednuta je opravdu zad.</para>
        /// </summary>
        private static int Radek(Vector3 acc, out double odklonDeg)
        {
            double vodorovne = Math.Sqrt(acc.X * acc.X + acc.Y * acc.Y);
            odklonDeg = Math.Atan2(vodorovne, Math.Abs(acc.Z)) * 180.0 / Math.PI;

            // Na rovine nema smer vyznam (delil by sum na ctyri skupiny) -> jeden radek.
            if (odklonDeg < MagCalThresholds.MinTiltDeg) return 0;

            double smer = Math.Atan2(acc.Y, acc.X) * 180.0 / Math.PI;
            smer += TiltDirSectorDeg / 2;                    // posun, aby osy byly ve stredu
            if (smer < 0) smer += 360.0;
            int sektor = (int)(smer / TiltDirSectorDeg) % (int)(360.0 / TiltDirSectorDeg);
            return 1 + sektor;
        }
    }
}
