using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Kartezsky occupancy grid sjizdnosti v okoli robotu, akumulovany v case (log-odds).
    /// Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para><b>Kotveni:</b> osy jsou pevne srovnane se SVETEM (ENU), bunka se adresuje absolutnim
    /// indexem <c>floor(x / Resolution)</c>. Grid je <b>kruhovy buffer</b> - do pameti se jde pres
    /// <c>index &amp; Mask</c>. Posun robotu tedy jen prepocita origin a vynuluje NOVE VSTOUPIVSI pruhy
    /// (O(sirka), ne O(N)); <b>rotace robotu se gridu vubec nedotkne</b> (zadny resampling, zadne
    /// rozmazani). Cenou je zavislost na kvalite lokalizace - resi se clampem log-odds a kratkou
    /// pameti (jednotky sekund), ne dokonalou lokalizaci.</para>
    ///
    /// <para><b>Dva rovnocenne kanaly</b>, oba jako log-odds NEPRUJEZDNOSTI (kladne = neprujezdne):
    /// <see cref="Occ"/> z hloubky (geometrie) a <see cref="Road"/> z barvy (semantika). Stav bunky
    /// (<see cref="CellState"/>) je odvozeny z obou - neprujezdnost od kterehokoli z nich staci.</para>
    ///
    /// <para><b>Vlaknova bezpecnost:</b> zadna. Grid vlastni jedno vlakno (<c>LocalNavigator</c>);
    /// odberatelum (UI, zaznam) se posila snapshot pres zpravu.</para>
    /// </summary>
    public sealed class OccupancyGrid
    {
        private readonly sbyte[] occ;
        private readonly sbyte[] road;
        private readonly double invResolution;
        private readonly sbyte blockedQ;
        private readonly sbyte freeQ;

        /// <summary>Konfigurace (po sestaveni se nemeni - kvantovane prahy jsou nacachovane).</summary>
        public OccupancyGridConfig Config { get; }

        /// <summary>Pocet bunek na stranu (mocnina dvou).</summary>
        public int Size { get; }

        /// <summary>Maska pro kruhovy buffer (<see cref="Size"/> - 1).</summary>
        public int Mask { get; }

        /// <summary>Velikost bunky [m].</summary>
        public double Resolution { get; }

        /// <summary>Absolutni index nejzapadnejsiho sloupce, ktery grid drzi.</summary>
        public int OriginX { get; private set; }

        /// <summary>Absolutni index nejjiznejsiho radku, ktery grid drzi.</summary>
        public int OriginY { get; private set; }

        /// <summary>Kanal geometrie: log-odds neprujezdnosti z hloubky, fixed-point
        /// (<see cref="OccupancyGridConfig.Scale"/>). Indexuje se pres <see cref="Index"/>.</summary>
        public sbyte[] Occ => occ;

        /// <summary>Kanal semantiky: log-odds neprujezdnosti z barvy, fixed-point
        /// (<see cref="OccupancyGridConfig.Scale"/>). Indexuje se pres <see cref="Index"/>.</summary>
        public sbyte[] Road => road;

        /// <param name="config">Konfigurace; null = vychozi.</param>
        public OccupancyGrid(OccupancyGridConfig config = null)
        {
            Config = config ?? new OccupancyGridConfig();
            Config.Validate();

            Size = Config.Size;
            Mask = Size - 1;
            Resolution = Config.Resolution;
            invResolution = 1.0 / Resolution;
            blockedQ = Config.BlockedQuantized;
            freeQ = Config.FreeQuantized;

            occ = new sbyte[Size * Size];
            road = new sbyte[Size * Size];
        }

        // ---------------- souradnice ----------------

        /// <summary>Absolutni index bunky pro svetovou souradnici X [m].</summary>
        public int CellX(double worldX) => (int)Math.Floor(worldX * invResolution);

        /// <summary>Absolutni index bunky pro svetovou souradnici Y [m].</summary>
        public int CellY(double worldY) => (int)Math.Floor(worldY * invResolution);

        /// <summary>Svetova X souradnice STREDU bunky [m].</summary>
        public double CenterX(int cx) => (cx + 0.5) * Resolution;

        /// <summary>Svetova Y souradnice STREDU bunky [m].</summary>
        public double CenterY(int cy) => (cy + 0.5) * Resolution;

        /// <summary>Drzi grid tuto bunku? (Mimo grid se zapisy zahazuji a cteni vraci
        /// <see cref="CellState.Unknown"/>.)</summary>
        public bool Contains(int cx, int cy)
            => (uint)(cx - OriginX) < (uint)Size && (uint)(cy - OriginY) < (uint)Size;

        /// <summary>Index do <see cref="Occ"/>/<see cref="Road"/> pro absolutni bunku (kruhovy buffer).
        /// Nekontroluje <see cref="Contains"/> - volajici si to hlida sam.</summary>
        public int Index(int cx, int cy) => (cx & Mask) + (cy & Mask) * Size;

        /// <summary>Index do <see cref="Occ"/>/<see cref="Road"/> pro LOKALNI souradnici 0..Size-1
        /// (0,0 = <see cref="OriginX"/>, <see cref="OriginY"/>). Pro smycky pres cely grid.</summary>
        public int LocalIndex(int i, int j) => ((OriginX + i) & Mask) + (((OriginY + j) & Mask) * Size);

        // ---------------- posun (kruhovy buffer) ----------------

        /// <summary>
        /// Posune grid tak, aby zadana svetova poloha (typicky robot) byla ve STREDU. Vynuluje jen
        /// nove vstoupivsi pruhy. Kdyz se poloha nezmenila o celou bunku, neudela nic.
        /// </summary>
        public void Recenter(double worldX, double worldY)
            => MoveOrigin(CellX(worldX) - Size / 2, CellY(worldY) - Size / 2);

        /// <summary>
        /// Nastavi novy origin. Pruhy, ktere z gridu vypadly, se vynuluji - v kruhovem bufferu jsou
        /// to TY SAME slozky pameti, do kterych se namapuji nove vstoupivsi pruhy
        /// (<c>(o + Size + k) &amp; Mask == (o + k) &amp; Mask</c>), takze staci vynulovat slot
        /// odchazejiciho pruhu.
        /// </summary>
        public void MoveOrigin(int newOriginX, int newOriginY)
        {
            int dx = newOriginX - OriginX;
            int dy = newOriginY - OriginY;
            if (dx == 0 && dy == 0) return;

            if (Math.Abs((long)dx) >= Size || Math.Abs((long)dy) >= Size)
            {
                // Skok mimo dosavadni okno - nic se neprekryva, zahodit vse.
                Clear();
            }
            else
            {
                if (dx > 0) for (int k = 0; k < dx; k++) ClearColumn(OriginX + k);
                else if (dx < 0) for (int k = 0; k < -dx; k++) ClearColumn(newOriginX + k);

                if (dy > 0) for (int k = 0; k < dy; k++) ClearRow(OriginY + k);
                else if (dy < 0) for (int k = 0; k < -dy; k++) ClearRow(newOriginY + k);
            }

            OriginX = newOriginX;
            OriginY = newOriginY;
        }

        /// <summary>Vynuluje cely grid (vse = <see cref="CellState.Unknown"/>).</summary>
        public void Clear()
        {
            Array.Clear(occ, 0, occ.Length);
            Array.Clear(road, 0, road.Length);
        }

        private void ClearColumn(int cx)
        {
            int ix = cx & Mask;
            for (int iy = 0; iy < Size; iy++)
            {
                int i = ix + iy * Size;
                occ[i] = 0;
                road[i] = 0;
            }
        }

        private void ClearRow(int cy)
        {
            int off = (cy & Mask) * Size;
            Array.Clear(occ, off, Size);
            Array.Clear(road, off, Size);
        }

        // ---------------- akumulace ----------------

        /// <summary>
        /// Prida prirustek log-odds do kanalu geometrie. Mimo grid se zahodi.
        /// <para>Prirustek se kvantuje na nejblizsi nasobek <see cref="OccupancyGridConfig.Scale"/> -
        /// pozorovani slabsi nez pulka kvanta se tedy <b>zahodi</b> (pri Scale 0,05 a
        /// OccupiedUpdate 0,85 je to duvera pod ~0,03). Je to zamer: takova bunka zustane
        /// <see cref="CellState.Unknown"/>, tedy "planuj skrz, nevjizdej" - a vyjasni se, jak se k ni
        /// robot priblizi a duvera stoupne.</para>
        /// </summary>
        public void AddOcc(int cx, int cy, float deltaLogOdds)
        {
            if (!Contains(cx, cy)) return;
            int i = Index(cx, cy);
            occ[i] = AddClamped(occ[i], deltaLogOdds);
        }

        /// <summary>Prida prirustek log-odds do kanalu semantiky. Detaily viz <see cref="AddOcc"/>.</summary>
        public void AddRoad(int cx, int cy, float deltaLogOdds)
        {
            if (!Contains(cx, cy)) return;
            int i = Index(cx, cy);
            road[i] = AddClamped(road[i], deltaLogOdds);
        }

        /// <summary>Pozorovani prekazky (geometrie) s danou duverou 0..1.</summary>
        public void ObserveOccupied(int cx, int cy, float confidence)
            => AddOcc(cx, cy, Config.OccupiedUpdate * confidence);

        /// <summary>Pozorovani volne plochy (geometrie) s danou duverou 0..1.</summary>
        public void ObserveFree(int cx, int cy, float confidence)
            => AddOcc(cx, cy, Config.FreeUpdate * confidence);

        /// <summary>
        /// Pozorovani sjizdnosti z barvy: <paramref name="pTraversable"/> je pravdepodobnost
        /// SJIZDNOSTI (1 = jiste cesta, 0 = jiste mimo cestu, 0,5 = nevim).
        /// </summary>
        public void ObserveRoad(int cx, int cy, float pTraversable, float confidence)
            => AddRoad(cx, cy, Config.RoadUpdateFromProbability(pTraversable) * confidence);

        private sbyte AddClamped(sbyte current, float deltaLogOdds)
        {
            int limit = (int)MathF.Round(Config.Clamp / Config.Scale);
            int q = current + (int)MathF.Round(deltaLogOdds / Config.Scale);
            if (q > limit) q = limit;
            if (q < -limit) q = -limit;
            return (sbyte)q;
        }

        // ---------------- cteni ----------------

        /// <summary>Log-odds neprujezdnosti z geometrie (0 = nevim); mimo grid 0.</summary>
        public float LogOddsOcc(int cx, int cy)
            => Contains(cx, cy) ? occ[Index(cx, cy)] * Config.Scale : 0f;

        /// <summary>Log-odds neprujezdnosti ze semantiky (0 = nevim); mimo grid 0.</summary>
        public float LogOddsRoad(int cx, int cy)
            => Contains(cx, cy) ? road[Index(cx, cy)] * Config.Scale : 0f;

        /// <summary>Stav bunky z OBOU kanalu; mimo grid <see cref="CellState.Unknown"/>.</summary>
        public CellState State(int cx, int cy)
            => Contains(cx, cy) ? StateAt(Index(cx, cy)) : CellState.Unknown;

        /// <summary>Stav bunky pro uz spocteny index (hot loop - <see cref="Index"/>/<see cref="LocalIndex"/>).</summary>
        public CellState StateAt(int index)
        {
            sbyte o = occ[index];
            sbyte r = road[index];
            if (o >= blockedQ || r >= blockedQ) return CellState.Blocked;
            if (o <= freeQ && r <= freeQ) return CellState.Free;
            return CellState.Unknown;
        }

        /// <summary>Stav bunky pro svetove souradnice [m].</summary>
        public CellState StateAtWorld(double worldX, double worldY)
            => State(CellX(worldX), CellY(worldY));

        // ---------------- prevod na zpravu ----------------

        /// <summary>
        /// Snapshot gridu jako zprava pro vizualizaci a zaznam. Kanaly se prekopiruji do LOKALNIHO
        /// poradi (<c>i + j * Size</c>), aby prijemce nemusel resit kruhovy buffer. Kopie je vzdy
        /// cerstva - zpravu drzi asynchronni odberatele.
        /// </summary>
        /// <param name="timeStamp">Cas, ke kteremu snapshot plati (typicky cas naposledy zapsaneho snimku).</param>
        public Logs.OccupancyGridMsg ToLogMessage(DateTime timeStamp)
        {
            var msg = new Logs.OccupancyGridMsg
            {
                Size = Size,
                Resolution = Resolution,
                OriginX = OriginX,
                OriginY = OriginY,
                Scale = Config.Scale,
                BlockedThreshold = Config.BlockedThreshold,
                FreeThreshold = Config.FreeThreshold,
                TimeStamp = timeStamp,
                Occ = new sbyte[Size * Size],
                Road = new sbyte[Size * Size],
            };

            for (int j = 0; j < Size; j++)
            {
                int dst = j * Size;
                for (int i = 0; i < Size; i++)
                {
                    int src = LocalIndex(i, j);
                    msg.Occ[dst + i] = occ[src];
                    msg.Road[dst + i] = road[src];
                }
            }
            return msg;
        }
    }
}
