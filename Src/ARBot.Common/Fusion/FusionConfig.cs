using System;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Konfigurace fuzniho filtru - parametry modelu, procesniho sumu, vychozich
    /// kovarianci merenia, detekce smyku a okna historie.
    /// </summary>
    public class FusionConfig
    {
        /// <summary>
        /// Rozchod kol [m] - pro prepocet odometrie na uhlovou rychlost
        /// (<c>omega = (vR - vL) / WheelBase</c>).
        /// <para>Bere se z <see cref="Profile.Rozchod"/>, aby byl jeden zdroj pravdy: konfigurace
        /// se v provozu nikde neprepisuje, takze nesouhlas s profilem by znamenal trvalou
        /// systematickou chybu uhlove rychlosti (drive tu bylo natvrdo 0,5 proti profilovym
        /// 0,41 = -18 %).</para>
        /// </summary>
        public double WheelBase = Profile.Rozchod;

        // --- procesni sum (near-constant-velocity) ---
        /// <summary>Smerodatna odchylka linearniho zrychleni [m/s^2].</summary>
        public double SigmaAccel = 1.0;
        /// <summary>Smerodatna odchylka uhloveho zrychleni [rad/s^2].</summary>
        public double SigmaAngAccel = 2.0;
        /// <summary>Maly izotropni sum polohy [m^2/s] (numericky prah / boCni skluz).</summary>
        public double PositionNoiseFloor = 1e-4;

        // --- vychozi smerodatne odchylky merenia ---
        public double OdoSpeedStd = 0.05;      // [m/s]
        public double OdoRateStd = 0.10;       // [rad/s]
        public double GyroRateStd = 0.02;      // [rad/s]
        public double CompassHeadingStd = 0.05; // [rad]

        /// <summary>
        /// <b>Podlaha sigmy kurzu z kompasu [rad]</b> — sklada se KVADRATICKY s tim, co hlasi sam
        /// senzor (<c>YprU</c>). Nula = vypnuto (stare chovani).
        ///
        /// <para><b>Nacpak.</b> VN100 hlasi <c>YprU</c> (yaw 1 sigma) p50 <b>0,057-0,061 stupne</b>
        /// a <see cref="Runtime.DefaultMeasurementMapper"/> si to bere primo jako sigmu mereni
        /// <c>IMU/heading</c>. Jenze zmerena chyba proti GPS kurzu je <b>-4,90 / -2,79 stupne</b>
        /// (dva zaznamy 12. 9. 2026), tedy senzor je proti sve skutecne chybe <b>~60-90x
        /// presvedcenejsi</b>. Fuze proto kurz z kompasu <b>nevaži, prebira</b>
        /// (<c>odhad - IMU yaw</c> = 0,00 +- 0,06 stupne) a chyba jde 1:1 do mapy i do mrkve.</para>
        ///
        /// <para><b>Proc to YprU nemuze vedet.</b> Popisuje <b>kratkodoby sum</b> atitudoveho
        /// reseni, ne jeho <b>bias vuci severu</b>. Ten bias je v <b>telesovem ramci</b> (pootoceni
        /// senzoru proti podvozku, zbytek magneticke kalibrace, sikme jeti robotu) a ani otacenim,
        /// ani casem nezmizi — zmereno, ze pulrozdil mezi dvema opacnymi smery jizdy je jen
        /// +-1 stupen, takže to neni tvrde zelezo. Viz doc/imu-and-frames.md.</para>
        ///
        /// <para><b>Odkud vychozich 0,087 rad (5 stupnu):</b> RMS namereneho biasu ze dvou behu je
        /// 4,0 stupne, mezi behy kolisa o ~2 stupne a mistni porucha pole pridava jednotky stupnu
        /// (eta^2 0,17-0,19 rozptylu vysvetli MISTO). Zaokrouhleno nahoru na 5.</para>
        ///
        /// <para><b>Proc kvadraticky a ne maximem:</b> kdyz <c>YprU</c> vyskoci (magneticka
        /// porucha, rozjete VPE), sigma ma rust dal — podlaha ma tu informaci doplnit, ne prebit.</para>
        ///
        /// <para>⚠️ <b>Poctivy filtr z toho nebude, jen min nepoctivy.</b> Chyba kompasu je casove
        /// KORELOVANA (je to bias), a filtr ji bere jako bily sum, takže si ji ze 100 vzorku za
        /// sekundu "vyprumeruje" a hlasena nejistota kurzu spadne rad pod skutecnou chybu. Je to
        /// <b>tataz past jako u <see cref="GpsPosStd"/></b> a jedina skutecna lecba je <b>bias
        /// kompasu jako stav EKF</b> (otevreny ukol, viz doc/ekf-fusion.md). Tohle je mezikrok,
        /// ktery srovnava VAHY proti ostatnim referencim.</para>
        /// </summary>
        public double CompassHeadingStdFloor = Common.Conversions.Deg2Rad(CompassHeadingStdFloorDeg);

        /// <summary>
        /// Vychozi podlaha sigmy kurzu ve <b>stupnich</b> — kanonicka hodnota, ze ktere se odvozuje
        /// jak <see cref="CompassHeadingStdFloor"/> (v radianech), tak default parametru
        /// <c>imuheadingstd=</c>. Psat ji dvakrat by znamenalo, ze se ty dve hodnoty jednou
        /// rozejdou a nikdo si toho nevsimne.
        /// </summary>
        public const double CompassHeadingStdFloorDeg = 5.0;

        /// <summary>
        /// <b>Nejmensi odstup mezi merenimi <c>IMU/heading</c> [s]</b>; 0 = neomezeno (kazdy vzorek).
        ///
        /// <para><b>Nacpak.</b> Podlaha <see cref="CompassHeadingStdFloor"/> srovnava, jak moc se
        /// veri JEDNOMU vzorku. Neresi ale to druhe: filtr bere vzorky jako <b>nezavisle</b>, takze
        /// ze 100 odectu za sekundu si informaci nascita stokrat — jenze chyba kompasu je
        /// <b>bias</b>, tedy pres cely beh temer konstantni, a sto odectu teze konstanty nese
        /// informaci <b>jednoho</b>.</para>
        ///
        /// <para><b>Zmereno 12. 9. 2026:</b> mistne zavisla cast chyby kompasu se pri 0,7 m/s obmeni
        /// za ~7 s (sd mezi bunkami 5 x 5 m byla 2,9-5,0 stupne), montazni a kalibracni cast se
        /// nedekoreluje nikdy. Vychozi <b>1 s</b> je proto konzervativni zacatek: pomer informace
        /// kompas : GPS kurz spadne z ~220 : 1 na ~2,2 : 1, ale kompas zustava kotvou. Data
        /// argumentuji spis pro 0,1 Hz; to je ale vetsi zmena chovani, takze az po zmereni.</para>
        ///
        /// <para>⚠️ <b>Skrti se JEN absolutni kurz, ne gyro.</b> <c>IMU/gyro</c> (uhlova rychlost)
        /// jde dal v plne kadenci a mezi odecty kompasu nese kurz prave ono — jeho chyba je
        /// prevazne BILA (angular random walk), takze u nej je predpoklad nezavislosti zhruba
        /// poctivy. Namereny klidovy bias gyra 0,1-62,6 stupne/h dela za sekundu nanejvys
        /// 0,017 stupne, tedy proti 5 stupnum biasu kompasu nic.</para>
        ///
        /// <para>Tataz lecba a tyz duvod jako <c>MapCorrelatorConfig.MinPeriod</c> (3 s) u korelace
        /// s mapou. Viz doc/ekf-fusion.md.</para>
        /// </summary>
        public double CompassHeadingMinPeriodSec = 1.0;
        public double GpsPosStd = 1.5;         // [m]
        public double GpsSpeedStd = 0.3;       // [m/s]

        // --- relativni yaw (T265 / VIO, 6. 9. 2026) ---
        //
        // T265 nema magnetometr: jeji yaw je o NEZNAMOU KONSTANTU vedle absolutniho kurzu, takze
        // jako kurz se poslat NESMI. Co pouzitelne je, je jeho ZMENA - tim se ta konstanta odecte.
        // Fuze proto z relativniho zdroje bere uhlovou rychlost spocitanou z rozdilu yaw.

        /// <summary>
        /// Delka okna, ze ktereho se pocita uhlova rychlost z rozdilu relativniho yaw [s].
        ///
        /// <para><b>Proc okno, a ne kazdy vzorek:</b> T265 dava pozu 200 Hz, takze derivovat vzorek
        /// po vzorku znamena delit sum yaw casem 5 ms — tedy ho zesilit dvestekrat. Na okne 0,5 s
        /// je z tehoz sumu <c>√2·sigma/0,5</c>, tedy o dva rady mensi cislo.</para>
        ///
        /// <para><b>Okna se NEPREKRYVAJI</b> (kotva se po kazdem mereni posune na soucasny vzorek):
        /// prekryvajici se okna by dala korelovana merenia a filtr by si nadsadil informaci. Tatáž
        /// past a tatáž lecba jako u korelace s mapou (<c>MinPeriod</c>), viz
        /// doc/map-correlation-localization.md.</para>
        /// </summary>
        public double RelYawWindowSec = 0.5;

        /// <summary>
        /// Sigma relativniho yaw z VIO [rad] — z ni vychazi sigma odvozene uhlove rychlosti jako
        /// <c>√2·RelYawStd / okno</c> (dva nezavisle odecty yaw na koncich okna).
        ///
        /// <para>Vychozich 0,002 rad (~0,1 stupne) je <b>odhad</b>, ne mereni: T265 hlasi kvalitu
        /// sledovani, ne sigmu yaw. Naostro se to musi nastavit z dat ze zarizeni — proto je to
        /// parametr a proto se do zaznamu ukladaji cela IMUState z T265.</para>
        /// </summary>
        public double RelYawStd = 0.002;

        // --- kvalita GPS fixu (6. 9. 2026) ---
        //
        // NACPAK: do teto zmeny brala fuze KAZDY fix, u ktereho GPSState.IsFixed rekl "ano",
        // a dala mu VZDY tutez sigmu GpsPosStd. Pocet druzic a DOP pritom GPSState nese - jen se
        // na ne nikdo nedival. Namereno na robotu 6. 9. 2026: odhad polohy ujel ~570 m jednim
        // smerem (~0,7 m/s), zatimco robot STAL a rychlost ve stavu byla nula - tedy polohu
        // netahla predikce, ale prave ta bezvyhradne prijimana mereni.
        //
        // Mise Robotour uz kriteria kvality ma (RobotourConfig.MinSatellites/MaxHdop pri armovani
        // depa), takze fuze byla jedine misto, kde se fix bral bez otazek.

        /// <summary>
        /// Nejmensi pocet druzic, pri kterem se poloha z GPS jeste pouzije; <c>0</c> = nekontrolovat.
        ///
        /// <para>Vychozi <b>4</b> je fyzikalni minimum pro 3D reseni, ne kriterium kvality — to dela
        /// <see cref="GpsMaxDop"/> a hlavne skalovani sigmy. Brana ma odstranit NESMYSL, ne vybirat
        /// dobre fixy: zahodit GPS uplne je horsi nez ji dat malou vahu. Prijimac, ktery pocet
        /// druzic nehlasi (0), branou projde — neznama hodnota neni spatna hodnota.</para>
        /// </summary>
        public int GpsMinSatellites = 4;

        /// <summary>
        /// Nejvyssi pripustny DOP; <c>0</c> = nekontrolovat. Nad touhle hodnotou se poloha zahodi.
        ///
        /// <para>Vychozich <b>10</b> je bezna hranice mezi „slabym" a „spatnym" resenim. Pozor, co
        /// v tom cisle je: NMEA plni HDOP (vodorovny), u-blox PDOP (prostorovy, vzdy >= HDOP),
        /// takze prah je proti u-bloxu prisnejsi. Hodnota 0 znamena „prijimac DOP nehlasi" a branou
        /// projde.</para>
        /// </summary>
        public double GpsMaxDop = 10.0;

        /// <summary>
        /// Skalovat sigma polohy z GPS podle DOP (<c>sigma = GpsPosStd * max(1, DOP)</c>)?
        ///
        /// <para>Tohle je ta <b>podstatna</b> cast: kvalita fixu je spojita velicina a DOP je presne
        /// ten nasobek, o ktery geometrie druzic zhorsuje presnost — takze slaby fix dostane malou
        /// vahu sam od sebe, misto aby se o nem rozhodovalo prahem ano/ne. <c>max(1, …)</c> proto,
        /// ze DOP pod 1 by sigmu zmensoval pod deklarovanou presnost prijimace.</para>
        /// </summary>
        public bool GpsScaleStdByDop = true;
        /// <summary>
        /// <b>PODLAHA</b> sigma kurzu z GPS [rad]. Skutecna sigma se pocita z rychlosti (viz
        /// <see cref="GpsCrossTrackStd"/>) a tohle je jeji fyzicky strop presnosti — pri vysoke
        /// rychlosti by jinak vysla libovolne mala, coz zadny prijimac neumi (multipath, antena,
        /// bocni skluz vozidla).
        /// </summary>
        public double GpsHeadingStd = 0.1;     // [rad]

        /// <summary>
        /// Smerodatna odchylka <b>pricne</b> slozky rychlosti z GPS [m/s] — z ni vychazi sigma
        /// kurzu jako <c>atan2(GpsCrossTrackStd, v)</c>.
        ///
        /// <para><b>Proc se sigma kurzu pocita, a ne zadava.</b> Kurz nad zemi neni merena velicina,
        /// je to <c>atan2</c> z vektoru rychlosti (tak ho pocita i <c>uBloxGps</c>; NMEA ho dostane
        /// z VTG). Jeho nejistota tedy <b>zavisi na rychlosti</b> a konstantni cislo by tu zavislost
        /// zahodilo: pri 0,5 m/s je to 31 stupnu, pri 3 m/s 5,7. Filtr by pri pomale jizde veril
        /// necemu skoro nahodnemu. Namereno 25. 8. 2026 nad simulaci: 12,2 stupne pri 0,5 m/s
        /// a 3,7 pri 3,0 — presne ta zavislost.</para>
        ///
        /// <para>Vychozi hodnota je stejna jako <see cref="GpsSpeedStd"/>: u prijimace, ktery resi
        /// rychlost z Dopplera, neni duvod cekat, ze pricna slozka je jinak presna nez podelna.</para>
        /// </summary>
        public double GpsCrossTrackStd = 0.3;  // [m/s]
        public double CameraPosStd = 0.1;      // [m]
        public double CameraHeadingStd = 0.03; // [rad]
        public double CameraSpeedStd = 0.1;    // [m/s]

        // --- detekce smyku ---
        /// <summary>Max fyzikalni zrychleni kola [m/s^2]; nad nim se predpoklada smyk/hrabani.</summary>
        public double MaxWheelAccel = 5.0;
        /// <summary>Nasobek R odometrie pri detekovanem smyku.</summary>
        public double SlipRScale = 100.0;

        /// <summary>Rychlost, pod kterou nedavame smysl kurzu/rychlosti z GPS [m/s].</summary>
        public double GpsMinSpeed = 0.3;

        /// <summary>
        /// Znamenko odometricke uhlove rychlosti: <c>omega = OdoOmegaSign * (vR - vL) / WheelBase</c>.
        /// Default <b>+1</b> je fyzikalne spravny (rychlejsi prave kolo = zatoceni vlevo = +CCW) a
        /// <b>shoduje se s predchozi generaci robotu</b>, ktera pocitala
        /// <c>OdometryRotationSpeed = (RightWheelSpeed - LeftWheelSpeed) / rozchod</c> - tedy tentyz
        /// vzorec vcetne znamenka. Prepinac tu zustava jen jako pojistka pro pripad zmeny polarity
        /// enkoderu driveru: kdyby odometricke omega slo proti gyroskopu, filtr by je proti sobe vazil
        /// a kurz by se rozjel. Overeni na zarizeni: otocit robotem na miste vlevo a porovnat znamenko
        /// s <c>IMUState.AngularVelocity.Z</c>.
        /// </summary>
        public double OdoOmegaSign = +1.0;

        /// <summary>Okno historie = max kompenzovatelna latence.</summary>
        public TimeSpan HistoryWindow = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Referencni bod lokalni ENU roviny - misto, kde plati [X, Y] = [0, 0].
        /// Pokud je null, GPS adapter ji zalozi z prvniho platneho fixu.
        /// </summary>
        public GeoReference GeoReference;
    }
}
