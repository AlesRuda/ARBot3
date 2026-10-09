using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Diagnostics
{
    /// <summary>Uroven stani podle <see cref="StuckDetector"/>.</summary>
    public enum StuckLevel : byte
    {
        /// <summary>Robot nestoji, nebo nejede (bez cile, nouzove / drzene zastaveni).</summary>
        None = 0,
        /// <summary>Stoji pri jizde dele nez <see cref="StuckConfig.StandingSec"/> — muze se vyresit samo.</summary>
        Standing = 1,
        /// <summary>Stoji dele nez <see cref="StuckConfig.StuckSec"/> — uvazl, ceka na zasah obsluhy.</summary>
        Stuck = 2,
    }

    /// <summary>Pricina stani odvozena z posledniho lokalniho planu.</summary>
    public enum StuckCause : byte
    {
        /// <summary>Pricina neni z ceho poznat.</summary>
        Unknown = 0,
        /// <summary>Lokalni plan nechodi (kamery, fuze) — robot nema podle ceho jet.</summary>
        NoPlan = 1,
        /// <summary>Robot stoji v blokovane bunce nebo tesne u prekazky (<c>RobotBlocked</c>/<c>EscapingBlocked</c>).</summary>
        InBlockedCell = 2,
        /// <summary>Mrkev je daleko, ale nedosazitelna - slepy konec (<c>LocalMinimum</c>; ve starsich zaznamech <c>AlreadyAtGoal</c>).</summary>
        CarrotUnreachable = 3,
        /// <summary>Cilova zona mrkve je cela neprujezdna / tesna (<c>GoalBlocked</c>/<c>GoalUnsafe</c>).</summary>
        GoalBlocked = 4,
        /// <summary>Plan vede dal (<c>Ok</c>/<c>Partial</c>), ale robot nejede.</summary>
        PlanButNoMotion = 5,
    }

    /// <summary>Okamzity stav hlidace pro stranku nahledu.</summary>
    public readonly struct StuckReading
    {
        public StuckReading(StuckLevel level, double standingSec, StuckCause cause, string text)
        {
            Level = level;
            StandingSec = standingSec;
            Cause = cause;
            Text = text ?? string.Empty;
        }

        /// <summary>Uroven; <see cref="StuckLevel.None"/> i tehdy, kdyz hlidac nema cerstva data.</summary>
        public StuckLevel Level { get; }
        /// <summary>Jak dlouho robot stoji [s].</summary>
        public double StandingSec { get; }
        /// <summary>Pricina.</summary>
        public StuckCause Cause { get; }
        /// <summary>Lidsky popis (s diakritikou), prazdny u <see cref="StuckLevel.None"/>.</summary>
        public string Text { get; }
    }

    /// <summary>Nastaveni <see cref="StuckDetector"/>. Hodnoty z dat, viz doc/global-navigation-runtime.md.</summary>
    public sealed class StuckConfig
    {
        /// <summary>
        /// Od kolika sekund stani se hlasi „stoji" (oranzove) [s]. Rozhodnuti autora 8. 10. 2026.
        /// <para>V datech (27 zaznamu, 4 h aktivni jizdy) trva prvni pul metr po startu useku az 11 s
        /// a prodleva planu po uvolneni holdu kamer 10–20 s — 20 s je tedy nad beznym rozjezdem,
        /// ale ukaze i uvaznuti, ktera obsluha ukoncila po 32 a 39 s.</para>
        /// </summary>
        public double StandingSec = 20.0;

        /// <summary>
        /// Od kolika sekund se hlasi „UVAZL" (cervene) [s]. Rozhodnuti autora 8. 10. 2026.
        /// <para>Samo vyresena stani trvala v datech nejvys ~50–60 s, vsechna delsi nez 60 s byla
        /// skutecna uvaznuti (126, 147, 157, 268 s).</para>
        /// </summary>
        public double StuckSec = 60.0;

        /// <summary>
        /// Kolik musi robot ujet, aby to byl pohyb [m] — tataz mez jako detektor A globalni navigace
        /// (<c>GlobalNavigatorConfig.MinMotionM</c>). Plizeni unikem 0,05 m/s ji prekona za 10 s,
        /// takze unik, ktery postupuje, se jako stani nehlasi.
        /// </summary>
        public double MinMotionM = 0.5;

        /// <summary>Jak stara smi byt zprava navigace / planu / FreeRunu, aby platila [s].</summary>
        public double FreshSec = 2.0;

        /// <summary>Jak casto pripominat trvajici uvaznuti do Trace [s].</summary>
        public double ReminderSec = 30.0;

        /// <summary>Jak casto posilat <see cref="StuckMsg"/>, dokud robot stoji nad prahem [s].</summary>
        public double MessagePeriodSec = 1.0;

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/>.</summary>
        public void Validate()
        {
            if (!(StandingSec > 0) || !(StuckSec >= StandingSec))
                throw new ArgumentException($"StuckConfig: musi platit 0 < StandingSec ({StandingSec}) <= StuckSec ({StuckSec}).");
            if (!(MinMotionM > 0) || !(FreshSec > 0) || !(ReminderSec > 0) || !(MessagePeriodSec > 0))
                throw new ArgumentException("StuckConfig: MinMotionM, FreshSec, ReminderSec a MessagePeriodSec musi byt kladne.");
        }
    }

    /// <summary>
    /// <b>Hlidac uvaznuti</b> — cista logika bez vlaken (<see cref="StuckMonitor"/> ji obaluje
    /// stupnem). Tema <c>nav-uvaznuti-neohlasene</c>, doc/global-navigation-runtime.md.
    ///
    /// <para><b>Co je uvaznuti</b> (rozhodnuti autora 8. 10. 2026): robot <b>ma jet</b> — ma cil
    /// globalni navigace (<c>Driving</c>/<c>GoalInMap</c>/<c>OffRoute</c>), nebo bezi FreeRun, nebo
    /// (bez mapy) chodi lokalni plan, ktery neni skutecny dojezd — <b>nedrzi ho</b> nouzove ani
    /// drzene zastaveni, a presto za posledni dobu neujel ani <see cref="StuckConfig.MinMotionM"/>.
    /// <b>Na stavu planu to nezavisi</b>: Kolo4 (19. 9.) stalo 268 s v <c>GoalUnsafe</c>/<c>GoalBlocked</c>,
    /// tedy v „platnem" planu, a hlidani postavene jen na <c>RobotBlocked</c> by ho minulo. Stav planu
    /// jen <b>pojmenuje pricinu</b>.</para>
    ///
    /// <para>⚠️ <b>Jizda <c>goal=</c> bez mapy</b> se pozna jen podle cerstveho lokalniho planu, takze
    /// vypadek kamer ji ukonci jako „jizda skoncila" — bez navigace neni jiny signal, ze cil trva
    /// (a zrusit ho jde i z UI, kdy plany zmlknou taky). Rezim pro testy, ne provoz; FreeRun a jizda
    /// s navigaci vypadek kamer hlasi jako „bez lokalniho planu".</para>
    ///
    /// <para><b>Jen hlasi</b> (autor 27. 8. a 8. 10. 2026): nic neprerusuje, nezavira ani nezastavuje.
    /// Dve urovne — „stoji" od 20 s (muze se vyresit samo), „UVAZL" od 60 s.</para>
    ///
    /// <para><b>Cas je cas DAT</b> (razitka <c>RobotStateMsg</c>/<c>DriveCommandMsg</c> z taktu ridici
    /// smycky, beh jen dopredu), takze hlidac dava totez v behu, v prehravani i v testech —
    /// a da se pustit nad starym zaznamem (<c>ARBot.Analyze uvazl</c>). Razitka planu a FreeRunu jsou
    /// casy snimku dvou kamer a jdou prehazene, proto se z nich bere jen prubezne maximum.</para>
    ///
    /// <para><b>Pohyb</b> = soucet <c>min(|Δpoza|, |v|·Δt)</c> (jako detektor A globalni navigace):
    /// skok pozy po korekci neni pohyb a otaceni na miste taky ne. Poza je odometricka, ma-li ji zprava
    /// (od 4. 10. 2026), jinak fuzovana.</para>
    /// </summary>
    public sealed class StuckDetector
    {
        private readonly StuckConfig cfg;

        // --- vstupy ---
        private DateTime now;                 // hodiny dat (max razitek taktu)
        private bool hasPose;
        private double prevX, prevY;
        private DateTime prevT;
        private double travelled;             // ujeta draha [m]

        private bool hasDrive, estop, held;
        private double cmdSpeed = double.NaN;

        private GlobalNavMsg nav;
        private DateTime navT;
        private DateTime freeRunT;

        private LocalPlanMsg plan;            // posledni plan, ktery NENI AbortedCollision (ten stav prepisuje)
        private DateTime planT;               // cas posledniho planu vcetne AbortedCollision
        private bool lastPlanAborted;

        // --- epizoda ---
        // Klouzave okno ujete drahy: body (cas, draha) po kazdem prirustku o PushStepM. Doba stani =
        // jak dlouho zpatky robot neujel MinMotionM. Prvni bod je okamzik, odkdy robot ma jet
        // (aktivace / konec STOPu) - driv se nepocita.
        private const double PushStepM = 0.01;
        private readonly LinkedList<(DateTime T, double D)> okno = new LinkedList<(DateTime, double)>();
        private DateTime episodeStart;        // zacatek stani, ktere se prave hlasi
        private readonly Queue<(DateTime T, StuckCause C, string Text)> priciny = new Queue<(DateTime, StuckCause, string)>();
        private StuckLevel level;
        private DateTime lastReminder;
        private DateTime lastMsg;
        private DateTime lastTick;

        public StuckDetector(StuckConfig config = null)
        {
            cfg = config ?? new StuckConfig();
            cfg.Validate();
        }

        /// <summary>Nastaveni.</summary>
        public StuckConfig Config => cfg;

        /// <summary>Aktualni uroven.</summary>
        public StuckLevel Level => level;

        /// <summary>
        /// Jak dlouho robot „stoji" pri jizde [s]: jak dlouho zpatky neujel <see cref="StuckConfig.MinMotionM"/>
        /// (0, kdyz nejede). Klouzave okno, ne kotva: robot, ktery se plizi 2,5 cm/s, ma stale ~20 s,
        /// ne pilu 0 → 20 s → 0, ktera by hlaseni zapinala a vypinala kazdy pul metr (videt nad
        /// zaznamy z 2. 9. 2026 v uzkem prostoru).
        /// </summary>
        public double StandingSec => okno.Count > 0 ? Math.Max(0, (now - okno.First.Value.T).TotalSeconds) : 0;

        /// <summary>Cas posledniho vyhodnoceni (taktu); <c>default</c> = jeste zadny.</summary>
        public DateTime LastTick => lastTick;

        /// <summary>
        /// Zpracuje zpravu. Hodnoti se jen na <see cref="RobotStateMsg"/> (takt ridici smycky, 10 Hz);
        /// ostatni zpravy jen aktualizuji vstupy. Do <paramref name="lines"/> pripise radky pro Trace
        /// (prechody urovni, pripominky), vraci zpravu do zaznamu, nebo <c>null</c>.
        /// </summary>
        public StuckMsg Feed(Message msg, List<string> lines)
        {
            switch (msg)
            {
                case DriveCommandMsg d:
                    hasDrive = true;
                    estop = d.EmergencyStop;
                    held = d.Held;
                    cmdSpeed = d.Speed;
                    Advance(d.TimeStamp);
                    return null;
                case GlobalNavMsg g:
                    nav = g;
                    if (g.TimeStamp > navT) navT = g.TimeStamp;
                    return null;
                case FreeRunMsg f:
                    if (f.TimeStamp > freeRunT) freeRunT = f.TimeStamp;
                    return null;
                case LocalPlanMsg p:
                    if (p.TimeStamp > planT) planT = p.TimeStamp;
                    lastPlanAborted = p.PlanStatus == LocalPlanStatus.AbortedCollision;
                    // AbortedCollision prepise stav, ktery planovac v tom cyklu vratil (LocalNavigator),
                    // takze o pricine nic nerika - plati pricina z planu pred nim.
                    if (!lastPlanAborted || plan == null) plan = p;
                    return null;
                case RobotStateMsg s:
                    Motion(s);
                    Advance(s.TimeStamp);
                    return Tick(lines);
                default:
                    return null;
            }
        }

        private void Advance(DateTime t)
        {
            if (t > now) now = t;
        }

        private void Motion(RobotStateMsg s)
        {
            double x = s.HasOdom ? s.OdomX : s.X;
            double y = s.HasOdom ? s.OdomY : s.Y;
            if (hasPose && s.TimeStamp > prevT)
            {
                double dt = (s.TimeStamp - prevT).TotalSeconds;
                if (dt <= 1.0)
                {
                    double dx = x - prevX, dy = y - prevY;
                    double step = Math.Min(Math.Sqrt(dx * dx + dy * dy), Math.Abs(s.V) * dt);
                    if (double.IsFinite(step)) travelled += step;
                }
            }
            if (!hasPose || s.TimeStamp > prevT)
            {
                prevX = x; prevY = y; prevT = s.TimeStamp; hasPose = true;
            }
        }

        private bool Fresh(DateTime t) => t != default && (now - t).TotalSeconds <= cfg.FreshSec;

        /// <summary>Ma robot jet? (Viz komentar u tridy.) <paramref name="why"/> = proc ne.</summary>
        private bool Active(out string why)
        {
            why = null;
            // FreeRun jede do konce runtime (mise nema konec ani stanoviste, mrkev klade porad). Jeji
            // zpravy ale vznikaji ze SNIMKU, takze pri vypadku kamer zmlknou - a to neni konec jizdy,
            // nybrz pricina „bez planu". Proto staci, ze FreeRun v tomhle behu jednou promluvil
            // (hlidac vznika s kazdym Start, takze jina mise ho nezdedi). Do 9. 10. 2026 tu byla
            // cerstvost a vypadek kamer ukoncil hlaseni duvodem „jizda skoncila" (nezavisla kontrola).
            if (freeRunT != default) return true;
            if (nav != null && Fresh(navT))
            {
                var st = (GlobalNavStatus)nav.Status;
                bool driving = nav.HasGoal
                               && (st == GlobalNavStatus.Driving || st == GlobalNavStatus.GoalInMap
                                   || st == GlobalNavStatus.OffRoute);
                if (!driving) why = st == GlobalNavStatus.Arrived ? "dojel do cíle" : "jízda skončila";
                return driving;
            }
            if (plan != null && Fresh(planT))
            {
                if (IsGenuineArrival(plan)) { why = "dojel do cíle"; return false; }
                return true;
            }
            why = "jízda skončila";
            return false;
        }

        /// <summary>
        /// Skutecny dojezd: <c>AlreadyAtGoal</c>, kde plan „dosahl" pozadovaneho cile. Lokalni
        /// minimum (18. 9. 2026: mrkev 7 m daleko) ma dosazeny bod na robotu, ne v cili.
        /// </summary>
        private static bool IsGenuineArrival(LocalPlanMsg p)
        {
            if (p.PlanStatus != LocalPlanStatus.AlreadyAtGoal) return false;
            double dx = p.RequestedGoalX - p.ReachedGoalX, dy = p.RequestedGoalY - p.ReachedGoalY;
            return Math.Sqrt(dx * dx + dy * dy) < 0.05;
        }

        private StuckMsg Tick(List<string> lines)
        {
            lastTick = now;
            bool legit = hasDrive && (estop || held);
            bool active = Active(out string why);
            if (legit || !active)
            {
                StuckMsg end = null;
                if (level != StuckLevel.None)
                {
                    string reason = legit ? (estop ? "nouzové zastavení" : "držené zastavení") : why;
                    end = End(lines, reason);
                }
                Reset();
                return end;
            }

            // Klouzave okno: novy bod po kazdem centimetru, vypadne vse, od ceho uz robot ujel MinMotionM.
            if (okno.Count == 0) Reset();
            if (travelled >= okno.Last.Value.D + PushStepM) okno.AddLast((now, travelled));
            while (okno.Count > 1 && okno.First.Value.D <= travelled - cfg.MinMotionM) okno.RemoveFirst();

            double standing = StandingSec;

            // Okamzita pricina do okna (prevazujici z nej jde do hlaseni).
            string okamzita = InstantCause(out var okamzitaPricina);
            priciny.Enqueue((now, okamzitaPricina, okamzita));
            while (priciny.Count > 0 && (now - priciny.Peek().T).TotalSeconds > CauseWindowSec) priciny.Dequeue();

            // Konec s hysterezi: az kdyz robot ujede MinMotionM za POLOVINU prahu (0,5 m za 10 s,
            // tedy jede aspon 5 cm/s). Bez ni by plizeni tesne kolem prahu hlaseni zapinalo a vypinalo.
            if (level != StuckLevel.None && standing < cfg.StandingSec * 0.5)
                return End(lines, "rozjel se");
            // Pri pomalem rozjezdu se okno zkracuje postupne - „UVAZL 16 s" by lhalo. Sestup se stejnou
            // hysterezi (pod polovinu prahu); bez radku do Trace, konec stani se ohlasi az pri End.
            if (level == StuckLevel.Stuck && standing < cfg.StuckSec * 0.5)
                level = StuckLevel.Standing;

            var target = standing >= cfg.StuckSec ? StuckLevel.Stuck
                       : standing >= cfg.StandingSec ? StuckLevel.Standing
                       : StuckLevel.None;

            if (target > level)
            {
                if (level == StuckLevel.None) episodeStart = okno.First.Value.T;
                level = target;
                string pricina = CauseText(out _);
                if (level == StuckLevel.Stuck)
                {
                    lines?.Add(string.Format(CultureInfo.InvariantCulture,
                        "UVAZL: robot stoji {0:F0} s pri jizde - {1}. Ceka na zasah obsluhy.", standing, Ascii(pricina)));
                    lastReminder = now;
                }
                else
                {
                    lines?.Add(string.Format(CultureInfo.InvariantCulture,
                        "STANI: robot stoji {0:F0} s pri jizde - {1}.", standing, Ascii(pricina)));
                }
                return Message();
            }

            if (level == StuckLevel.Stuck && (now - lastReminder).TotalSeconds >= cfg.ReminderSec)
            {
                lastReminder = now;
                lines?.Add(string.Format(CultureInfo.InvariantCulture,
                    "UVAZL trva {0:F0} s - {1}.", standing, Ascii(CauseText(out _))));
            }
            if (level != StuckLevel.None && (now - lastMsg).TotalSeconds >= cfg.MessagePeriodSec)
                return Message();
            return null;
        }

        /// <summary>Okno od ted - robot zacina (znovu) mit jet.</summary>
        private void Reset()
        {
            priciny.Clear();
            okno.Clear();
            okno.AddLast((now, travelled));
        }

        /// <summary>Konec epizody; doba je od zacatku stani (ne aktualni okno - to uz se zkratilo).</summary>
        private StuckMsg End(List<string> lines, string reason)
        {
            double trvalo = Math.Max(0, (now - episodeStart).TotalSeconds);
            lines?.Add(string.Format(CultureInfo.InvariantCulture,
                "STANI skoncilo po {0:F0} s: {1}.", trvalo, Ascii(reason)));
            level = StuckLevel.None;
            var m = Message();
            m.EpisodeSec = trvalo;
            m.Text = "stání skončilo: " + reason;
            return m;
        }

        private StuckMsg Message()
        {
            lastMsg = now;
            string text = level == StuckLevel.None ? string.Empty : Describe();
            CauseText(out var cause);
            return new StuckMsg
            {
                Level = (byte)level,
                StandingSec = StandingSec,
                EpisodeSec = level != StuckLevel.None ? Math.Max(0, (now - episodeStart).TotalSeconds) : 0,
                Cause = (byte)cause,
                PlanStatus = plan != null ? plan.Status : -1,
                PlanAgeSec = planT != default ? Math.Max(0, (now - planT).TotalSeconds) : double.NaN,
                GoalDistanceM = plan?.GoalDistanceM ?? double.NaN,
                StartBlock = plan?.StartBlock ?? 0,
                StartClearanceM = plan?.StartClearanceM ?? double.NaN,
                CommandSpeed = cmdSpeed,
                Text = text,
                TimeStamp = now,
            };
        }

        /// <summary>
        /// Text pro stranku nahledu: „stojí 34 s při jízdě — …", „UVÁZL 75 s — … — zásah obsluhy",
        /// nebo prazdny.
        /// </summary>
        public string Describe()
        {
            if (level == StuckLevel.None) return string.Empty;
            string pricina = CauseText(out _);
            return level == StuckLevel.Stuck
                ? string.Format(CultureInfo.InvariantCulture, "UVÁZL {0:F0} s — {1} — zásah obsluhy", StandingSec, pricina)
                : string.Format(CultureInfo.InvariantCulture, "stojí {0:F0} s při jízdě — {1}", StandingSec, pricina);
        }

        /// <summary>
        /// Pricina stani (s diakritikou): <b>nejcastejsi</b> za poslednich <see cref="CauseWindowSec"/>
        /// sekund taktu, ne jen podle posledniho planu. Stavy planu se stridaji ~19× za sekundu
        /// (1. 10. bylo uvnitr RobotBlocked 16× EscapingBlocked, 18. 9. uvnitr AlreadyAtGoal Partial)
        /// a hlaseni podle posledniho by pricinu tahalo nahodne - 18. 9. vyslo „povel je nulovy"
        /// uprostred stani, ktere bylo cele o nedosazitelne mrkvi.
        /// </summary>
        public string CauseText(out StuckCause cause)
        {
            if (priciny.Count == 0) return InstantCause(out cause);
            var pocty = new Dictionary<StuckCause, int>();
            foreach (var p in priciny) pocty[p.C] = pocty.TryGetValue(p.C, out int n) ? n + 1 : 1;
            int max = pocty.Values.Max();
            // Pri shode vyhraje ta, ktera prisla naposled; text je jeji nejnovejsi.
            string text = null;
            cause = StuckCause.Unknown;
            foreach (var p in priciny)
                if (pocty[p.C] == max) { cause = p.C; text = p.Text; }
            return text;
        }

        /// <summary>Okno, pres ktere se urcuje prevazujici pricina [s].</summary>
        public const double CauseWindowSec = 10.0;

        /// <summary>Pricina podle posledniho planu (okamzita).</summary>
        private string InstantCause(out StuckCause cause)
        {
            if (plan == null || !Fresh(planT))
            {
                cause = StuckCause.NoPlan;
                return planT == default
                    ? "lokální plán nepřišel (kamery nebo fúze)"
                    : string.Format(CultureInfo.InvariantCulture,
                        "bez lokálního plánu {0:F0} s (kamery nebo fúze)", (now - planT).TotalSeconds);
            }

            var st = plan.PlanStatus;
            switch (st)
            {
                case LocalPlanStatus.RobotBlocked:
                case LocalPlanStatus.EscapingBlocked:
                {
                    cause = StuckCause.InBlockedCell;
                    string kde;
                    var b = plan.StartBlockReason;
                    if (b != CellBlockReason.None)
                        kde = "v buňce blokované " + (b == (CellBlockReason.Geometry | CellBlockReason.Semantics)
                                                       ? "hloubkou i barvou"
                                                       : b == CellBlockReason.Geometry ? "hloubkou" : "barvou");
                    else if (double.IsFinite(plan.StartClearanceM))
                        kde = string.Format(CultureInfo.InvariantCulture, "těsně u překážky (odstup {0:0.00} m)",
                                            plan.StartClearanceM);
                    else
                        kde = "v blokované buňce";
                    return st == LocalPlanStatus.EscapingBlocked ? kde + ", únik nepostupuje" : kde + ", únik nenalezen";
                }
                // Slepy konec: od 9. 10. 2026 vlastni stav LocalMinimum, ve starsich zaznamech
                // AlreadyAtGoal s mrkvi daleko (skutecny dojezd sem nedojde - ten ukonci jizdu v Active).
                case LocalPlanStatus.LocalMinimum:
                case LocalPlanStatus.AlreadyAtGoal:
                {
                    cause = StuckCause.CarrotUnreachable;
                    double d = double.IsFinite(plan.GoalDistanceM) ? plan.GoalDistanceM : ReqReached(plan);
                    return string.Format(CultureInfo.InvariantCulture, "mrkev {0:0.0} m nedosažitelná (lokální minimum)", d);
                }
                case LocalPlanStatus.GoalBlocked:
                case LocalPlanStatus.GoalUnsafe:
                {
                    cause = StuckCause.GoalBlocked;
                    string co = st == LocalPlanStatus.GoalBlocked ? "v překážce" : "těsně u překážky";
                    return double.IsFinite(plan.GoalDistanceM)
                        ? string.Format(CultureInfo.InvariantCulture, "mrkev {0:0.0} m {1}", plan.GoalDistanceM, co)
                        : "mrkev " + co;
                }
                case LocalPlanStatus.Ok:
                case LocalPlanStatus.Partial:
                    cause = StuckCause.PlanButNoMotion;
                    return double.IsFinite(cmdSpeed) && Math.Abs(cmdSpeed) > 0.05
                        ? string.Format(CultureInfo.InvariantCulture,
                            "plán vede dál, povel {0:0.00} m/s, ale robot nejede (překážka u kol? motory?)", cmdSpeed)
                        : "plán vede dál, ale povel je nulový";
                default:
                    cause = StuckCause.Unknown;
                    return "stav plánu " + st;
            }
        }

        private static double ReqReached(LocalPlanMsg p)
        {
            double dx = p.RequestedGoalX - p.ReachedGoalX, dy = p.RequestedGoalY - p.ReachedGoalY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Okamzity stav pro stranku.</summary>
        public StuckReading Reading()
        {
            CauseText(out var cause);
            return new StuckReading(level, level == StuckLevel.None ? 0 : StandingSec, cause, Describe());
        }

        /// <summary>
        /// Text bez diakritiky pro Trace (radky v projektu jsou ASCII, stranka diakritiku ma). Jeden
        /// zdroj textu, aby se stranka a Trace nerozesly.
        /// </summary>
        public static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c switch { '—' => '-', '–' => '-', _ => c });
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    /// <summary>
    /// <b>Hlidac uvaznuti</b> jako stupen na Streamu (vzor <see cref="BatteryMonitor"/>): cte
    /// <c>RobotStateMsg</c>, <c>DriveCommandMsg</c>, <c>LocalPlanMsg</c>, <c>GlobalNavMsg</c>
    /// a <c>FreeRunMsg</c>, hlasi prechody do Trace, posila <see cref="StuckMsg"/> do zaznamu
    /// a stranka nahledu cte stav z tehoz objektu (<see cref="Read"/>), aby se nerozesla s Trace.
    ///
    /// <para><b>Proc samostatny stupen, ne globalni navigace:</b> ta neexistuje bez mapy a ve FreeRunu
    /// nema cil, takze by uvaznuti FreeRunu ani jizdy <c>goal=</c> bez mapy nevidela. Grid tu potreba
    /// neni — pricinu nese <see cref="LocalPlanMsg"/> (od verze 4 i kde robot stoji).</para>
    ///
    /// <para>Logika je v <see cref="StuckDetector"/> (bez vlaken, testovatelna, prehratelna nad
    /// zaznamem). Tema <c>nav-uvaznuti-neohlasene</c>, doc/global-navigation-runtime.md.</para>
    /// </summary>
    public sealed class StuckMonitor : MessageProcessor
    {
        /// <summary>Po jak dlouhe dobe bez taktu se stav na strance neukazuje [s] (hlidac nema data).</summary>
        public const double StaleSec = 3.0;

        private readonly StuckDetector detector;
        private readonly Action<string> report;
        private readonly object gate = new object();
        private readonly List<string> lines = new List<string>();

        /// <param name="config">Nastaveni; null = vychozi (20 s / 60 s).</param>
        /// <param name="report">Kam hlasit radky; null = <c>Trace.WriteLine</c>.</param>
        public StuckMonitor(StuckConfig config = null, Action<string> report = null)
            : base(OverflowPolicy.DropOldest, 128)
        {
            detector = new StuckDetector(config);
            this.report = report ?? (s => System.Diagnostics.Trace.WriteLine(s));
        }

        /// <summary>Nastaveni.</summary>
        public StuckConfig Config => detector.Config;

        /// <summary>
        /// Zpracuje zpravu synchronne (pro testy a prehravani bez vlakna); vraci <see cref="StuckMsg"/>,
        /// je-li co poslat. Radky jdou do Trace (mimo zamek).
        /// </summary>
        public StuckMsg Process(Message msg)
        {
            StuckMsg outMsg;
            string[] toReport = null;
            lock (gate)
            {
                lines.Clear();
                outMsg = detector.Feed(msg, lines);
                if (lines.Count > 0) toReport = lines.ToArray();
            }
            // Mimo zamek - hlaseni muze tect do streamu (TraceInfoBridge).
            if (toReport != null) foreach (var l in toReport) report(l);
            return outMsg;
        }

        /// <summary>
        /// Stav pro stranku v case <paramref name="now"/>; <see cref="StuckLevel.None"/>, kdyz hlidac
        /// dele nez <see cref="StaleSec"/> nedostal takt (fuze nebo smycka stoji - stare cislo by lhalo).
        /// </summary>
        public StuckReading Read(DateTime now)
        {
            lock (gate)
            {
                var t = detector.LastTick;
                if (t == default || (now - t).TotalSeconds > StaleSec)
                    return new StuckReading(StuckLevel.None, 0, StuckCause.Unknown, string.Empty);
                return detector.Reading();
            }
        }

        /// <summary>Stav ted (<see cref="TimeBase.Now"/>).</summary>
        public StuckReading Read() => Read(TimeBase.Now);

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (msg is StuckMsg) return;   // vlastni vystup tece zpet ze Streamu
            var m = Process(msg);
            if (m != null) EmitDerived(m);
        }
    }
}
