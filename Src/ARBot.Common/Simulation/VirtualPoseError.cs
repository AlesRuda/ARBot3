using System;
using System.Globalization;
using ARBot.Common.Fusion;

namespace ARBot.Common.Simulation
{
    /// <summary>
    /// Umela chyba pozy vnucena do RENDEROVACI cesty virtualni kamery (viz doc/virtual-hw.md).
    ///
    /// <para><b>K cemu to je.</b> Ve virtualnim HW renderuje kamera z <c>engine.GetStateAt(t)</c>
    /// a occupancy grid se ukotvuje touz pozou, takze obsah gridu s mapou souhlasi vzdycky a
    /// korelator hlasi <c>Dx = Dy = 0</c> <b>strukturalne</b> - i kdyby byl rozbity. Kdyz se ale
    /// renderuje z pozy posunute o <c>e</c>, obsah gridu se proti mape posune o <c>-e</c>, coz je
    /// presne totez, jako kdyby robot ve skutecnosti stal na <c>odhad + e</c>. Korelator hlasi
    /// "skutecna poloha = odhad + D", takze <b>musi vyjit D = e</b> - a to uz je overitelna
    /// predpoved se znamou odpovedi. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para><b>Ramec.</b> Chyba se zadava v ramci ROBOTU (FLU: X vpred, Y vlevo), protoze otevrena
    /// vada "falesna podelna jistota" je prave o rozdilu podel vs. napric cesty. Do svetovych
    /// slozek, ktere hlasi zprava, ji prevede <see cref="ExpectedWorldOffset"/>.</para>
    ///
    /// <para><b>Sdileni.</b> Jedna instance patri obema virtualnim kameram - kdyby mela leva jinou
    /// chybu nez prava, fuzovany grid by nedaval smysl. Drzi ji <c>ARBotHW</c>.</para>
    ///
    /// <para><b>Meni se za behu</b> z UI (nastroj nad virtualni kamerou), cte se z renderovaciho
    /// vlakna kamery. Slozky jsou <c>volatile</c>-like semantiky diky tomu, ze jde o
    /// <c>double</c> zapisovane atomicky na 64bit platformach; presnou synchronizaci to
    /// nepotrebuje - je to ladici pomucka, ne ridici cesta.</para>
    /// </summary>
    public sealed class VirtualPoseError
    {
        /// <summary>Posun VPRED v ramci robotu [m] (FLU +X).</summary>
        public double ForwardM { get; set; }

        /// <summary>Posun VLEVO v ramci robotu [m] (FLU +Y).</summary>
        public double LeftM { get; set; }

        /// <summary>Chyba kurzu [rad], matematicky (+CCW).</summary>
        public double HeadingRad { get; set; }

        /// <summary>Je nastavena nejaka nenulova slozka?</summary>
        public bool IsActive => ForwardM != 0.0 || LeftM != 0.0 || HeadingRad != 0.0;

        /// <summary>
        /// Rozebere parametr prikazove radky <c>poseerror=vpred,vlevo[,stupne]</c> (metry, stupne).
        /// Kurz je nepovinny. Vzdy <b>invariantni</b> kultura, aby tentyz prikazovy radek delal
        /// totez na ceskem i anglickem stroji.
        /// <para>Nesmysl vraci false a nic nenastavi - vadny parametr nesmi shodit start aplikace
        /// (stejna zasada jako u <c>map=</c> a <c>start=</c>).</para>
        /// </summary>
        public static bool TryParse(string text, out VirtualPoseError error)
        {
            error = new VirtualPoseError();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(',');
            if (parts.Length < 2) return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double fwd)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double left))
                return false;

            error.ForwardM = fwd;
            error.LeftM = left;

            if (parts.Length >= 3
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double deg))
                error.HeadingRad = deg * Math.PI / 180.0;

            return true;
        }

        /// <summary>Prevezme slozky z jine instance (sdilena instance na ARBotHW se nenahrazuje).</summary>
        public void CopyFrom(VirtualPoseError other)
        {
            if (other == null) return;
            ForwardM = other.ForwardM;
            LeftM = other.LeftM;
            HeadingRad = other.HeadingRad;
        }

        /// <summary>Vynuluje vsechny slozky.</summary>
        public void Reset()
        {
            ForwardM = 0.0;
            LeftM = 0.0;
            HeadingRad = 0.0;
        }

        /// <summary>
        /// Svetovy posun, ktery ma korelator ohlasit jako <c>Dx</c>/<c>Dy</c>, pro dany kurz robotu.
        /// Prevod FLU -&gt; ENU: vpred jde po smeru kurzu, vlevo o 90 deg proti smeru hod. rucicek.
        /// </summary>
        public (double Dx, double Dy) ExpectedWorldOffset(double thetaRad)
        {
            double c = Math.Cos(thetaRad), s = Math.Sin(thetaRad);
            return (ForwardM * c - LeftM * s, ForwardM * s + LeftM * c);
        }

        /// <summary>
        /// Vrati KOPII pozy posunutou o vnucenou chybu. Vstupni stav se <b>nemutuje</b> - prichazi
        /// z fuze a mutace by injektaz protlacila zpatky do filtru.
        /// <para>Pri nulove chybe vraci tentyz objekt (bez alokace) - to je bezny provozni stav.</para>
        /// </summary>
        public RobotState Apply(RobotState pose)
        {
            if (pose == null || !IsActive) return pose;

            var (dx, dy) = ExpectedWorldOffset(pose.Theta);

            return new RobotState
            {
                X = pose.X + dx,
                Y = pose.Y + dy,
                Theta = pose.Theta + HeadingRad,
                V = pose.V,
                Omega = pose.Omega,
                TimeStamp = pose.TimeStamp,
                Covariance = pose.Covariance,
                Roll = pose.Roll,
                Pitch = pose.Pitch,
            };
        }
    }
}
