using System;
using System.Diagnostics;
using ARBot.Common.Communication;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Koreluje semanticky kanal occupancy gridu s vozovkou podle mapy a vysledek posila do fuze
    /// jako dve skalarni osova merenia plus kurz. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Vlakno:</b> je to <see cref="MessageProcessor"/> nad snapshotem gridu
    /// (<see cref="OccupancyGridMsg"/>), tedy VLASTNI vlakno. Nekrade cas planovaci - tik
    /// <c>LocalNavigator</c> smi trvat 15 ms a korelace by se do nej nevesla. Fronta je
    /// <see cref="OverflowPolicy.DropOldest"/>: kdyz korelace nestiha, je spravne zpracovat
    /// NEJNOVEJSI snapshot.</para>
    ///
    /// <para><b>Kapacita fronty musi pocitat s cizim provozem.</b> Vystup lokalni vrstvy nese vedle
    /// snapshotu gridu (500 ms) i <c>LocalPlanMsg</c> z kazdeho tiku (10-30 Hz), takze fronta je
    /// vetsinou plna zprav, ktere <see cref="Consume"/> jen zahodi. Pri male kapacite by
    /// <c>DropOldest</c> vytlacil ZARAZENY SNAPSHOT jeste driv, nez by na nej doslo - a to je jedina
    /// zprava, ktera korelatoru k necemu je. Kapacita se proto voli tak, aby se do ni vesel plan
    /// z celeho jednoho cyklu korelace (na ARM realne 100-200 ms proti periode planu 33 ms).</para>
    ///
    /// <para><b>Nezna trasu.</b> Mapovou pravdou je cela sit (<see cref="RoadScene"/>). Korelovat
    /// proti vybrane trase by byla potvrzovaci zaujatost - kdyby robot odbocil jinam, prilepilo by
    /// ho to k trase.</para>
    /// </summary>
    public sealed class MapCorrelator : MessageProcessor
    {
        private readonly AsyncFusionEngine engine;
        private readonly RoadScene scene;
        private readonly MapCorrelatorConfig config;
        private readonly Stopwatch sw = new Stopwatch();

        private DateTime lastProcessedAt = DateTime.MinValue;

        /// <summary>Konfigurace (po sestaveni se nemeni).</summary>
        public MapCorrelatorConfig Config => config;

        /// <summary>DIAGNOSTIKA: kolik cyklu se dopocitalo.</summary>
        public long ProcessedCycles { get; private set; }

        /// <summary>
        /// DIAGNOSTIKA: kolik snapshotu gridu z fronty VUBEC PRISLO (jeste pred rozhodnutim, co
        /// s nimi bude).
        ///
        /// <para><b>K cemu to je:</b> ztrata snapshotu ve fronte je jinak NEVIDITELNA - fronta je
        /// <c>DropOldest</c> a tece po ni i cizi provoz (<c>LocalPlanMsg</c>), takze vytlaceny
        /// snapshot nikde nezanecha stopu. Rozdil
        /// <c>ReceivedSnapshots - (ProcessedCycles + ThrottledCycles + DroppedNoPose)</c> je proti
        /// tomu prakticky nula: kazdy PRIJATY snapshot skonci presne v jednom z tech tri stavu. Kdyz
        /// to nula neni, snapshoty se ztraceji jeste PRED prijetim - tedy ve fronte, a je cas zvednout
        /// jeji kapacitu. (Pocitat naopak zahozene ne-gridove zpravy by nemelo cenu: to je bezny
        /// provoz, ktery tam tece porad.)</para>
        /// </summary>
        public long ReceivedSnapshots { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik snapshotu se zahodilo, protoze fuze neumela dat pozu.</summary>
        public long DroppedNoPose { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik snapshotu se preskocilo kvuli <see cref="MapCorrelatorConfig.MinPeriod"/>.</summary>
        public long ThrottledCycles { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik merenia se celkem poslalo do fuze.</summary>
        public long EmittedCorrections { get; private set; }

        /// <summary>Posledni vysledek (diagnostika pro UI).</summary>
        public MapCorrelationResult LastResult { get; private set; }

        /// <param name="engine">Fuze - dotazuje se na pozu v case snapshotu a posila do ni merenia.</param>
        /// <param name="scene">Vozovka podle mapy (cela sit, ne trasa).</param>
        /// <param name="config">Konfigurace; null = vychozi.</param>
        /// <param name="queueCapacity">Kapacita vstupni fronty (DropOldest); default 2.</param>
        public MapCorrelator(AsyncFusionEngine engine, RoadScene scene,
                             MapCorrelatorConfig config = null, int queueCapacity = 2)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.config = config ?? new MapCorrelatorConfig();
            this.config.Validate();
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Frontou tece i cizi provoz (LocalPlanMsg z kazdeho tiku planovace) - zajima nas
            // vyhradne snapshot gridu. Pocita se, kolik jich PRISLO, aby byla videt pripadna ztrata
            // ve fronte (viz ReceivedSnapshots).
            if (!(msg is OccupancyGridMsg grid)) return;
            ReceivedSnapshots++;

            try
            {
                var result = Process(grid);
                if (result != null) EmitDerived(result.ToLogMessage());
            }
            catch (Exception ex) { Debug.WriteLine($"MapCorrelator: {ex}"); }
        }

        /// <summary>
        /// Jeden cyklus korelace. Vraci <c>null</c>, kdyz se cyklus preskocil (nedostupna poza nebo
        /// <see cref="MapCorrelatorConfig.MinPeriod"/>).
        ///
        /// <para>Verejne schvalne: takhle se da cyklus spustit nad zaznamem i z testu BEZ vlakna,
        /// takze testy zustanou deterministicke.</para>
        /// </summary>
        public MapCorrelationResult Process(OccupancyGridMsg msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            // Cas skocil DOZADU (seek v prehravani zaznamu, novy zaznam). Odstup je pak zaporny,
            // tedy vzdy mensi nez MinPeriod - bez resetu by korelator omezoval frekvenci NAVZDY
            // a nad zaznamem uz nikdy nic nespocital. Ladeni ve fazi 4 se dela prave nad zaznamy,
            // takze by to zaruceni zaseklo.
            if (msg.TimeStamp < lastProcessedAt) lastProcessedAt = DateTime.MinValue;

            if (lastProcessedAt != DateTime.MinValue && msg.TimeStamp - lastProcessedAt < config.MinPeriod)
            {
                ThrottledCycles++;
                return null;
            }

            // (1) Poza v case snapshotu. null = mimo okno historie -> zahodit; korelovat proti
            //     spatne poze je horsi nez nekorelovat.
            var pose = engine.GetStateAt(msg.TimeStamp);
            if (pose == null)
            {
                DroppedNoPose++;
                return null;
            }

            sw.Restart();

            // (2) Mapa do rastru zarovnaneho s gridem (jednou za cyklus - dal se uz jen indexuje).
            //     Marze se bere jako VETSI z nastavene a te, kterou si vyzada skutecna geometrie
            //     gridu (rotace kandidata odnese rohovou bunku dal, nez staci posun sam) - jinak by
            //     dukazy padaly mimo rastr a preskocene nesouhlasne bunky by extremnim kandidatum
            //     skore ZVEDALY. Viz MapCorrelatorConfig.RequiredRasterMarginM.
            double margin = Math.Max(config.MapRasterMarginM,
                                     config.RequiredRasterMarginM(msg.Size, msg.Resolution));
            var raster = RoadRaster.Build(scene, msg.OriginX, msg.OriginY, msg.Size, msg.Resolution,
                                          margin);

            // (3) Dukazni bunky ze semantiky (kanal Occ se neucastni).
            var cloud = EvidenceCloud.FromGrid(msg, config.EvidenceThreshold);

            // (4) Hrube-jemne skenovani + (5) kovariance ze zakriveni skore.
            var scorer = new CorrelationScorer(cloud, raster, pose.X, pose.Y);
            var scan = scorer.Scan(config);
            var cov = cloud.Count >= config.MinEvidenceCells
                ? CorrelationCovariance.Estimate(scorer, scan, config)
                : CorrelationCovariance.NoPeak();

            // (6) Konkurencni maxima - test nejednoznacnosti. Bez maxima nema osa smysl, takze se
            //     konkurent nemeri a nejednoznacnost nikdy nezasahne.
            double rivalTight = cov.HasPeak
                ? scorer.BestRivalAlongAxis(scan, cov.TightAxisAngle, config)
                : double.NegativeInfinity;

            // Volna osa se hlida jen kdyz se ma opravdu poslat. Na prime ceste zarovnane s osami
            // gridu je SigmaLoose nekonecna, takze se tenhle vypocet vubec nespusti - dva dalsi
            // desitky vyhodnoceni skore se plati jen tam, kde ta osa neco ovlivni.
            double rivalLoose = cov.HasPeak && cov.SigmaLoose <= config.SigmaCeilingM
                ? scorer.BestRivalAlongAxis(scan, cov.TightAxisAngle + Math.PI / 2, config)
                : double.NegativeInfinity;

            var result = MapCorrelationResult.From(msg.TimeStamp, scan, cov, cloud.Count,
                                                   rivalTight, rivalLoose, config);

            // Poza, PROTI KTERE se korelovalo. Musi cestovat ve vysledku (a dal ve zprave): Dx/Dy je
            // posun proti NI, takze bez ni nejde poznat, jestli je nenulovy posun chybou korelatoru,
            // nebo chybou pozy, kterou korelator spravne nasel. Dohledavat ji pozdeji podle razitka
            // je past - viz MapCorrelationResult.PoseX.
            result.PoseX = pose.X;
            result.PoseY = pose.Y;
            result.PoseTheta = pose.Theta;
            result.HasPose = true;

            sw.Stop();
            result.ProcessingTime = sw.Elapsed;

            lastProcessedAt = msg.TimeStamp;
            ProcessedCycles++;
            LastResult = result;

            if (config.SendCorrections) SendMeasurements(result, pose);

            // Zpetna vazba z fuze: kolik NASICH korekci uz zahodila jako starsi nez okno historie.
            // Cte se AZ PO odeslani, aby zprava odpovidala stavu po tomto cyklu. Bez toho hlasi
            // telemetrie "Reason = Ok" i ve chvili, kdy do fuze nedojde nic (viz doc).
            engine.DroppedTooOldBySource().TryGetValue(config.MeasurementSource, out long dropped);
            result.DroppedByFusion = dropped;
            return result;
        }

        /// <summary>
        /// Posle korekce do fuze. Osy jsou VLASTNI osy translacni kovariance, takze R zustava
        /// diagonalni v tom spravnem ramci - viz doc/map-correlation-localization.md.
        /// </summary>
        private void SendMeasurements(MapCorrelationResult r, Fusion.RobotState pose)
        {
            double gate = Gating.ChiSquareThreshold(1);

            // Lepe urcena osa (na ceste typicky napric) a osa k ni kolma.
            double tx = Math.Cos(r.TightAxisAngle), ty = Math.Sin(r.TightAxisAngle);
            double lx = -ty, ly = tx;

            double trueX = pose.X + r.Dx;
            double trueY = pose.Y + r.Dy;

            var mode = config.GateMode;

            if (r.EmitTightAxis)
            {
                engine.Enqueue(new AxisOffsetMeasurement(tx, ty, tx * trueX + ty * trueY,
                                                         r.SigmaTight, r.TimeStamp, config.MeasurementSource)
                { GateThreshold = gate, GateMode = mode });
                EmittedCorrections++;
            }
            if (r.EmitLooseAxis)
            {
                engine.Enqueue(new AxisOffsetMeasurement(lx, ly, lx * trueX + ly * trueY,
                                                         r.SigmaLoose, r.TimeStamp, config.MeasurementSource)
                { GateThreshold = gate, GateMode = mode });
                EmittedCorrections++;
            }
            if (r.EmitHeading)
            {
                engine.Enqueue(new HeadingMeasurement(pose.Theta + r.Phi, r.SigmaPhi,
                                                      r.TimeStamp, config.MeasurementSource)
                { GateThreshold = gate, GateMode = mode });
                EmittedCorrections++;
            }
        }
    }
}
