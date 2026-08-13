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

        /// <summary>
        /// Normalni rozdeleni se stredni hodnotou 0 a rozptylem 1 (Box-Muller ze dvou hashu).
        /// </summary>
        public static double Gaussian(int seed, int sample, int index, int channel)
        {
            double u1 = Uniform(seed, sample, index, channel * 2);
            double u2 = Uniform(seed, sample, index, channel * 2 + 1);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Gauss pro senzory, ktere maji jen poradi vzorku a kanal.</summary>
        public static double Gaussian(int seed, int sample, int channel)
            => Gaussian(seed, sample, 0, channel);
    }
}
