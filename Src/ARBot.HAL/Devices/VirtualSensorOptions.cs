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

        /// <summary>Frekvence GPS [Hz]. Cte se jen pri zalozeni senzoru.</summary>
        public int GpsRateHz { get; set; } = 5;

        /// <summary>Pocet „viditelnych druzic" hlaseny ve fixu (jen kosmetika pro UI a logy).</summary>
        public int GpsSatellites { get; set; } = 12;

        /// <summary>HDOP hlaseny ve fixu (jen kosmetika pro UI a logy - simulace geometrii druzic
        /// nemodeluje). Nula by v telemetrii vypadala jako rozbity udaj, proto realna hodnota
        /// odpovidajici dobremu fixu pod otevrenym nebem.</summary>
        public double GpsHdop { get; set; } = 0.9;

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
