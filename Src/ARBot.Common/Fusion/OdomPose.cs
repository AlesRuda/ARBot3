using System;
using ARBot.Common.Common;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Poza robotu v <b>odometricke soustave</b> (ROS REP-105 <c>odom</c>): spojita, bez skoku
    /// z korekci GPS / koridoru / korelace, za to s driftem. Pocatek je tam, kde fuze dostala prvni
    /// merenie; osy jsou ENU jen v tom smyslu, ze se integruje z tehoz kurzu — proti svetu je
    /// soustava posunuta i pootocena o to, co mezitim opravily korekce.
    ///
    /// <para><b>Proc to existuje.</b> Occupancy grid se kresli pozou z fuze, takze kazda korekce
    /// pozy posune jeho obsah proti robotu, ackoli se robot nepohnul
    /// (<c>lp-grid-posun-pomalou-korekci</c>). Lokalni vrstva globalni pravdu nepotrebuje — grid
    /// kresleny z posunute pozy je lokalne spravne, korekce ho lokalne rozbije. Rozhodnuti autora
    /// a navrh: <c>doc/ukoly.yaml</c>, tema <c>lp-grid-odometricka-soustava</c>.</para>
    ///
    /// <para>⚠️ <b>Neni to stav EKF.</b> Integruje se deterministicky z fuzovanych rychlosti
    /// (<c>v</c>, <c>ω</c>) mimo kovarianci a mimo update — jako stav s kovarianci by sdilel
    /// <c>v</c> a <c>θ</c> s globalni pozou a kazdy update GPS by ho pres Kalmanuv zisk posunul
    /// skokem, tedy presne to, cemu se ma vyhnout.</para>
    /// </summary>
    public readonly record struct OdomPose(double X, double Y, double Theta)
    {
        /// <summary>
        /// Posune pozu o krok <paramref name="dt"/> [s] rychlostmi <paramref name="v"/> [m/s]
        /// a <paramref name="omega"/> [rad/s]. <b>Tentyz vzorec jako
        /// <see cref="EKFModel"/>.PredictState</b> (stredni orientace <c>θ + ω·dt/2</c>), aby
        /// bez korekci sla odometricka poza s globalni presne soubezne.
        /// </summary>
        public OdomPose Integrate(double v, double omega, double dt)
        {
            if (dt == 0) return this;
            double b = Theta + omega * dt / 2.0;
            return new OdomPose(X + v * Math.Cos(b) * dt,
                                Y + v * Math.Sin(b) * dt,
                                Conversions.NormalizeOrientation(Theta + omega * dt));
        }
    }
}
