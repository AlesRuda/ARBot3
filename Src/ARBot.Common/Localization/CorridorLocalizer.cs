using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>Proc se z koridoru (ne)stalo merenie do fuze.</summary>
    public enum CorridorFixReason : byte
    {
        /// <summary>Merenie vzniklo.</summary>
        Ok = 0,

        /// <summary>Koridor se nenasel (duvod je v <see cref="RoadCorridor.Reason"/>).</summary>
        NoCorridor = 1,

        /// <summary>Chybi druha kamera v casovem okne - koridor potrebuje obe strany.</summary>
        NoPair = 2,

        /// <summary>Fuze nezna pozu v case snimku (mimo okno historie).</summary>
        NoPose = 3,

        /// <summary>Mapa v okoli pozy zadnou cestu nema.</summary>
        NoEdge = 4,

        /// <summary>Nejblizsi hrana je moc daleko - nejsme na te ceste.</summary>
        EdgeTooFar = 5,

        /// <summary>Merena pricna poloha se od mapove lisi vic, nez je strop.</summary>
        LateralDisagreement = 6,

        /// <summary>Merena sirka se od mapove (nebo filtrovane) lisi vic, nez je strop.</summary>
        WidthDisagreement = 7,

        /// <summary>
        /// Robot je podle merení <b>mimo koridor</b> (dal od osy nez polosirka + rezerva). Bud se
        /// prolozila jina dvojice hranic, nebo robot z cesty sjel - v obou pripadech merenie
        /// nema co opravovat.
        /// </summary>
        OutsideCorridor = 8,
    }

    /// <summary>
    /// Prevede koridor z kamer na <b>merenia do fuze</b>: pricnou polohu podel normaly mapove osy
    /// a kurz. Mapovou protistranou je <see cref="RoadAxis"/>, kamerovou <see cref="CorridorFinder"/>.
    ///
    /// <para><b>Proc dve skalarni merenia a ne poloha.</b> Kamera nemeri polohu, meri vztah
    /// k cestě: pricna slozka je urcena dobre, podelna na prime ceste <b>vubec</b>. Posila se proto
    /// jen to, co je videt — osa merenia je <b>normala mapove osy</b>, tedy presne znama, ne
    /// odhadovana ze zakriveni skore. Podelna slozka se neposila, takze zadne stropy sigma ani
    /// test nejednoznacnosti podel osy nejsou potreba.</para>
    ///
    /// <para><b>Kazda kamera vidi jen jednu stranu cesty</b> (jsou namirene do stran), takze
    /// koridor vznika z <b>dvojice</b> snimku parovanych casem. Stupen si drzi posledni snimek
    /// z kazde kamery.</para>
    ///
    /// <para>Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public sealed class CorridorLocalizer : Communication.MessageProcessor
    {
        private readonly AsyncFusionEngine engine;
        private readonly RoadNetwork network;
        private readonly GeoReference origin;
        private readonly CorridorLocalizerConfig config;
        private readonly RoadWidthFilter widths;

        /// <summary>Mapove nezavisly zdroj koridoru — parovani kamer, kompenzace, prolozeni.</summary>
        private readonly CorridorSource source;

        /// <param name="queueCapacity">Vstupni fronta snimku; <c>DropOldest</c> - kdyz stupen
        /// nestiha, je lepsi pracovat s nejnovejsim snimkem nez se zpozdovat.</param>
        public CorridorLocalizer(AsyncFusionEngine engine, RoadNetwork network, GeoReference origin,
                                 CorridorLocalizerConfig config = null, int queueCapacity = 4)
            : base(Communication.OverflowPolicy.DropOldest, queueCapacity)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.config = config ?? new CorridorLocalizerConfig();
            source = new CorridorSource(engine, this.config);
            widths = new RoadWidthFilter(this.config.WidthFilterAlpha);
        }

        /// <summary>Nastaveni, se kterym stupen pracuje.</summary>
        public CorridorLocalizerConfig Config => config;

        /// <summary>Odhady sirky cest - vedlejsi produkt merení.</summary>
        public RoadWidthFilter Widths => widths;

        /// <summary>Kolik snimku vstoupilo.</summary>
        public long Frames { get; private set; }

        /// <summary>Kolik merenii se poslalo do fuze.</summary>
        public long EmittedCorrections { get; private set; }

        /// <summary>Posledni vysledek (i neuspesny) - pro telemetrii.</summary>
        public CorridorFix LastFix { get; private set; }

        /// <summary>
        /// Zpracuje snimek kamery. Vraci vysledek, kdyz se z nej (spolu s poslednim snimkem druhe
        /// kamery) dal postavit koridor; jinak <c>null</c> — a duvod je v <see cref="LastFix"/>.
        ///
        /// <para>Verejne schvalne: takhle jde stupen prohnat zaznamem i z testu bez vlakna.</para>
        /// </summary>
        public CorridorFix Process(CameraFrame frame)
        {
            // MAPOVE NEZAVISLA polovina (parovani kamer, kompenzace pohybu, prolozeni hranic) sedi
            // v CorridorSource - potrebuje ji i FreeRunMission, ktera mapu nema. Viz
            // doc/mission-freerun.md.
            var src = source.Process(frame);
            if (src == null) return null;
            Frames = source.Frames;

            var pose = src.Pose;
            if (!src.Ok)
            {
                LastFix = WithPose(new CorridorFix
                {
                    Time = src.Time, Corridor = src.Corridor, Reason = src.Reason,
                }, pose);
                return null;
            }

            var corridor = src.Corridor;
            var fix = WithPose(new CorridorFix { Time = src.Time, Corridor = corridor }, pose);

            if (pose == null)
            {
                fix.Reason = CorridorFixReason.NoPose;
                LastFix = fix;
                return null;
            }

            var axis = RoadAxis.Match(network, origin, pose.X, pose.Y, pose.Theta);
            if (!axis.Found)
            {
                fix.Reason = CorridorFixReason.NoEdge;
                LastFix = fix;
                return null;
            }
            if (axis.DistanceM > config.MaxEdgeDistanceM)
            {
                fix.Reason = CorridorFixReason.EdgeTooFar;
                LastFix = fix;
                return null;
            }

            fix.Axis = axis;
            fix.MapWidthM = widths.Estimate(axis.WayId, axis.WidthM);
            fix.LateralDisagreement = corridor.Lateral - axis.Lateral;
            fix.HeadingDisagreementRad = corridor.DirectionRad - axis.HeadingRelRad;
            fix.WidthDisagreement = corridor.Width - fix.MapWidthM;

            if (Math.Abs(fix.WidthDisagreement) > config.MaxWidthDisagreementM)
            {
                fix.Reason = CorridorFixReason.WidthDisagreement;
                LastFix = fix;
                return null;
            }
            if (Math.Abs(fix.LateralDisagreement) > config.MaxLateralDisagreementM)
            {
                fix.Reason = CorridorFixReason.LateralDisagreement;
                LastFix = fix;
                return null;
            }

            // Sirka se uci jen z cyklu, kde poza sedi (jinak by se do ni zapsala chyba pozy).
            if (Math.Abs(fix.LateralDisagreement) <= config.WidthUpdateMaxDisagreementM)
                fix.FilteredWidthM = widths.Update(axis.WayId, corridor.Width);
            else
                fix.FilteredWidthM = fix.MapWidthM;

            fix.Reason = CorridorFixReason.Ok;
            if (config.SendCorrections) Send(fix);
            LastFix = fix;
            return fix;
        }

        /// <summary>
        /// Zapise do vysledku pozu, se kterou se merilo (nebo nic, kdyz ji fuze nezna).
        /// Vola se na VSECH cestach vcetne zamitnutych - vrstva ve World pohledu potrebuje pozu
        /// i u cyklu, ktery neprosel, aby slo nakreslit, kudy prolozeni vedlo.
        /// </summary>
        private static CorridorFix WithPose(CorridorFix fix, Fusion.RobotState pose)
        {
            if (pose == null) return fix;
            fix.PoseX = pose.X;
            fix.PoseY = pose.Y;
            fix.PoseTheta = pose.Theta;
            fix.HasPose = true;
            return fix;
        }

        /// <summary>
        /// Posle merenia do fuze: projekci polohy na <b>normalu mapove osy</b> a kurz.
        ///
        /// <para>Kamera rika „jsem <c>e</c> vlevo od osy koridoru". Kdyz je osa koridoru osa cesty,
        /// plati <c>n · p_true = n · A + e</c>, kde <c>n</c> je leva normala hrany a <c>A</c> bod
        /// na ose. Kurz: cesta se v ramci robotu jevi stocena o <c>d</c>, mapa rika, ze vede pod
        /// <c>θ_edge</c>, tedy <c>θ_true = θ_edge − d</c>.</para>
        /// </summary>
        private void Send(CorridorFix fix)
        {
            double gate = Gating.ChiSquareThreshold(1);
            var a = fix.Axis;
            var c = fix.Corridor;

            double value = a.NormalX * a.AxisX + a.NormalY * a.AxisY + c.Lateral;
            engine.Enqueue(new AxisOffsetMeasurement(a.NormalX, a.NormalY, value,
                                                     c.SigmaLateral, fix.Time, config.MeasurementSource)
            { GateThreshold = gate, GateMode = config.GateMode });
            EmittedCorrections++;
            fix.EmittedLateral = true;

            if (config.SendHeading)
            {
                // θ_edge = kurz robotu + relativni sklon hrany; θ_true = θ_edge − smer koridoru.
                double edgeDir = fix.PoseTheta + a.HeadingRelRad;
                engine.Enqueue(new HeadingMeasurement(edgeDir - c.DirectionRad, c.SigmaDirectionRad,
                                                      fix.Time, config.MeasurementSource)
                { GateThreshold = gate, GateMode = config.GateMode });
                EmittedCorrections++;
                fix.EmittedHeading = true;
            }
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Frontou tece i cizi provoz - zajimaji nas vyhradne snimky kamer s hranicemi cesty.
            if (!(msg is CameraFrame frame)) return;

            try
            {
                Process(frame);
                // Zprava se emituje i kdyz merenie nevzniklo - duvod je jeji hlavni obsah
                // (past "Reason = Ok sviti a do fuze nejde nic" ma byt videt v telemetrii).
                if (LastFix != null)
                {
                    // Zpetna vazba z fuze: kolik NASICH merenii uz zahodila jako starsi nez okno.
                    // Doplnuje se u KAZDEHO cyklu (i neuspesneho), aby cislo v telemetrii nechybelo
                    // prave v okamzicich, kdy je nejzajimavejsi.
                    engine.DroppedTooOldBySource().TryGetValue(config.MeasurementSource, out long dropped);
                    LastFix.DroppedByFusion = dropped;
                    EmitDerived(LastFix.ToLogMessage());
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CorridorLocalizer: {ex}"); }
        }
    }
}
