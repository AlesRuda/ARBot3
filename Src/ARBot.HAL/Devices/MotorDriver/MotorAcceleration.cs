using System;
using System.Diagnostics;

namespace ARBot.HAL.Devices.MotorDrivers
{
    /// <summary>
    /// Prevod zrychleni [m/s^2] na jednotky ridici jednotky motoru (Roboteq SDC2160) - spolecny
    /// pro <see cref="SDC2160"/> i <see cref="SDC2160Ex"/>, aby vzorce i pojistky byly na jednom
    /// miste.
    ///
    /// <para>⚠️ <b>Dve ruzne jednotky.</b> <see cref="SDC2160"/> posila zrychleni nativnim prikazem
    /// <c>!AC</c>/<c>!DC</c> v jednotkach 0,1 ot/min za sekundu (<see cref="ToUnits"/>).
    /// <see cref="SDC2160Ex"/> ho posila do promenne MicroBasic skriptu (<c>!VAR 1/2</c>), ktery ho
    /// bere v <b>tisicinach plneho rozsahu rychlosti za sekundu</b> (<see cref="ToScriptUnits"/>).
    /// Do 5. 10. 2026 sel i do skriptu prvni prevod, takze rampa byla 0,6·MaxTheoreticalSpeed/obvod
    /// = 2,6× strmejsi, nez rikal <c>Profile.MaxAcceleration</c> (0,40 m/s² → 1,04 m/s²; zmereno
    /// ze zaznamu, registr <c>hw-motor-rampa-jednotky</c>).</para>
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

            // 0,1 ot/min za sekundu: a / obvod [ot/s²] · 60 [ot/min za s] · 10.
            return Guard(acceleration, magnitude => 10 * 60 * magnitude / wheelCircumference);
        }

        /// <summary>
        /// Prevede zrychleni na jednotky RIDICIHO SKRIPTU (<c>RizeniDiffPodvozku.mbs</c>,
        /// <see cref="SDC2160Ex"/>): tisiciny plneho rozsahu rychlosti za sekundu. Skript dela
        /// <c>curSpeed += time[ms] · acceleration</c> nad rychlosti v miliontinach plneho rozsahu,
        /// tedy za sekundu pribyde <c>acceleration / 1000</c> plneho rozsahu. Tytez pojistky jako
        /// <see cref="ToUnits"/>.
        /// </summary>
        /// <param name="acceleration">Zrychleni [m/s^2].</param>
        /// <param name="maxPossibleSpeed">Plny rozsah rychlosti, ktery skript bere jako 1000
        /// (<c>Profile.MaxTheoreticalSpeed</c>) [m/s]; musi byt kladny.</param>
        public static int ToScriptUnits(double acceleration, double maxPossibleSpeed)
        {
            if (maxPossibleSpeed <= 0 || double.IsNaN(maxPossibleSpeed))
                throw new ArgumentOutOfRangeException(nameof(maxPossibleSpeed),
                    "Plny rozsah rychlosti musi byt kladny.");

            return Guard(acceleration, magnitude => 1000 * magnitude / maxPossibleSpeed);
        }

        /// <summary>
        /// Spolecne pojistky obou prevodu: zaporna hodnota se bere jako velikost, vysledek je vzdy
        /// aspon <see cref="MinUnits"/>.
        /// </summary>
        private static int Guard(double acceleration, Func<double, double> convert)
        {
            double magnitude = Math.Abs(acceleration);
            if (double.IsNaN(magnitude))
                magnitude = 0;
            if (magnitude != acceleration)
                Debug.WriteLine($"SetAcceleration: zaporne/neplatne zrychleni {acceleration} -> {magnitude} m/s^2.");

            int units = (int)Math.Round(convert(magnitude));
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
