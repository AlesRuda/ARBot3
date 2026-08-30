using System;
using System.Collections.Generic;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Vychozi prevod senzor -&gt; <see cref="IMeasurement"/> pro fuzni jadro.
    /// <list type="bullet">
    /// <item><see cref="IMUState"/> -&gt; kurz (<see cref="HeadingMeasurement"/>) + uhlova rychlost.</item>
    /// <item><see cref="GPSState"/> -&gt; poloha (<see cref="PositionMeasurement"/>) v lokalni ENU rovine,
    ///       volitelne rychlost.</item>
    /// <item><see cref="IMotorState"/> (odometrie) -&gt; dopredna rychlost <c>v</c> a uhlova rychlost
    ///       <c>omega</c> z rychlosti kol.</item>
    /// </list>
    /// Kamera se doplni pozdeji. Viz doc/ekf-fusion.md a doc/global-navigation-runtime.md.
    /// </summary>
    public sealed class DefaultMeasurementMapper : IMeasurementMapper
    {
        private readonly FusionConfig cfg;

        /// <summary>
        /// Fuze, ktere se hlasi inicializace polohy z prvniho pouzitelneho fixu; <c>null</c> =
        /// prevod jen na merenia (poloha se pak inicializuje jinde, typicky misi).
        /// </summary>
        private readonly AsyncFusionEngine engine;

        /// <param name="config">Konfigurace fuze (sigma merenia, <see cref="FusionConfig.GeoReference"/>).</param>
        /// <param name="engine">
        /// Volitelne fuzni jadro. Je-li zadane, mapper zaridi <b>fallback inicializaci polohy</b>
        /// z prvniho pouzitelneho GPS fixu (<see cref="AsyncFusionEngine.InitializePosition"/>) -
        /// pro rezimy bez mise (rucni jizda, ladeni, cil klikem v mape). Je to zamerne HLOUPEJSI
        /// varianta nez misni: neprumeruje a nepamatuje si depo, jen aby robot vedel, kde je.
        /// Viz doc/global-navigation-runtime.md.
        /// </param>
        public DefaultMeasurementMapper(FusionConfig config = null, AsyncFusionEngine engine = null)
        {
            cfg = config ?? new FusionConfig();
            this.engine = engine;
        }

        /// <inheritdoc/>
        public IEnumerable<IMeasurement> ToMeasurements(Message msg)
        {
            switch (msg)
            {
                case IMUState imu:
                    // Kurz z absolutni atitude (ENU): yaw = matematicka orientace.
                    if (imu.Rotation.HasValue)
                    {
                        var ypr = imu.YPR();
                        if (ypr != null)
                        {
                            double std = imu.OrientationUncertainty?.X ?? cfg.CompassHeadingStd;
                            yield return new HeadingMeasurement(ypr.Yaw, std, imu.TimeStamp, "IMU/heading");
                        }
                    }
                    // Uhlova rychlost (yaw rate) z gyroskopu (slozka Z v BODY framu).
                    if (imu.AngularVelocity.HasValue)
                    {
                        yield return ScalarStateMeasurement.AngularRate(
                            imu.AngularVelocity.Value.Z, cfg.GyroRateStd, imu.TimeStamp, "IMU/gyro");
                    }
                    break;

                case GPSState gps:
                    foreach (var m in FromGps(gps))
                        yield return m;
                    break;

                // Odometrie. IMotorState NENI SensorStateBase (je to rozhrani), takze se matchuje
                // az za GPSState/IMUState - MotorStateBase prochazi timto pripadem.
                case IMotorState odo:
                    foreach (var m in FromOdometry(odo, msg))
                        yield return m;
                    break;
            }
        }

        /// <summary>
        /// GPS -&gt; poloha v lokalni ENU rovine (+ volitelne rychlost). Bez platneho fixu nebo bez
        /// <see cref="FusionConfig.GeoReference"/> nevznikne nic.
        /// </summary>
        private IEnumerable<IMeasurement> FromGps(GPSState gps)
        {
            if (!gps.IsFixed)
                yield break;

            // GPSState.Latitude/Longitude jsou RADIANY, tedy tatáž jednotka jako LLA (od 26. 8. 2026).
            // Do te doby to byly stupne a tohle misto na to muselo mit varovani - viz GPSState.Latitude.
            var lla = new LLA(gps.Latitude, gps.Longitude, gps.Altitude);

            // Bez referencniho bodu nelze LLA prevest na metry. Fallback: zaloz ho z tohoto fixu
            // (rezim bez mapy a bez mise - viz ctor).
            var geoRef = cfg.GeoReference;
            if (geoRef == null)
            {
                if (engine == null)
                    yield break;                       // pocatek zalozi nekdo jiny (mapa / mise)
                geoRef = new GeoReference(lla);
                cfg.GeoReference = geoRef;
            }

            var local = geoRef.ToLocal(lla);

            // Prvni pouzitelny fix polohu INICIALIZUJE (nastavi stav), dalsi ji uz jen koriguji.
            // Bez toho by gating prvni fix zahodil - viz AsyncFusionEngine.InitializePosition.
            if (engine != null && !engine.IsPositionInitialized)
            {
                engine.InitializePosition(local.X, local.Y, cfg.GpsPosStd, gps.TimeStamp);
                yield break;                           // stav uz polohu ma, korekce by byla nadbytecna
            }

            yield return new PositionMeasurement(local.X, local.Y,
                                                 cfg.GpsPosStd, cfg.GpsPosStd,
                                                 gps.TimeStamp, "GPS/position");

            // Rychlost z GPS ma smysl az nad prahem - pri stani je to sum.
            if (gps.Speed.HasValue && gps.Speed.Value >= cfg.GpsMinSpeed)
                yield return ScalarStateMeasurement.Velocity(gps.Speed.Value, cfg.GpsSpeedStd,
                                                             gps.TimeStamp, "GPS/speed");

            // KURZ Z GPS - DRUHA ABSOLUTNI REFERENCE (25. 8. 2026).
            //
            // Nacpak: do teto zmeny mela fuze JEDINOU absolutni referenci kurzu (IMU/heading
            // z magnetometru), takze bias kompasu nemela proti cemu zmerit. Namereno, ze pri
            // imubias=3 zustane chyba kurzu na 3,0 stupne a odhad sedi na IMU na 100 % - kompas
            // kurz DEFINUJE, ne vazi. GPS kurz pritom zna a je NEVYCHYLENY (+0,20 stupne proti
            // pravde pri sumu 5,02). Viz doc/ekf-fusion.md a doc/map-correlation-localization.md.
            foreach (var m in FromGpsHeading(gps)) yield return m;
        }

        /// <summary>
        /// Kurz z GPS jako merenie. Dva zdroje, ktere NEJSOU totez:
        ///
        /// <list type="bullet">
        ///   <item><b><see cref="GPSState.Orientation"/></b> = skutecny kurz VOZIDLA (dvouantennovy
        ///   prijimac, <c>uBlox HeadVeh</c>). Plati i pri stani a nezavisi na rychlosti, takze ma
        ///   prednost a sigma je konstantni.</item>
        ///   <item><b><see cref="GPSState.DynamicOrientation"/></b> = kurz NAD ZEMI (course over
        ///   ground) z vektoru rychlosti. Pouziva se az nad prahem rychlosti a sigma se pocita
        ///   z rychlosti.</item>
        /// </list>
        ///
        /// <para><b>Jizda vzad je vylouceny stav.</b> Kurz nad zemi je pri jizde vzad o 180 stupnu
        /// jinde nez kurz vozidla, a rychlost z NMEA je BEZ ZNAMENKA, takze to z fixu nejde poznat.
        /// Proto se pozaduje kladna rychlost nad prahem: radeji zadne merenie nez merenie 180 stupnu
        /// vedle. (Kdyby robot jezdil vzad delsi dobu, musel by znamenko dodat stav fuze.)</para>
        /// </summary>
        private IEnumerable<IMeasurement> FromGpsHeading(GPSState gps)
        {
            // Kurz VOZIDLA - prednost, plati i pri stani.
            if (gps.Orientation.HasValue)
            {
                yield return new HeadingMeasurement(gps.Orientation.Value, cfg.GpsHeadingStd,
                                                    gps.TimeStamp, "GPS/heading");
                yield break;
            }

            if (!gps.DynamicOrientation.HasValue) yield break;

            // Rychlost se bere z fixu; DynamicSpeed je dopoctena z poloh, takze je horsi, ale
            // lepsi nez nic. Bez rychlosti nejde sigma spocitat, takze se merenie vynecha.
            double? v = gps.Speed ?? gps.DynamicSpeed;
            if (!v.HasValue || v.Value < cfg.GpsMinSpeed) yield break;

            // sigma = atan2(pricny sum, rychlost), zdola omezena fyzickym stropem prijimace.
            double std = Math.Max(cfg.GpsHeadingStd, Math.Atan2(cfg.GpsCrossTrackStd, v.Value));
            yield return new HeadingMeasurement(gps.DynamicOrientation.Value, std,
                                                gps.TimeStamp, "GPS/heading");
        }

        /// <summary>
        /// Odometrie z rychlosti kol: <c>v = (vL + vR)/2</c>, <c>omega = (vR - vL)/rozchod</c>
        /// (matematicky smysl, +CCW - rychlejsi prave kolo znamena zatoceni vlevo).
        ///
        /// <para>Vzorec i znamenko jsou <b>shodne s predchozi generaci robotu</b>
        /// (<c>OdometryRotationSpeed = (RightWheelSpeed - LeftWheelSpeed) / rozchod</c>). Pojistka pro
        /// pripad jine polarity enkoderu je <see cref="FusionConfig.OdoOmegaSign"/>.</para>
        ///
        /// <para><b>Nouzove zastaveni se NEROZLISUJE</b> (od 27. 8. 2026). Do te doby se pod nim
        /// odometrie zahazovala s oduvodnenim „kola stoji, ale robot muze byt tlacen, a hlavne je to
        /// stav, kdy do nej clovek zasahuje". <b>Autor to vyvratil:</b> ridici jednotka ma pod stopem
        /// prikaz STAT a motory jsou rizene pozicne ve zpetne vazbe, takze kola nemohou vyrobit nic
        /// jineho nez nulu — stop odometrii nijak nezhorsuje. A ze robota muze clovek zvednout a
        /// prenest, plati stejne BEZ stisknuteho stopu, takze tim se ty dva stavy nerozlisi.</para>
        ///
        /// <para><b>Tlaceni robota to nemeni</b> (upresneni autora): pozicni smycka drzi polohu, takze
        /// se s tlakem <b>pere a dorovnava ji</b> — enkodery ukazou vychylku a navrat, ne cisty posun.
        /// Odometrie tedy pod stopem ani netvrdi „jedu", ani neprozradi, ze robot byl posunut; chova
        /// se stejne jako bez stopu. Neni to argument pro ani proti, jen dalsi dukaz, ze <b>stop
        /// v tomhle nic nemeni</b>.</para>
        ///
        /// <para>Cena toho zahazovani byla vysoka: pod drzenym stopem nemela fuze <b>zadnou vazbu na
        /// rychlost</b>, takze polohu tahal sum GPS a odhad se za desitky sekund rozesel o metry.
        /// Projevilo se to jako „robot na mape zbesile poskakuje" v misi Robotour — prvni veci, ktera
        /// stop drzi dlouho (servisni okno). Viz doc/ekf-fusion.md.</para>
        /// </summary>
        private IEnumerable<IMeasurement> FromOdometry(IMotorState odo, Message msg)
        {
            // Zastupny ramec po chybe driveru NENI merenie: SDC2160 pri nedostupnem portu nebo
            // neparsovatelne odpovedi vraci nuly a stop=true (fail-safe). Stop z nej plati, cisla
            // ne - a "v = 0" poslane fuzi prave v okamziku, kdy o robotu nevime nic, je horsi nez
            // zadne merenie: robot muze jet ze setrvacnosti. Rozliseni musi byt priznakem, ne
            // stopem - pod stopem je nula plnohodnotne merenie (viz vyse).
            if (!odo.HasMeasurement)
                yield break;

            // Cas porizeni: MotorStateBase je SensorStateBase, takze ma TimeStamp.
            var t = (msg as SensorStateBase)?.TimeStamp ?? default;
            if (t == default)
                yield break;

            double vL = odo.LeftWheelSpeed, vR = odo.RightWheelSpeed;
            double v = 0.5 * (vL + vR);
            double omega = cfg.OdoOmegaSign * (vR - vL) / cfg.WheelBase;

            yield return ScalarStateMeasurement.Velocity(v, cfg.OdoSpeedStd, t, "Odo/speed");
            yield return ScalarStateMeasurement.AngularRate(omega, cfg.OdoRateStd, t, "Odo/rate");
        }
    }
}
