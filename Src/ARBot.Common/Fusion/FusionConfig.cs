using System;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Konfigurace fuzniho filtru - parametry modelu, procesniho sumu, vychozich
    /// kovarianci merenia, detekce smyku a okna historie.
    /// </summary>
    public class FusionConfig
    {
        /// <summary>Rozchod kol [m] - pro prepocet odometrie na uhlovou rychlost.</summary>
        public double WheelBase = 0.5;

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
        public double GpsHeadingStd = 0.1;     // [rad]
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
        public TimeSpan HistoryWindow = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Referencni bod lokalni ENU roviny - misto, kde plati [X, Y] = [0, 0].
        /// Pokud je null, GPS adapter ji zalozi z prvniho platneho fixu.
        /// </summary>
        public GeoReference GeoReference;
    }
}
