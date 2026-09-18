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

        /// <summary>
        /// <b>Historicka hodnota - od 18. 9. 2026 se uz nevyrabi</b> (pricna brana
        /// <c>MaxLateralDisagreementM</c> zrusena, duvod je u samotne brany v <c>Update</c>).
        /// Polozka zustava kvuli <b>starsim zaznamum</b>: <c>RoadCorridorMsg.FixReason</c> je
        /// v <c>.rec</c> bajt, takze bez ni by se cykly zamitnute starou branou cetly jako
        /// neznamy duvod a report by o nich mlcel.
        /// </summary>
        LateralDisagreement = 6,

        /// <summary>Merena sirka se od mapove (nebo filtrovane) lisi vic, nez je strop.</summary>
        WidthDisagreement = 7,

        /// <summary>
        /// Robot je podle merení <b>mimo koridor</b> (dal od osy nez polosirka + rezerva). Bud se
        /// prolozila jina dvojice hranic, nebo robot z cesty sjel - v obou pripadech merenie
        /// nema co opravovat.
        /// </summary>
        OutsideCorridor = 8,

        /// <summary>
        /// <b>Odhad sirky teto hrany jeste nema kvalitu</b> — merení je zatim malo, nebo si
        /// navzajem nesednou (viz <see cref="RoadWidthEstimator"/>).
        ///
        /// <para>Merenie se proto <b>neposila</b>: bez duveryhodne sirky nechyti „prolozila se
        /// jina dvojice hranic" <b>nic</b>. Koridor se pritom pocita dal a do odhadu prispiva —
        /// je to rozjezd, ne porucha, a vyresi se sam za jednotky sekund.</para>
        /// </summary>
        WidthNotTrusted = 9,

        /// <summary>
        /// <b>Zadna hrana v okoli nesedla na to, co vidi kamera</b> — vsichni kandidati padli na
        /// veto azimutu nebo na strop chi-kvadratu (<see cref="EdgeAssociator"/>).
        ///
        /// <para>Liší se od <see cref="EdgeTooFar"/>: tam mapa cestu ma, ale je daleko; tady je
        /// blizko, jenze vede jinam, nez kudy vede videny koridor.</para>
        /// </summary>
        EdgeMismatch = 10,

        /// <summary>
        /// <b>Dve hrany vysly podobne</b> — nevime, po ktere ceste jedeme, takze se neposila nic.
        ///
        /// <para>Vybrat tu o chlup lepsi by znamenalo hadat. Podil tohohle duvodu v zaznamu je
        /// zaroven meridlo, jak casto je mapa v okoli nejednoznacna.</para>
        /// </summary>
        AmbiguousEdge = 11,
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
        private readonly RoadWidthEstimator widths;

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
            widths = new RoadWidthEstimator(this.config.WidthEstimator);
        }

        /// <summary>Nastaveni, se kterym stupen pracuje.</summary>
        public CorridorLocalizerConfig Config => config;

        /// <summary>Odhady sirky cest - vedlejsi produkt merení.</summary>
        public RoadWidthEstimator Widths => widths;

        /// <summary>Kolik snimku vstoupilo.</summary>
        public long Frames { get; private set; }

        /// <summary>Kolik merenii se poslalo do fuze.</summary>
        public long EmittedCorrections { get; private set; }

        /// <summary>
        /// DIAGNOSTIKA: kolik hotovych merenii se zahodilo kvuli
        /// <see cref="CorridorLocalizerConfig.MinSendPeriodSec"/>. Bez tohohle cisla by skrceni
        /// vypadalo jako porucha detektoru.
        /// </summary>
        public long ThrottledSends { get; private set; }

        /// <summary>Cas posledniho ODESLANI do fuze (skrceni kadence).</summary>
        private DateTime posledniOdeslani;

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

            // KTEROU CESTU vlastne jedeme. Nejblizsi hrana to nemusi byt: pri chybe polohy
            // nekolika metru vyhraje u krizovatky pricna ulice (zmereno 16. 9. 2026 - tyka se to
            // POLOVINY cyklu). Rozhoduje proto azimut, ktery na poloze nezavisi, skladany
            // s pricnou odchylkou pres chi-kvadrat. Viz EdgeAssociator.
            RoadAxisMatch axis;
            if (config.Association.Enabled)
            {
                var assoc = EdgeAssociator.Associate(network, origin, pose, corridor,
                                                     config.Association, config.MaxEdgeDistanceM);
                axis = assoc.Axis;
                fix.AssocChi2 = assoc.Chi2;
                fix.AssocChi2Second = assoc.Chi2Second;
                fix.AssocCandidates = assoc.Candidates;

                if (assoc.Result != EdgeAssocResult.Ok)
                {
                    fix.Axis = axis;
                    fix.Reason = assoc.Result switch
                    {
                        EdgeAssocResult.Ambiguous => CorridorFixReason.AmbiguousEdge,
                        EdgeAssocResult.NoCandidate => axis.Found && axis.DistanceM > config.MaxEdgeDistanceM
                                                       ? CorridorFixReason.EdgeTooFar
                                                       : CorridorFixReason.EdgeMismatch,
                        _ => CorridorFixReason.NoEdge,
                    };
                    LastFix = fix;
                    return null;
                }
            }
            else
            {
                axis = RoadAxis.Match(network, origin, pose.X, pose.Y, pose.Theta);
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
            }

            fix.Axis = axis;
            fix.LateralDisagreement = corridor.Lateral - axis.Lateral;

            // Rozdil smeru dvou PRIMEK, tedy slozeny na +-90 stupnu. Kamera nevidi, kterym smerem
            // po ceste jedeme (smer x a x+180 jsou totez), takze bez slozeni vyjde u cesty kolme
            // na kurz rozdil 178 stupnu tam, kde je nesouhlas 2. Driv se ukladal surovy.
            fix.HeadingDisagreementRad =
                Conversions.NormalizeHalfOrientation(corridor.DirectionRad - axis.HeadingRelRad);

            // ⚠️ PRICNA BRANA TU UZ NENI (zrusena 18. 9. 2026; drive MaxLateralDisagreementM
            // = 1,5 m, zamitnuti CorridorFixReason.LateralDisagreement).
            //
            // Testovala PRESNE TUTEZ velicinu jako EdgeAssociator o par radku vys
            // (dLat = corridor.Lateral - axis.Lateral), jen pevnym pravitkem v metrech misto
            // chi-kvadratu skalovaneho kovarianci pozy - a stala az ZA nim, takze poradi bylo
            // „porovnej poctive, pak zahod podle konstanty". Rozsah dLat je pritom omezeny uz
            // konstrukci (|corridor.Lateral| <= Width/2 + MaxOutsideCorridorM, |axis.Lateral|
            // <= MaxEdgeDistanceM), takze pro ni neexistuje ani hodnota, ktera by byla jen
            // pojistkou: bud rezala do ziveho, nebo byla mrtvy kod.
            //
            // Nad 20260917-160558.rec zahazovala 81,8 % cyklu, ktere dostaly hranu, pri sigma
            // polohy z fuze 3,73 m - tedy brana na 0,4 sigma vlastni nejistoty: zamitala podle
            // veliciny, kterou filtr prave nezna. Je to tataz vada, jaka se u MapCorrelatoru
            // zmerila 25. 8. 2026 (tvrdy GateMode.Reject delal vysledek HORSI nez nekorigovat
            // vubec): tvrdy strop na innovaci zahazuje prave ty velke korekce, ktere jsou
            // potreba, takze chyba pozy zustane nad stropem navzdy a hrana uz nikdy nepromluvi.
            //
            // Velikost odchylky posuzuji dve mista, ktera na to maji meritko: EdgeAssociator
            // (vyber hrany - chi2 proti kovarianci pozy s podlahou) a GateMode.Soft ve fuzi
            // (NIS proti sigma_pozy + sigma_merenia; misto zahozeni nafoukne R, takze se poza
            // muze po cyklech dotahnout). Podminkou zruseni bylo, aby o hrane rozhodoval
            // AZIMUT, ktery na poloze nezavisi - to plati od 16. 9. 2026.
            //
            // Na poze NEZAVISLE pojistky zustavaji beze zmeny: MaxOutsideCorridorM
            // (CorridorSource), sirkove brany nize, veto azimutu a odstup druheho kandidata
            // v prirazeni. Viz doc/map-correlation-localization.md.

            // Odhad sirky bezi BEZ sirkove brany - jinak by se nemel z ceho naucit (viz nize).
            //
            // ⚠️ Zamerne to NENI podmineno WidthUpdateMaxDisagreementM: sirka je rozdil offsetu
            // dvou primek v RAMCI ROBOTU (CorridorFinder: Width = cL − cR), takze na poze nezavisi
            // a chyba pozy se do ni dostat nemuze. Podminovat ji shodou s pozou na 0,3 m by
            // vyrobilo TYZ zamek, ktery se tu prave odstranuje: pri chybe pozy 0,6 m by se odhad
            // nezalozil nikdy. Viz RoadWidthEstimator a doc/map-correlation-localization.md.
            widths.Add(axis.WayId, corridor.Width);

            // Sirkova brana plati AZ od chvile, kdy ma odhad kvalitu.
            //
            // ⚠️ Do 15. 9. 2026 se brana ptala na MAPOVOU sirku uz v prvnim cyklu, tedy DRIV, nez
            // se filtr mel z ceho naucit - na ceste sirsi nez roadwidth ± MaxWidthDisagreementM
            // se proto prvni merenie neprijalo NIKDY, filtr se nezalozil a hrana zustala nema
            // navzdy. Mapova sirka uz proto referenci brany neni; dokud odhad nema kvalitu,
            // brana NEPLATI (neni s cim nesouhlasit) a merenie se neposila.
            bool trusted = widths.TryGetWidth(axis.WayId, out double estimate);
            fix.MapWidthM = trusted ? estimate : axis.WidthM;
            fix.FilteredWidthM = widths.RawEstimate(axis.WayId, axis.WidthM);
            fix.WidthDisagreement = corridor.Width - fix.MapWidthM;

            if (!trusted)
            {
                fix.Reason = CorridorFixReason.WidthNotTrusted;
                LastFix = fix;
                return null;
            }
            if (Math.Abs(fix.WidthDisagreement) > config.MaxWidthDisagreementM)
            {
                fix.Reason = CorridorFixReason.WidthDisagreement;
                LastFix = fix;
                return null;
            }

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
            if (!VydatMerenie(fix.Time)) { ThrottledSends++; return; }

            double gate = Gating.ChiSquareThreshold(1);
            var a = fix.Axis;
            var c = fix.Corridor;

            double value = a.NormalX * a.AxisX + a.NormalY * a.AxisY + c.Lateral;
            engine.Enqueue(new AxisOffsetMeasurement(a.NormalX, a.NormalY, value,
                                                     Nafoukni(c.SigmaLateral, config.SigmaLateralExtraM),
                                                     fix.Time, config.MeasurementSource)
            { GateThreshold = gate, GateMode = config.GateMode });
            EmittedCorrections++;
            fix.EmittedLateral = true;

            if (config.SendHeading)
            {
                // θ_true = kurz robotu + (sklon hrany − smer koridoru).
                //
                // ⚠️ Ten rozdil je rozdil dvou PRIMEK: kamera vidi cestu, ale ne kterym smerem po
                // ni jedeme, takze DirectionRad je slozeny na ±90° a smysl nenese. Slozit se proto
                // musi i rozdil — bez toho vyjde u cesty zhruba kolme na kurz jedno cislo u +89°
                // a druhe u −89° a do fuze jde kurz otoceny az o 180°. Naměřeno nad
                // 20260916-164926.rec: tykalo by se to 40 ze 424 prijatych cyklu (9,4 %), a
                // GateMode.Soft takove merenie NEZAHODI, jen odtlumi.
                //
                // Kterym smerem cesta vede, rozhodne KURZ ROBOTU - jina reference na to neni.
                // ⚠️ Cena: koridor tim uz nikdy nerekne „jsi otoceny o 180°" (potvrdil by i
                // obraceny kurz). Na prevraceni musi hlidat kurz z GPS, ktery je skutecny smer,
                // ne primka. Viz doc/map-correlation-localization.md.
                double d = Conversions.NormalizeHalfOrientation(a.HeadingRelRad - c.DirectionRad);
                double heading = Conversions.NormalizePrimaryOrientation(fix.PoseTheta,
                                                                         fix.PoseTheta + d);
                engine.Enqueue(new HeadingMeasurement(heading,
                                                      Nafoukni(c.SigmaDirectionRad, config.SigmaHeadingExtraRad),
                                                      fix.Time, config.MeasurementSource)
                { GateThreshold = gate, GateMode = config.GateMode });
                EmittedCorrections++;
                fix.EmittedHeading = true;
            }
        }

        /// <summary>
        /// Sigma do fuze: co rika prolozeni, <b>slozene kvadraticky</b> s prirazkem z konfigurace.
        /// Prirazek 0 vraci presne starou hodnotu (A/B).
        ///
        /// <para>⚠️ <b>Ve zprave zustava sigma z PROLOZENI</b>, nafouknuta jde jen do fuze.
        /// <c>RoadCorridorMsg</c> je meritko estimatoru; kdyby v ni byla nafouknuta hodnota,
        /// <c>ARBot.Analyze corridor</c> by prestal merit estimator a zacal merit konfiguraci.
        /// Odeslana sigma je ze zaznamu dopocitatelna - ucinna konfigurace je v nem od 5. 9. 2026.</para>
        /// </summary>
        private static double Nafoukni(double sigma, double prirazek)
            => prirazek > 0 ? Math.Sqrt(sigma * sigma + prirazek * prirazek) : sigma;

        /// <summary>
        /// <b>Ma se z tohohle cyklu vydat merenie?</b> Skrceni na
        /// <see cref="CorridorLocalizerConfig.MinSendPeriodSec"/>; 0 = kazdy cyklus.
        ///
        /// <para><b>Skok casu vzad</b> (seek pri prehravani, novy zaznam) skrceni RESETUJE - jinak
        /// by se po skoku dozadu neposlalo uz nic. Tataz past je okomentovana
        /// u <c>MapCorrelator.Process</c>.</para>
        /// </summary>
        private bool VydatMerenie(DateTime t)
        {
            double perioda = config.MinSendPeriodSec;
            if (!(perioda > 0)) return true;
            if (posledniOdeslani == default || t < posledniOdeslani)
            {
                posledniOdeslani = t;
                return true;
            }
            if ((t - posledniOdeslani).TotalSeconds + 1e-9 < perioda) return false;
            posledniOdeslani = t;
            return true;
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
