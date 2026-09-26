using System;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Runtime;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Mise FreeRun: jizda v prave polovine koridoru, bez mapy.</b> Viz doc/mission-freerun.md.
    ///
    /// <para>Pouziti: <b>homologace</b> a <b>presun mezi stanovisti</b>. Nepotrebuje zadnou <c>.osm</c>,
    /// zadnou trasu ani cil — jen kamery a lokalni mapu.</para>
    ///
    /// <para><b>Je to producent mrkve.</b> Sedi presne tam, kde jinak <c>GlobalNavigator</c>, a mluvi
    /// tymz svem: <see cref="ILocalGoalSink.SetGoal"/>. Lokalni vrstva (occupancy grid, A*, odstupy
    /// od prekazek, rychlostni obalka) se pouzije NEZMENENA — mise jen posouva cil. Proto je mala:
    /// nevznika novy ridici retez.</para>
    ///
    /// <para><b>Koridor je PREFERENCE, ne omezeni</b> (rozhodnuti autora): kdyz prekazka blokuje
    /// pravou polovinu, A* ji objede kudy muze — klidne pres osu nebo mimo koridor — a robot se pak
    /// vrati vpravo. Do planovace se nesaha.</para>
    ///
    /// <para><b>Bez koridoru robot drzi AKTUALNI kurz</b> (mrkev primo vpred). Take rozhodnuti
    /// autora: jednodussi a predvidatelnejsi nez podrzeni posledniho koridoru. Kdyby to v praxi
    /// cukalo, znama lecba je to podrzeni — viz doc/mission-freerun.md.</para>
    ///
    /// <para><b>Hlaseni stavu</b> (<see cref="IMissionStatus"/>): FreeRun <b>neceka na nic zvenci</b>
    /// — nema stanoviste, kod ani operatora, jede z toho, co zrovna vidi. Proto ma vzdy
    /// <see cref="MissionWait.None"/> a odpoved na „co robot dela" nese <see cref="PhaseText"/>
    /// (jede v koridoru / drzi kurz / ceka na pozu). Kdyby se sem cpal umely „ceka na koridor",
    /// prestal by ten radek na strance znamenat „bez zasahu cloveka se nic nestane".</para>
    ///
    /// <para><b>Vlakno:</b> <see cref="MessageProcessor"/> nad <see cref="CameraFrame"/>, fronta
    /// <see cref="OverflowPolicy.DropOldest"/> — kdyz mise nestiha, je spravne pracovat
    /// s NEJNOVEJSIM snimkem.</para>
    /// </summary>
    public sealed class FreeRunMission : MessageProcessor, IMissionStatus
    {
        private readonly ILocalGoalSink localGoal;
        private readonly AsyncFusionEngine engine;
        private readonly CorridorSource corridors;
        private readonly FreeRunConfig config;
        private readonly Func<RobotState, RoadCorridor, double?> mapWidth;

        /// <param name="engine">Fuze — poza k casu snimku a aktualni poza pri jizde bez koridoru.</param>
        /// <param name="localGoal">Prijemce mrkve (lokalni navigator).</param>
        /// <param name="corridors">Mapove nezavisly zdroj koridoru.</param>
        /// <param name="config">Konfigurace mise; null = vychozi.</param>
        /// <param name="queueCapacity">Vstupni fronta snimku (DropOldest).</param>
        /// <param name="mapWidth">
        /// Sirka cesty z mapy v miste robotu pro mrkev podle JEDINE hrany (typicky
        /// <see cref="MapWidthAt"/>); <c>null</c> = mise mapu nema a u jedine hrany drzi zmereny
        /// odstup od ni. Funkce smi vratit <c>null</c> (sirka neznama).
        /// </param>
        public FreeRunMission(AsyncFusionEngine engine, ILocalGoalSink localGoal,
                              CorridorSource corridors, FreeRunConfig config = null,
                              int queueCapacity = 4,
                              Func<RobotState, RoadCorridor, double?> mapWidth = null)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.mapWidth = mapWidth;
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.localGoal = localGoal ?? throw new ArgumentNullException(nameof(localGoal));
            this.corridors = corridors ?? throw new ArgumentNullException(nameof(corridors));
            this.config = config ?? new FreeRunConfig();
            this.config.Validate();
        }

        /// <summary>Nastaveni, se kterym mise pracuje.</summary>
        public FreeRunConfig Config => config;

        /// <summary>DIAGNOSTIKA: kolik snimku proslo.</summary>
        public long Frames { get; private set; }

        /// <summary>DIAGNOSTIKA: kolikrat se mrkev polozila podle KORIDORU.</summary>
        public long CarrotsFromCorridor { get; private set; }

        /// <summary>DIAGNOSTIKA: kolikrat se mrkev polozila podle JEDINE hrany.</summary>
        public long CarrotsFromSingleEdge { get; private set; }

        /// <summary>DIAGNOSTIKA: kolikrat se jelo rovne (koridor nebyl).</summary>
        public long CarrotsStraightAhead { get; private set; }

        /// <summary>Posledni vysledek (diagnostika pro UI a telemetrii).</summary>
        public FreeRunResult LastResult { get; private set; }

        // --- IMissionStatus: jednotne hlaseni stavu pro webovy nahled a UI ---

        // Cas prvniho a posledniho zpracovaneho snimku - hodiny DAT, ne stroje, aby Elapsed
        // znamenal totez pri prehravani zaznamu i v testech (tataz zasada jako u RobotourMission).
        private DateTime firstFrameAt, lastFrameAt;

        /// <inheritdoc/>
        public string MissionName => MissionStatusText.FreeRun;

        /// <summary>
        /// Co mise prave dela. <b>Klicovy je rozdil „v koridoru" x „drzi kurz"</b> — to je jediny
        /// stav, ktery se z venku pozna jako jina jizda a je to prvni otazka pri diagnostice.
        /// </summary>
        public string PhaseText => PhaseTextFor(LastResult);

        /// <summary>
        /// Text stavu z vysledku cyklu; <c>null</c> = jeste zadny nebyl.
        ///
        /// <para>Verejne staticke ze stejneho duvodu jako <see cref="CarrotBody"/>: jde to overit
        /// bez vlakna, bez kamery a bez fuze.</para>
        /// </summary>
        public static string PhaseTextFor(FreeRunResult result)
        {
            if (result == null) return "ceka na prvni snimek";
            if (!result.HasPose) return "ceka na pozu z fuze";
            if (result.FromCorridor) return "jede v koridoru";
            if (result.FromSingleEdge)
                return (result.SingleSide == CorridorSide.Left ? "jede podle leve hrany" : "jede podle prave hrany")
                       + (result.WidthFromMap ? ", sirka z mapy" : ", drzi odstup");
            return "bez koridoru, drzi kurz";
        }

        /// <summary>
        /// Vzdy <see cref="MissionWait.None"/> — FreeRun nema na co zvenci cekat, viz komentar
        /// u tridy.
        /// </summary>
        public MissionWait WaitingFor => MissionWait.None;

        /// <summary>Jak dlouho mise bezi, mereno razitky zpracovanych snimku.</summary>
        public TimeSpan Elapsed
            => firstFrameAt == default || lastFrameAt <= firstFrameAt
               ? TimeSpan.Zero
               : lastFrameAt - firstFrameAt;

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (!(msg is CameraFrame frame)) return;
            try
            {
                var result = Process(frame);
                if (result != null) EmitDerived(result.ToLogMessage());
            }
            // Trace, ne Debug: v Release (na zarizeni) by po selhani cyklu mise nezustala stopa a
            // vypadalo by to, ze mise "jen nic nedela". Viz CLAUDE.md.
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"FreeRunMission: cyklus selhal: {ex}"); }
        }

        /// <summary>
        /// Jeden snimek: spocte mrkev a posle ji do lokalni vrstvy. Vraci <c>null</c>, kdyz se
        /// snimkem nejde nic delat (bez hranicnich bodu, nebo fuze nezna pozu).
        ///
        /// <para>Verejne schvalne: takhle jde mise prohnat zaznamem i z testu BEZ vlakna.</para>
        /// </summary>
        public FreeRunResult Process(CameraFrame frame)
        {
            var src = corridors.Process(frame);
            if (src == null) return null;
            Frames++;

            // Doba behu mise se meri od PRVNIHO zpracovaneho snimku (hodiny dat), ne od vzniku
            // objektu: stupen vznika pri skladani grafu, tedy driv, nez zacnou chodit data.
            if (firstFrameAt == default) firstFrameAt = frame.TimeStamp;
            if (frame.TimeStamp > lastFrameAt) lastFrameAt = frame.TimeStamp;

            // Poza: z koridoru, kdyz ji ma (je to poza POŘÍZENÍ snimku); jinak aktualni.
            var pose = src.Pose ?? engine.GetStateAt(frame.TimeStamp);
            if (pose == null)
            {
                // Bez pozy se mrkev nema kam polozit. Cil se NERUSI - lokalni vrstva dojede po
                // posledni draze a rizene dobrzdi (viz LocalNavigator.ClearGoal), coz je lepsi
                // nez skokem zastavit kvuli jednomu snimku bez pozy.
                LastResult = new FreeRunResult
                {
                    TimeStamp = frame.TimeStamp, Reason = src.Reason, HasPose = false,
                };
                return LastResult;
            }

            var result = new FreeRunResult
            {
                TimeStamp = frame.TimeStamp,
                Reason = src.Reason,
                HasPose = true,
                PoseX = pose.X, PoseY = pose.Y, PoseTheta = pose.Theta,
            };

            double gx, gy, width;
            if (src.Ok)
            {
                var c = src.Corridor;
                (gx, gy) = CarrotWorld(c, pose, config);
                width = c.Width;
                result.FromCorridor = true;
                result.Width = c.Width;
                result.Lateral = c.Lateral;
                result.DirectionRad = c.DirectionRad;
                CarrotsFromCorridor++;
            }
            else if (config.UseSingleEdge && src.SingleEdgeUsable && src.Corridor != null
                     && src.Corridor.HasSingleEdge)
            {
                // Jedna hrana: smer cesty i pricna poloha vuci hrane jsou zmerene. Se sirkou z mapy
                // vznikne osa a mrkev jde doprostred prave poloviny jako u oboustranneho koridoru;
                // bez ni jde ve smeru hrany se zachovanym odstupem. Viz FreeRunConfig.UseSingleEdge.
                var c = src.Corridor;
                double? w = null;
                try { w = mapWidth?.Invoke(pose, c); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"FreeRunMission: sirka z mapy selhala: {ex.Message}"); }
                if (w.HasValue && !(w.Value > 0)) w = null;

                var (bx, by) = CarrotBodySingleEdge(c, w, config);
                (gx, gy) = BodyToWorld(bx, by, pose);
                width = w ?? 0;
                result.FromCorridor = false;
                result.SingleSide = c.SingleSide;
                result.EdgeOffset = c.EdgeOffset;
                result.DirectionRad = c.DirectionRad;
                result.WidthFromMap = w.HasValue;
                if (w.HasValue)
                {
                    result.Width = w.Value;
                    result.Lateral = c.SingleEdgeLateral(w.Value);
                }
                CarrotsFromSingleEdge++;
            }
            else
            {
                // Koridor neni -> drzet AKTUALNI kurz. Sirka se neposila (0 = neresit): bez koridoru
                // se nema o cem tvrdit, jak je cesta siroka.
                (gx, gy) = CarrotStraightAhead(pose, config);
                width = 0;
                result.FromCorridor = false;
                CarrotsStraightAhead++;
            }

            result.GoalX = gx;
            result.GoalY = gy;
            localGoal.SetGoal(gx, gy, width);

            LastResult = result;
            return result;
        }

        /// <summary>
        /// Mrkev v ramci robotu (FLU: +X vpred, +Y vlevo) pro dany koridor.
        ///
        /// <para><b>Odvozeni.</b> Pozadovana pricna poloha je <c>−Width·f</c> (tedy VPRAVO od osy;
        /// <see cref="RoadCorridor.Lateral"/> je kladna, kdyz je robot VLEVO). Osa lezi vuci robotu
        /// na <c>−Lateral·n</c>, pozadovana cara tedy na <c>(−Lateral − Width·f)·n</c>. Mrkev je
        /// tento bod posunuty o lookahead PO SMERU CESTY:</para>
        /// <code>
        /// d = (cos φ, sin φ)      n = (−sin φ, cos φ)      φ = DirectionRad
        /// mrkev = L·d + (−Lateral − Width·f)·n
        /// </code>
        /// </summary>
        public static (double bodyX, double bodyY) CarrotBody(RoadCorridor corridor, FreeRunConfig cfg)
        {
            if (corridor == null) throw new ArgumentNullException(nameof(corridor));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double phi = corridor.DirectionRad;
            double c = Math.Cos(phi), s = Math.Sin(phi);
            double offset = -corridor.Lateral - RightOffsetFromAxis(corridor.Width, cfg);

            // L*d + offset*n
            return (cfg.LookaheadM * c + offset * -s,
                    cfg.LookaheadM * s + offset * c);
        }

        /// <summary>
        /// Mrkev ve svete [m, ENU]. Prevadi se <b>pozou POŘÍZENÍ snimku</b>, ne „posledni znamou" —
        /// jinak by se mrkev za jizdy pokladala vedle. Tataz konvence jako
        /// <see cref="Logs.RoadCorridorMsg.PoseX"/>.
        /// </summary>
        public static (double worldX, double worldY) CarrotWorld(RoadCorridor corridor,
                                                                 RobotState pose, FreeRunConfig cfg)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));

            var (bx, by) = CarrotBody(corridor, cfg);
            return BodyToWorld(bx, by, pose);
        }

        /// <summary>
        /// O kolik vpravo od osy lezi pozadovana cara [m] (kladne cislo = vpravo).
        ///
        /// <para><b>Pravidlo (autor, 26. 9. 2026):</b> prava polovina (<c>Width·f</c> od osy) jen
        /// tehdy, kdyz od praveho kraje zbyde aspon <see cref="FreeRunConfig.MinRightEdgeClearanceM"/>;
        /// jinak se cara posune k ose tak, aby ten odstup drzela, a kdyz nejde ani to, je na stredu
        /// cesty (0). Je to OMEZENI, ne prepinac: prepinac „prava polovina / stred" by pri sirce
        /// kolisajici kolem prahu skakal o ctvrtinu sirky snimek od snimku.</para>
        /// <code>
        /// vpravo = min(Width·f, max(0, Width/2 − MinRightEdgeClearanceM))
        /// </code>
        /// <para>S vychozimi 0,55 m: cesta nad 2,2 m = ctvrtina sirky jako dosud, pod 1,1 m = stred.</para>
        /// </summary>
        public static double RightOffsetFromAxis(double widthM, FreeRunConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (!(widthM > 0)) return 0;
            double desired = widthM * cfg.RightOffsetFraction;
            double allowed = Math.Max(0, widthM / 2 - cfg.MinRightEdgeClearanceM);
            return Math.Min(desired, allowed);
        }

        /// <summary>Bod z ramce robotu (FLU) do sveta [m, ENU] podle pozy.</summary>
        private static (double worldX, double worldY) BodyToWorld(double bx, double by, RobotState pose)
        {
            double c = Math.Cos(pose.Theta), s = Math.Sin(pose.Theta);
            return (pose.X + bx * c - by * s,
                    pose.Y + bx * s + by * c);
        }

        /// <summary>
        /// Mrkev v ramci robotu (FLU) podle <b>JEDINE</b> hrany.
        ///
        /// <para><b>Se sirkou</b> <paramref name="widthM"/> (z mapy): pricna poloha vuci ose je
        /// <see cref="RoadCorridor.SingleEdgeLateral"/> a dal plati tentyz vzorec jako
        /// v <see cref="CarrotBody"/> — mrkev doprostred prave poloviny. U prave hrany to vychazi
        /// na <c>Width·f</c> od ni dovnitr cesty, u leve na <c>Width·(1 − f)</c>.</para>
        ///
        /// <para><b>Bez sirky</b> (<c>null</c>): mrkev ve smeru hrany se <b>zachovanym zmerenym
        /// odstupem</b>, tedy <c>L·d</c> — rovnobezne s hranou. Pricna poloha vuci ose znama neni,
        /// takze se na ni ani nereguluje. Jedina vyjimka: u PRAVE hrany blize nez
        /// <see cref="FreeRunConfig.MinRightEdgeClearanceM"/> se cara odsune doleva na tento odstup.</para>
        ///
        /// <para>Se sirkou plati i omezeni odstupu od praveho kraje (<see cref="RightOffsetFromAxis"/>).</para>
        /// </summary>
        public static (double bodyX, double bodyY) CarrotBodySingleEdge(RoadCorridor corridor, double? widthM,
                                                                       FreeRunConfig cfg)
        {
            if (corridor == null) throw new ArgumentNullException(nameof(corridor));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (!corridor.HasSingleEdge) throw new ArgumentException("Koridor nenese mereni z jedne hrany.", nameof(corridor));

            double phi = corridor.DirectionRad;
            double c = Math.Cos(phi), s = Math.Sin(phi);
            double offset = 0;
            if (widthM.HasValue && widthM.Value > 0)
            {
                double w = widthM.Value;
                offset = -corridor.SingleEdgeLateral(w) - RightOffsetFromAxis(w, cfg);
            }
            else if (corridor.SingleSide == CorridorSide.Right)
            {
                // Bez sirky se drzi zmereny odstup - ale od PRAVEHO kraje nejmene
                // MinRightEdgeClearanceM (EdgeOffset je u prave hrany zaporny = vzdalenost vpravo).
                offset = Math.Max(0, cfg.MinRightEdgeClearanceM + corridor.EdgeOffset);
            }
            return (cfg.LookaheadM * c + offset * -s,
                    cfg.LookaheadM * s + offset * c);
        }

        /// <summary>
        /// Sirka cesty z mapy v miste robotu pro mrkev podle jedine hrany, nebo <c>null</c>, kdyz se
        /// neda verit: mapova cesta je dal nez <see cref="FreeRunConfig.MapWidthMaxDistanceM"/>, nebo
        /// neni rovnobezna s viditelnou hranou (<see cref="FreeRunConfig.MapWidthMaxAngleDeg"/> —
        /// u krizovatky by sirku jinak dala pricna ulice).
        /// </summary>
        public static double? MapWidthAt(RoadNetwork network, GeoReference origin, RobotState pose,
                                         RoadCorridor corridor, FreeRunConfig cfg)
        {
            if (network == null || origin == null || pose == null || corridor == null || cfg == null) return null;
            var m = RoadAxis.Match(network, origin, pose.X, pose.Y, pose.Theta);
            if (!m.Found || m.DistanceM > cfg.MapWidthMaxDistanceM || !(m.WidthM > 0)) return null;
            double dAng = Math.Abs(Conversions.NormalizeHalfOrientation(corridor.DirectionRad - m.HeadingRelRad));
            if (dAng > cfg.MapWidthMaxAngleDeg * Math.PI / 180.0) return null;
            return m.WidthM;
        }

        /// <summary>
        /// Mrkev pri jizde bez koridoru: lookahead <b>primo vpred</b> od dane pozy, tedy „drzet
        /// aktualni kurz". Zadne pricne uhnuti.
        /// </summary>
        public static (double worldX, double worldY) CarrotStraightAhead(RobotState pose, FreeRunConfig cfg)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            return (pose.X + cfg.LookaheadM * Math.Cos(pose.Theta),
                    pose.Y + cfg.LookaheadM * Math.Sin(pose.Theta));
        }
    }
}
