namespace ARBot.HAL.Devices
{
    /// <summary>
    /// Parametry simulovanych senzoru polohy a orientace (viz doc/virtual-hw.md).
    /// Kazda slozka sumu se vypina nulou - to je rezim pro deterministicke testy.
    /// <para>
    /// Volba „idealni motory" se tyka MODELU POHYBU, ne senzoru: odometrie hlasi presne
    /// skutecne rychlosti kol, ale GPS a IMU sum maji, jinak by fuze nemela co opravovat.
    /// </para>
    /// </summary>
    public sealed class VirtualSensorOptions
    {
        /// <summary>Smerodatna odchylka polohy GPS [m].</summary>
        public double GpsPositionNoiseM = 1.5;

        /// <summary>Smerodatna odchylka rychlosti z GPS [m/s].</summary>
        public double GpsSpeedNoiseMps = 0.1;

        /// <summary>Frekvence GPS [Hz].</summary>
        public int GpsRateHz = 5;

        /// <summary>Pocet „viditelnych druzic" hlaseny ve fixu (jen kosmetika pro UI a logy).</summary>
        public int GpsSatellites = 12;

        /// <summary>Smerodatna odchylka kurzu z IMU [rad] (~1 stupen).</summary>
        public double ImuHeadingNoiseRad = 0.017;

        /// <summary>Smerodatna odchylka uhlove rychlosti z gyra [rad/s] (~0,5 stupne/s).</summary>
        public double ImuGyroNoiseRad = 0.0087;

        /// <summary>Frekvence IMU [Hz].</summary>
        public int ImuRateHz = 100;

        /// <summary>Seed sumu - se stejnym seedem vyjde stejna posloupnost vzorku.</summary>
        public int Seed = 1;
    }
}
