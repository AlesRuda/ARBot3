using System;
using System.Collections.Generic;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Odhad skutecne sirky cesty <b>per hrana</b> (klic je OSM way) z merene sirky koridoru —
    /// <b>s verdiktem kvality</b>.
    ///
    /// <para><b>Nacpak to je.</b> Mapova hranice cesty je synteticka: osa odsazena o polosirku
    /// z OSM, a kde tag <c>width</c> chybi (coz je bezny stav), vezme se default
    /// <c>roadwidth=</c>. Kdyz hranice v obraze merime, muzeme sirku <b>zpresnit</b> — vedlejsi
    /// produkt je „tady je cesta o 0,8 m uzsi, nez rika mapa".</para>
    ///
    /// <para><b>Bezi BEZ brany.</b> Prijme kazdou merenou sirku, ktera prosla geometrickou
    /// pricetnosti <see cref="CorridorFinder"/>u (rozsah, rovnobeznost, inliery). Zadna
    /// zavislost na mapove sirce ani na poze — a to neni nedbalost:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Sirka pozu nepouziva vubec.</b> <c>CorridorFinder</c> ji pocita jako
    /// <c>cL − cR</c>, tedy rozdil offsetu dvou prolozenych primek v <b>ramci robotu</b>. Chyba
    /// pozy se do ni dostat nemuze. ⚠️ Starsi zduvodneni u
    /// <c>CorridorLocalizerConfig.WidthUpdateMaxDisagreementM</c> („jinak by se do sirky zapisovala
    /// chyba pozy") je proto nepresne — co velky pricny nesouhlas signalizuje, je <b>spatne
    /// prolozeni</b> nebo spatne prirazeni k hrane, ne chyba pozy v sirce.</item>
    /// <item><b>Na spatne prolozeni se ptame primo.</b> Spatna prolozeni se navzajem
    /// <b>rozchazeji</b>, spravna si sednou — rozptyl merení je tedy prime meritko kvality
    /// a nepotrebuje zadnou vnejsi referenci.</item>
    /// </list>
    ///
    /// <para><b>Proc ne <see cref="RoadWidthFilter"/>.</b> Ten se zaklada PRVNIM merenim a sirkova
    /// brana koridoru se pak toho cisla drzi — jedno spatne prolozeni je tedy <b>lepkave navzdy</b>
    /// (kazde dalsi spravne merenie by branou neproslo). Navic se brana ptala na mapovou sirku
    /// jeste PREDTIM, nez se filtr mel z ceho naucit, takze na ceste sirsi nez
    /// <c>roadwidth ± MaxWidthDisagreementM</c> se odhad nezalozil <b>nikdy</b>. Tenhle estimator
    /// oboji odstranuje: okno + median se samy opravi a dokud si merenia nesednou, rekne „nevim"
    /// misto toho, aby vnutil spatne cislo.</para>
    ///
    /// <para>Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public sealed class RoadWidthEstimator
    {
        private readonly RoadWidthEstimatorConfig config;
        private readonly Dictionary<long, List<double>> window = new Dictionary<long, List<double>>();
        private readonly Dictionary<long, int> next = new Dictionary<long, int>();

        public RoadWidthEstimator(RoadWidthEstimatorConfig config = null)
        {
            this.config = config ?? new RoadWidthEstimatorConfig();
            this.config.Validate();
        }

        /// <summary>Nastaveni, se kterym estimator pracuje.</summary>
        public RoadWidthEstimatorConfig Config => config;

        /// <summary>Kolik hran uz ma nejaka merenia.</summary>
        public int Count => window.Count;

        /// <summary>Kolik merení je pro hranu <b>v okne</b> (ne kolik jich kdy proslo).</summary>
        public int Samples(long wayId) => window.TryGetValue(wayId, out var w) ? w.Count : 0;

        /// <summary>
        /// Zapracuje merenou sirku. Nesmyslna hodnota (nekladna) se zahodi — to neni merenie,
        /// to je vada volajiciho.
        /// </summary>
        public void Add(long wayId, double measuredWidthM)
        {
            if (!(measuredWidthM > 0)) return;

            if (!window.TryGetValue(wayId, out var w))
            {
                w = new List<double>(config.WindowSize);
                window[wayId] = w;
                next[wayId] = 0;
            }

            if (w.Count < config.WindowSize)
            {
                w.Add(measuredWidthM);
            }
            else
            {
                // Kruhovy buffer: nejstarsi merenie vypadne. Diky tomu se odhad SAM OPRAVI
                // ze spatneho zacatku - to je cely duvod, proc tahle trida vznikla.
                int i = next[wayId];
                w[i] = measuredWidthM;
                next[wayId] = (i + 1) % config.WindowSize;
            }
        }

        /// <summary>
        /// Sirka hrany, kdyz uz se ji da <b>verit</b>. Vraci <c>false</c>, dokud merení neni dost
        /// nebo dokud si navzajem nesednou — a „nevim" je v tom pripade spravna odpoved,
        /// ne duvod vratit mapovou hodnotu.
        /// </summary>
        public bool TryGetWidth(long wayId, out double widthM)
        {
            widthM = 0;
            if (!window.TryGetValue(wayId, out var w) || w.Count < config.MinSamples) return false;

            double median = Median(w);
            if (Dispersion(w, median) > config.MaxDispersionM) return false;

            widthM = median;
            return true;
        }

        /// <summary>
        /// Rozptyl merení v okne (MAD) — DIAGNOSTIKA. <c>NaN</c>, kdyz hrana zadna merenia nema.
        /// </summary>
        public double DispersionOf(long wayId)
        {
            if (!window.TryGetValue(wayId, out var w) || w.Count == 0) return double.NaN;
            return Dispersion(w, Median(w));
        }

        /// <summary>
        /// Odhad bez ohledu na kvalitu — DIAGNOSTIKA a kresleni. Pro rozhodovani pouzij
        /// <see cref="TryGetWidth"/>; tohle umi vratit i cislo, kteremu se verit nema.
        /// </summary>
        public double RawEstimate(long wayId, double fallbackM)
            => window.TryGetValue(wayId, out var w) && w.Count > 0 ? Median(w) : fallbackM;

        /// <summary>Zahodi vsechny odhady (novy beh, seek v zaznamu).</summary>
        public void Clear()
        {
            window.Clear();
            next.Clear();
        }

        /// <summary>Median kopie (vstup se nesmi preusporadat - je to zivy kruhovy buffer).</summary>
        private static double Median(List<double> values)
        {
            var a = values.ToArray();
            Array.Sort(a);
            int n = a.Length;
            return n % 2 == 1 ? a[n / 2] : (a[n / 2 - 1] + a[n / 2]) / 2;
        }

        /// <summary>MAD: median absolutnich odchylek od medianu. Odlehla hodnota s nim nehne.</summary>
        private static double Dispersion(List<double> values, double median)
        {
            var d = new double[values.Count];
            for (int i = 0; i < values.Count; i++) d[i] = Math.Abs(values[i] - median);
            Array.Sort(d);
            int n = d.Length;
            return n % 2 == 1 ? d[n / 2] : (d[n / 2 - 1] + d[n / 2]) / 2;
        }
    }
}
