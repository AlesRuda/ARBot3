using System;

namespace ARBot.Common.Simulation
{
    /// <summary>
    /// Sum pro simulace. Nejde o sekvenci <see cref="Random"/>, ale o cistou funkci vstupu
    /// (seed, vzorek, kanal) - hodnota tedy nezavisi na poradi zpracovani ani na poctu vlaken,
    /// takze vysledek je reprodukovatelny a jde pocitat paralelne. Viz doc/virtual-hw.md.
    /// <para>Pouziva ho vizualni simulace (sum hloubky, drsnost travy, sum barvy) i simulovane
    /// senzory (GPS, IMU).</para>
    /// </summary>
    public static class DeterministicNoise
    {
        /// <summary>Michani bitu (varianta splitmix32) - lavinovy efekt na malych vstupech.</summary>
        private static uint Mix(uint x)
        {
            x += 0x9E3779B9u;
            x = (x ^ (x >> 16)) * 0x85EBCA6Bu;
            x = (x ^ (x >> 13)) * 0xC2B2AE35u;
            return x ^ (x >> 16);
        }

        /// <summary>Hash ctverice vstupu na 32 bitu.</summary>
        private static uint Hash(int seed, int sample, int index, int channel)
        {
            uint h = Mix((uint)seed);
            h = Mix(h ^ (uint)sample);
            h = Mix(h ^ (uint)index);
            return Mix(h ^ (uint)channel);
        }

        /// <summary>Rovnomerne rozdeleni v intervalu (0, 1].</summary>
        public static double Uniform(int seed, int sample, int index, int channel)
        {
            // +1 posune 0 na nenulovou hodnotu (Gauss potrebuje log z kladneho cisla).
            return (Hash(seed, sample, index, channel) + 1.0) / 4294967296.0;
        }

        // --- Kvantilova tabulka normalniho rozdeleni ---
        //
        // Puvodne se pocital Box-Muller ze DVOU hashu: 8x Mix + Log + Sqrt + Cos, tedy 38 ns na
        // vzorek. Virtualni kamera si o vzorek rekne ~1,5 M krat na snimek (3 kanaly barvy na
        // pixel + drsnost travy + sum hloubky), takze na tom stravila 57 ms z 93 ms renderu -
        // 71 % casu simulovane kamery slo do generovani sumu a kamery jely 6,8 Hz misto 30
        // (nameřeno 23. 8. 2026).
        //
        // Tabulka je pole KVANTILU: prvek i je inverzni distribucni funkce v bode (i+0,5)/N.
        // Vyber nahodneho prvku tedy dava presne normalni rozdeleni (az na kvantovani), a stoji
        // jeden hash a jedno cteni z pole.
        //
        // 4096 polozek = 16 kB, tedy se vejde do L1. Vetsi tabulka (65536 = 256 kB) davala kvuli
        // vypadkum cache 12 ns misto 7 ns na vzorek, aniz by na kvalite sumu zalezelo - kvantovani
        // na 4096 kvantilu je hluboko pod rozlisenim cehokoli, co se tim sumem modeluje.
        private const int TableBits = 12;
        private const int TableSize = 1 << TableBits;
        private const int TableMask = TableSize - 1;

        private static readonly float[] Quantiles = BuildQuantiles();

        private static float[] BuildQuantiles()
        {
            var t = new float[TableSize];
            for (int i = 0; i < TableSize; i++)
                t[i] = (float)InverseNormalCdf((i + 0.5) / TableSize);
            return t;
        }

        /// <summary>
        /// Inverzni distribucni funkce normalniho rozdeleni (Acklamova racionalni aproximace,
        /// relativni chyba pod 1,2e-9). Pocita se jen pri stavbe tabulky, takze na jeji rychlosti
        /// nezalezi.
        /// </summary>
        private static double InverseNormalCdf(double p)
        {
            double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
                            1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
            double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
                            6.680131188771972e+01, -1.328068155288572e+01 };
            double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
                           -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
            double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
                           3.754408661907416e+00 };

            const double pLow = 0.02425, pHigh = 1 - pLow;

            if (p < pLow)
            {
                double q = Math.Sqrt(-2 * Math.Log(p));
                return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                       / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
            if (p > pHigh)
            {
                double q = Math.Sqrt(-2 * Math.Log(1 - p));
                return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                       / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }

            double r = p - 0.5, r2 = r * r;
            return (((((a[0] * r2 + a[1]) * r2 + a[2]) * r2 + a[3]) * r2 + a[4]) * r2 + a[5]) * r
                   / (((((b[0] * r2 + b[1]) * r2 + b[2]) * r2 + b[3]) * r2 + b[4]) * r2 + 1);
        }

        /// <summary>
        /// Normalni rozdeleni se stredni hodnotou 0 a rozptylem 1 - <b>cista funkce vstupu</b>,
        /// stejne jako drive. Hodnoty se proti Box-Mullerove variante lisi (jina realizace tehoz
        /// rozdeleni), takze zaznamy porizene pred 23. 8. 2026 maji jiny sum.
        /// </summary>
        public static double Gaussian(int seed, int sample, int index, int channel)
            => Quantiles[Hash(seed, sample, index, channel) & TableMask];

        /// <summary>Gauss pro senzory, ktere maji jen poradi vzorku a kanal.</summary>
        public static double Gaussian(int seed, int sample, int channel)
            => Gaussian(seed, sample, 0, channel);
    }
}
