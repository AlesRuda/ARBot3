using System;
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
        }

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

        public AsyncFusionEngine(EKFModel model, TimeSpan? historyWindow = null)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            window = historyWindow ?? model.Config.HistoryWindow;
        }

        public EKFModel Model => model;

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
                Debug.WriteLine(string.Format(
                    "[Fusion] zahozeno merenie starsi nez okno: {0} @ {1:HH:mm:ss.fff} (tBase={2:HH:mm:ss.fff})",
                    m.Source, m.TimeStamp, tBase));
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
                }
                else
                {
                    // nejstarsi uzel jeste nebyl spocten -> zapec ho do baze jednim krokem
                    var pr = model.PredictStep(xBase, pBase, (n0.T - tBase).TotalSeconds);
                    var up = model.UpdateStep(pr.X, pr.P, n0.M);
                    xBase = up.X; pBase = up.P;
                }
                tBase = n0.T;
                nodes.RemoveAt(0);
                if (dirtyFrom > 0)
                    dirtyFrom--;
            }
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
