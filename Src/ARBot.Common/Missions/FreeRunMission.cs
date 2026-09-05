using System;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
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

        /// <param name="engine">Fuze — poza k casu snimku a aktualni poza pri jizde bez koridoru.</param>
        /// <param name="localGoal">Prijemce mrkve (lokalni navigator).</param>
        /// <param name="corridors">Mapove nezavisly zdroj koridoru.</param>
        /// <param name="config">Konfigurace mise; null = vychozi.</param>
        /// <param name="queueCapacity">Vstupni fronta snimku (DropOldest).</param>
        public FreeRunMission(AsyncFusionEngine engine, ILocalGoalSink localGoal,
                              CorridorSource corridors, FreeRunConfig config = null,
                              int queueCapacity = 4)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
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
            return result.FromCorridor ? "jede v koridoru" : "bez koridoru, drzi kurz";
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FreeRunMission: {ex}"); }
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
            double offset = -corridor.Lateral - corridor.Width * cfg.RightOffsetFraction;

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
            double c = Math.Cos(pose.Theta), s = Math.Sin(pose.Theta);
            return (pose.X + bx * c - by * s,
                    pose.Y + bx * s + by * c);
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
