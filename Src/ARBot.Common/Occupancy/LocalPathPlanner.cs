using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Regulators;

// Zamerne BCL prioritni fronta (4-arni halda, Clear() drzi kapacitu -> zadna alokace na volani),
// ne ARBot.Common.Common.PriorityQueue (SortedDictionary + List na kazdou prioritu = GC churn).
// Alias resi kolizi jmen mezi obema typy.
using OpenQueue = System.Collections.Generic.PriorityQueue<int, double>;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Lokalni planovac cesty nad <see cref="OccupancyGrid"/>: z aktualni pozy robotu a cilove polohy
    /// vyrobi <see cref="RegulatorWayPoint"/>[] pro <see cref="IPathPlanner"/>.
    /// Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para><b>Cena = jizdni cas.</b> Cena hrany je <c>delka / v_limit(odstup)</c>, tvrdy odstup
    /// <see cref="LocalPlannerConfig.SafeDist"/> je zvlast jako neprujezdnost. Tim se pozadavek
    /// "drz se od prekazek dal, ale kdyz neni mista dost, smis blize za cenu nizsi rychlosti" stane
    /// jedinou cenovou funkci - siroky koridor je rychly (levny), uzky pomaly (drahy, ale pouzitelny).</para>
    ///
    /// <para><b>Cena pocatecniho otoceni</b> (<c>|Δθ| / ω_max</c>) je soucasti ceny prvni hrany.
    /// Neni to trik na stabilitu, ale poctivejsi model: cesta vyzadujici otocku o 90° na miste opravdu
    /// trva delsi dobu. Diky tomu se strana objezdu prekazky preklopi jen tehdy, kdyz je druha varianta
    /// lepsi VIC, nez stoji to otoceni - a neni potreba zadna hystereze (drzet plan spocteny nad starsi
    /// mapou = jet proti dukazum, ktere robot uz ma).</para>
    ///
    /// <para><b>Plan se pocita cely znovu pri kazdem volani</b> z aktualniho stavu gridu; nic starsiho
    /// se nepretahuje. Poradi expanze je deterministicke (pevne poradi sousedu + tie-break na indexu),
    /// takze stejny vstup da stejny vystup.</para>
    ///
    /// <para><b>Vlaknova bezpecnost:</b> zadna (znovupouzite buffery). Jedna instance = jedno vlakno.</para>
    /// </summary>
    public sealed class LocalPathPlanner
    {
        // Osmiokoli v PEVNEM poradi (determinismus). Diagonaly jsou az za ortogonalami.
        private static readonly int[] NeighDx = { 1, 0, -1, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighDy = { 0, 1, 0, -1, 1, -1, 1, -1 };

        private readonly int size;
        private readonly LocalPlannerConfig cfg;

        // Znovupouzite buffery (velikost size*size, lokalni indexovani i + j*size).
        private readonly byte[] state;        // CellState po bunkach (snapshot gridu)
        private readonly byte[] blockReason;  // CellBlockReason po bunkach (cim je blokovana)
        private readonly float[] clearance;   // odstup [m] po bunkach (snapshot pole vzdalenosti)
        private readonly double[] gScore;
        private readonly float[] lenFromStart;
        private readonly int[] parent;
        private readonly int[] stamp;         // generace "bunka ma platne gScore" - nahrazuje mazani poli
        private readonly int[] closedStamp;   // generace "bunka je uz expandovana" (lazy deletion ve fronte)

        /// <summary>Rezim UNIKU z blokovane bunky - meni pravidlo prujezdnosti i cil hledani.</summary>
        private bool escape;

        /// <summary>Index vychozi bunky (pri uniku je vzdy prujezdna - robot na ni stoji).</summary>
        private int startIdx;
        private readonly OpenQueue open = new OpenQueue();
        private readonly List<int> pathCells = new List<int>();
        private readonly List<int> pulled = new List<int>();
        private int generation;

        // Vzorkovani vysledne lomene cary (znovupouzite, aby Plan nealokoval na kazde volani).
        private readonly List<double> sampleS = new List<double>();
        private readonly List<float> sampleClear = new List<float>();
        private readonly List<bool> sampleFree = new List<bool>();
        private double[] frontierAfter = new double[0];
        private double[] nodeS = new double[0];
        private int[] nodeSample = new int[0];

        // Rozpad rychlostni obalky posledniho planu (naplni BuildWayPoints, prectе Plan do vysledku).
        private double envMinFreeAhead, envMinVClear, envMinVBrake, envMinSpeed;

        /// <summary>Konfigurace planovace.</summary>
        public LocalPlannerConfig Config => cfg;

        /// <param name="size">Pocet bunek na stranu gridu, se kterym se bude planovat.</param>
        /// <param name="config">Konfigurace; null = vychozi (hodnoty z <c>Profile</c>).</param>
        public LocalPathPlanner(int size, LocalPlannerConfig config = null)
        {
            if (size <= 0) throw new ArgumentException($"LocalPathPlanner: size musi byt > 0, je {size}.");
            this.size = size;
            cfg = config ?? new LocalPlannerConfig();
            cfg.Validate();

            int n = size * size;
            state = new byte[n];
            blockReason = new byte[n];
            clearance = new float[n];
            gScore = new double[n];
            lenFromStart = new float[n];
            parent = new int[n];
            stamp = new int[n];
            closedStamp = new int[n];
        }

        /// <summary>
        /// Naplanuje cestu z pozy robotu k cili.
        /// </summary>
        /// <param name="grid">Occupancy grid (musi byt vycentrovany na robota).</param>
        /// <param name="field">Pole vzdalenosti; MUSI byt prepoctene ze stejneho stavu gridu
        /// (<see cref="ClearanceField.Build"/>).</param>
        /// <param name="robotX">Poloha robotu [m, world ENU].</param>
        /// <param name="robotY">Poloha robotu [m, world ENU].</param>
        /// <param name="heading">Kurz robotu [rad] (0 = vychod, +CCW) - pro cenu pocatecniho otoceni.</param>
        /// <param name="goalX">Cil [m, world ENU].</param>
        /// <param name="goalY">Cil [m, world ENU].</param>
        public LocalPlanResult Plan(OccupancyGrid grid, ClearanceField field,
                                    double robotX, double robotY, double heading,
                                    double goalX, double goalY)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (grid.Size != size)
                throw new ArgumentException($"LocalPathPlanner: grid ma Size {grid.Size}, planovac {size}.");
            if (field.OriginX != grid.OriginX || field.OriginY != grid.OriginY)
                throw new ArgumentException(
                    "LocalPathPlanner: ClearanceField neni prepoctene z aktualniho stavu gridu (jiny origin).");

            var res = new LocalPlanResult
            {
                RequestedGoalX = goalX,
                RequestedGoalY = goalY,
                ReachedGoalX = goalX,
                ReachedGoalY = goalY,
            };

            double cell = grid.Resolution;
            int i0 = grid.CellX(robotX) - grid.OriginX;
            int j0 = grid.CellY(robotY) - grid.OriginY;
            if ((uint)i0 >= (uint)size || (uint)j0 >= (uint)size)
            {
                res.Status = LocalPlanStatus.RobotOutsideGrid;
                return res;
            }

            Snapshot(grid, field);

            // Robot UZ STOJI v blokovane bunce (dojel tam, nez se to o ni vedelo, nebo se posunula
            // mapa). Vracet RobotBlocked znamena stat tam navzdy - misto toho se hleda nejkratsi
            // cesta VEN. Viz doc/occupancy-and-local-planning.md.
            if (state[i0 + j0 * size] == (byte)CellState.Blocked)
                return PlanEscape(res, grid, robotX, robotY, heading, i0, j0, cell);

            // Cil orizneme na grid (o bunku od kraje) - dalsi cil znamena jet k hranici gridu jeho smerem.
            bool goalClipped = ClipToGrid(grid, robotX, robotY, ref goalX, ref goalY);
            res.ReachedGoalX = goalX;
            res.ReachedGoalY = goalY;

            double dxGoal = goalX - robotX, dyGoal = goalY - robotY;
            if (Math.Sqrt(dxGoal * dxGoal + dyGoal * dyGoal) <= cfg.EpsMax)
            {
                res.Status = LocalPlanStatus.AlreadyAtGoal;
                return res;
            }

            int iG = Clamp(grid.CellX(goalX) - grid.OriginX, 0, size - 1);
            int jG = Clamp(grid.CellY(goalY) - grid.OriginY, 0, size - 1);

            int goalIdx = Search(i0, j0, iG, jG, heading, cell, out int bestIdx, out int expanded);
            res.ExpandedCells = expanded;

            int target = goalIdx >= 0 ? goalIdx : bestIdx;
            if (target < 0)
            {
                res.Status = LocalPlanStatus.NoRoute;
                return res;
            }

            res.Status = (goalIdx >= 0 && !goalClipped) ? LocalPlanStatus.Ok : LocalPlanStatus.Partial;
            res.CostSeconds = gScore[target];
            res.LengthM = lenFromStart[target];

            // Skutecne dosazeny bod = stred cilove bunky.
            res.ReachedGoalX = grid.CenterX(grid.OriginX + target % size);
            res.ReachedGoalY = grid.CenterY(grid.OriginY + target / size);

            BuildPathCells(target);
            StringPull(grid, i0, j0);

            res.WayPoints = BuildWayPoints(grid, robotX, robotY,
                                           finalGoal: res.Status == LocalPlanStatus.Ok,
                                           minClearance: out double minClear);
            res.MinClearanceM = minClear;

            // Rozpad rychlostni obalky (diagnostika "proc robot leze") - viz LocalPlanResult.
            res.MinFreeAheadM = envMinFreeAhead;
            res.MinVClear = envMinVClear;
            res.MinVBrake = envMinVBrake;
            res.MinWayPointSpeed = envMinSpeed;

            if (res.WayPoints == null || res.WayPoints.Length < 2)
                res.Status = LocalPlanStatus.AlreadyAtGoal;   // cil je blize nez jedna pouzitelna hrana

            return res;
        }

        // ---------------- unik z blokovane bunky ----------------

        /// <summary>
        /// Robot stoji v blokovane bunce - najde nejkratsi cestu k nejblizsi bunce, odkud muze
        /// pokracovat BEZNE planovani (neni blokovana a ma odstup &ge; <c>SafeDist</c>).
        ///
        /// <para><b>Delici cara je kanal, ne vzdalenost:</b> ven se smi pres bunky blokovane
        /// SEMANTIKOU (z travy zpatky na cestu), pres geometricky blokovane NIKDY. Vychozi bunka je
        /// vyjimka - robot na ni stoji, takze z ni odjet musi i kdyz ji blokuje geometrie (to je
        /// typicky posun mapy chybou lokalizace).</para>
        ///
        /// <para>Draha je omezena na <see cref="LocalPlannerConfig.EscapeMaxLength"/>: kdyz je
        /// nejblizsi legalni bunka dal, unik se nezkousi a vraci se
        /// <see cref="LocalPlanStatus.RobotBlocked"/> - bloudit metry mimo cestu je horsi nez stat.
        /// Rychlost neresi zadny zvlastni strop: rychlostni obalka v <see cref="BuildWayPoints"/>
        /// srazi rychlost sama (uvnitr skvrny neni nic potvrzene sjizdneho pred robotem), takze
        /// unik je popojeti krokem.</para>
        /// </summary>
        private LocalPlanResult PlanEscape(LocalPlanResult res, OccupancyGrid grid,
                                           double robotX, double robotY, double heading,
                                           int i0, int j0, double cell)
        {
            escape = true;
            try
            {
                int exit = Search(i0, j0, -1, -1, heading, cell, out _, out int expanded);
                res.ExpandedCells = expanded;

                if (exit < 0)
                {
                    // Legalni bunka v dosahu neexistuje (nebo vede jen pres geometrii) - stat.
                    res.Status = LocalPlanStatus.RobotBlocked;
                    return res;
                }

                res.Status = LocalPlanStatus.EscapingBlocked;
                res.CostSeconds = gScore[exit];
                res.LengthM = lenFromStart[exit];
                res.ReachedGoalX = grid.CenterX(grid.OriginX + exit % size);
                res.ReachedGoalY = grid.CenterY(grid.OriginY + exit / size);

                BuildPathCells(exit);
                StringPull(grid, i0, j0);

                // finalGoal: true - na konci uniku robot zastavi a dalsi cyklus uz planuje bezne.
                res.WayPoints = BuildWayPoints(grid, robotX, robotY,
                                               finalGoal: true, minClearance: out double minClear);
                res.MinClearanceM = minClear;
                res.MinFreeAheadM = envMinFreeAhead;
                res.MinVClear = envMinVClear;
                res.MinVBrake = envMinVBrake;
                res.MinWayPointSpeed = envMinSpeed;

                // Vylez blize nez jedna pouzitelna hrana - neni co predat regulatoru.
                if (res.WayPoints == null || res.WayPoints.Length < 2)
                    res.Status = LocalPlanStatus.RobotBlocked;

                return res;
            }
            finally
            {
                escape = false;
            }
        }

        /// <summary>Je bunka prujezdna BEZNYM pravidlem? Tam unik konci.</summary>
        private bool IsEscapeExit(int idx)
            => state[idx] != (byte)CellState.Blocked && clearance[idx] >= cfg.SafeDist;

        // ---------------- snapshot gridu ----------------

        /// <summary>Prekopiruje stav bunek a odstupy do lokalne indexovanych bufferu (i + j*size),
        /// aby hot loopy nemusely pocitat index kruhoveho bufferu.</summary>
        private void Snapshot(OccupancyGrid grid, ClearanceField field)
        {
            for (int j = 0; j < size; j++)
            {
                int row = j * size;
                for (int i = 0; i < size; i++)
                {
                    int local = grid.LocalIndex(i, j);
                    state[row + i] = (byte)grid.StateAt(local);
                    blockReason[row + i] = (byte)grid.BlockReasonAt(local);
                    clearance[row + i] = field.DistanceLocal(i, j);
                }
            }
        }

        // ---------------- A* ----------------

        /// <summary>
        /// A* z (i0,j0) do (iG,jG). Vraci index cilove bunky, nebo -1, kdyz cil neni dosazitelny;
        /// v <paramref name="bestIdx"/> pak vraci nejlepsi dosazitelnou bunku ve smyslu vzdalenosti
        /// k cili (fallback "jed alespon co nejbliz").
        /// </summary>
        private int Search(int i0, int j0, int iG, int jG, double heading, double cell,
                           out int bestIdx, out int expanded)
        {
            generation++;
            open.Clear();

            startIdx = i0 + j0 * size;
            double escapeR2 = cfg.EscapeRadius * cfg.EscapeRadius;
            // Pri uniku neni cil bod, ale "prvni legalni bunka" - heuristika by nemela k cemu merit,
            // takze se hleda uniformni cenou (Dijkstra) a jen do EscapeMaxLength.
            double horizon = escape ? cfg.EscapeMaxLength : cfg.HorizonM;
            double invMaxSpeed = 1.0 / cfg.MaxSpeed;
            double diag = Math.Sqrt(2.0) * cell;

            stamp[startIdx] = generation;
            gScore[startIdx] = 0;
            lenFromStart[startIdx] = 0;
            parent[startIdx] = -1;
            open.Enqueue(startIdx, escape ? 0 : Heuristic(i0, j0, iG, jG, cell, invMaxSpeed));

            bestIdx = startIdx;
            double bestGoalDist2 = Dist2Cells(i0, j0, iG, jG);
            expanded = 0;

            while (open.TryDequeue(out int cur, out double _))
            {
                if (closedStamp[cur] == generation) continue;   // uz expandovano (duplikat ve fronte)
                closedStamp[cur] = generation;
                expanded++;

                int ci = cur % size, cj = cur / size;
                // Pri uniku je cilem prvni bunka prujezdna BEZNYM pravidlem, ne konkretni bod.
                if (escape ? IsEscapeExit(cur) : (ci == iG && cj == jG))
                    return cur;
                double d2 = Dist2Cells(ci, cj, iG, jG);
                if (d2 < bestGoalDist2)
                {
                    bestGoalDist2 = d2;
                    bestIdx = cur;
                }

                // Horizont lokalniho planu.
                if (lenFromStart[cur] >= horizon) continue;

                for (int k = 0; k < 8; k++)
                {
                    int ni = ci + NeighDx[k];
                    int nj = cj + NeighDy[k];
                    if ((uint)ni >= (uint)size || (uint)nj >= (uint)size) continue;

                    int nidx = ni + nj * size;
                    if (closedStamp[nidx] == generation) continue;

                    if (!Passable(nidx, ni, nj, i0, j0, cell, escapeR2, out double clr)) continue;

                    bool diagonal = k >= 4;
                    if (diagonal)
                    {
                        // Bez rezani rohu: oba orto sousedi musi byt take prujezdni.
                        int a = ni + cj * size;
                        int b = ci + nj * size;
                        if (!Passable(a, ni, cj, i0, j0, cell, escapeR2, out _)) continue;
                        if (!Passable(b, ci, nj, i0, j0, cell, escapeR2, out _)) continue;
                    }

                    double stepLen = diagonal ? diag : cell;
                    double stepCost = stepLen / cfg.VCost(clr);
                    if (state[nidx] == (byte)CellState.Unknown)
                        stepCost *= cfg.UnknownCostFactor;
                    // Pri uniku se pres semanticky blokovane bunky smi, ale drazeji - unik ma
                    // mimo cestu strávit co nejmene.
                    if (escape && state[nidx] == (byte)CellState.Blocked)
                        stepCost *= cfg.EscapeBlockedCostFactor;

                    // Cena pocatecniho otoceni je soucasti prvni hrany.
                    if (cur == startIdx)
                    {
                        double dir = Math.Atan2(NeighDy[k], NeighDx[k]);
                        double dTheta = Math.Abs(Conversions.NormalizeOrientation(dir - heading));
                        stepCost += dTheta / cfg.MaxRotationSpeed;
                    }

                    double ng = gScore[cur] + stepCost;
                    if (stamp[nidx] == generation && ng >= gScore[nidx]) continue;

                    stamp[nidx] = generation;
                    gScore[nidx] = ng;
                    lenFromStart[nidx] = (float)(lenFromStart[cur] + stepLen);
                    parent[nidx] = cur;
                    open.Enqueue(nidx, escape ? ng : ng + Heuristic(ni, nj, iG, jG, cell, invMaxSpeed));
                }
            }

            return -1;
        }

        private double Heuristic(int i, int j, int iG, int jG, double cell, double invMaxSpeed)
            => Math.Sqrt(Dist2Cells(i, j, iG, jG)) * cell * invMaxSpeed;

        private static double Dist2Cells(int i, int j, int iG, int jG)
        {
            double di = i - iG, dj = j - jG;
            return di * di + dj * dj;
        }

        /// <summary>
        /// Je bunka prujezdna? Tvrde pravidlo je <c>odstup &gt;= SafeDist</c>.
        /// <para>Vyjimka <b>eskapovaci zona</b>: v okoli <see cref="LocalPlannerConfig.EscapeRadius"/>
        /// od vychozi bunky se pripousti i mensi odstup (ale NIKDY
        /// <see cref="CellState.Blocked"/>). Bez ni by robot, ktery zastavil blize u prekazky, nemel
        /// zadnou vychozi bunku a nemohl by odjet. Dal od robotu se odstup nikdy neslevuje.</para>
        /// </summary>
        private bool Passable(int idx, int i, int j, int i0, int j0, double cell, double escapeR2,
                              out double clr)
        {
            clr = clearance[idx];

            // UNIK z blokovane bunky ma vlastni pravidlo: rozhoduje KANAL, ne odstup.
            if (escape)
            {
                if (idx == startIdx) return true;   // na vychozi bunce robot stoji, odjet z ni musi
                return (blockReason[idx] & (byte)CellBlockReason.Geometry) == 0;
            }

            if (state[idx] == (byte)CellState.Blocked) return false;
            if (clr >= cfg.SafeDist) return true;

            double di = (i - i0) * cell, dj = (j - j0) * cell;
            return di * di + dj * dj <= escapeR2;
        }

        // ---------------- rekonstrukce a zjednoduseni drahy ----------------

        private void BuildPathCells(int target)
        {
            pathCells.Clear();
            for (int c = target; c >= 0; c = parent[c])
                pathCells.Add(c);
            pathCells.Reverse();
        }

        /// <summary>
        /// String-pulling: slucuje po sobe jdouci bunky do useku, dokud podel cele usecky plati
        /// stejne pravidlo prujezdnosti jako v A*. Vysledkem je kratky seznam vrcholu.
        /// </summary>
        private void StringPull(OccupancyGrid grid, int i0, int j0)
        {
            pulled.Clear();
            if (pathCells.Count == 0) return;

            double cell = grid.Resolution;
            double escapeR2 = cfg.EscapeRadius * cfg.EscapeRadius;

            pulled.Add(pathCells[0]);
            int anchor = 0;
            while (anchor < pathCells.Count - 1)
            {
                int next = anchor + 1;
                for (int probe = pathCells.Count - 1; probe > anchor + 1; probe--)
                {
                    if (SegmentPassable(grid, pathCells[anchor], pathCells[probe], i0, j0, cell, escapeR2))
                    {
                        next = probe;
                        break;
                    }
                }
                pulled.Add(pathCells[next]);
                anchor = next;
            }
        }

        /// <summary>Je cela usecka mezi stredy dvou bunek prujezdna? Vzorkuje se s krokem 1/2 bunky.</summary>
        private bool SegmentPassable(OccupancyGrid grid, int fromIdx, int toIdx,
                                     int i0, int j0, double cell, double escapeR2)
        {
            double x0 = fromIdx % size, y0 = fromIdx / size;
            double x1 = toIdx % size, y1 = toIdx / size;
            double dx = x1 - x0, dy = y1 - y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int steps = (int)Math.Ceiling(len * 2.0) + 1;

            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                int i = (int)Math.Round(x0 + dx * t);
                int j = (int)Math.Round(y0 + dy * t);
                if ((uint)i >= (uint)size || (uint)j >= (uint)size) return false;
                if (!Passable(i + j * size, i, j, i0, j0, cell, escapeR2, out _)) return false;
            }
            return true;
        }

        // ---------------- waypointy ----------------

        /// <summary>
        /// Prevede zjednodusenou drahu na <see cref="RegulatorWayPoint"/>[]:
        /// <list type="bullet">
        /// <item><description><c>Speed</c> = strop z rychlostni obalky (bocni odstup + brzdna obalka
        /// k hranici potvrzene prujezdneho). U mezilehlych uzlu NIKDY 0 - <c>PathPlanner</c> chape
        /// <c>Speed == 0</c> jako "bez stropu", takze nula by strop naopak zrusila. Nula patri jen
        /// poslednimu uzlu, kde znamena zastaveni.</description></item>
        /// <item><description><c>MaxPositionError</c> = skutecna volna rezerva
        /// (<c>odstup - SafeDist</c>), takze zaobleni rohu obloukem nikdy nezasahne do bezpecnostniho
        /// odstupu.</description></item>
        /// </list>
        /// </summary>
        private RegulatorWayPoint[] BuildWayPoints(OccupancyGrid grid, double robotX, double robotY,
                                                   bool finalGoal, out double minClearance)
        {
            minClearance = double.MaxValue;
            envMinFreeAhead = double.MaxValue;
            envMinVClear = double.MaxValue;
            envMinVBrake = double.MaxValue;
            envMinSpeed = double.MaxValue;
            if (pulled.Count < 1) return null;

            // Vrcholy ve svetovych souradnicich; prvni bod je SKUTECNA poloha robotu (ne stred bunky).
            var xs = new List<double>(pulled.Count + 1);
            var ys = new List<double>(pulled.Count + 1);
            xs.Add(robotX);
            ys.Add(robotY);
            double minStep = grid.Resolution * 0.5;
            for (int k = 1; k < pulled.Count; k++)
            {
                double x = grid.CenterX(grid.OriginX + pulled[k] % size);
                double y = grid.CenterY(grid.OriginY + pulled[k] / size);
                double dx = x - xs[xs.Count - 1], dy = y - ys[ys.Count - 1];
                if (dx * dx + dy * dy < minStep * minStep) continue;   // PathPlanner nesnese nulovy usek
                xs.Add(x);
                ys.Add(y);
            }
            int n = xs.Count;
            if (n < 2) return null;

            // Jeden vzorkovaci pruchod CELOU lomenou carou (arc-length s). Uzly jsou take vzorky.
            SamplePath(grid, xs, ys, n);

            // Vzdalenost k prvni NE-Free bunce OD KAZDEHO VZORKU DOPREDU (pruchod od konce).
            //
            // Frontier se inicializuje na KONEC DRAHY, ne na nekonecno: za poslednim uzlem uz nic
            // overeneho neni (horizont planu, kraj gridu, konec potvrzene sjizdne plochy). Bez toho
            // by posledni uzel dostal plnou rychlost a robot by do neoverenoho prostoru vlétl s
            // brzdnou drahou ~1 m. U skutecneho cile je to jedno (tam je Speed = 0 tak jako tak),
            // ale u mezilehlych uzlu to spravne vynuti, ze se lze zastavit na konci znameho.
            int m = sampleS.Count;
            if (frontierAfter.Length < m) frontierAfter = new double[m * 2];
            double frontier = nodeS[n - 1];
            for (int i = m - 1; i >= 0; i--)
            {
                if (!sampleFree[i]) frontier = sampleS[i];
                frontierAfter[i] = frontier;
            }

            var wps = new RegulatorWayPoint[n];
            for (int k = 0; k < n; k++)
            {
                // Okno uzlu pro ODSTUP = usek k nemu vedouci + usek z nej vychazejici. Kazdy vzorek je
                // tak zastropovan alespon jednim uzlem -> po zpetnem pruchodu PathPlanneru konzervativni.
                double sFrom = k > 0 ? nodeS[k - 1] : nodeS[0];
                double sTo = k < n - 1 ? nodeS[k + 1] : nodeS[n - 1];
                double clr = double.MaxValue;
                for (int i = 0; i < m; i++)
                {
                    if (sampleS[i] < sFrom || sampleS[i] > sTo) continue;
                    if (sampleClear[i] < clr) clr = sampleClear[i];
                }
                if (clr == double.MaxValue) clr = sampleClear[nodeSample[k]];
                if (clr < minClearance) minClearance = clr;

                // Brzdna obalka: vzdalenost k hranici potvrzeneho, merena OD TOHOTO UZLU dopredu.
                double freeAhead = frontierAfter[nodeSample[k]] - nodeS[k];
                if (freeAhead < 0) freeAhead = 0;

                double vClear = cfg.VClear(clr), vBrake = cfg.VBrake(freeAhead);
                double v = Math.Min(vClear, vBrake);
                bool last = k == n - 1;

                // Diagnostika obalky - jen mezilehle uzly: v poslednim je Speed = 0 z definice
                // (konec drahy), takze by minimum vzdycky vyslo tam a nic by nereklo.
                if (!last)
                {
                    if (freeAhead < envMinFreeAhead) envMinFreeAhead = freeAhead;
                    if (vClear < envMinVClear) envMinVClear = vClear;
                    if (vBrake < envMinVBrake) envMinVBrake = vBrake;
                    double speed = Math.Max(cfg.MinCostSpeed, v);
                    if (speed < envMinSpeed) envMinSpeed = speed;
                }

                wps[k] = new RegulatorWayPoint
                {
                    X = xs[k],
                    Y = ys[k],
                    // Mezilehly uzel: strop musi byt KLADNY - PathPlanner chape Speed == 0 jako
                    // "bez stropu", takze nula by strop naopak zrusila. Za hranici potvrzeneho tedy
                    // strop klesne na MinCostSpeed (plouzeni ~5 cm/s), ne presne na nulu: tvrde
                    // zastaveni by mohlo zadrhnout (stani samo prostor nedosviti), zatimco plouzeni
                    // ho vyjasni. Tvrda garance zustava jinde - bunky Blocked na draze nejsou a
                    // odstup SafeDist se nikdy neporusi.
                    Speed = last ? (finalGoal ? 0.0 : Math.Max(0.0, v))
                                 : Math.Max(cfg.MinCostSpeed, v),
                    MaxPositionError = Clamp(clr - cfg.SafeDist, cfg.EpsMin, cfg.EpsMax),
                };
            }

            return wps;
        }

        /// <summary>
        /// Navzorkuje celou lomenou caru krokem 1/2 bunky do <see cref="sampleS"/> (arc-length),
        /// <see cref="sampleClear"/> (odstup) a <see cref="sampleFree"/> (je bunka potvrzene sjizdna?).
        /// Uzly jsou take vzorky - jejich indexy jdou do <see cref="nodeSample"/>, arc-length do
        /// <see cref="nodeS"/>. Bod mimo grid se bere jako ne-Free s odstupem 0.
        /// </summary>
        private void SamplePath(OccupancyGrid grid, List<double> xs, List<double> ys, int n)
        {
            sampleS.Clear();
            sampleClear.Clear();
            sampleFree.Clear();
            if (nodeS.Length < n) { nodeS = new double[n * 2]; nodeSample = new int[n * 2]; }

            double step = grid.Resolution * 0.5;
            double s = 0;
            for (int k = 0; k < n; k++)
            {
                nodeS[k] = s;
                nodeSample[k] = sampleS.Count;
                AddSample(grid, xs[k], ys[k], s);

                if (k == n - 1) break;

                double dx = xs[k + 1] - xs[k], dy = ys[k + 1] - ys[k];
                double len = Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)Math.Ceiling(len / step));
                for (int q = 1; q < steps; q++)   // vnitrni vzorky; koncovy bod je uzel k+1
                {
                    double t = (double)q / steps;
                    AddSample(grid, xs[k] + dx * t, ys[k] + dy * t, s + len * t);
                }
                s += len;
            }
        }

        private void AddSample(OccupancyGrid grid, double x, double y, double s)
        {
            int i = grid.CellX(x) - grid.OriginX;
            int j = grid.CellY(y) - grid.OriginY;
            bool inside = (uint)i < (uint)size && (uint)j < (uint)size;
            int idx = inside ? i + j * size : -1;

            sampleS.Add(s);
            sampleClear.Add(inside ? clearance[idx] : 0f);
            sampleFree.Add(inside && state[idx] == (byte)CellState.Free);
        }

        // ---------------- pomocne ----------------

        /// <summary>
        /// Orizne cil na obdelnik gridu (o jednu bunku od kraje) podel usecky robot -&gt; cil.
        /// Vraci true, kdyz oriznuti nastalo (cil je mimo grid).
        /// </summary>
        private bool ClipToGrid(OccupancyGrid grid, double robotX, double robotY,
                               ref double goalX, ref double goalY)
        {
            double xmin = grid.CenterX(grid.OriginX + 1);
            double xmax = grid.CenterX(grid.OriginX + size - 2);
            double ymin = grid.CenterY(grid.OriginY + 1);
            double ymax = grid.CenterY(grid.OriginY + size - 2);

            if (goalX >= xmin && goalX <= xmax && goalY >= ymin && goalY <= ymax) return false;

            double dx = goalX - robotX, dy = goalY - robotY;
            double t = 1.0;
            if (dx > 1e-12) t = Math.Min(t, (xmax - robotX) / dx);
            else if (dx < -1e-12) t = Math.Min(t, (xmin - robotX) / dx);
            if (dy > 1e-12) t = Math.Min(t, (ymax - robotY) / dy);
            else if (dy < -1e-12) t = Math.Min(t, (ymin - robotY) / dy);
            if (t < 0) t = 0;

            goalX = robotX + dx * t;
            goalY = robotY + dy * t;
            return true;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
