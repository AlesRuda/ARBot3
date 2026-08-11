using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Pole vzdalenosti k nejblizsi NEPRUJEZDNE bunce (<see cref="CellState.Blocked"/>) nad
    /// <see cref="OccupancyGrid"/> - exaktni euklidovsky distance transform (Felzenszwalb-Huttenlocher,
    /// dva 1D pruchody, O(N)). Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para>Jedno pole obsluhuje vsechny odstupy naraz: tvrdou podminku <c>d &gt;= SafeDist</c>
    /// i rychlostni strop <c>v_clear(d)</c>. <see cref="CellState.Unknown"/> NENI prekazka
    /// (planovat se skrz smi) - o to, ze se do nej nesmi vjet, se stara rychlostni obalka.</para>
    ///
    /// <para>Bunky mimo grid se pri transformaci berou jako prazdne (ne prekazka), takze u kraje
    /// gridu je vzdalenost optimisticka. Planovac za hranici gridu stejne nejde a jizdu do
    /// nepotvrzeneho prostoru hlida brzdna obalka.</para>
    ///
    /// <para>Buffery se alokuji jednou v konstruktoru a <see cref="Build"/> je znovupouziva
    /// (bezi 10x za sekundu, zadny GC churn).</para>
    /// </summary>
    public sealed class ClearanceField
    {
        // "Nekonecno" pro EDT. Musi byt >> nejvetsi mozna kvadraticka vzdalenost (2*Size^2)
        // a zaroven bezpecne pod float.MaxValue, aby secteni v transformaci nepreteklo.
        private const float Inf = 1e20f;

        private readonly float[] sq;      // kvadraticka vzdalenost v bunkach (mezivysledek i vysledek)
        private readonly float[] line;    // vstup 1D transformace
        private readonly float[] outLine; // vystup 1D transformace
        private readonly float[] z;       // hranice parabol
        private readonly int[] v;         // vrcholy parabol
        private readonly float maxSq;

        /// <summary>Pocet bunek na stranu (stejny jako u gridu).</summary>
        public int Size { get; }

        /// <summary>Velikost bunky [m].</summary>
        public double Resolution { get; }

        /// <summary>Origin gridu v okamziku posledniho <see cref="Build"/> (absolutni index bunky).</summary>
        public int OriginX { get; private set; }

        /// <summary>Origin gridu v okamziku posledniho <see cref="Build"/> (absolutni index bunky).</summary>
        public int OriginY { get; private set; }

        /// <param name="size">Pocet bunek na stranu (musi odpovidat gridu).</param>
        /// <param name="resolution">Velikost bunky [m].</param>
        public ClearanceField(int size, double resolution)
        {
            if (size <= 0) throw new ArgumentException($"ClearanceField: size musi byt > 0, je {size}.");
            if (resolution <= 0) throw new ArgumentException($"ClearanceField: resolution musi byt > 0, je {resolution}.");

            Size = size;
            Resolution = resolution;
            sq = new float[size * size];
            line = new float[size];
            outLine = new float[size];
            z = new float[size + 1];
            v = new int[size];
            maxSq = 2f * size * size;
        }

        /// <summary>Sestavi pole pro rozmery daneho gridu (bez vypoctu - ten dela <see cref="Build"/>).</summary>
        public ClearanceField(OccupancyGrid grid)
            : this((grid ?? throw new ArgumentNullException(nameof(grid))).Size, grid.Resolution)
        {
        }

        /// <summary>
        /// Prepocte pole vzdalenosti z aktualniho stavu gridu. Zapamatuje si i origin gridu,
        /// aby slo cist podle absolutnich indexu bunek.
        /// </summary>
        public void Build(OccupancyGrid grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (grid.Size != Size)
                throw new ArgumentException($"ClearanceField: grid ma Size {grid.Size}, pole {Size}.");

            OriginX = grid.OriginX;
            OriginY = grid.OriginY;

            // 1) Semena: prekazka = 0, ostatni = Inf.
            for (int j = 0; j < Size; j++)
            {
                int row = j * Size;
                for (int i = 0; i < Size; i++)
                    sq[row + i] = grid.StateAt(grid.LocalIndex(i, j)) == CellState.Blocked ? 0f : Inf;
            }

            // 2) 1D transformace po sloupcich (osa j).
            for (int i = 0; i < Size; i++)
            {
                for (int j = 0; j < Size; j++) line[j] = sq[j * Size + i];
                Transform1D(line, outLine, Size);
                for (int j = 0; j < Size; j++) sq[j * Size + i] = outLine[j];
            }

            // 3) 1D transformace po radcich (osa i) - po ni je v sq exaktni kvadraticka vzdalenost.
            for (int j = 0; j < Size; j++)
            {
                int row = j * Size;
                Array.Copy(sq, row, line, 0, Size);
                Transform1D(line, outLine, Size);
                for (int i = 0; i < Size; i++)
                {
                    float s = outLine[i];
                    sq[row + i] = s > maxSq ? maxSq : s;   // prazdny grid -> Inf; zastropovat
                }
            }
        }

        /// <summary>
        /// Vzdalenost k nejblizsi neprujezdne bunce [m] pro LOKALNI souradnici 0..Size-1.
        /// Mimo rozsah vraci 0 (konzervativne = jako by tam prekazka byla).
        /// </summary>
        public float DistanceLocal(int i, int j)
        {
            if ((uint)i >= (uint)Size || (uint)j >= (uint)Size) return 0f;
            return (float)(Math.Sqrt(sq[j * Size + i]) * Resolution);
        }

        /// <summary>Vzdalenost k nejblizsi neprujezdne bunce [m] pro ABSOLUTNI index bunky.</summary>
        public float Distance(int cx, int cy) => DistanceLocal(cx - OriginX, cy - OriginY);

        /// <summary>Kvadraticka vzdalenost v bunkach (bez odmocniny) - pro srovnani v hot loopu.</summary>
        public float SquaredCells(int i, int j)
        {
            if ((uint)i >= (uint)Size || (uint)j >= (uint)Size) return 0f;
            return sq[j * Size + i];
        }

        /// <summary>
        /// Exaktni 1D distance transform (Felzenszwalb-Huttenlocher): pro kazde q spocte
        /// <c>min_p ((q-p)^2 + f[p])</c> pomoci obalky dolni paraboly. O(n).
        /// </summary>
        private void Transform1D(float[] f, float[] d, int n)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -Inf;
            z[1] = Inf;

            for (int q = 1; q < n; q++)
            {
                // Prusecik paraboly z q s aktualne posledni parabolou obalky.
                float s = Intersect(f, q, v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = Intersect(f, q, v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = Inf;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        private static float Intersect(float[] f, int q, int p)
            => ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * q - 2f * p);
    }
}
