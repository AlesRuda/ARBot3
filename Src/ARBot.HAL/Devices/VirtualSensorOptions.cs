namespace ARBot.HAL.Devices
{
    /// <summary>
    /// Parametry simulovanych senzoru polohy a orientace (viz doc/virtual-hw.md).
    /// Kazda slozka sumu se vypina nulou - to je rezim pro deterministicke testy.
    /// <para>
    /// Volba „idealni motory" se tyka MODELU POHYBU, ne senzoru: odometrie hlasi presne
    /// skutecne rychlosti kol, ale GPS a IMU sum maji, jinak by fuze nemela co opravovat.
    /// </para>
    ///
    /// <para><b>Sum vs. bias vs. prokluz.</b> Sum je nahodny s nulovou stredni hodnotou - fuze si
    /// ho vyprumeruje a chyba odhadu zustane ohranicena. <b>Bias</b> (IMU) a <b>prokluz kol</b>
    /// (odometrie) jsou systematicke: neprumeruji se pryc a chyba polohy i kurzu <b>roste s casem</b>.
    /// Bez nich v simulaci nevznikne pripad, ktery ma hranova lokalizace lecit, a chybu je nutne
    /// vnucovat rucne (<c>poseerror=</c>). Vychozi hodnoty jsou nulove - realny drift se zapina
    /// vedome (<c>imubias=</c>, <c>wheelslip=</c> nebo nastrojem „Virtuální senzory").</para>
    ///
    /// <para><b>Co jde menit za behu.</b> Sum, biasy a prokluz cte simulace pri kazdem vzorku,
    /// takze se projevi hned. <b>Frekvence</b> (<see cref="GpsRateHz"/>, <see cref="ImuRateHz"/>)
    /// se ctou jen pri zalozeni senzoru - jejich zmena plati az po novem zapnuti virtualniho HW.</para>
    ///
    /// <para><b>Sdileni a vlakna.</b> Jedna instance patri celemu virtualnimu HW
    /// (<c>ARBotHW.VirtualSensors</c>); zapisuje do ni UI vlakno, ctou ji vlakna jednotlivych
    /// senzoru. Presnou synchronizaci to nepotrebuje - je to ladici pomucka, ne ridici cesta
    /// (stejna uvaha jako u <c>VirtualPoseError</c>).</para>
    /// </summary>
    public sealed class VirtualSensorOptions
    {
        /// <summary>Smerodatna odchylka polohy GPS [m].</summary>
        public double GpsPositionNoiseM { get; set; } = 1.5;

        /// <summary>Smerodatna odchylka rychlosti z GPS [m/s].</summary>
        public double GpsSpeedNoiseMps { get; set; } = 0.1;

        /// <summary>
        /// Smerodatna odchylka PRICNE slozky rychlosti z GPS [m/s] — z ni vychazi sum KURZU
        /// (course over ground).
        ///
        /// <para><b>Proc takhle a ne „sum kurzu ve stupnich".</b> Kurz z GPS neni merena velicina,
        /// je to <c>atan2</c> z vektoru rychlosti (tak ho pocita i <c>uBloxGps</c>; NMEA ho dostane
        /// z VTG). Jeho nejistota proto <b>zavisi na rychlosti</b>: <c>sigma_kurz ≈ sigma_v / v</c>.
        /// Pri stani je nekonecna, pri 1 m/s a sumu 0,1 m/s je to ~5,7 stupne, pri 3 m/s ~1,9.
        /// Zadat konstantni sum ve stupnich by tuhle zavislost zahodilo — a prave ona rozhoduje,
        /// jestli je kurz z GPS pouzitelny jako DRUHA absolutni reference (a tedy jestli je bias
        /// kompasu observabilni bez mapy).</para>
        ///
        /// <para>Vychozi hodnota je stejna jako <see cref="GpsSpeedNoiseMps"/>: u prijimace, ktery
        /// resi rychlost z Dopplera, neni duvod cekat, ze pricna slozka je jinak presna nez podelna.</para>
        /// </summary>
        public double GpsCrossTrackNoiseMps { get; set; } = 0.1;

        /// <summary>
        /// Pod touto rychlosti se kurz z GPS <b>vubec nehlasi</b> [m/s].
        ///
        /// <para>Neni to volba komfortu: <c>atan2</c> ze sumu je pri stani rovnomerne rozdeleny uhel,
        /// tedy cista dezinformace. Skutecny prijimac se chova stejne (NMEA VTG kurz pri stani
        /// „poskakuje"). Tataz uvaha, jakou uz ma <c>FusionConfig.GpsMinSpeed</c> u rychlosti.</para>
        /// </summary>
        public double GpsCourseMinSpeedMps { get; set; } = 0.3;

        /// <summary>Frekvence GPS [Hz]. Cte se jen pri zalozeni senzoru.</summary>
        public int GpsRateHz { get; set; } = 5;

        /// <summary>Pocet „viditelnych druzic" hlaseny ve fixu (jen kosmetika pro UI a logy).</summary>
        public int GpsSatellites { get; set; } = 12;

        /// <summary>HDOP hlaseny ve fixu (jen kosmetika pro UI a logy - simulace geometrii druzic
        /// nemodeluje). Nula by v telemetrii vypadala jako rozbity udaj, proto realna hodnota
        /// odpovidajici dobremu fixu pod otevrenym nebem.</summary>
        public double GpsHdop { get; set; } = 0.9;

        /// <summary>
        /// <b>Nouzove zastaveni</b> hlasene virtualnimi motory (jako by obsluha drzela tlacitko).
        /// Meni se za behu z panelu <i>Tools → Virtualni senzory</i>.
        ///
        /// <para><b>Nacpak to je:</b> cely handshake mise Robotour stoji na tom, ze obsluha stop
        /// <b>zmackne</b> a pak <b>uvolni</b> (servisni okno, cteni QR, potvrzeni cile). Bez tohohle
        /// prepinace se v simulaci servisni okno <b>neda projit vubec</b> — virtualni motory hlasily
        /// nouzove zastaveni natvrdo jako <c>false</c>. Viz doc/robotour-mission.md.</para>
        ///
        /// <para>Kola to nezastavuje samo: o zastaveni se stara <c>ControlLoop</c>, ktery pod stopem
        /// posila <c>Drive(0, …)</c> — simulovany robot tedy dobrzdi svou rampou, presne jako na
        /// zeleze. Priznak je jen <i>vstup</i>, ne zkratka.</para>
        /// </summary>
        public bool EmergencyStop { get; set; }

        /// <summary>Smerodatna odchylka kurzu z IMU [rad] (~1 stupen).</summary>
        public double ImuHeadingNoiseRad { get; set; } = 0.017;

        /// <summary>Smerodatna odchylka uhlove rychlosti z gyra [rad/s] (~0,5 stupne/s).</summary>
        public double ImuGyroNoiseRad { get; set; } = 0.0087;

        /// <summary>Frekvence IMU [Hz]. Cte se jen pri zalozeni senzoru.</summary>
        public int ImuRateHz { get; set; } = 100;

        /// <summary>
        /// SYSTEMATICKA chyba kurzu z IMU [rad] - konstantni posun, jako spatne zkalibrovany
        /// magnetometr. Nula = vychozi. Na rozdil od sumu se neprumeruje pryc.
        /// </summary>
        public double ImuHeadingBiasRad { get; set; }

        /// <summary>
        /// SYSTEMATICKA chyba gyra [rad/s] - konstantni offset uhlove rychlosti. Fuze ho
        /// integruje, takze vyrobi <b>rostouci</b> chybu kurzu. Nula = vychozi.
        /// </summary>
        public double ImuGyroBiasRadPerSec { get; set; }

        /// <summary>
        /// Prokluz LEVEHO kola [-]: nasobek mezi tim, co kolo namerí (enkoder), a tim, oc se robot
        /// skutecne posune. 1 = ideal (vychozi). Rozdil vlevo/vpravo dela drift kurzu, stejna
        /// hodnota na obou chybu merítka drahy. Prenasi se do
        /// <c>ARBot.Common.Simulation.SimulatedRobot.LeftWheelSlip</c>.
        /// </summary>
        public double LeftWheelSlip { get; set; } = 1.0;

        /// <summary>Prokluz PRAVEHO kola [-] - viz <see cref="LeftWheelSlip"/>.</summary>
        public double RightWheelSlip { get; set; } = 1.0;

        /// <summary>Seed sumu - se stejnym seedem vyjde stejna posloupnost vzorku.</summary>
        public int Seed { get; set; } = 1;

        /// <summary>Je nastavena nejaka SYSTEMATICKA chyba? (Zvyrazneni v UI - snadno se zapomene vypnout.)</summary>
        public bool HasSystematicError
            => ImuHeadingBiasRad != 0.0 || ImuGyroBiasRadPerSec != 0.0
               || LeftWheelSlip != 1.0 || RightWheelSlip != 1.0;

        /// <summary>Vynuluje systematicke chyby (sum a frekvence zustavaji).</summary>
        public void ResetSystematicError()
        {
            ImuHeadingBiasRad = 0.0;
            ImuGyroBiasRadPerSec = 0.0;
            LeftWheelSlip = 1.0;
            RightWheelSlip = 1.0;
        }
    }
}
