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
