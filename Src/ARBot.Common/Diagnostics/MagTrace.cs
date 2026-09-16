using System;
using System.Collections.Generic;
using System.Numerics;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// <b>Krátká historie magnetického pole pro živé měření rušení na robotu.</b> Kruhový buffer
    /// posledních <see cref="Okno"/> sekund měření z magnetometru plus <b>nula</b> (referenční
    /// vektor, proti kterému se počítá rozdíl) a <b>kontrola klidu</b>.
    ///
    /// <para><b>Nač to je.</b> Železo na robotu se pozná jedině tak, že se s ním pohne a kouká se,
    /// co to udělá s polem — přesně to je test „ovlivňují magnetometr kabely ke kamerám?"
    /// (nález ze 14. 9. 2026, viz doc/imu-and-frames.md). Jedno číslo na to nestačí: rušení je
    /// jednotky až desítky mG proti poli 490 mG, takže se musí dívat na <b>rozdíl proti nule</b>,
    /// ne na absolutní hodnotu, a je potřeba vidět <b>průběh v čase</b> — člověk má při tom ruce
    /// na kabelu, ne na myši.</para>
    ///
    /// <para>⚠️ <b>Hlavní past je záměna s otáčením.</b> Magnetometr měří pole <b>v rámci robotu</b>,
    /// takže zemská složka (~490 mG) se otáčí spolu s ním — pootočení o 1° udělá ve vodorovné
    /// složce ~3,5 mG, tedy <b>víc, než kolik dělá celý hledaný efekt</b>. Proto si stopa nese
    /// úhlovou rychlost a yaw a sama říká, jestli robot <see cref="MagSnimek.Klid">stál</see>.
    /// Bez té kontroly by měřidlo tiše lhalo — a při rozboru záznamu se to už jednou stalo
    /// (širší okno dalo 22,7 mG místo 6,4, protože do něj spadlo 6° otočení).</para>
    ///
    /// <para><b>Co se NEPOČÍTÁ a proč:</b> sklon pole (inklinace) je druhá veličina, která má být
    /// konstantní, ale počítá se <b>z akcelerometru</b> — a ten má na tomhle robotu změřený bias
    /// (+7 % ve velikosti, 0,27 m/s² v Z), takže by do měřidla vnesl vlastní chybu. Sklon patří
    /// do offline rozboru (<c>ARBot.Analyze vn100</c>), kde jde oddělit; tady stačí velikost
    /// pole a vektor, což hard-iron popisuje úplně.</para>
    ///
    /// <para><b>Vlákna:</b> <see cref="Pridej"/> volá vlákno senzoru (100 Hz), <see cref="Snimek"/>
    /// vlákno UI — obojí pod zámkem. Sběr se <b>neškrtí</b> (na rozdíl od překreslování), aby
    /// statistika přes okno byla poctivá.</para>
    /// </summary>
    public sealed class MagTrace
    {
        /// <summary>Výchozí délka okna historie.</summary>
        public static readonly TimeSpan VychoziOkno = TimeSpan.FromSeconds(60);

        /// <summary>Z jak dlouhého úseku se průměruje nula (jeden vzorek při 100 Hz je šum).</summary>
        public static readonly TimeSpan NulaOkno = TimeSpan.FromSeconds(0.5);

        /// <summary>Práh klidu pro úhlovou rychlost [°/s].</summary>
        public const double KlidOmegaDegS = 2.0;

        /// <summary>Práh klidu pro otočení od nuly [°].</summary>
        public const double KlidYawDeg = 2.0;

        private readonly object zamek = new object();
        private readonly Queue<MagVzorek> vzorky = new Queue<MagVzorek>();
        private DateTime zaklad = DateTime.MinValue;
        private Vector3? nula;
        private double? nulaYaw;
        private double? nulaCas;

        public MagTrace(TimeSpan? okno = null)
        {
            Okno = okno ?? VychoziOkno;
            if (Okno <= TimeSpan.Zero) Okno = VychoziOkno;
        }

        /// <summary>Jak dlouhá historie se drží.</summary>
        public TimeSpan Okno { get; }

        /// <summary>
        /// Přidá měření. <paramref name="uhlovaRychlost"/> a <paramref name="yaw"/> jsou volitelné —
        /// bez nich se klid nedá posoudit a <see cref="MagSnimek.Klid"/> bude <c>false</c>
        /// (⚠️ záměrně: „nevím" se tady musí chovat jako „neplatí", jinak by měřidlo mlčky
        /// vydávalo otočení za rušení).
        /// </summary>
        /// <param name="cas">Razítko měření (z <c>TimeBase</c>, viz CLAUDE.md).</param>
        /// <param name="poleG">Pole v tělese [G].</param>
        /// <param name="uhlovaRychlost">Úhlová rychlost [rad/s], nebo <c>null</c>.</param>
        /// <param name="yaw">Kurz [rad], nebo <c>null</c>.</param>
        public void Pridej(DateTime cas, Vector3 poleG, Vector3? uhlovaRychlost, double? yaw)
        {
            lock (zamek)
            {
                if (zaklad == DateTime.MinValue) zaklad = cas;
                double t = (cas - zaklad).TotalSeconds;

                // Skok casu zpet (novy zaznam pri replay, prepnuti senzoru) - zahodit historii,
                // jinak by se v grafu potkaly dva nesouvisejici useky.
                if (vzorky.Count > 0 && t < PosledniCas() - 1.0)
                {
                    vzorky.Clear();
                    nula = null; nulaYaw = null; nulaCas = null;
                    zaklad = cas;
                    t = 0;
                }

                vzorky.Enqueue(new MagVzorek(t, poleG,
                                             uhlovaRychlost.HasValue
                                                 ? uhlovaRychlost.Value.Length() * 180.0 / Math.PI
                                                 : double.NaN,
                                             yaw ?? double.NaN));

                double mez = t - Okno.TotalSeconds;
                while (vzorky.Count > 0 && vzorky.Peek().T < mez) vzorky.Dequeue();
            }
        }

        /// <summary>
        /// Nastaví nulu z posledních <see cref="NulaOkno"/> sekund. Vrací <c>false</c>, když je
        /// v tom úseku míň než dva vzorky — nula z jednoho vzorku by nesla jeho šum.
        /// </summary>
        public bool Vynuluj()
        {
            lock (zamek)
            {
                if (vzorky.Count < 2) return false;
                double konec = PosledniCas();
                double od = konec - NulaOkno.TotalSeconds;

                Vector3 suma = Vector3.Zero;
                double sy = 0, cy = 0;
                int n = 0, nYaw = 0;
                foreach (var v in vzorky)
                {
                    if (v.T < od) continue;
                    suma += v.Pole;
                    n++;
                    if (!double.IsNaN(v.Yaw)) { sy += Math.Sin(v.Yaw); cy += Math.Cos(v.Yaw); nYaw++; }
                }
                if (n < 2) return false;

                nula = suma / n;
                nulaYaw = nYaw > 0 ? Math.Atan2(sy, cy) : (double?)null;
                nulaCas = konec;
                return true;
            }
        }

        /// <summary>Zruší nulu (graf i čísla se vrátí k počítání proti průměru okna).</summary>
        public void ZrusNulu()
        {
            lock (zamek) { nula = null; nulaYaw = null; nulaCas = null; }
        }

        /// <summary>Zahodí historii i nulu.</summary>
        public void Vymaz()
        {
            lock (zamek)
            {
                vzorky.Clear();
                nula = null; nulaYaw = null; nulaCas = null;
                zaklad = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Konzistentní kopie pro vykreslení a pro čísla. Kopíruje se <b>celé okno</b> — při 60 s
        /// a 100 Hz je to ~6 000 struktur, tedy pod 200 kB; držet místo toho živý odkaz by
        /// znamenalo kreslit z dat, která se pod rukama mění.
        /// </summary>
        public MagSnimek Snimek()
        {
            lock (zamek)
            {
                var pole = vzorky.ToArray();
                return new MagSnimek(pole, nula, nulaYaw, nulaCas, Okno.TotalSeconds);
            }
        }

        private double PosledniCas()
        {
            double t = 0;
            foreach (var v in vzorky) t = v.T;   // Queue nema indexer; okno je male
            return t;
        }
    }

    /// <summary>Jeden vzorek stopy. <see cref="OmegaDegS"/>/<see cref="Yaw"/> jsou <c>NaN</c>, když nebyly.</summary>
    public readonly struct MagVzorek
    {
        public MagVzorek(double t, Vector3 pole, double omegaDegS, double yaw)
        {
            T = t; Pole = pole; OmegaDegS = omegaDegS; Yaw = yaw;
        }

        /// <summary>Čas od založení stopy [s].</summary>
        public double T { get; }
        /// <summary>Pole v tělese [G].</summary>
        public Vector3 Pole { get; }
        /// <summary>Velikost úhlové rychlosti [°/s], nebo <c>NaN</c>.</summary>
        public double OmegaDegS { get; }
        /// <summary>Kurz [rad], nebo <c>NaN</c>.</summary>
        public double Yaw { get; }

        /// <summary>Velikost pole [G] — zemské pole je konstanta, takže každá změna je rušení.</summary>
        public double Velikost => Pole.Length();

        /// <summary>Stál robot v tomhle vzorku? Neznámá úhlová rychlost je <c>false</c>.</summary>
        public bool Klid => !double.IsNaN(OmegaDegS) && OmegaDegS <= MagTrace.KlidOmegaDegS;
    }

    /// <summary>
    /// Vyhodnocená kopie stopy. <b>Nula</b> (<see cref="Nula"/>) je referenční vektor; když není,
    /// bere se za referenci <b>průměr okna</b> — graf tak zůstane vycentrovaný i bez nulování.
    /// </summary>
    public sealed class MagSnimek
    {
        public MagSnimek(IReadOnlyList<MagVzorek> vzorky, Vector3? nula, double? nulaYaw, double? nulaCas,
                         double oknoSek)
        {
            Vzorky = vzorky ?? Array.Empty<MagVzorek>();
            Nula = nula;
            NulaCas = nulaCas;
            OknoSek = oknoSek > 0 ? oknoSek : MagTrace.VychoziOkno.TotalSeconds;

            if (Vzorky.Count == 0) return;

            TOd = Vzorky[0].T;
            TDo = Vzorky[Vzorky.Count - 1].T;

            Vector3 suma = Vector3.Zero;
            double sumaV = 0, sumaV2 = 0;
            MinG = double.MaxValue; MaxG = double.MinValue;
            foreach (var v in Vzorky)
            {
                suma += v.Pole;
                double m = v.Velikost;
                sumaV += m; sumaV2 += m * m;
                if (m < MinG) MinG = m;
                if (m > MaxG) MaxG = m;
            }
            int n = Vzorky.Count;
            PrumerPole = suma / n;
            PrumerG = sumaV / n;
            SdG = Math.Sqrt(Math.Max(0, sumaV2 / n - PrumerG * PrumerG));

            var posl = Vzorky[n - 1];
            Posledni = posl.Pole;
            VelikostG = posl.Velikost;
            OmegaDegS = posl.OmegaDegS;

            // Reference pro rozdil i pro graf: nula, kdyz je; jinak prumer okna.
            Reference = nula ?? PrumerPole;
            Rozdil = posl.Pole - Reference;
            // ⚠️ Reference pro VELIKOST neni delka referencniho VEKTORU: bez nuly je referenci
            // prumerny vektor, a |prumer| je pri sumu vzdy mensi nez prumer z |.|, takze by
            // krivka |B| sedela mimo nulu o sum^2/(2|B|). S nulou je to delka nuly (tam je to
            // presne to, proti cemu se meri), bez ni prumer velikosti pres okno.
            ReferenceVelikostG = nula.HasValue ? nula.Value.Length() : PrumerG;
            RozdilVelikostG = posl.Velikost - ReferenceVelikostG;

            if (nulaYaw.HasValue && !double.IsNaN(posl.Yaw))
            {
                double d = posl.Yaw - nulaYaw.Value;
                while (d > Math.PI) d -= 2 * Math.PI;
                while (d < -Math.PI) d += 2 * Math.PI;
                YawOdNulyDeg = Math.Abs(d) * 180.0 / Math.PI;
            }

            // ⚠️ Klid = robot se netoci TED a zaroven se neotocil OD NULY. Prvni podminka chrani
            // okamzitou hodnotu, druha ten rozdil - bez ni by stacilo robotem otocit, pockat
            // a rozdil proti nule by vydaval zemskou slozku za ruseni.
            Klid = posl.Klid && (!YawOdNulyDeg.HasValue || YawOdNulyDeg.Value <= MagTrace.KlidYawDeg);
        }

        public IReadOnlyList<MagVzorek> Vzorky { get; }
        /// <summary>Rozsah času v okně [s].</summary>
        public double TOd { get; }
        public double TDo { get; }
        /// <summary>
        /// Délka okna stopy [s]. ⚠️ <b>Není</b> totéž co <c>TDo − TOd</c>: dokud se buffer plní,
        /// je rozsah dat kratší. Graf musí mít osu z <b>okna</b>, ne z rozsahu dat — jinak se
        /// měřítko času při plnění plynule mění a po naplnění skokem ustane, což vypadá
        /// jako poskakující křivka.
        /// </summary>
        public double OknoSek { get; }
        /// <summary>Poslední změřený vektor [G].</summary>
        public Vector3? Posledni { get; }
        /// <summary>Velikost posledního pole [G].</summary>
        public double VelikostG { get; }
        /// <summary>Průměr, směrodatná odchylka, minimum a maximum <c>|B|</c> přes okno [G].</summary>
        public double PrumerG { get; }
        public double SdG { get; }
        public double MinG { get; }
        public double MaxG { get; }
        /// <summary>Rozpětí <c>|B|</c> přes okno [G] — přes pomalou otočku je to přímo míra tvrdého železa.</summary>
        public double RozpetiG => Vzorky.Count == 0 ? 0 : MaxG - MinG;
        /// <summary>Průměrný vektor přes okno [G].</summary>
        public Vector3 PrumerPole { get; }
        /// <summary>Nastavená nula [G], nebo <c>null</c>.</summary>
        public Vector3? Nula { get; }
        /// <summary>Čas nastavení nuly [s] — svislá značka v grafu.</summary>
        public double? NulaCas { get; }
        /// <summary>Reference, proti které se počítá rozdíl: nula, jinak průměr okna.</summary>
        public Vector3 Reference { get; }
        /// <summary>Reference pro <c>|B|</c> [G] — délka nuly, jinak průměr velikostí přes okno.</summary>
        public double ReferenceVelikostG { get; }
        /// <summary>Poslední vektor mínus reference [G].</summary>
        public Vector3 Rozdil { get; }
        /// <summary>Rozdíl velikostí [G].</summary>
        public double RozdilVelikostG { get; }
        /// <summary>Velikost úhlové rychlosti posledního vzorku [°/s], nebo <c>NaN</c>.</summary>
        public double OmegaDegS { get; }
        /// <summary>O kolik se robot otočil od nastavení nuly [°], nebo <c>null</c>.</summary>
        public double? YawOdNulyDeg { get; }
        /// <summary>Platí odečet? Viz poznámka o záměně s otáčením v <see cref="MagTrace"/>.</summary>
        public bool Klid { get; }
    }
}
