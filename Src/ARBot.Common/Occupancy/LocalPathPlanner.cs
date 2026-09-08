using System;
using System.Collections.Generic;
using ARBot.Common.Configuration;
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

        /// <summary>Kinematicky profil regulatoru - z nej se pocita predpovezena rampa pri vyhlazovani.</summary>
        private readonly IMotionProfile motion;

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
        private readonly List<double> sampleClosing = new List<double>();   // priblizovani k prekazce 0..1

        /// <summary>Rozliseni gridu z posledniho <see cref="Plan"/> [m] - pro gradient pole odstupu.</summary>
        private double cellSize = 0.05;

        /// <summary>Kurz robotu z posledniho <see cref="Plan"/> [rad] - pro cenu otoceni ve vyhlazovani.</summary>
        private double planHeading;

        /// <summary>Kumulativni jizdni cas podel <see cref="pathCells"/> [s] (znovupouzity buffer).</summary>
        private double[] pathTime = new double[0];

        /// <summary>Zpetne brzdna obalka podel <see cref="pathCells"/> [m/s] (znovupouzity buffer).</summary>
        private double[] pathVLim = new double[0];
        private double[] frontierAfter = new double[0];
        private double[] nodeS = new double[0];
        private int[] nodeSample = new int[0];

        // Rozpad rychlostni obalky posledniho planu PO UZLECH (naplni BuildWayPoints, prectе Plan
        // do vysledku). Zamerne po uzlech, ne jen minimum pres plan: minimum rekne, ze se robot
        // plazi, ale ne KDE na draze se to stane - a prave to rozlisuje "jsem tesne u okraje hned
        // u sebe" od "za dva metry se cesta zuzuje". Buffery se znovupouzivaji; platny je prvnich
        // envNodes prvku. Viz LocalPlanResult a doc/occupancy-and-local-planning.md.
        private float[] envClearance = new float[0];
        private float[] envClosing = new float[0];
        private float[] envFreeAhead = new float[0];
        private float[] envVClearance = new float[0];
        private float[] envVBrake = new float[0];
        private int envNodes;

        /// <summary>Konfigurace planovace.</summary>
        public LocalPlannerConfig Config => cfg;

        /// <param name="size">Pocet bunek na stranu gridu, se kterym se bude planovat.</param>
        /// <param name="config">Konfigurace; null = vychozi (hodnoty z <c>Profile</c>).</param>
        /// <param name="motionProfile">
        /// Kinematicky profil, podle ktereho se pri vyhlazovani predpovida rampa mezi uzly
        /// (<see cref="ShortcutKeepsTime"/>). <b>Musi to byt tentyz profil, ktery dostal
        /// <c>PathPlanner</c></b> - planovac tady predpovida presne to, co regulator odjede, takze
        /// dva ruzne profily znamenaji, ze overena rampa neni ta skutecna. null = lichobeznikovy
        /// profil z <paramref name="config"/> (tytez limity, jake pouziva rychlostni obalka).
        /// </param>
        public LocalPathPlanner(int size, LocalPlannerConfig config = null,
                                IMotionProfile motionProfile = null)
        {
            if (size <= 0) throw new ArgumentException($"LocalPathPlanner: size musi byt > 0, je {size}.");
            this.size = size;
            cfg = config ?? new LocalPlannerConfig();
            cfg.Validate();
            motion = motionProfile ?? new TrapezoidMotionProfile(
                cfg.MaxSpeed, cfg.MaxRotationSpeed, cfg.MaxDeceleration, Profile.Rozchod);

            // Rychlostni obalka (VBrake/VClosing) pocita s cfg.MaxDeceleration, predpovezena rampa
            // s decelaraci profilu. Kdyz regulator brzdi POMALEJI, nez obalka predpoklada, poruse ji
            // i bez jakehokoli slucovani - a tuhle vazbu mezi dvema konfiguracemi jinak nikdo nehlida.
            // Do Trace, ne do Debug: v Release na zarizeni po poruse musi zustat stopa.
            if (motion.Acceleration < cfg.MaxDeceleration * (1 - 1e-9))
                System.Diagnostics.Trace.WriteLine(
                    $"LocalPathPlanner: profil brzdi {motion.Acceleration:F2} m/s^2, ale rychlostni "
                    + $"obalka pocita s {cfg.MaxDeceleration:F2} m/s^2 - obalka se muze porusit "
                    + "i bez slucovani useku. Srovnat Profile.MaxAcceleration a MaxDecceleration.");

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
            cellSize = cell;
            planHeading = heading;
            int i0 = grid.CellX(robotX) - grid.OriginX;
            int j0 = grid.CellY(robotY) - grid.OriginY;
            if ((uint)i0 >= (uint)size || (uint)j0 >= (uint)size)
            {
                res.Status = LocalPlanStatus.RobotOutsideGrid;
                return res;
            }

            Snapshot(grid, field);

            // Robot UZ STOJI v blokovane bunce (dojel tam, nez se to o ni vedelo, nebo se posunula
            // mapa) NEBO stoji TESNE u prekazky (odstup pod SafeDist: zastavil pozde, prekazka se
            // objevila, mapa se posunula). V obou pripadech by bezne pravidlo prujezdnosti nemelo
            // odkud vyjet a vracet RobotBlocked znamena stat tam navzdy - misto toho se hleda nejkratsi
            // cesta VEN k nejblizsi bunce, odkud jde planovat bezne (UNIK).
            //
            // Do 3. 9. 2026 resil tesny start jinak: "eskapovaci zona" - vyjimka z odstupu v okoli
            // aktualni bunky. Byla ale SYMETRICKA (pustila robota i bliz k prekazce) a posouvala se
            // s robotem, takze se k okraji cesty dalo doplizit po bunce s libovolne velkym SafeDist
            // (namereno s mrkvi v trave a SafeDist 0,7 m). Unik miri k nejblizsi bezpecne bunce, ne
            // k cili, takze vede vzdy PRYC od prekazky. Viz doc/occupancy-and-local-planning.md.
            //
            // Hystereze pul bunky: unik se spousti az pod SafeDist - cell/2, konci (IsEscapeExit) az
            // na plnem SafeDist. Bez ni by robot, ktery unikem vyjel na bunku s odstupem tesne nad
            // SafeDist, po sumu gridu priste zase "unikal" o bunku dal a na hranici kmital. V pasmu
            // mezi se planuje bezne - start je prujezdny vzdy (robot na nem stoji) a soused dal od
            // prekazky ma odstup o bunku vyssi, tedy nad SafeDist.
            int s0 = i0 + j0 * size;
            double tightBelow = cfg.SafeDist - cell / 2;
            if (state[s0] == (byte)CellState.Blocked || clearance[s0] < tightBelow)
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

            // Cil v neprujezdne nebo tesne bunce: A* se do nej nikdy netrefi a plan povede k nejblizsi
            // bezpecne bunce. Od Partial (cil legitimne za horizontem) se to rozlisuje schvalne -
            // sem robot NIKDY nedojede, na konci drahy ma zastavit a producent cile se to ma dozvedet.
            // Do 3. 9. 2026 to vychazelo jako Partial a na konci jako AlreadyAtGoal, coz u mrkve
            // polozene do travy vypadalo jako "uz jsem v cili".
            int goalCell = iG + jG * size;
            bool goalBlocked = state[goalCell] == (byte)CellState.Blocked;
            bool goalUnsafe = !goalBlocked && clearance[goalCell] < cfg.SafeDist;

            int goalIdx = Search(i0, j0, iG, jG, heading, cell, out int bestIdx, out int expanded);
            res.ExpandedCells = expanded;

            int target = goalIdx >= 0 ? goalIdx : bestIdx;
            if (target < 0)
            {
                res.Status = LocalPlanStatus.NoRoute;
                return res;
            }

            if (goalIdx >= 0)
                res.Status = goalClipped ? LocalPlanStatus.Partial : LocalPlanStatus.Ok;
            else
                res.Status = goalBlocked ? LocalPlanStatus.GoalBlocked
                           : goalUnsafe ? LocalPlanStatus.GoalUnsafe
                           : LocalPlanStatus.Partial;
            res.CostSeconds = gScore[target];
            res.LengthM = lenFromStart[target];

            // Skutecne dosazeny bod = stred cilove bunky.
            res.ReachedGoalX = grid.CenterX(grid.OriginX + target % size);
            res.ReachedGoalY = grid.CenterY(grid.OriginY + target / size);

            BuildPathCells(target);
            StringPull();

            // Zastavit na konci: v cili (Ok), nebo na nejblizsi bezpecne bunce, kdyz je cil
            // neprujezdny/tesny - dal se nejede a dalsi mrkev na tom nic nezmeni. U Partial se
            // nezastavuje: dalsi cyklus dostane dalsi kus drahy.
            bool stopAtEnd = res.Status == LocalPlanStatus.Ok
                          || res.Status == LocalPlanStatus.GoalBlocked
                          || res.Status == LocalPlanStatus.GoalUnsafe;
            res.WayPoints = BuildWayPoints(grid, robotX, robotY,
                                           finalGoal: stopAtEnd,
                                           minClearance: out double minClear);
            res.MinClearanceM = minClear;

            // Rozpad rychlostni obalky po uzlech (diagnostika "proc robot leze") - viz LocalPlanResult.
            StoreEnvelope(res);

            if (res.WayPoints == null || res.WayPoints.Length < 2)
            {
                // Cil (nebo nejblizsi dosazitelna bunka) je blize nez jedna pouzitelna hrana - neni co
                // predat regulatoru. U Ok/Partial je to "uz jsem tam". U GoalBlocked/GoalUnsafe stav
                // ZUSTAVA: robot stoji na nejblizsi bezpecne bunce, cil nedosahl a nedosahne, a prave
                // to ma byt videt (HasPath je false, ridit se podle toho neda).
                if (res.Status == LocalPlanStatus.Ok || res.Status == LocalPlanStatus.Partial)
                    res.Status = LocalPlanStatus.AlreadyAtGoal;
            }

            return res;
        }

        /// <summary>
        /// Prekopiruje rozpad obalky POSLEDNIHO <see cref="BuildWayPoints"/> do vysledku. Kopie je
        /// vzdy cerstva - vysledek jde do zpravy asynchronnim odberatelum, kdezto buffery se
        /// znovupouzivaji. Kdyz draha nevznikla, zustanou v poli <c>null</c>.
        /// </summary>
        private void StoreEnvelope(LocalPlanResult res)
        {
            if (envNodes <= 0) { res.SetEnvelope(null, null, null, null, null); return; }
            res.SetEnvelope(Copy(envClearance), Copy(envClosing), Copy(envFreeAhead),
                            Copy(envVClearance), Copy(envVBrake));
        }

        private float[] Copy(float[] src)
        {
            var dst = new float[envNodes];
            Array.Copy(src, dst, envNodes);
            return dst;
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
                StringPull();

                // finalGoal: true - na konci uniku robot zastavi a dalsi cyklus uz planuje bezne.
                res.WayPoints = BuildWayPoints(grid, robotX, robotY,
                                               finalGoal: true, minClearance: out double minClear);
                res.MinClearanceM = minClear;
                StoreEnvelope(res);

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

        // ---------------- smer k prekazce ----------------

        /// <summary>
        /// Rychlost PRIBLIZOVANI k nejblizsi prekazce na jednotku dopredne rychlosti (0..1) pri jizde
        /// smerem (<paramref name="dirX"/>, <paramref name="dirY"/>) (jednotkovy vektor) pres bunku
        /// <paramref name="idx"/>: <c>max(0, -dir . grad d)</c>, kde <c>d</c> je pole odstupu.
        ///
        /// <para>Gradient se bere centralnimi diferencemi ze snapshotu odstupu (na kraji jednostranne).
        /// EDT je 1-lipschitzovske, takze |grad d| &lt;= 1 az na diskretizaci - orezava se na 1.
        /// Na hrebeni pole (stred cesty, stejne daleko od obou okraju) je gradient ~0, tedy "nic se
        /// nepriblizuje" - presne to, co ma smerovy model rikat. Neni-li v gridu zadna prekazka,
        /// jsou odstupy nekonecne a rozdil je NaN -> 0.</para>
        /// </summary>
        private double Closing(int idx, double dirX, double dirY)
        {
            int i = idx % size, j = idx / size;
            int ip = i + 1 < size ? idx + 1 : idx, im = i > 0 ? idx - 1 : idx;
            int jp = j + 1 < size ? idx + size : idx, jm = j > 0 ? idx - size : idx;
            double gx = (clearance[ip] - clearance[im]) / ((ip - im) * cellSize);
            double gy = (clearance[jp] - clearance[jm]) / (((jp - jm) / size) * cellSize);
            double c = -(dirX * gx + dirY * gy);
            if (!(c > 0)) return 0.0;        // vzdaluje se, jede podel, nebo NaN (bez prekazek)
            return c > 1.0 ? 1.0 : c;
        }

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

                    if (!Passable(nidx, out double clr)) continue;

                    bool diagonal = k >= 4;
                    if (diagonal)
                    {
                        // Bez rezani rohu: oba orto sousedi musi byt take prujezdni.
                        int a = ni + cj * size;
                        int b = ci + nj * size;
                        if (!Passable(a, out _)) continue;
                        if (!Passable(b, out _)) continue;
                    }

                    double stepLen = diagonal ? diag : cell;
                    // Cena = jizdni cas podle TEZE obalky, kterou dostanou waypointy: smerovy model
                    // zdrazuje priblizovani k prekazce, ne jizdu podel ni (radialni obe stejne).
                    double closing = Closing(nidx, NeighDx[k] * cell / stepLen, NeighDy[k] * cell / stepLen);
                    double stepCost = stepLen / cfg.VCost(clr, closing);
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
        /// Je bunka prujezdna? Bezne pravidlo je TVRDE a bez vyjimek: bunka neni
        /// <see cref="CellState.Blocked"/> a odstup je alespon <see cref="LocalPlannerConfig.SafeDist"/>.
        /// Jedina vyjimka je vychozi bunka - na ni robot stoji, takze z ni odjet musi (a string-pulling
        /// ji vzorkuje jako zacatek kazde usecky).
        /// <para>Drivejsi "eskapovaci zona" (odstup slevovany v okoli startu) tu od 3. 9. 2026 neni:
        /// tesny start resi UNIK, viz <see cref="Plan"/>. Zona byla symetricka a posouvala se
        /// s robotem, takze ho pustila k prekazce bliz po bunce.</para>
        /// <para>UNIK ma vlastni pravidlo: rozhoduje KANAL, ne odstup - ven se smi pres semanticky
        /// blokovane bunky (z travy zpatky na cestu), pres geometricky blokovane nikdy.</para>
        /// </summary>
        private bool Passable(int idx, out double clr)
        {
            clr = clearance[idx];
            if (idx == startIdx) return true;
            if (escape) return (blockReason[idx] & (byte)CellBlockReason.Geometry) == 0;
            return state[idx] != (byte)CellState.Blocked && clr >= cfg.SafeDist;
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
        ///
        /// <para><b>Od 8. 9. 2026 rozhoduje i CAS</b> (<see cref="PathSmoothingMode.TimeAware"/>,
        /// vychozi): zkratka se prijme, jen kdyz nezhorsi jizdni cas proti useku, ktery nahrazuje.
        /// Bez toho vyhlazovani optimalizovalo DELKU (jedinou podminkou bylo tvrde
        /// <c>d &gt;= SafeDist</c>), kdezto A* optimalizoval CAS - a rozdil obou kriterii zahodil
        /// objizdku, kterou cena koupila. Zmereno: na scene se skvrnou 2x2 bunky stranou od spojnice
        /// slo A* objizdkou 4,59 m misto 2,80 m a <b>vystupem byla stejne primka</b> s odstupem na
        /// mezi prujezdnosti. Viz doc/occupancy-and-local-planning.md.</para>
        ///
        /// <para><b>Unik</b> (<see cref="escape"/>) zustava na puvodnim pravidle: tam jde o to dostat
        /// se ven z tesne bunky, ne jet rychle, a cena uniku ma jinou stupnici
        /// (<see cref="LocalPlannerConfig.EscapeBlockedCostFactor"/>).</para>
        /// </summary>
        private void StringPull()
        {
            pulled.Clear();
            if (pathCells.Count == 0) return;

            bool timeAware = cfg.Smoothing == PathSmoothingMode.TimeAware && !escape;
            if (timeAware) BuildPathTimes();

            pulled.Add(pathCells[0]);
            int anchor = 0;
            while (anchor < pathCells.Count - 1)
            {
                int next = anchor + 1;
                for (int probe = pathCells.Count - 1; probe > anchor + 1; probe--)
                {
                    if (!SegmentPassable(pathCells[anchor], pathCells[probe])) continue;
                    if (timeAware && !ShortcutKeepsTime(anchor, probe)) continue;
                    next = probe;
                    break;
                }
                pulled.Add(pathCells[next]);
                anchor = next;
            }
        }

        /// <summary>
        /// Kumulativni jizdni cas podel drahy A* po bunkach (<see cref="pathTime"/>): cena kroku je
        /// <c>delka / VCost(odstup, priblizovani)</c>, tedy TATAZ funkce jako v <see cref="Search"/>.
        ///
        /// <para>Zamerne BEZ <see cref="LocalPlannerConfig.UnknownCostFactor"/> a bez ceny pocatecniho
        /// otoceni: obojí je slozka <b>planovaci</b> ceny (preference), ne cas. <c>gScore</c> by bylo
        /// zadarmo, ale nese je - a nafouknuty referencni cas by zkratky pres neznamo prijimal
        /// prilis ochotne. Cena otoceni se pricita zvlast na OBOU stranach porovnani, protoze zkratka
        /// z prvniho uzlu mivá jiny smer nez prvni krok A* (kvantovani do 8 smeru je az 22,5°, coz
        /// pri omega = pi/6 dela 0,75 s).</para>
        /// </summary>
        private void BuildPathTimes()
        {
            int n = pathCells.Count;
            if (pathTime.Length < n) { pathTime = new double[n * 2]; pathVLim = new double[n * 2]; }
            double diag = Math.Sqrt(2.0) * cellSize;

            // 1) Obalka po bunkach (rychlost kroku DO bunky - stejne jako cena hrany v A*).
            for (int k = 0; k < n; k++)
            {
                int cur = pathCells[k];
                int dx, dy;
                if (k > 0) { int p = pathCells[k - 1]; dx = cur % size - p % size; dy = cur / size - p / size; }
                else if (n > 1) { int q = pathCells[1]; dx = q % size - cur % size; dy = q / size - cur / size; }
                else { dx = 1; dy = 0; }
                double inv = 1.0 / Math.Sqrt(dx * dx + dy * dy);
                pathVLim[k] = cfg.VCost(clearance[cur], Closing(cur, dx * inv, dy * inv));
            }

            // 2) Zpetna brzdna obalka: rychlost, ze ktere se jeste stihnu zbrzdit na vsechno, co je
            //    dal po draze. Bez ni by reference tvrdila, ze robot smi jet plnou rychlost az do
            //    bunky pred skvrnou a tam skokem zpomalit - fyzikalne nemozne, takze by se zamitala
            //    i slouceni, ktera nic nestoji. Je to tataz uvaha jako zpetny pruchod v PathPlanneru.
            // Brzdny zakon si drzi profil (Dist2MaxSpeed) - tady se nesmi opisovat.
            for (int k = n - 2; k >= 0; k--)
            {
                int cur = pathCells[k], nxt = pathCells[k + 1];
                bool diagStep = cur % size != nxt % size && cur / size != nxt / size;
                double stepLen = diagStep ? diag : cellSize;
                double cap = motion.Dist2MaxSpeed(stepLen, pathVLim[k + 1]);
                if (cap < pathVLim[k]) pathVLim[k] = cap;
            }

            // 3) Kumulativni cas po bunkach.
            pathTime[0] = 0;
            for (int k = 1; k < n; k++)
            {
                int prev = pathCells[k - 1], cur = pathCells[k];
                bool diagStep = cur % size != prev % size && cur / size != prev / size;
                pathTime[k] = pathTime[k - 1] + (diagStep ? diag : cellSize) / pathVLim[k];
            }
        }


        /// <summary>
        /// Smi se usek <paramref name="anchor"/> -&gt; <paramref name="probe"/> slouncit do jedine
        /// usecky? Musi platit OBOJI:
        /// <list type="number">
        /// <item><description><b>Rampa se vejde pod obalku</b> - predpovezena rychlost regulatoru
        /// (drzi strop vjezdoveho uzlu a dobrzduje na strop vyjezdoveho) nesmi v zadnem vzorku
        /// prekrocit obalku. Tohle nahrazuje drivejsi "kazdy vzorek zastropuje aspon jeden uzel":
        /// misto minima pres usek se overuje prubeh, ktery se opravdu pojede.</description></item>
        /// <item><description><b>Nezhorsi to jizdni cas</b> proti jemnemu deleni
        /// (<see cref="BuildPathTimes"/>). Rampa sama o sobe pomale misto na konci useku rozlije
        /// dopredu, takze bez tohohle by se slucovalo i tam, kde to stoji cas.</description></item>
        /// </list>
        ///
        /// <para>Rychlosti krajnich uzlu se berou ze <b>zpetne brzdne obalky</b>
        /// (<see cref="pathVLim"/>), tedy z toho, co robot v tom miste opravdu pojede - ne z holé
        /// obalky, kterou by stejne nestihl.</para>
        ///
        /// <para>Cena pocatecniho otoceni se pricita na OBOU stranach (A* ji ma v prvni hrane,
        /// zkratka ma svou vlastni) - do rampy samotne se otaceni neplete.</para>
        /// </summary>
        private bool ShortcutKeepsTime(int anchor, int probe)
        {
            int fromIdx = pathCells[anchor], toIdx = pathCells[probe];
            double x0 = fromIdx % size, y0 = fromIdx / size;
            double dx = toIdx % size - x0, dy = toIdx / size - y0;
            double lenCells = Math.Sqrt(dx * dx + dy * dy);
            if (!(lenCells > 0)) return true;
            double ux = dx / lenCells, uy = dy / lenCells;
            double len = lenCells * cellSize;

            double budget = pathTime[probe] - pathTime[anchor];
            if (anchor == 0 && pathCells.Count > 1)
            {
                int first = pathCells[1], start = pathCells[0];
                budget += RotationTime(first % size - start % size, first / size - start / size);
                budget -= RotationTime(ux, uy);
            }
            if (!(budget > 0)) return false;

            // Rampa: drzi strop vjezdoveho uzlu (PathResult.Control bod 3b) a vcas dobrzdi na strop
            // vyjezdoveho - tvar brzdeni si drzi PROFIL (Dist2MaxSpeed), planovac ho neopisuje.
            double vEnter = pathVLim[anchor], vExit = pathVLim[probe];
            double vCruise = Math.Min(vEnter, motion.Dist2MaxSpeed(len, vExit));
            if (!(vCruise > 0)) return false;

            // Jeden pruchod vzorky: rampa pod obalkou + cas. Cas se integruje pres tytez vzorky
            // (lichobeznikove), ne uzavrenym vzorcem - ten by predpokladal konstantni deceleraci,
            // coz treba SqrtMotionProfile nema.
            int steps = (int)Math.Ceiling(lenCells * 2.0) + 1;
            double ramp = 0, vPrev = 0;
            for (int st = 0; st <= steps; st++)
            {
                double t = (double)st / steps;
                int i = (int)Math.Round(x0 + dx * t);
                int j = (int)Math.Round(y0 + dy * t);
                if ((uint)i >= (uint)size || (uint)j >= (uint)size) return false;
                int idx = i + j * size;

                // Zbyvajici draha se meri od STREDU TE BUNKY, ne ze spojiteho t. Obalka je funkce
                // odstupu te bunky, takze kdyby se rampa brala spojite, lisily by se obe strany
                // o pul bunky - a u brzdne krivky to dela az jednotky procent, takze by se
                // rovnomerne zpomalovani neslouncilo a schody z A* by zustaly. Takhle si obe strany
                // odpovidaji presne: tam, kde obalka JE brzdna krivka, vyjde rampa == obalka.
                double remaining = ((x0 + dx - i) * ux + (y0 + dy - j) * uy) * cellSize;
                if (remaining < 0) remaining = 0;

                double vPred = Math.Min(vCruise, motion.Dist2MaxSpeed(remaining, vExit));
                double vAllowed = cfg.VCost(clearance[idx], Closing(idx, ux, uy));
                if (vPred > vAllowed * (1 + 1e-6)) return false;

                if (st > 0) ramp += (len / steps) * 0.5 * (1.0 / vPred + 1.0 / vPrev);
                vPrev = vPred;
                if (ramp > budget * (1 + 1e-6)) return false;
            }
            return true;
        }

        /// <summary>Cas otoceni z aktualniho kurzu do smeru <paramref name="dirX"/>,<paramref name="dirY"/> [s].</summary>
        private double RotationTime(double dirX, double dirY)
            => Math.Abs(Conversions.NormalizeOrientation(Math.Atan2(dirY, dirX) - planHeading))
               / cfg.MaxRotationSpeed;

        /// <summary>Je cela usecka mezi stredy dvou bunek prujezdna? Vzorkuje se s krokem 1/2 bunky.</summary>
        private bool SegmentPassable(int fromIdx, int toIdx)
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
                if (!Passable(i + j * size, out _)) return false;
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
            envNodes = 0;
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

            if (envClearance.Length < n)
            {
                envClearance = new float[n * 2];
                envClosing = new float[n * 2];
                envFreeAhead = new float[n * 2];
                envVClearance = new float[n * 2];
                envVBrake = new float[n * 2];
            }
            envNodes = n;

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

            // Nejmensi odstup PRES CELOU DRAHU (diagnostika a MaxPositionError uzlu se resi zvlast).
            for (int i = 0; i < m; i++)
                if (sampleClear[i] < minClearance) minClearance = sampleClear[i];

            bool timeAwareNodes = cfg.Smoothing == PathSmoothingMode.TimeAware && !escape;

            var wps = new RegulatorWayPoint[n];
            for (int k = 0; k < n; k++)
            {
                // Strop uzlu = obalka V UZLU (od 8. 9. 2026), ne minimum pres okno sousednich useku.
                //
                // Do te doby to minimum bylo: kazdy vzorek tak byl zastropovan aspon jednim uzlem,
                // coz je bezpecne konstrukci, ale usek se pak CELY jede rychlosti sveho nejhorsiho
                // mista (PathResult.Control drzi WayPoints[seg].Speed podel celeho useku). Rampa se
                // tim splacla na konstantu: jizda kolmo k prekazce se plazila uz od zacatku, ackoli
                // u sebe mel robot odstup 1,2 m.
                //
                // Bezpecnostni argument je ted jiny, ne slabsi: vyhlazovani usek prijme jen tehdy,
                // kdyz PREDPOVEZENA rampa (z ceho se vjizdi, kam se dobrzdi) nikde na nem obalku
                // neprekroci - viz ShortcutKeepsTime. Zbytek uz umi vrstva pod tim: PathPlanner z
                // Speed udela VLimit uzlu a PathResult k nemu dobrzduje, takze mezi dvema uzly
                // vznikne prave ta rampa. Predpoklad: planovac brzdi konzervativneji nez regulator
                // (cfg.MaxDeceleration <= decelerace profilu) - jinak by rampa byla plossi nez
                // overena. Viz doc/occupancy-and-local-planning.md a decisions.md 8. 9. 2026.
                //
                // Puvodni pravidlo (minimum pres okno) proto ZUSTAVA u smooth=passable a u uniku:
                // tam se rampa neoveruje, takze jedina zaruka je to minimum. Obe poloviny jsou pár.
                int si = nodeSample[k];
                double clr, closingAtMin, vClear;
                if (timeAwareNodes)
                {
                    clr = sampleClear[si];
                    closingAtMin = sampleClosing[si];
                    vClear = cfg.VEnvelope(clr, closingAtMin);
                }
                else
                {
                    // Okno uzlu = usek k nemu vedouci + usek z nej vychazejici; strop z odstupu je
                    // MINIMUM OBALKY pres vzorky okna, ne obalka minima odstupu (ve smerovem modelu
                    // zalezi u kazdeho vzorku i na tom, kam draha miri).
                    double sFrom = k > 0 ? nodeS[k - 1] : nodeS[0];
                    double sTo = k < n - 1 ? nodeS[k + 1] : nodeS[n - 1];
                    clr = double.MaxValue;
                    vClear = double.MaxValue;
                    closingAtMin = 0;
                    for (int i = 0; i < m; i++)
                    {
                        if (sampleS[i] < sFrom || sampleS[i] > sTo) continue;
                        if (sampleClear[i] < clr) clr = sampleClear[i];
                        double ve = cfg.VEnvelope(sampleClear[i], sampleClosing[i]);
                        if (ve < vClear) { vClear = ve; closingAtMin = sampleClosing[i]; }
                    }
                    if (clr == double.MaxValue)
                    {
                        clr = sampleClear[si];
                        closingAtMin = sampleClosing[si];
                        vClear = cfg.VEnvelope(clr, closingAtMin);
                    }
                }

                // Brzdna obalka: vzdalenost k hranici potvrzeneho, merena OD TOHOTO UZLU dopredu.
                double freeAhead = frontierAfter[nodeSample[k]] - nodeS[k];
                if (freeAhead < 0) freeAhead = 0;

                double vBrake = cfg.VBrake(freeAhead);
                double v = Math.Min(vClear, vBrake);
                bool last = k == n - 1;

                // Diagnostika obalky - za KAZDY uzel. Minimum pres plan si dopocita
                // LocalPlanResult (a vynecha pritom posledni uzel, kde je Speed = 0 z definice).
                envClearance[k] = (float)clr;
                envClosing[k] = (float)closingAtMin;
                envFreeAhead[k] = (float)freeAhead;
                envVClearance[k] = (float)vClear;
                envVBrake[k] = (float)vBrake;

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
            sampleClosing.Clear();
            if (nodeS.Length < n) { nodeS = new double[n * 2]; nodeSample = new int[n * 2]; }

            double step = grid.Resolution * 0.5;
            double s = 0;
            double ux = 1, uy = 0;   // smer aktualniho useku (jednotkovy); posledni uzel dedi smer posledniho useku
            for (int k = 0; k < n; k++)
            {
                double dx = 0, dy = 0, len = 0;
                if (k < n - 1)
                {
                    dx = xs[k + 1] - xs[k];
                    dy = ys[k + 1] - ys[k];
                    len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0) { ux = dx / len; uy = dy / len; }
                }

                nodeS[k] = s;
                nodeSample[k] = sampleS.Count;
                AddSample(grid, xs[k], ys[k], s, ux, uy);

                if (k == n - 1) break;

                int steps = Math.Max(1, (int)Math.Ceiling(len / step));
                for (int q = 1; q < steps; q++)   // vnitrni vzorky; koncovy bod je uzel k+1
                {
                    double t = (double)q / steps;
                    AddSample(grid, xs[k] + dx * t, ys[k] + dy * t, s + len * t, ux, uy);
                }
                s += len;
            }
        }

        private void AddSample(OccupancyGrid grid, double x, double y, double s, double dirX, double dirY)
        {
            int i = grid.CellX(x) - grid.OriginX;
            int j = grid.CellY(y) - grid.OriginY;
            bool inside = (uint)i < (uint)size && (uint)j < (uint)size;
            int idx = inside ? i + j * size : -1;

            sampleS.Add(s);
            sampleClear.Add(inside ? clearance[idx] : 0f);
            sampleClosing.Add(inside ? Closing(idx, dirX, dirY) : 1.0);   // mimo grid: nejhorsi pripad

            // Sjizdne = potvrzene Free, NEBO lezi pod robotem (pudorys) a neni Blocked. Robot na tech
            // bunkach stoji, takze sjizdne jsou i kdyz je kamera nevidi (slepa zona ~0,5 m pred
            // robotem) - bez toho je po startu "volno" 0 a robot leze MinCostSpeed, dokud zonu
            // neprejede. Blocked se nepromiji: robot v trave unika a ma se plouzit. Grid se nemeni.
            // Viz LocalPlannerConfig.FootprintRadiusM.
            double footprint = Math.Min(cfg.FootprintRadiusM, cfg.SafeDist);
            bool free = inside && (state[idx] == (byte)CellState.Free
                                   || (s < footprint && state[idx] != (byte)CellState.Blocked));
            sampleFree.Add(free);
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
