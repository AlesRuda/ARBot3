using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// <b>Koridor cesty ze snímků kamer — bez mapy.</b> Spáruje snímky obou kamer, zkompenzuje pohyb
    /// mezi nimi a proloží hranice; vrací <see cref="RoadCorridor"/> v ramci robotu (FLU) plus pozu,
    /// se kterou se merilo.
    ///
    /// <para><b>Nacpak samostatny stupen.</b> Tuhle praci delal <see cref="CorridorLocalizer"/>, ktery
    /// ale MAPU VYZADUJE (hodi vyjimku na null <c>RoadNetwork</c>), protoze koridor rovnou srovnava
    /// s mapovou osou. <c>FreeRunMission</c> mapu nema a potrebuje presne tuhle mapove nezavislou
    /// polovinu — viz doc/mission-freerun.md. Duplikovat parovani by bylo spatne: je to ta nejchytrejsi
    /// cast toho kodu (parovaci okno, kompenzace pohybu) a stala nejvic mereni.</para>
    ///
    /// <para><b>Co tady NENI:</b> srovnani s mapou, merenia do fuze, filtr sirky cest. To zustava
    /// v <see cref="CorridorLocalizer"/>, ktery nad timhle stupnem stoji.</para>
    ///
    /// <para><b>Vlakno:</b> zadne. Je to cista sluzba volana z <see cref="Process"/> — vlakno resi
    /// az volajici (<c>CorridorLocalizer</c> i mise jsou <c>MessageProcessor</c>).</para>
    /// </summary>
    public sealed class CorridorSource
    {
        private readonly AsyncFusionEngine engine;
        private readonly CorridorLocalizerConfig config;
        private readonly CorridorFinder finder;

        // Posledni hranicni body z kazde kamery (v ramci robotu) + jejich cas.
        private readonly Dictionary<string, (DateTime T, List<Point2D> Left, List<Point2D> Right)> lastByCamera
            = new Dictionary<string, (DateTime, List<Point2D>, List<Point2D>)>();

        /// <param name="engine">Fuze — dotazuje se na pozu v case snimku (kompenzace pohybu).</param>
        /// <param name="config">
        /// Konfigurace. <b>Jmenuje se podle lokalizatoru, i kdyz vetsina jejich hodnot patri sem</b>
        /// (parovaci okno, kompenzace, tolerance mimo koridor) — rozdelit ji by znamenalo churn
        /// v konfiguraci, ktera je uz zaznamenana v dokumentaci a v prikazovych radkach mereni.
        /// </param>
        public CorridorSource(AsyncFusionEngine engine, CorridorLocalizerConfig config = null)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.config = config ?? new CorridorLocalizerConfig();
            finder = new CorridorFinder(this.config.Corridor);
        }

        /// <summary>Nastaveni, se kterym stupen pracuje.</summary>
        public CorridorLocalizerConfig Config => config;

        /// <summary>Kolik snimku vstoupilo.</summary>
        public long Frames { get; private set; }

        /// <summary>Vysledek jednoho snimku.</summary>
        public sealed class Result
        {
            /// <summary>Cas snimku, ze ktereho vysledek vznikl.</summary>
            public DateTime Time;

            /// <summary>
            /// Nalezeny koridor, nebo <c>null</c> (<see cref="CorridorFixReason.NoPair"/> /
            /// <see cref="CorridorFixReason.NoPose"/>). Pri <c>NoCorridor</c> a
            /// <c>OutsideCorridor</c> vyplneny je — duvod je pak v nem.
            /// </summary>
            public RoadCorridor Corridor;

            /// <summary>Poza v case snimku, nebo <c>null</c>, kdyz ji fuze nezna.</summary>
            public RobotState Pose;

            /// <summary>
            /// Proc koridor (ne)vznikl. <b>Jen mapove nezavisla podmnozina</b>:
            /// <c>Ok</c>, <c>NoPair</c>, <c>NoPose</c>, <c>NoCorridor</c>, <c>OutsideCorridor</c>.
            ///
            /// <para><b>Pozor na vyznam <c>Ok</c>:</b> tady znamena „koridor je pouzitelny", ne
            /// „merenie se poslalo do fuze" — to druhe rozhoduje az
            /// <see cref="CorridorLocalizer"/> po srovnani s mapou.</para>
            /// </summary>
            public CorridorFixReason Reason;

            /// <summary>Je koridor pouzitelny?</summary>
            public bool Ok => Corridor != null && Reason == CorridorFixReason.Ok;
        }

        /// <summary>
        /// Zpracuje snimek kamery a vrati koridor, kdyz se z nej (spolu s poslednim snimkem druhe
        /// kamery) dal postavit. Vraci <b>vzdy</b> vysledek — i zamitnuty nese pozu a duvod, protoze
        /// vrstva ve World pohledu potrebuje pozu i u cyklu, ktery neprosel.
        /// </summary>
        /// <returns><c>null</c> jen kdyz snimek vubec neni pouzitelny (bez <c>PathEdges</c>).</returns>
        public Result Process(CameraFrame frame)
        {
            if (frame?.PathEdges == null) return null;
            Frames++;

            var (left, right) = MetricPoints(frame.PathEdges);
            string cam = frame.Name ?? string.Empty;
            lastByCamera[cam] = (frame.TimeStamp, left, right);

            // Poza k casu snimku se vyzvedne HNED a jen jednou. Pouziva ji kompenzace pohybu,
            // mapova polovina i zprava. Driv se `GetStateAt` volalo dvakrat s tymz argumentem,
            // a u cyklu, ktere padly na NoPair, vubec - takze zprava nemela cim promitnout
            // usecky prolozeni do mapy. Viz CorridorFix.PoseX.
            var pose = engine.GetStateAt(frame.TimeStamp);

            // Druha kamera: nejblizsi cas z jineho jmena.
            if (!TryPair(cam, frame.TimeStamp, out var other))
                return new Result { Time = frame.TimeStamp, Pose = pose, Reason = CorridorFixReason.NoPair };

            // KOMPENZACE POHYBU mezi snimky. Body druhe kamery jsou v ramci robotu z JEJIHO casu;
            // mezitim robot popojel a pootocil se, takze slozit je s aktualnimi bez prepoctu
            // znamena vyrobit si nerovnobeznost z niceho. Pri 1,2 m/s a 150 ms je to 0,18 m posunu.
            //
            // Prevadi se jen RELATIVNI pohyb mezi dvema casy (odometrie na desetiny sekundy),
            // ne absolutni poza - merenie tedy zustava nezavisle na chybe lokalizace, coz je prave
            // to, co z nej dela poctivy vstup do fuze. Viz doc/map-correlation-localization.md.
            var otherLeft = other.Left;
            var otherRight = other.Right;
            double skewMs = Math.Abs((other.T - frame.TimeStamp).TotalMilliseconds);

            if (config.CompensateCameraSkew && skewMs > config.NoCompensationSkewMs)
            {
                var poseThen = engine.GetStateAt(other.T);
                if (pose == null || poseThen == null)
                {
                    // Bez pozy nelze prepocitat a bez prepoctu by to lhalo - radsi nic.
                    return new Result { Time = frame.TimeStamp, Pose = pose, Reason = CorridorFixReason.NoPose };
                }

                otherLeft = Reproject(otherLeft, poseThen, pose);
                otherRight = Reproject(otherRight, poseThen, pose);
            }

            // Leva hranice od te kamery, ktera ji vidi lip; totez pro pravou.
            var leftPts = left.Count >= otherLeft.Count ? left : otherLeft;
            var rightPts = right.Count >= otherRight.Count ? right : otherRight;

            var corridor = finder.Find(leftPts, rightPts);
            var result = new Result { Time = frame.TimeStamp, Pose = pose, Corridor = corridor };

            if (!corridor.Ok)
            {
                result.Reason = CorridorFixReason.NoCorridor;
                return result;
            }

            // Robot MUSI byt uvnitr koridoru (s malou rezervou). Bez teto kontroly hlasil stupen
            // platna merenia i pri pricne poloze 2,1 m od osy koridoru sirokeho 2 m - tedy metr
            // mimo cestu, coz s tvrzenim "jsem na teto ceste" nejde dohromady.
            if (Math.Abs(corridor.Lateral) > corridor.Width / 2 + config.MaxOutsideCorridorM)
            {
                result.Reason = CorridorFixReason.OutsideCorridor;
                return result;
            }

            result.Reason = CorridorFixReason.Ok;
            return result;
        }

        /// <summary>
        /// Prepocte body z ramce robotu v case <paramref name="then"/> do ramce robotu v case
        /// <paramref name="now"/>. Cistě rigidni transformace z ROZDILU obou poz - absolutni poloha
        /// se vykrati, takze chyba lokalizace do vysledku nevstupuje.
        /// </summary>
        public static List<Point2D> Reproject(List<Point2D> pts, RobotState then, RobotState now)
        {
            if (pts == null || pts.Count == 0) return pts;

            // p_svet = P_then + R(th_then) * p_then;  p_now = R(-th_now) * (p_svet - P_now)
            //       => p_now = d + R(th_then - th_now) * p_then
            double dth = then.Theta - now.Theta;
            double cd = Math.Cos(dth), sd = Math.Sin(dth);

            double ex = then.X - now.X, ey = then.Y - now.Y;
            double cn = Math.Cos(now.Theta), sn = Math.Sin(now.Theta);
            double dx = ex * cn + ey * sn;
            double dy = -ex * sn + ey * cn;

            var result = new List<Point2D>(pts.Count);
            foreach (var p in pts)
                result.Add(new Point2D(dx + p.X * cd - p.Y * sd,
                                       dy + p.X * sd + p.Y * cd));
            return result;
        }

        /// <summary>Metricke hranicni body snimku (uz je nese <see cref="PathEdge"/>).</summary>
        private static (List<Point2D> left, List<Point2D> right) MetricPoints(List<PathEdge> edges)
        {
            var left = new List<Point2D>();
            var right = new List<Point2D>();
            foreach (var e in edges)
            {
                if (e.LeftPoint.A != 0) left.Add(new Point2D(e.LeftPoint.X, e.LeftPoint.Y));
                if (e.RightPoint.A != 0) right.Add(new Point2D(e.RightPoint.X, e.RightPoint.Y));
            }
            return (left, right);
        }

        /// <summary>Najde snimek JINE kamery v casovem okne.</summary>
        private bool TryPair(string camera, DateTime t,
                             out (DateTime T, List<Point2D> Left, List<Point2D> Right) other)
        {
            other = default;
            double best = double.MaxValue;
            foreach (var kv in lastByCamera)
            {
                if (kv.Key == camera) continue;
                double dt = Math.Abs((kv.Value.T - t).TotalMilliseconds);
                if (dt > config.MaxCameraSkewMs || dt >= best) continue;
                best = dt;
                other = kv.Value;
            }
            return best < double.MaxValue;
        }
    }
}
