using ARBot.Common.Common;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Obaluje <see cref="EKFModel"/> a resi asynchronni merenia s ruznymi kmitocty a latenci.
    /// Merenia se zpracovavaji podle casu POŘÍZENÍ (capture), ne prichodu.
    ///
    /// Buffer drzi uzly {merenie, x, P} - u kazdeho merenia se pamatuje filtrovany stav
    /// PO jeho aplikaci (checkpoint). Index <c>dirtyFrom</c> oznacuje prvni neplatny uzel.
    ///
    /// - Vlozeni merenia (in-order i out-of-sequence) jen posune dirtyFrom na misto vlozeni;
    ///   nic se hned nepocita. Diky kauzalite EKF je vse pred t_m stale platne.
    /// - Prepocet je LINY (<see cref="EnsureValid"/>, pri dotazu) a dopocita jen ocas
    ///   [dirtyFrom .. konec] z posledniho platneho checkpointu - u kamery s malou latenci
    ///   je to jen par uzlu.
    /// - Merenie starsi nez okno historie se zahodi (zaloguje).
    ///
    /// Prune: nejstarsi uzly mimo okno se natrvalo zapecou do bazoveho checkpointu (fold-in).
    /// </summary>
    public class AsyncFusionEngine
    {
        private sealed class Node
        {
            public IMeasurement M;
            public Vector<double> X;   // filtrovany stav po aplikaci M (v case M.TimeStamp)
            public Matrix<double> P;
            public double Nis;         // NIS merenia pri jeho aplikaci
            public bool Accepted;      // false = zahozeno gatingem
            public DateTime T => M.TimeStamp;
        }

        /// <summary>Diagnosticky zaznam o zpracovanem merenii.</summary>
        public struct MeasurementInfo
        {
            public string Source;
            public DateTime Time;
            public double Nis;
            public bool Accepted;

            /// <summary>Jak s merenim fuze naloadila (rozlisi „pozde" od „zamitl gating").</summary>
            public MeasurementVerdict Verdict;

            /// <summary>Namerena hodnota z (kopie, muze byt null u starsich cest).</summary>
            public double[] Z;

            /// <summary>Diagonala kovariance sumu R (kopie).</summary>
            public double[] DiagR;

            /// <summary>
            /// Zprava pro telemetrii a zaznam. Konverzi vlastni domena — zprava zustava pasivni
            /// DTO (viz CLAUDE.md).
            /// </summary>
            public Logs.MeasurementDiagMsg ToLogMessage()
                => new Logs.MeasurementDiagMsg
                {
                    Source = Source,
                    TimeStamp = Time,
                    Nis = Nis,
                    Accepted = Accepted,
                    Verdict = (byte)Verdict,
                    Z = Z,
                    DiagR = DiagR,
                };
        }

        /// <summary>
        /// Odberatel verdiktu o kazdem merenii (null = vypnuto, nic se nepocita ani nealokuje).
        ///
        /// <para><b>Kdy se vola.</b> <see cref="MeasurementVerdict.TooOld"/> <b>ihned</b> pri
        /// zarazeni (merenie do bufferu nevstoupi, tak uz se o nem nic nedozvime). Ostatni
        /// verdikty az ve chvili, kdy merenie <b>vypadava z okna</b> historie: do te doby se
        /// jeho NIS i prijeti muze prepocitat, kdykoli dojde starsi merenie (out-of-sequence),
        /// takze verdikt neni konecny. Diagnostika je proto opozdena az o okno historie —
        /// pro rozbor zaznamu to nevadi, pro rizeni se nepouziva.</para>
        ///
        /// <para><b>Vola se pod vnitrnim zamkem</b> — odberatel musi jen odlozit data (frontu),
        /// ne pocitat ani volat zpet do fuze, jinak si zamek zablokuje.</para>
        /// </summary>
        public Action<MeasurementInfo> OnMeasurement;

        private readonly EKFModel model;
        private readonly TimeSpan window;
        private readonly List<Node> nodes = new List<Node>();
        // Zamek chranici cely vnitrni stav (nodes, base checkpoint, model.X/P) - umoznuje
        // provozovat fuzi (Enqueue, reaktivni vlakno) a rizeni (GetStateAt, vlakno scheduleru)
        // jako paralelni stupne bez datoveho zavodu.
        private readonly object sync = new object();

        // bazovy checkpoint: stav filtru v case tBase (zahrnuje vsechna merenia s casem <= tBase)
        private Vector<double> xBase;
        private Matrix<double> pBase;
        private DateTime tBase;
        // index prvniho neplatneho uzlu; == nodes.Count kdyz je cely buffer platny
        private int dirtyFrom;
        private bool initialized;
        private bool positionInitialized;

        // Kolik merenii se zahodilo jako starsi nez okno historie (celkem a po zdrojich).
        private long droppedTooOld;
        private readonly Dictionary<string, long> droppedBySource = new Dictionary<string, long>();

        public AsyncFusionEngine(EKFModel model, TimeSpan? historyWindow = null)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            window = historyWindow ?? model.Config.HistoryWindow;
        }

        public EKFModel Model => model;

        /// <summary>
        /// Referencni bod lokalni ENU roviny (kde plati X=Y=0), sdileny s celym runtime - occupancy
        /// grid, globalni navigace i UI musi pouzivat TUTEZ rovinu. Zaklada ji ten, kdo nacte mapu
        /// (stred bboxu OSM mapy), nebo fallbackem GPS adapter z prvniho platneho fixu.
        /// <c>null</c> = jeste neni znama -&gt; nelze prevadet LLA &lt;-&gt; metry.
        /// Viz doc/global-navigation-runtime.md.
        /// </summary>
        public Coordinates.GeoReference GeoReference
        {
            get => model.Config.GeoReference;
            set => model.Config.GeoReference = value;
        }

        /// <summary>
        /// Kolik merenii se zahodilo, protoze prisla STARSI nez okno historie
        /// (<see cref="FusionConfig.HistoryWindow"/>).
        ///
        /// <para><b>Proc to ma vlastni pocitadlo.</b> Zahozeni je spravne chovani, ale bylo
        /// <b>neviditelne</b>: hlasil ho jen <c>Debug.WriteLine</c>, ktery je
        /// <c>[Conditional("DEBUG")]</c>, takze v Release nezustala zadna stopa - a v Release se
        /// meri na zarizeni. <see cref="Diagnostics"/> to ukazat nemuze: zahozene merenie do
        /// bufferu nikdy nevstoupi. Nejvic to hrozi korekci z korelace s mapou, ktera je stara
        /// o celou dobu vypoctu (194 ms na x64, na ARM vic) - kdyby se zacala zahazovat, telemetrie
        /// by dal hlasila <c>Reason = Ok</c> a vypadalo by to, ze funkce jede.
        /// Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public long DroppedTooOld
        {
            get { lock (sync) { return droppedTooOld; } }
        }

        /// <summary>
        /// Zahozena merenia (viz <see cref="DroppedTooOld"/>) rozpadla podle
        /// <see cref="IMeasurement.Source"/> - aby slo odlisit podezrele (korelace s mapou) od
        /// bezneho (opozdeny GPS fix). Vraci KOPII.
        /// </summary>
        public IReadOnlyDictionary<string, long> DroppedTooOldBySource()
        {
            lock (sync) { return new Dictionary<string, long>(droppedBySource); }
        }

        /// <summary>
        /// Byla uz polohova cast stavu inicializovana (viz <see cref="InitializePosition"/>)?
        /// Dokud ne, jsou X/Y filtru bez vyznamu - stav zacina na [0,0], coz je pri pocatku ENU
        /// roviny ve stredu mapy misto stovky metru daleko.
        /// </summary>
        public bool IsPositionInitialized
        {
            get { lock (sync) { return positionInitialized; } }
        }

        /// <summary>
        /// Nastavi polohovou cast stavu na <paramref name="x"/>, <paramref name="y"/> [m, ENU] s
        /// nejistotou <paramref name="std"/> [m] v case <paramref name="t"/>. Neni to korekce
        /// merenim, ale <b>inicializace</b> - stav se na polohu prepise a jeji kovariance se nastavi
        /// na <c>std²</c> (vcetne vynulovani korelaci polohy se zbytkem stavu).
        ///
        /// <para><b>Proc to nejde nechat na prvnim merenii polohy:</b> filtr startuje s
        /// <c>P0 = I</c>, tedy sigma = 1 m, a je-li pocatek ENU roviny ve stredu mapy, je prvni fix
        /// stovky metru daleko. NIS takoveho merenia je radove 10⁴ proti chi² prahu ~6, takze by ho
        /// gating <b>zahodil</b> a filtr by robota nikdy nenasel. Rozhodnuti "tomuhle fixu uz verim
        /// tak, ze podle nej postavim pocatek" navic patri volajicimu (mise ceka v depu na kvalitni
        /// fix a prumeruje ho), ne merici ceste. Viz doc/global-navigation-runtime.md.</para>
        ///
        /// <para>Merenia starsi nez <paramref name="t"/> se zahodi (poloha pred inicializaci nema
        /// vyznam); novejsi zustanou a prepocitaji se z noveho zakladu.</para>
        /// </summary>
        public void InitializePosition(double x, double y, double std, DateTime t)
        {
            if (std <= 0) throw new ArgumentOutOfRangeException(nameof(std), "std musi byt > 0");

            lock (sync)
            {
                InitializeAxesLocked(t,
                                     new[] { EKFModel.IX, EKFModel.IY },
                                     new[] { x, y },
                                     new[] { std, std });
                positionInitialized = true;
            }
        }

        /// <summary>
        /// Nastavi KURZ stavu na <paramref name="theta"/> [rad, matematicky] s nejistotou
        /// <paramref name="std"/> [rad] v case <paramref name="t"/>. Stejne jako u
        /// <see cref="InitializePosition"/> je to <b>inicializace</b>, ne korekce merenim.
        ///
        /// <para><b>Proc to nejde nechat na merenii kurzu</b> (tak to bylo do 19. 8. 2026): filtr
        /// startuje s <c>P0 = I</c>, tedy sigma = 1 rad (57 deg). Merenie o 170 deg vedle - a presne
        /// to nastane, kdyz robot miri na zapad - ma NIS ~8,7 proti chi²(1; 0,95) = 3,84, takze
        /// jakmile se zapnou prahy gatingu, <b>zahodi se</b>. Tataz latentni past jako u polohy.</para>
        ///
        /// <para>Dopad nebyl teoreticky: dokud kurz nekonvergoval, zapisoval <c>LocalNavigator</c>
        /// do world-kotveneho occupancy gridu bunky se spatnym kurzem, takze prvni korelace s mapou
        /// z nich vysla s OPACNYM znamenkem. Viz doc/map-correlation-localization.md.</para>
        ///
        /// <para>Kdo kurz nezna (napr. GPS fix ho nenese), tuhle metodu nevola a posila ho dal jako
        /// <c>HeadingMeasurement</c> - to zustava v platnosti.</para>
        /// </summary>
        public void InitializeHeading(double theta, double std, DateTime t)
        {
            if (std <= 0) throw new ArgumentOutOfRangeException(nameof(std), "std musi byt > 0");

            lock (sync)
            {
                // Normalizace, aby stav zustal kanonicky (jinak by rezidua pocitala s 190 misto -170).
                InitializeAxesLocked(t,
                                     new[] { EKFModel.ITh },
                                     new[] { Conversions.NormalizeOrientation(theta) },
                                     new[] { std });
            }
        }

        /// <summary>
        /// Spolecne jadro inicializaci: prepise zadane slozky stavu, prohlasi je za znama nezavisle
        /// na zbytku (vynuluje korelace, nastavi sigma²) a prerovna zaklad na cas <paramref name="t"/>.
        /// Volat pod <see cref="sync"/>.
        ///
        /// <para>Zamerne jedno misto pro polohu i kurz - dve kopie te same logiky by se casem
        /// rozesly a chyba by se projevila az na zarizeni.</para>
        /// </summary>
        private void InitializeAxesLocked(DateTime t, int[] indices, double[] values, double[] stds)
        {
            // Zaklad = aktualni stav filtru v case t (kdyz uz nejaky mame), s prepsanymi slozkami.
            var xv = (initialized ? StateAtLocked(t) ?? xBase : model.X).Clone();
            var P = (initialized ? pBase : model.P).Clone();

            for (int n = 0; n < indices.Length; n++)
                xv[indices[n]] = values[n];

            // Slozka je nyni znama nezavisle na zbytku stavu -> vynuluj korelace a nastav sigma².
            foreach (int i in indices)
                for (int k = 0; k < P.ColumnCount; k++)
                {
                    P[i, k] = 0; P[k, i] = 0;
                }

            for (int n = 0; n < indices.Length; n++)
                P[indices[n], indices[n]] = stds[n] * stds[n];

            xBase = xv;
            pBase = P;
            tBase = t;

            // Merenia z doby pred inicializaci zahodit, novejsi prepocitat z noveho zakladu.
            while (nodes.Count > 0 && nodes[0].T <= t)
                nodes.RemoveAt(0);
            dirtyFrom = 0;

            model.X = xv.Clone();
            model.P = P.Clone();

            initialized = true;
        }

        /// <summary>Krátky vypis hodnoty merenia do logu (delsi vektory se zkrati).</summary>
        private static string Format(Vector<double> z)
        {
            if (z == null) return "?";
            var sb = new System.Text.StringBuilder();
            int n = Math.Min(z.Count, 4);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(z[i].ToString("F3", CultureInfo.InvariantCulture));
            }
            if (z.Count > n) sb.Append("; ...");
            return sb.ToString();
        }

        /// <summary>Stav v case t bez zamku (volat pod <see cref="sync"/>); null = mimo okno.</summary>
        private Vector<double> StateAtLocked(DateTime t)
        {
            EnsureValid();
            if (t < tBase) return null;
            if (t == tBase) return xBase;

            int idx = LastNodeAtOrBefore(t);
            var x = idx < 0 ? xBase : nodes[idx].X;
            var P = idx < 0 ? pBase : nodes[idx].P;
            var tt = idx < 0 ? tBase : nodes[idx].T;
            return model.PredictStep(x, P, (t - tt).TotalSeconds).X;
        }

        /// <summary>Cas nejnovejsiho merenia v bufferu (resp. tBase kdyz je prazdny).</summary>
        public DateTime FilterTime
        {
            get { lock (sync) { return nodes.Count > 0 ? nodes[nodes.Count - 1].T : tBase; } }
        }

        /// <summary>Pocet merenia aktualne drzenych v okne (pro diagnostiku/testy).</summary>
        public int BufferedCount
        {
            get { lock (sync) { return nodes.Count; } }
        }

        /// <summary>Zaradi merenie k fuzi. Prepocet je odlozeny do prvniho dotazu.</summary>
        public void Enqueue(IMeasurement m)
        {
            if (m == null)
                return;

            lock (sync)
            {
            if (!initialized)
            {
                tBase = m.TimeStamp;
                xBase = model.X.Clone();
                pBase = model.P.Clone();
                nodes.Add(new Node { M = m });
                dirtyFrom = 0;               // novy uzel je zatim nespocteny
                initialized = true;
                return;
            }

            if (m.TimeStamp <= tBase)
            {
                droppedTooOld++;
                string src = m.Source ?? "?";
                droppedBySource.TryGetValue(src, out long c);
                droppedBySource[src] = c + 1;

                // TRACE, ne Debug: Debug.WriteLine je [Conditional("DEBUG")], takze v Release
                // nezustala po zahozeni ZADNA stopa - a prave v Release se meri na zarizeni.
                // Pri latenci korekce z korelace az k oknu je rozdil mezi "jede" a "nedela nic"
                // (viz doc/map-correlation-localization.md).
                //
                // Hlaska schvalne nese TYP merenia i O KOLIK bylo pozde: samo "starsi nez okno"
                // nerika, jestli pomuze vetsi okno nebo rychlejsi vypocet, a u korelace s mapou
                // chodi tri RUZNA merenia (dve osova + kurz), takze bez typu nejde poznat ktere.
                // Po Initialize* je buffer PRAZDNY, i kdyz je filtr inicializovany (uzly se
                // promazou a zustane jen bazovy checkpoint) - pak je nejnovejsi znalosti tBase.
                // Bez tohoto osetreni padal prvni opozdeny prispevek po inicializaci na index -1.
                DateTime newest = nodes.Count > 0 ? nodes[nodes.Count - 1].T : tBase;
                Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[Fusion] zahozeno mereni starsi nez okno historie: {0} '{1}' @ {2:HH:mm:ss.fff}"
                    + " z=[{3}] - opozdeno o {4:F0} ms za nejnovejsim ({5:HH:mm:ss.fff}),"
                    + " okno je {6:F0} ms (tBase={7:HH:mm:ss.fff})",
                    m.GetType().Name, src, m.TimeStamp, Format(m.Value),
                    (newest - m.TimeStamp).TotalMilliseconds, newest,
                    window.TotalMilliseconds, tBase));

                // Zahozene merenie do bufferu nevstoupi, takze verdikt je konecny uz tady.
                Report(m, double.NaN, false, MeasurementVerdict.TooOld);
                return;
            }

            int i = InsertIndex(m.TimeStamp);
            nodes.Insert(i, new Node { M = m });
            // vse od mista vlozeni (vcetne) je nyni neplatne; drivejsi checkpointy plati dal
            if (i < dirtyFrom)
                dirtyFrom = i;

            Prune();
            }
        }

        /// <summary>Index, kam vlozit merenie s casem t, aby zustal buffer serazen vzestupne.</summary>
        private int InsertIndex(DateTime t)
        {
            int i = nodes.Count;
            while (i > 0 && nodes[i - 1].T > t)
                i--;
            return i;
        }

        /// <summary>Dopocita neplatny ocas bufferu [dirtyFrom .. konec] z posledniho platneho checkpointu.</summary>
        private void EnsureValid()
        {
            if (dirtyFrom >= nodes.Count)
                return;

            Vector<double> x;
            Matrix<double> P;
            DateTime t;
            if (dirtyFrom == 0)
            {
                x = xBase; P = pBase; t = tBase;
            }
            else
            {
                var prev = nodes[dirtyFrom - 1];
                x = prev.X; P = prev.P; t = prev.T;
            }

            for (int k = dirtyFrom; k < nodes.Count; k++)
            {
                var node = nodes[k];
                var pr = model.PredictStep(x, P, (node.T - t).TotalSeconds);
                var up = model.UpdateStep(pr.X, pr.P, node.M);
                node.X = up.X;
                node.P = up.P;
                node.Nis = up.Nis;
                node.Accepted = up.Accepted;
                x = up.X; P = up.P; t = node.T;
            }
            dirtyFrom = nodes.Count;

            // synchronizace instancniho stavu modelu s aktualnim (nejnovejsim) checkpointem
            var last = nodes[nodes.Count - 1];
            model.X = last.X.Clone();
            model.P = last.P.Clone();
        }

        private void Prune()
        {
            var lastT = nodes[nodes.Count - 1].T;
            while (nodes.Count > 0 && lastT - nodes[0].T > window)
            {
                var n0 = nodes[0];
                if (dirtyFrom > 0)
                {
                    // checkpoint nejstarsiho uzlu je platny -> je to primo novy bazovy stav
                    xBase = n0.X; pBase = n0.P;
                    ReportFinal(n0);
                }
                else
                {
                    // nejstarsi uzel jeste nebyl spocten -> zapec ho do baze jednim krokem
                    var pr = model.PredictStep(xBase, pBase, (n0.T - tBase).TotalSeconds);
                    var up = model.UpdateStep(pr.X, pr.P, n0.M);
                    xBase = up.X; pBase = up.P;
                    n0.Nis = up.Nis; n0.Accepted = up.Accepted;
                    ReportFinal(n0);
                }
                tBase = n0.T;
                nodes.RemoveAt(0);
                if (dirtyFrom > 0)
                    dirtyFrom--;
            }
        }

        /// <summary>
        /// Konecny verdikt uzlu, ktery prave vypadava z okna historie (uz se neprepocita).
        /// </summary>
        private void ReportFinal(Node n)
            => Report(n.M, n.Nis, n.Accepted,
                      n.Accepted ? MeasurementVerdict.Accepted : MeasurementVerdict.GatedOut);

        /// <summary>
        /// Ohlasi verdikt odberateli <see cref="OnMeasurement"/>. Bez odberatele se nic nepocita
        /// ani nealokuje (kopie z/R je jinak alokace na kazde merenie, tedy stovky za sekundu).
        /// </summary>
        private void Report(IMeasurement m, double nis, bool accepted, MeasurementVerdict verdict)
        {
            var sink = OnMeasurement;
            if (sink == null) return;

            sink(new MeasurementInfo
            {
                Source = m.Source ?? "?",
                Time = m.TimeStamp,
                Nis = nis,
                Accepted = accepted,
                Verdict = verdict,
                Z = m.Value?.ToArray(),
                DiagR = Diagonal(m.NoiseCovariance),
            });
        }

        /// <summary>Diagonala matice jako pole (null pro null matici).</summary>
        private static double[] Diagonal(Matrix<double> r)
        {
            if (r == null) return null;
            int n = Math.Min(r.RowCount, r.ColumnCount);
            var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = r[i, i];
            return d;
        }

        /// <summary>Index posledniho uzlu s casem &lt;= t (-1 kdyz zadny takovy neni).</summary>
        private int LastNodeAtOrBefore(DateTime t)
        {
            int lo = 0, hi = nodes.Count - 1, res = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (nodes[mid].T <= t) { res = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return res;
        }

        /// <summary>
        /// Odhad stavu v case t (read-only vuci logice filtru, jen dopocita liny ocas).
        /// - t &gt;= cas posledniho merenia: dopREDna predikce z posledniho stavu (na "ted"/budoucnost).
        /// - t v okne historie: najde nejblizsi drivejsi checkpoint a dopredikuje do t (filtrovany
        ///   odhad platny v case t; ne smoother - nepouziva pozdejsi merenia).
        /// - t == tBase: vraci presne bazovy stav (zadna extrapolace, odhad tam plati).
        /// - <b>t &lt; tBase (mimo okno historie): vraci <c>null</c></b>.
        ///
        /// <para><b>Proc null a ne "nejlepsi snaha":</b> drive se v tomto pripade vracel bazovy stav,
        /// tedy poza az o <c>FusionConfig.HistoryWindow</c> (1 s) stara, a volajici to nijak nepoznal.
        /// Pri 0,8 m/s je to 80 cm - zapsat takovou pozu do lokalni mapy ji otravi mnohem hur, nez kdyz
        /// jeden snimek chybi. Volajici tedy MUSI null osetrit: <c>ControlLoop</c> zastavi (bezpecny
        /// stav), <c>LocalNavigator</c> snimek zahodi. Viz doc/occupancy-and-local-planning.md.</para>
        ///
        /// <para>Pripad "jeste nedoslo zadne merenie" (<c>initialized == false</c>) zustava beze zmeny -
        /// vraci pocatecni stav modelu, aby se pri startu emitoval <c>RobotStateMsg</c>.</para>
        /// </summary>
        /// <returns>Odhad stavu, nebo <c>null</c>, je-li <paramref name="t"/> mimo okno historie.</returns>
        public RobotState GetStateAt(DateTime t)
        {
            lock (sync)
            {
                if (!initialized)
                    return model.Current(t);

                EnsureValid();

                if (t < tBase)
                    return null;                                        // mimo okno -> "nevim"
                if (t == tBase)
                    return model.ToRobotState(xBase, pBase, t);         // presne na bazi odhad plati

                int idx = LastNodeAtOrBefore(t);
                Vector<double> x;
                Matrix<double> P;
                DateTime tt;
                if (idx < 0)
                {
                    x = xBase; P = pBase; tt = tBase;
                }
                else
                {
                    x = nodes[idx].X; P = nodes[idx].P; tt = nodes[idx].T;
                }

                var fin = model.PredictStep(x, P, (t - tt).TotalSeconds);
                return model.ToRobotState(fin.X, fin.P, t);
            }
        }

        /// <summary>
        /// Diagnosticke NIS/prijeti pro merenia aktualne v okne (nejstarsi -&gt; nejnovejsi).
        /// Dopocita liny ocas, aby byly NIS platne.
        /// </summary>
        public IReadOnlyList<MeasurementInfo> Diagnostics()
        {
            lock (sync)
            {
                EnsureValid();
                var list = new List<MeasurementInfo>(nodes.Count);
                foreach (var n in nodes)
                    list.Add(new MeasurementInfo { Source = n.M.Source, Time = n.T, Nis = n.Nis, Accepted = n.Accepted });
                return list;
            }
        }
    }
}
