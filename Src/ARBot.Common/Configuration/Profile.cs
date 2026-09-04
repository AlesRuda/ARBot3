using ARBot.Common;
using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Configuration
{
    public class Profile
    {
        /// <summary>
        /// Perioda vzorkovani v ms
        /// </summary>
        public static int Ts = 100;

        /// <summary>
        /// Timeout zastaralosti dráhy pro nižší řídicí smyčku [ms]. Když path controller nedostane
        /// novou dráhu déle než tento čas, smyčka nouzově dobrzdí po poslední trase. Viz doc/path-following.md.
        /// </summary>
        public static int PathControlTimeOut = 500;
        /// <summary>
        /// Čas dohledu τ_look [s] pro cílový (lookahead) bod sledování dráhy (<c>L_d = τ_look·v</c>).
        /// </summary>
        public static double LookaheadTime = 0.3;
        /// <summary>
        /// Minimální vzdálenost cílového bodu [m] (floor při nízké rychlosti).
        /// </summary>
        public static double LookaheadMin = 0.15;
        /// <summary>
        /// Bezpečnostní rezerva odečtená od tolerance ε při plánování rohů [m].
        /// </summary>
        public static double PathEpsilonMargin = 0.01;

        /// <summary>
        /// Rozchod kol robotu
        /// </summary>
        public static double Rozchod = 0.41;
        /// <summary>
        /// Uhlova rychlost samovolneho zataceni v radianech na ujety meter.
        /// </summary>
        public static double wErr = 0.0;
        /// <summary>
        /// Polomer kola v metrech.
        /// 0.94 - je konstanta urcena merenim, reprezentuje zmacknuti pneumatiky vahou robotu
        /// </summary>
        public static double WheelRadius = 0.085944*0.94;
#if true   //rychly motor
        /// <summary>
        /// Prevodovy pomer prevodovky
        /// </summary>
        public static double MotorGearBoxReduction = 27;
        /// <summary>
        /// Maximalni otacky nezatizeneho motoru za sekundu
        /// </summary>
        public static double MotorMaxRPS = 260.0 * MotorGearBoxReduction / 60.0;
        /// <summary>
        /// Pocet pulzu encoderu na jednu otacku kola
        /// </summary>
        public static double EncoderCounts = 16*4;
        /// <summary>
        /// Maximalni dovolena rychlost v m/s
        /// </summary>
//        public static double MaxAllowedSpeed = WheelPerimeter * MotorMaxRPS / MotorGearBoxReduction;
        public static double MaxAllowedSpeed = 1.2;
#else
        /// <summary>
        /// Maximalni otacky nezatizeneho motoru za sekundu
        /// </summary>
        public static double MotorMaxRPS = 6400.0 / 60.0;
        /// <summary>
        /// Prevodovy pomer prevodovky
        /// </summary>
        public static double MotorGearBoxReduction = 50.9;
        /// <summary>
        /// Pocet pulzu encoderu na jednu otacku kola
        /// </summary>
        public static double EncoderCounts = 12;
        /// <summary>
        /// Maximalni dovolena rychlost v m/s
        /// </summary>
//        public static double MaxAllowedSpeed = WheelPerimeter * MotorMaxRPS / MotorGearBoxReduction;
        public static double MaxAllowedSpeed = 0.8;
#endif

        /// <summary>
        /// Obvod kola
        /// </summary>
        public static double WheelPerimeter = WheelRadius * 2 * Math.PI;
        /// <summary>
        /// Maximalni dovolena rychlost otaceni v rad/s
        /// </summary>
        public static double MaxAllowedRotationSpeed = Math.PI/6;
        /// <summary>
        /// Maximalni technicky dosazitelna rychlost v m/s 
        /// </summary>
        public static double MaxTheoreticalSpeed = WheelPerimeter*MotorMaxRPS / MotorGearBoxReduction;
        /// <summary>
        /// Maximalni zrychleni v m/(s^2)
        /// </summary>
        /// <remarks>
        /// Magicky koeficient 0.45 je odhad z odezvy na jednotkovy skok, aby fungovat Regulator a nedochazelo k preregulovani
        /// </remarks>
        public static double MaxAcceleration = 0.50;// * WheelPerimeter * MotorMaxRPS / MotorGearBoxReduction ;
        public static double MaxDecceleration = 0.50;// odhad na zaklade mereni pro Akceleraci 1 m/s^2;

        /// <summary>
        /// Robot se bude snazit zastavit dle udaju z lidaru LidarSafetyZone mru pred prekazkou.
        /// </summary>
        public static double LidarSafetyZone = 0.6;

        /// <summary>
        /// Bezpecna vzdalenost prekazek od robota, aby projel.
        /// TVRDY minimalni odstup - planovac ho nikdy neporusi (blize je neprujezdno).
        /// </summary>
        public static double SafeDist = 0.4;

        /// <summary>
        /// Odstup od prekazek, od ktereho uz se rychlost neomezuje - dal je bezpecne volno pro
        /// prujezd i otoceni. Mezi <see cref="SafeDist"/> a timto odstupem se rychlost linearne
        /// snizuje (u BOCNIHO odstupu nejde o brzdnou drahu - ta je zvlast v brzdne obalce).
        /// Viz doc/occupancy-and-local-planning.md.
        /// </summary>
        public static double PrefDist = 0.8;

        /// <summary>
        /// Seriove porty UART senzoru podle platformy (default parametru UartAHRS= / UartMotor= /
        /// UartGPS=; prazdny = senzor se nezaklada). Do 4. 9. 2026 bydlely v ARBotHW.Init a registr
        /// je vedl jako "default z kodu podle detekce" - zadna detekce ale neni, jsou to konstanty
        /// podle platformy, tedy vlastnost zeleza jako ostatni pole Profile.
        ///
        /// <para>OrangePI/Armbian: zmereno na robotu 31. 8. 2026 (OrangePi5Ultra/find-serial-ports.sh) -
        /// vsechny tri periferie visi na USB, zadny onboard UART se nepouziva: VN100 IMU pres prevodnik
        /// CP2102 (/dev/ttyUSB0), SDC2160Ex ma vlastni USB CDC-ACM (/dev/ttyACM0), u-blox GPS take
        /// (/dev/ttyACM1). Zapsana jsou jmena z /dev/serial/by-id, ne ttyUSB0/ttyACM0: cisla uzlu se
        /// prideluji podle poradi enumerace USB, takze prohozeni GPS a motoru po restartu nebo po
        /// prepojeni kabelu je realne - a bylo by TICHE (oba jsou ttyACM*, oba se otevrou). Jmeno
        /// v by-id plyne z USB deskriptoru, takze drzi. Predchozi "/dev/ttyS0" byl jen odhad a byl
        /// spatne: na RK3588 zadny /dev/ttyS0 neexistuje, jediny zivy onboard UART je ttyS7 a drzi
        /// si ho bluetooth.</para>
        /// </summary>
#if IsX64
        public static string PortAHRS = "COM5";
        public static string PortMotor = "COM9";
        public static string PortGPS = "COM8";
#elif IsARM64
        public static string PortAHRS = "/dev/serial/by-id/usb-Silicon_Labs_CP2102_USB_to_UART_Bridge_Controller_0001-if00-port0";
        public static string PortMotor = "/dev/serial/by-id/usb-Roboteq_Motor_Controller_SDC2XXX-if00";
        public static string PortGPS = "/dev/serial/by-id/usb-u-blox_AG_-_www.u-blox.com_u-blox_GNSS_receiver-if00";
#else
        public static string PortAHRS = null;
        public static string PortMotor = null;
        public static string PortGPS = null;
#endif

        /// <summary>
        /// Posunuti lidaru vuci referencnimu bodu robotu (prusecik osy rotace a zeme).
        /// </summary>
        public static Vector3 LidarOffset = new Vector3(0, 0.08f, 0);
        /// <summary>
        /// Posunuti lidaru vuci referencnimu bodu robotu (prusecik osy rotace a zeme).
        /// </summary>
        public static Point2D LidarOffset2D = new Point2D(0.08, 0);

        /// <summary>
        /// Posunuti leve kamery vuci referencnimu bodu robotu (prusecik osy rotace a zeme).
        /// </summary>
        public static Vector3 LeftCameraOff = new Vector3(0.0155f, 0.1f, 0.522f);
        /// <summary>
        /// Posunuti prave kamery vuci referencnimu bodu robotu (prusecik osy rotace a zeme).
        /// </summary>
        public static Vector3 RightCameraOff = new Vector3(-0.0155f, -0.1f, 0.525f);
        /// <summary>
        /// Pootoceni stereoskopicke kamery oproti primemu smeru v radianech
        /// </summary>
        public static double CameraYaw = Conversions.Deg2Rad(29.0);
        /// <summary>
        /// Skloneni stereoskopicke kamery oproti horizontu v radianech, - je smerem dolu
        /// </summary>
//        public static double CameraPitch = Conversions.Deg2Rad(-23.5);



        /// <summary>
        /// Transformace leve kamery
        /// </summary>
        public static Matrix4x4 LeftCameraTransform = Conversions.CameraToWorldTransform(CameraYaw, Conversions.Deg2Rad(-20.2), Conversions.Deg2Rad(0.9), LeftCameraOff);
        /// <summary>
        /// Transformace prave kamery
        /// </summary>
        public static Matrix4x4 RightCameraTransform = Conversions.CameraToWorldTransform(-CameraYaw, Conversions.Deg2Rad(-18.6), Conversions.Deg2Rad(-1.1), RightCameraOff);
        /// <summary>
        /// Rotace leve kamery
        /// </summary>
        public static Matrix4x4 LeftCameraRotation = Conversions.CameraToWorldTransform(CameraYaw, Conversions.Deg2Rad(-20.2), Conversions.Deg2Rad(0.9), new Vector3(0, 0, 0));
        /// <summary>
        /// Rotace prave kamery
        /// </summary>
        public static Matrix4x4 RightCameraRotation = Conversions.CameraToWorldTransform(-CameraYaw, Conversions.Deg2Rad(-18.6), Conversions.Deg2Rad(-1.1), new Vector3(0, 0, 0));


        /// <summary>
        /// Magneticka deklinace - odklon magnetickeho severu od geometrickeho ve stupnich. Kladny na vychod.
        /// Pouzivat jen pokud neni reseno kompasem
        /// </summary>
        public static double DeklinaceDeg = 0;

        /// <summary>
        /// Magneticka deklinace - odklon magnetickeho severu od geometrickeho v darianech. Kladny na vychod.
        /// </summary>
        public static double DeklinaceRad { get { return Conversions.Deg2Rad(DeklinaceDeg); } }
    }
}
