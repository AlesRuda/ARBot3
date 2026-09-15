using System;
using System.Collections.Generic;
using System.Diagnostics;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Robot
{
    /// <summary>Nastaveni <see cref="RoadWidthMapUpdater"/>.</summary>
    public sealed class RoadWidthMapUpdaterConfig
    {
        /// <summary>
        /// O kolik se musi duveryhodna sirka lisit od te, se kterou se stavelo naposled, aby se
        /// prestavovalo [m]. Proti kolisani odhadu v radu centimetru.
        /// <para>⚠️ <b>ODHAD, ne mereni</b> — nastavit z prvniho zaznamu. Cely mechanismus je
        /// ciste funkce posloupnosti <c>Width</c> z <c>RoadCorridorMsg</c>, takze pocet prestaveb
        /// za jizdu jde spocitat OFFLINE.</para>
        /// </summary>
        public double RebuildThresholdM = 0.25;

        /// <summary>
        /// Nejmensi odstup mezi prestavbami [s] — proti rade cest, ktere se usadi tesne po sobe.
        /// <para>⚠️ Taky odhad. Prestavba neni zadarmo: <see cref="RoadScene"/> se stavi cela
        /// vcetne prostoroveho indexu a <c>MapMsg</c> ma na realne mape stovky uzlu, takze casta
        /// republikace by nafoukla zaznam (dnes se posila JEDNOU za beh).</para>
        /// </summary>
        public double MinRebuildPeriodSec = 10.0;
    }

    /// <summary>
    /// Prenasi <b>naucenou sirku cesty</b> z koridoru do <see cref="RoadScene"/> korelatoru
    /// a do <see cref="MapMsg"/> (World pohled, webovy pudorys).
    ///
    /// <para><b>Tik a data jsou schvalne dve ruzne veci:</b> tikem je <see cref="RoadCorridorMsg"/>
    /// ze streamu — chodi prave tehdy, kdy se odhad mohl zmenit (emituje se i u zamitnutych cyklu),
    /// takze updater nepotrebuje vlastni casovac. Daty je <see cref="RoadWidthEstimator"/> primo,
    /// protoze zprava nese jednu NAMERENOU sirku, ne verdikt kvality per hrana.</para>
    ///
    /// <para>⚠️ <b>Scenu VIRTUALNI KAMERY to nedostane.</b> Ta se stavi zvlast v <c>ARBotHW</c>
    /// a nikdo ji prekryv nepreda. Kdyby ho dostala, simulace by renderovala cestu podle odhadu
    /// a koridor by meril <b>SAM SEBE</b> — tataz past jako <c>camerapose=fusion</c>, kterou
    /// projekt uz jednou nasel (22. 8. 2026). Hlida to
    /// <c>RoadWidthVirtualCameraIsolationTests</c>.</para>
    ///
    /// <para>⚠️ <b>Stupen je STAVOVY a zavisly na poradi</b> — zaruka record/replay plati pro
    /// CERSTVOU instanci, ne pro sdilenou (tataz poznamka jako u <c>DefaultMeasurementMapper</c>
    /// po skrceni kompasu). Pri <c>Start</c> se zaklada novy graf, takze to sedi.</para>
    ///
    /// <para>Viz doc/plan-naucena-sirka-do-mapy.md.</para>
    /// </summary>
    public sealed class RoadWidthMapUpdater : MessageProcessor
    {
        private readonly RoadNetwork network;
        private readonly GeoReference origin;
        private readonly RoadWidthEstimator odhady;
        private readonly Action<RoadScene> naScenu;
        private readonly Action<MapMsg> naMapu;
        private readonly RoadWidthMapUpdaterConfig config;
        private readonly string mapName;

        /// <summary>Sirky per cesta, se kterymi se stavelo naposled.</summary>
        private readonly Dictionary<long, double> zapecene = new Dictionary<long, double>();
        private DateTime posledniPrestavba = DateTime.MinValue;

        /// <param name="network">Sit; <b>nemeni se</b> (prekryv se predava konzumentum).</param>
        /// <param name="origin">Pocatek lokalni ENU roviny - tyz, jaky ma korelator.</param>
        /// <param name="odhady">Odhady sirky z koridoru (<c>CorridorLocalizer.Widths</c>).</param>
        /// <param name="naScenu">Kam predat novou scenu (typicky <c>MapCorrelator.Scene</c>).</param>
        /// <param name="naMapu">Kam predat novy <see cref="MapMsg"/> (stream a <c>MapMessage</c>).</param>
        /// <param name="config">Nastaveni; null = vychozi.</param>
        /// <param name="mapName">Jmeno mapy do zpravy.</param>
        /// <param name="queueCapacity">Vstupni fronta (DropOldest) - zmeskany tik nevadi,
        /// dalsi <c>RoadCorridorMsg</c> prijde za desetiny sekundy.</param>
        public RoadWidthMapUpdater(RoadNetwork network, GeoReference origin,
                                   RoadWidthEstimator odhady,
                                   Action<RoadScene> naScenu, Action<MapMsg> naMapu,
                                   RoadWidthMapUpdaterConfig config = null,
                                   string mapName = null, int queueCapacity = 4)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.odhady = odhady ?? throw new ArgumentNullException(nameof(odhady));
            this.naScenu = naScenu ?? throw new ArgumentNullException(nameof(naScenu));
            this.naMapu = naMapu ?? throw new ArgumentNullException(nameof(naMapu));
            this.config = config ?? new RoadWidthMapUpdaterConfig();
            this.mapName = mapName ?? string.Empty;
        }

        /// <summary>DIAGNOSTIKA: kolikrat se mapa prestavela.</summary>
        public long Rebuilds { get; private set; }

        /// <summary>
        /// Prestav, kdyz je to potreba. Vraci <c>true</c>, kdyz k prestavbe doslo.
        ///
        /// <para>Verejne schvalne: takhle jde updater prohnat testem i zaznamem <b>bez vlakna</b>
        /// (tyz duvod jako u <c>MapCorrelator.Process</c> a <c>CorridorLocalizer.Process</c>).</para>
        /// </summary>
        public bool Zkus(DateTime t)
        {
            if (!JeCoPrestavet()) return false;

            // Skok casu VZAD (seek v zaznamu, novy beh) odstup RESETUJE - bez toho by byl rozdil
            // zaporny, tedy vzdy mensi nez perioda, a uz by se neprestavelo nikdy. Tataz past je
            // okomentovana u MapCorrelator.Process.
            if (posledniPrestavba != DateTime.MinValue && t >= posledniPrestavba
                && (t - posledniPrestavba).TotalSeconds < config.MinRebuildPeriodSec)
                return false;

            var prekryv = RoadWidthOverrides.Build(network, NaucenaSirka);
            naScenu(new RoadScene(network, origin, prekryv));
            naMapu(network.ToLogMessage(mapName, prekryv));

            ZapecUzite();
            posledniPrestavba = t;
            Rebuilds++;
            Trace.WriteLine($"Naucena sirka cesty: mapa prestavena ({prekryv.Count} uzlu, "
                            + $"prestaveb celkem {Rebuilds}).");
            return true;
        }

        /// <summary>Duveryhodna sirka cesty, nebo <c>null</c>.</summary>
        private double? NaucenaSirka(long wayId)
            => odhady.TryGetWidth(wayId, out double w) ? w : (double?)null;

        /// <summary>Lisi se nektera duveryhodna sirka od te, se kterou se stavelo naposled?</summary>
        private bool JeCoPrestavet()
        {
            foreach (var e in network.Edges)
            {
                if (!odhady.TryGetWidth(e.WayId, out double w)) continue;
                if (!zapecene.TryGetValue(e.WayId, out double stara)
                    || Math.Abs(w - stara) > config.RebuildThresholdM)
                    return true;
            }
            return false;
        }

        private void ZapecUzite()
        {
            foreach (var e in network.Edges)
                if (odhady.TryGetWidth(e.WayId, out double w)) zapecene[e.WayId] = w;
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Frontou tece i cizi provoz - tikem je vyhradne RoadCorridorMsg.
            if (!(msg is RoadCorridorMsg m)) return;

            try { Zkus(m.TimeStamp); }
            catch (Exception ex) { Trace.WriteLine($"RoadWidthMapUpdater: {ex}"); }
        }
    }
}
