using System;
using System.Diagnostics;

namespace ARBot.HAL.Devices.MotorDrivers
{
    /// <summary>
    /// Prevod zrychleni [m/s^2] na jednotky ridici jednotky motoru (Roboteq SDC2160) - spolecny
    /// pro <see cref="SDC2160"/> i <see cref="SDC2160Ex"/>, aby vzorec i pojistky byly na jednom
    /// miste.
    ///
    /// <para><b>Proc pojistky.</b> Hodnota jde do rampy v ridici jednotce
    /// (<c>curSpeed += time * acceleration</c>, viz <c>Src/RoboRun/RizeniDiffPodvozku.mbs</c>)
    /// a nesmyslna hodnota tam nadela vic skody nez chybejici prikaz:</para>
    /// <list type="bullet">
    /// <item><description><b>Zaporna</b> by rampu hnala OD cile - druha vetev (<c>curSpeed &gt;
    /// cil</c>) uz by nenastala, takze by divergovala az na saturaci, tedy na plnou rychlost
    /// OPACNYM smerem.</description></item>
    /// <item><description><b>Nula</b> rampu zmrazi: uz jedouci robot by nezastavil ani pod
    /// nouzovym zastavenim (<c>reqSpeed=0</c> nema cim zabrat) a protoze skript nuluje rotaci az
    /// pri <c>curSpeed=0</c>, jel by dal i v zatacce. Nula pritom nemusi prijit zamerne - staci
    /// male zrychleni, ktere se zaokrouhli k nule.</description></item>
    /// </list>
    ///
    /// <para>Skript v jednotce se proti tomu branit nemuze (kdyz je rampa mrtva, uz nema cim
    /// brzdit), takze se to musi uhlidat tady, nez to odejde po lince.</para>
    /// </summary>
    public static class MotorAcceleration
    {
        /// <summary>Nejmensi hodnota, ktera smi odejit do jednotky (nikdy ne nula).</summary>
        public const int MinUnits = 1;

        /// <summary>
        /// Prevede zrychleni na jednotky jednotky motoru. Zaporna hodnota se bere jako velikost,
        /// vysledek je vzdy alespon <see cref="MinUnits"/>; oboji se hlasi do Debug outputu, aby
        /// se spatna konfigurace poznala, misto aby se tise spravila.
        /// </summary>
        /// <param name="acceleration">Zrychleni [m/s^2].</param>
        /// <param name="wheelCircumference">Obvod kola [m]; musi byt kladny.</param>
        public static int ToUnits(double acceleration, double wheelCircumference)
        {
            if (wheelCircumference <= 0 || double.IsNaN(wheelCircumference))
                throw new ArgumentOutOfRangeException(nameof(wheelCircumference),
                    "Obvod kola musi byt kladny.");

            double magnitude = Math.Abs(acceleration);
            if (double.IsNaN(magnitude))
                magnitude = 0;
            if (magnitude != acceleration)
                Debug.WriteLine($"SetAcceleration: zaporne/neplatne zrychleni {acceleration} -> {magnitude} m/s^2.");

            int units = (int)Math.Round(10 * 60 * magnitude / wheelCircumference);
            if (units < MinUnits)
            {
                Debug.WriteLine($"SetAcceleration: {acceleration} m/s^2 dava {units} jednotek "
                                + $"-> zvedam na {MinUnits} (nula by zmrazila rampu a nouzove "
                                + "zastaveni by nemelo cim brzdit).");
                units = MinUnits;
            }

            return units;
        }
    }
}
