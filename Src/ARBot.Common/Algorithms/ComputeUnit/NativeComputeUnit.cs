using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.ComputeUnit
{
    public class NativeComputeUnit: IComputeUnit
    {
        private Dictionary<int, int> dist2Cnt = new Dictionary<int, int>();

        [StructLayout(LayoutKind.Sequential)]
        public struct PathEdgeItem
        {
            public int Left, Right, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RGB
        {
            public byte R, G, B;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BGR32
        {
            public byte B, G, R, A;
        }


        private static class NativeMethods
        {
#if IsX64
        [DllImport("NativeLib.dll", EntryPoint = "ComputeAlloc", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr ComputeAlloc(int maxPoints, int width, int height, int xOff, int yOff, float resolution);
        [DllImport("NativeLib.dll", EntryPoint = "ComputeFree", SetLastError = true)]
        internal static extern void ComputeFree(IntPtr ci);

        [DllImport("NativeLib.dll", EntryPoint = "Segment2", SetLastError = true)]
        internal static extern void Segment2(IntPtr ci, byte[] leftDist, float[] leftTransformMatrix, Point2DF[,] leftTransform,
            byte[] rightDist, float[] rightTransformMatrix, Point2DF[,] rightTransform,
            float[] globalTransformMatrix,
            int len, float maxZ);
        [DllImport("NativeLib.dll", EntryPoint = "BackProjectImpl")]
        internal static extern void BackProject(byte[] probability, byte[] imgData, byte[] backProjectTab, int len);
        [DllImport("NativeLib.dll", EntryPoint = "BackProjectBGR32Impl")]
        internal static extern void BackProjectBGR32(byte[] probability, byte[] imgData, byte[] backProjectTab, int len);

        [DllImport("NativeLib.dll", EntryPoint = "FindPathEdge", SetLastError = true)]
        internal static extern int FindPathEdge([In, Out] PathEdgeItem[] dst, byte[] probability, int width, int height);

        [DllImport("NativeLib.dll", EntryPoint = "TestCopy", SetLastError = false)]
        internal static extern int TestCopy(byte[] i, byte[] o, int mode, int cnt);
        [DllImport("NativeLib.dll", EntryPoint = "TestCopy", SetLastError = false)]
        internal static extern int TestCopy2(IntPtr i, IntPtr o, int mode, int cnt);
        [DllImport("NativeLib.dll", EntryPoint = "Test2", SetLastError = false)]
        internal static extern void Test2();

        /// <summary>
        /// Alokuje blok pameti
        /// </summary>
        /// <param name="len">delka bloku v bajtech</param>
        /// <returns></returns>
        [DllImport("NativeLib.dll", EntryPoint = "Alloc", SetLastError = false)]
        internal static extern IntPtr Alloc(Int32 len);
        /// <summary>
        /// Uvolnuje blok pameti
        /// </summary>
        /// <param name="ptr">pointer na blok pameti, ktery ma byt ovolnen, puvodne vracena hodnota metodou Alloc</param>
        [DllImport("NativeLib.dll", EntryPoint = "Free", SetLastError = false)]
        internal static extern void Free(IntPtr ptr);

        /// <summary>
        /// Agreguje body sveta v rovine x,y pro budouci extrakci prekazek. 
        /// </summary>
        /// <param name="wordPoints">pole bodu sveta v metrech</param>
        /// <param name="wordPointsCount">Pocet bodu v poli wordPoints</param>
        /// <param name="r">rozliseni pro agregaci</param>
        /// <param name="xOff">posunuti v agregacnim poli ais</param>
        /// <param name="yOff">posunuti v agregacnim poli ais</param>
        /// <param name="ais">pole agregacnich bodu o velikosti width*height</param>
        /// <param name="uais">pole offsetu na pouzite agregacni body o velikosti width*height</param>
        /// <param name="width">sirka (odpovida x) agregacniho pole</param>
        /// <param name="height">vyska (odpovida y) agregacniho pole</param>
        /// <param name="v">rovnice roviny po ktere robot jede, vznika regresi z bodu v okoli robotu, slouzi pro upravu z souradnice agregovaneho bodu z' = v.x * p.x + v.y * p.y + v.z * p.z + v.a * p.a; </param>
        /// <returns>pocet obsazenych agregacnich bodu</returns>
        [DllImport("NativeLib.dll", EntryPoint = "AggregateObstacles", SetLastError = false)]
        internal static extern int AggregateObstacles(IntPtr wordPoints, int wordPointsCount, double r, int xOff, int yOff, IntPtr ais, IntPtr uais, int width, int height, Point4D v);
        [DllImport("NativeLib.dll", EntryPoint = "AggregateObstaclesImpl", SetLastError = false)]
        internal static extern int AggregateObstaclesImpl(Point4D[] wordPoints, Int32 wordPointsCount, float r, Int32 xOff, Int32 yOff, [In, Out] AggregateItem[] ais, [In, Out] Int32[] uais, Int32 width, Int32 height, Point4D v);

        /// <summary>
        /// Prolozi rovinou mnozinu bodu jejichz abs(z) je mensi jak MaxZ.
        /// z=a*x+b*y+d
        /// Lepe receno spocte parametry pro vypocet prolozeni
        /// </summary>
        /// <param name="param">Struktura popisujici prolozeni rovinou </param>
        /// <param name="src">Prokladane body</param>
        /// <param name="maxZ">Maximalni hodnota abs(z) aby byl bod pouzit pro vypocet prolozeni</param>
        /// <param name="len">Pocet bodu v poli src</param>
        [DllImport("NativeLib.dll", EntryPoint = "XYZ2PlaneImpl", SetLastError = false)]
        internal static extern void XYZ2PlaneImpl([In, Out] ref PlaneParams param, Point4D[] src, float maxZ, int len);

        /// <summary>
        /// Prepocte agregovane hodnoty pro prolozeni bodu rovinou na rovinu
	    /// vysledkem bude nastaveni atributu v
        /// </summary>
        /// <param name="pars"></param>
        [DllImport("NativeLib.dll", EntryPoint = "CalcPlaneParams", SetLastError = false)]
        internal static extern void CalcPlaneParams([In, Out] ref PlaneParams pars);

        /// <summary>
        /// Inizializuje pouzite agregacni itemy nastavenim Count na 0
        /// </summary>
        /// <param name="uais">Pole offseru pouzitych AggregateItem</param>
        /// <param name="cnt">Pocet prvku v uias</param>
        [DllImport("NativeLib.dll", EntryPoint = "ClearAggregateImpl", SetLastError = false)]
        internal static extern void ClearAggregateImpl([In, Out] AggregateItem[] ais, Int32[] uias, Int32 cnt);

        /// <summary>
        /// Z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery)
        /// hodnoty 0 a -1 v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
        /// hodnoty do dst se ukladaji od konce dist
        /// </summary>
        /// <param name="dst">Vysledne pole xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery) v metrech. xyz[j]=(trasform[i].x*dist[i], trasform[i].y*dist[i], dist[i]) </param>
        /// <param name="dist">Pole vzdalenosti v mm</param>
        /// <param name="transform">Pole popisujici kameru. Pro kazdy bod kamery obsahuje promitnuti do plochy XY</param>
        /// <param name="len">Delky poli dist a transform</param>
        /// <returns>Pocet zapsanych bodu do dst</returns>
        [DllImport("NativeLib.dll", EntryPoint = "Depth2XYZImpl", SetLastError = false)]
        internal static extern int Depth2XYZImpl([In, Out]Point4D[] dst, short[] dist, Point2DF[] transform, int len);

        // z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery(x - roste doprava, y - roste dolu a z od kamery)
        // nasledne bod pootoci v prostoru pomoci rotate
        // transform je pole vektoru xy, plati xyz = [x*dist, y*dist, dist], pole transform a dist obsahuje len prvku,
        // hodnoty 0 a -1 v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
        // funkce vraci pocet zapsanych zaznamu do dst
        [DllImport("NativeLib.dll", EntryPoint = "DepthTransformImpl", SetLastError = false)]
        internal static extern int DepthTransformImpl([In, Out] Point4D[] dst, Point2DF[] transform, float[] rotate, short[] dist, int len);

        /// <summary>
        /// z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery(x - roste doprava, y - roste dolu a z od kamery)
        /// nasledne bod pootoci v prostoru pomoci rotate
        /// transform je pole vektoru xy, plati dst[i] =[transform[i].x * dist[i], transform[i].y * dist[i], dist[i]] * rotate, pole transform, dst a dist obsahuje len prvku,
        /// nektere hodnoty v dist reprezentuji nezmerenou hodnotu, tyto body se do vystupu dst ulozi jako[0, 0, 0, 0]
        /// data se do dst ukladaji v opacnem poradi oproti dist
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="transform"></param>
        /// <param name="rotate"></param>
        /// <param name="dist"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        [DllImport("NativeLib.dll", EntryPoint = "DepthTransform2Impl", SetLastError = false)]
        internal static extern int DepthTransform2Impl([In, Out] Point4D[] dst, Point2DF[,] transform, float[] rotate, byte[] dist, int len);

        /// <summary>
        /// extrahuje z agregacniho pole prekazky
        /// </summary>
        /// <param name="ais">Pole agregacnich bodu</param>
        /// <param name="uais">Pole offsetu na pouzite AggregateItem</param>
        /// <param name="len">Pocet zaznamu v uias</param>
        /// <param name="ops">Pole prekazek</param>
        /// <param name="minCount">Minimalni pocet pointu z kterych bylo agregovano tj. AggregateItem.Count</param>
        /// <param name="minStd2">Minimalni hodnota rozptylu</param>
        /// <returns></returns>
        [DllImport("NativeLib.dll", EntryPoint = "ExtractObstaclesImpl", SetLastError = false)]
        internal static extern int ExtractObstaclesImpl(AggregateItem[] ais, Int32[] uais, Int32 len, [In, Out] Point4D[] ops, float minCount, float minStd2);

        /// <summary>
        /// Resetuje agrgovane udaje ve vypoctu aproximace bodu rovinou
        /// </summary>
        /// <param name="ptr">pointer na blok pameti, ktery ma byt ovolnen, puvodne vracena hodnota metodou Alloc</param>
        [DllImport("NativeLib.dll", EntryPoint = "ResetPlaneParams", SetLastError = false)]
        internal static extern void ResetPlaneParams([In, Out] ref PlaneParams pars);

        /// <summary>
        /// Pole vektoru src vynasobi matici transform a vysledek ulozi do dst
        /// dst=transform*src
        /// </summary>
        /// <param name="dst">Cilove pole</param>
        /// <param name="rotate">Pole transformacni matice float[16]. V matici na pozici 0 je prvni radek prvni sloupec, na pozici 1 je prvni radek druhy sloupec, ....</param>
        /// <param name="src">Zdrojove pole</param>
        /// <param name="len">Delka pole dst a src</param>
        [DllImport("NativeLib.dll", EntryPoint = "TransformPoint4DImpl", SetLastError = false)]
        internal static extern void TransformPoint4DImpl([In, Out]Point4D[] dst, float[] rotate, Point4D[] src, int len);


        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32 v reverznim poradi
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseRGB24ToBGR32", SetLastError = false)]
        internal static extern void ReverseRGB24ToBGR32([In, Out] BGR32[] dst, RGB[] src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32 v reverznim poradi
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseRGB24ToBGR32", SetLastError = false)]
        internal static extern void ReverseRGB24ToBGR32IntPtr([In, Out] byte[] dst, IntPtr src, int len);

        /// <summary>
        /// Reverzuje pole Int16, ze zdroje src kopiruje do dst
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseInt16", SetLastError = false)]
        internal static extern void ReverseInt16([In, Out] Int16[] dst, Int16[] src, int len);

        /// <summary>
        /// Reverzuje pole Int16, ze zdroje src kopiruje do dst
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych in16</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseInt16", SetLastError = false)]
        internal static extern void ReverseInt16IntPtr([In, Out] byte[] dst, IntPtr src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyRGB24ToBGR32", SetLastError = false)]
        internal static extern void CopyRGB24ToBGR32([In, Out] BGR32[] dst, RGB[] src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyRGB24ToBGR32", SetLastError = false)]
        internal static extern void CopyRGB24ToBGR32IntPtr([In, Out] byte[] dst, IntPtr src, int len);


        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyBGR24ToBGR32", SetLastError = false)]
        internal static extern void CopyBGR24ToBGR32([In, Out] BGR32[] dst, RGB[] src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyBGR24ToBGR32", SetLastError = false)]
        internal static extern void CopyBGR24ToBGR32IntPtr([In, Out] byte[] dst, IntPtr src, int len);


        /// <summary>
	    /// Kopiruje pole bajtu, ze zdroje src kopiruje do dst.
        /// Src a dst se ensmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "Copy", SetLastError = false)]
        internal static extern void CopyByte([In, Out] byte[] dst, byte[] src, int len);

        /// <summary>
	    /// Kopiruje pole bajtu, ze zdroje src kopiruje do dst.
        /// Src a dst se ensmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "Copy", SetLastError = false)]
        internal static extern void CopyIntPtr([In, Out] byte[] dst, IntPtr src, int len);


        /// <summary>
        /// bacha neni naimplementovano v ASM
        /// segmentace zalozena na rozdilu z dvou pixelu, ktere maji vzdalenost v z>konst. Pak jete je nutna smernice >konst
        /// </summary>
        /// <param name="dst">Cilove pole, musi byt velikosti len</param>
        /// <param name="len">Pocet zpracovavanych pixelu = width*height</param>
        /// <param name="width">Sirka radku v pixelech</param>
        /// <param name="worldPoints">XYZ souradnice boduv prostoru. Prvni radek odpovida pixelum nejbliz robotu tj. spodni radek kamery. Delka poje je len.</param>
        /// <returns>Vraci pocet zapsanych pixelu do dst</returns>
        [DllImport("NativeLib.dll", EntryPoint = "SegmentNew2Impl", SetLastError = false)]
        internal static extern int SegmentNew2Impl([In, Out] Point4D[] dst, int len, int width, [In] Point4D[] worldPoints);


#else
        [DllImport("NativeLib.dll", EntryPoint = "ComputeAlloc", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ComputeAlloc(int maxPoints, int width, int height, int xOff, int yOff, float resolution);
        [DllImport("NativeLib.dll", EntryPoint = "ComputeFree", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ComputeFree(IntPtr ci);

        [DllImport("NativeLib.dll", EntryPoint = "Segment2", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Segment2(IntPtr ci, byte[] leftDist, float[] leftTransformMatrix, Point2DF[,] leftTransform,
            byte[] rightDist, float[] rightTransformMatrix, Point2DF[,] rightTransform,
            float[] globalTransformMatrix,
            int len, float maxZ);

        [DllImport("NativeLib.dll", EntryPoint = "BackProjectImpl", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BackProject(byte[] probability, byte[] imgData, byte[] backProjectTab, int len);
        [DllImport("NativeLib.dll", EntryPoint = "BackProjectBGR32Impl", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BackProjectBGR32(byte[] probability, byte[] imgData, byte[] backProjectTab, int len);

        [DllImport("NativeLib.dll", EntryPoint = "FindPathEdge", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FindPathEdge([In, Out] PathEdgeItem[] dst, byte[] probability, int width, int height);

        [DllImport("NativeLib.dll", EntryPoint = "TestCopy", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int TestCopy(byte[] i, byte[] o, int mode, int cnt);
        [DllImport("NativeLib.dll", EntryPoint = "TestCopy", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int TestCopy2(IntPtr i, IntPtr o, int mode, int cnt);
        [DllImport("NativeLib.dll", EntryPoint = "Test2", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Test2();

        /// <summary>
        /// Alokuje blok pameti
        /// </summary>
        /// <param name="len">delka bloku v bajtech</param>
        /// <returns></returns>
        [DllImport("NativeLib.dll", EntryPoint = "Alloc", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Alloc(Int32 len);
        /// <summary>
        /// Uvolnuje blok pameti
        /// </summary>
        /// <param name="ptr">pointer na blok pameti, ktery ma byt ovolnen, puvodne vracena hodnota metodou Alloc</param>
        [DllImport("NativeLib.dll", EntryPoint = "Free", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(IntPtr ptr);

        /// <summary>
        /// extrahuje z agregacniho pole prekazky
        /// </summary>
        /// <param name="ais">Pole agregacnich bodu</param>
        /// <param name="uais">Pole offsetu na pouzite AggregateItem</param>
        /// <param name="len">Pocet zaznamu v uias</param>
        /// <param name="ops">Pole prekazek</param>
        /// <param name="minCount">Minimalni pocet pointu z kterych bylo agregovano tj. AggregateItem.Count</param>
        /// <param name="minStd2">Minimalni hodnota rozptylu</param>
        /// <returns></returns>
        [DllImport("NativeLib.dll", EntryPoint = "ExtractObstaclesImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ExtractObstaclesImpl(AggregateItem[] ais, Int32[] uais, Int32 len, [In, Out] Point4D[] ops, float minCount, float minStd2);

        /// <summary>
        /// Inizializuje pouzite agregacni itemy nastavenim Count na 0
        /// </summary>
        /// <param name="uais">Pole offseru pouzitych AggregateItem</param>
        /// <param name="cnt">Pocet prvku v uias</param>
        [DllImport("NativeLib.dll", EntryPoint = "ClearAggregateImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ClearAggregateImpl([In, Out] AggregateItem[] ais, Int32[] uias, Int32 cnt);

        /// <summary>
        /// Agreguje body sveta v rovine x,y pro budouci extrakci prekazek. 
        /// </summary>
        /// <param name="wordPoints">pole bodu sveta v metrech</param>
        /// <param name="wordPointsCount">Pocet bodu v poli wordPoints</param>
        /// <param name="r">rozliseni pro agregaci</param>
        /// <param name="xOff">posunuti v agregacnim poli ais</param>
        /// <param name="yOff">posunuti v agregacnim poli ais</param>
        /// <param name="ais">pole agregacnich bodu o velikosti width*height</param>
        /// <param name="uais">pole offsetu na pouzite agregacni body o velikosti width*height</param>
        /// <param name="width">sirka (odpovida x) agregacniho pole</param>
        /// <param name="height">vyska (odpovida y) agregacniho pole</param>
        /// <param name="v">rovnice roviny po ktere robot jede, vznika regresi z bodu v okoli robotu, slouzi pro upravu z souradnice agregovaneho bodu z' = v.x * p.x + v.y * p.y + v.z * p.z + v.a * p.a; </param>
        /// <returns>pocet obsazenych agregacnich bodu</returns>
        [DllImport("NativeLib.dll", EntryPoint = "AggregateObstacles", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int AggregateObstacles(IntPtr wordPoints, int wordPointsCount, double r, int xOff, int yOff, IntPtr ais, IntPtr uais, int width, int height, Point4D v);
        [DllImport("NativeLib.dll", EntryPoint = "AggregateObstaclesImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int AggregateObstaclesImpl(Point4D[] wordPoints, Int32 wordPointsCount, float r, Int32 xOff, Int32 yOff, [In, Out] AggregateItem[] ais, [In, Out] Int32[] uais, Int32 width, Int32 height, Point4D v);

        // z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery(x - roste doprava, y - roste dolu a z od kamery)
        // nasledne bod pootoci v prostoru pomoci rotate
        // transform je pole vektoru xy, plati xyz = [x*dist, y*dist, dist], pole transform a dist obsahuje len prvku,
        // hodnoty 0 a -1 v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
        // funkce vraci pocet zapsanych zaznamu do dst
        [DllImport("NativeLib.dll", EntryPoint = "DepthTransformImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DepthTransformImpl([In, Out] Point4D[] dst, Point2DF[] transform, float[] rotate, short[] dist, int len);

        /// <summary>
        /// z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery, pote pootoci pomoci rotate.
        /// dst[i] = [transform[i].x * dist[i], transform[i].y * dist[i], dist[i]] * rotate; data se ukladaji v opacnem poradi.
        /// (deklarace i pro non-x64/ARM - funkce je exportovana i v asm_linux_arm64.S a pouziva ji SegmentNew*)
        /// </summary>
        [DllImport("NativeLib.dll", EntryPoint = "DepthTransform2Impl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DepthTransform2Impl([In, Out] Point4D[] dst, Point2DF[,] transform, float[] rotate, byte[] dist, int len);





        /// <summary>
        /// Pole vektoru src vynasobi matici transform a vysledek ulozi do dst
        /// dst=transform*src
        /// </summary>
        /// <param name="dst">Cilove pole</param>
        /// <param name="rotate">Pole transformacni matice float[16]. V matici na pozici 0 je prvni radek prvni sloupec, na pozici 1 je prvni radek druhy sloupec, ....</param>
        /// <param name="src">Zdrojove pole</param>
        /// <param name="len">Delak pole dst a src</param>
        [DllImport("NativeLib.dll", EntryPoint = "TransformPoint4DImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void TransformPoint4DImpl([In, Out]Point4D[] dst, float[] rotate, Point4D[] src, int len);


        /// <summary>
        /// Z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery)
        /// hodnoty 0 a -1 v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
        /// hodnoty do dst se ukladaji od konce dist
        /// </summary>
        /// <param name="dst">Vysledne pole xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery) v metrech. xyz[j]=(trasform[i].x*dist[i], trasform[i].y*dist[i], dist[i]) </param>
        /// <param name="dist">Pole vzdalenosti v mm</param>
        /// <param name="transform">Pole popisujici kameru. Pro kazdy bod kamery obsahuje promitnuti do plochy XY</param>
        /// <param name="len">Delky poli dist a transform</param>
        /// <returns>Pocet zapsanych bodu do dst</returns>
        [DllImport("NativeLib.dll", EntryPoint = "Depth2XYZImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Depth2XYZImpl([In, Out]Point4D[] dst, short[] dist, Point2DF[] transform, int len);

        /// <summary>
        /// Resetuje agrgovane udaje ve vypoctu aproximace bodu rovinou
        /// </summary>
        /// <param name="ptr">pointer na blok pameti, ktery ma byt ovolnen, puvodne vracena hodnota metodou Alloc</param>
        [DllImport("NativeLib.dll", EntryPoint = "ResetPlaneParams", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ResetPlaneParams([In, Out] ref PlaneParams pars);

        /// <summary>
        /// Prepocte agregovane hodnoty pro prolozeni bodu rovinou na rovinu
	    /// vysledkem bude nastaveni atributu v
        /// </summary>
        /// <param name="pars"></param>
        [DllImport("NativeLib.dll", EntryPoint = "CalcPlaneParams", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CalcPlaneParams([In, Out] ref PlaneParams pars);


        /// <summary>
        /// Prolozi rovinou mnozinu bodu jejichz abs(z) je mensi jak MaxZ.
        /// z=a*x+b*y+d
        /// Lepe receno spocte parametry pro vypocet prolozeni
        /// </summary>
        /// <param name="param">Struktura popisujici prolozeni rovinou </param>
        /// <param name="src">Prokladane body</param>
        /// <param name="maxZ">Maximalni hodnota abs(z) aby byl bod pouzit pro vypocet prolozeni</param>
        /// <param name="len">Pocet bodu v poli src</param>
        [DllImport("NativeLib.dll", EntryPoint = "XYZ2PlaneImpl", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void XYZ2PlaneImpl([In, Out] ref PlaneParams param, Point4D[] src, float maxZ, int len);


        /// <summary>
	    /// Kopiruje pole bajtu, ze zdroje src kopiruje do dst.
        /// Src a dst se ensmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "Copy", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CopyByte([In, Out] byte[] dst, byte[] src, int len);

        /// <summary>
	    /// Kopiruje pole bajtu, ze zdroje src kopiruje do dst.
        /// Src a dst se ensmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "Copy", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CopyIntPtr([In, Out] byte[] dst, IntPtr src, int len);

        /// <summary>
        /// Reverzuje pole Int16, ze zdroje src kopiruje do dst
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseInt16", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ReverseInt16([In, Out] Int16[] dst, Int16[] src, int len);

        /// <summary>
        /// Reverzuje pole Int16, ze zdroje src kopiruje do dst
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych in16</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseInt16", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ReverseInt16IntPtr([In, Out] byte[] dst, IntPtr src, int len);


        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyRGB24ToBGR32", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CopyRGB24ToBGR32([In, Out] BGR32[] dst, RGB[] src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyRGB24ToBGR32", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CopyRGB24ToBGR32IntPtr([In, Out] byte[] dst, IntPtr src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyBGR24ToBGR32", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CopyBGR24ToBGR32([In, Out] BGR32[] dst, RGB[] src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "CopyBGR24ToBGR32", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void CopyBGR24ToBGR32IntPtr([In, Out] byte[] dst, IntPtr src, int len);


        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32 v reverznim poradi
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseRGB24ToBGR32", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ReverseRGB24ToBGR32([In, Out] BGR32[] dst, RGB[] src, int len);

        /// <summary>
	    /// Kopiruje pole RGB do pole BGR32 v reverznim poradi
        /// Src a dst se nesmi prekryvat
        /// </summary>
        /// <param name="dst">Cil kopirovani</param>
        /// <param name="src">Zdroj kopirovani</param>
        /// <param name="len">Pocet komirovanych bajtu</param>
        [DllImport("NativeLib.dll", EntryPoint = "ReverseRGB24ToBGR32", SetLastError = false, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ReverseRGB24ToBGR32IntPtr([In, Out] byte[] dst, IntPtr src, int len);





        /*
                        //Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
                        internal static extern void BackProjectImpl(char* probability, BGR* img, char* backProjectTab, int len);
                        */


#endif
        }

        // Verejne wrappery nad P/Invoke deklaracemi v NativeMethods.
        // Zachovavaji puvodni verejne API (volani NativeComputeUnit.X z jinych assembly).
        public static void Test2() => NativeMethods.Test2();

        public static int AggregateObstaclesImpl(Point4D[] wordPoints, Int32 wordPointsCount, float r, Int32 xOff, Int32 yOff, AggregateItem[] ais, Int32[] uais, Int32 width, Int32 height, Point4D v)
            => NativeMethods.AggregateObstaclesImpl(wordPoints, wordPointsCount, r, xOff, yOff, ais, uais, width, height, v);

        public static void XYZ2PlaneImpl(ref PlaneParams param, Point4D[] src, float maxZ, int len)
            => NativeMethods.XYZ2PlaneImpl(ref param, src, maxZ, len);

        public static void CalcPlaneParams(ref PlaneParams pars) => NativeMethods.CalcPlaneParams(ref pars);

        public static void ClearAggregateImpl(AggregateItem[] ais, Int32[] uias, Int32 cnt) => NativeMethods.ClearAggregateImpl(ais, uias, cnt);

        public static int Depth2XYZImpl(Point4D[] dst, short[] dist, Point2DF[] transform, int len)
            => NativeMethods.Depth2XYZImpl(dst, dist, transform, len);

        public static int DepthTransformImpl(Point4D[] dst, Point2DF[] transform, float[] rotate, short[] dist, int len)
            => NativeMethods.DepthTransformImpl(dst, transform, rotate, dist, len);

        /// <summary>
        /// Nativni SIMD depth-&gt;pointcloud. <paramref name="dist"/> je surovy Gray16 obraz (mm),
        /// prevod mm-&gt;m je uvnitr. Vystup <paramref name="dst"/> je v OPACNEM poradi oproti pixelum
        /// (dst[len-1-p] = bod pixelu p); nezmerene pixely = [0,0,0,0].
        /// </summary>
        public static int DepthTransform2Impl(Point4D[] dst, Point2DF[,] transform, float[] rotate, byte[] dist, int len)
            => NativeMethods.DepthTransform2Impl(dst, transform, rotate, dist, len);

        public static int ExtractObstaclesImpl(AggregateItem[] ais, Int32[] uais, Int32 len, Point4D[] ops, float minCount, float minStd2)
            => NativeMethods.ExtractObstaclesImpl(ais, uais, len, ops, minCount, minStd2);

        public static void ResetPlaneParams(ref PlaneParams pars) => NativeMethods.ResetPlaneParams(ref pars);

        public static void TransformPoint4DImpl(Point4D[] dst, float[] rotate, Point4D[] src, int len)
            => NativeMethods.TransformPoint4DImpl(dst, rotate, src, len);

        public static void ReverseRGB24ToBGR32(BGR32[] dst, RGB[] src, int len) => NativeMethods.ReverseRGB24ToBGR32(dst, src, len);

        public static void ReverseRGB24ToBGR32IntPtr(byte[] dst, IntPtr src, int len) => NativeMethods.ReverseRGB24ToBGR32IntPtr(dst, src, len);

        public static void ReverseInt16(Int16[] dst, Int16[] src, int len) => NativeMethods.ReverseInt16(dst, src, len);

        public static void ReverseInt16IntPtr(byte[] dst, IntPtr src, int len) => NativeMethods.ReverseInt16IntPtr(dst, src, len);

        public static void CopyRGB24ToBGR32(BGR32[] dst, RGB[] src, int len) => NativeMethods.CopyRGB24ToBGR32(dst, src, len);

        public static void CopyRGB24ToBGR32IntPtr(byte[] dst, IntPtr src, int len) => NativeMethods.CopyRGB24ToBGR32IntPtr(dst, src, len);

        public static void CopyByte(byte[] dst, byte[] src, int len) => NativeMethods.CopyByte(dst, src, len);

        public static void CopyIntPtr(byte[] dst, IntPtr src, int len) => NativeMethods.CopyIntPtr(dst, src, len);

            /// <summary>
            /// rozliseni agregacniho pole
            /// </summary>
        public float AggregateResolution { get; private set; }

        private BackProject BackProjectData { get; set; }

        private IntPtr computeInfoPtr;
        ComputeInfo? computeInfo;
        public ComputeInfo ComputeInfo
        {
            get
            {
                if (computeInfo == null)
                    computeInfo = (ComputeInfo)Marshal.PtrToStructure(computeInfoPtr, typeof(ComputeInfo));
                return computeInfo.Value;
            }
        }

        public PlaneParams LeftCameraParams => ComputeInfo.LeftCameraParams;
        public PlaneParams RightCameraParams => ComputeInfo.RightCameraParams;
        // pocet bodu v poli WordPoints
        public int WordPointsCount => ComputeInfo.WordPointsCount;

        Point4D[] obstaclePoints;
        /// <summary>
        /// Body prekazek  - xyz body v orientaci kamery tj. podle left/right TransformMatrix - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        public Point4D[] ObstaclePoints
        {
            get
            {
                if (obstaclePoints == null)
                {
                    var ci = ComputeInfo;
                    float[] f = new float[ci.ObstaclePointsCount * 4];
                    Marshal.Copy(ci.ObstaclePointsPtr, f, 0, ci.ObstaclePointsCount * 4);
                    Point4D[] o = new Point4D[ci.ObstaclePointsCount];
                    for (int i = 0; i < ci.ObstaclePointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    obstaclePoints = o;
                }
                return obstaclePoints;
            }
        }

        Point4D[] wordPoints;
        /// <summary>
        /// xyz body zhloubkoveho obrazku ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        public Point4D[] WordPoints
        {
            get
            {
                if (wordPoints == null)
                {
                    var ci = ComputeInfo;
                    float[] f = new float[ci.WordPointsCount * 4];
                    Marshal.Copy(ci.WordPointsPtr, f, 0, ci.WordPointsCount * 4);
                    Point4D[] o = new Point4D[ci.WordPointsCount];
                    for (int i = 0; i < ci.WordPointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    wordPoints = o;
                }
                return wordPoints;
            }
        }

        Point4D[] wordObstaclePoints;
        /// <summary>
        /// Body prekazek  - xyz body ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        public Point4D[] WordObstaclePoints
        {
            get
            {
                if (wordObstaclePoints == null)
                {
                    var ci = ComputeInfo;
                    if (ci.WordObstaclePointsCount == 0)
                        return ObstaclePoints;
                    float[] f = new float[ci.WordObstaclePointsCount * 4];
                    Marshal.Copy(ci.WordObstaclePointsPtr, f, 0, ci.WordObstaclePointsCount * 4);
                    Point4D[] o = new Point4D[ci.WordObstaclePointsCount];
                    for (int i = 0; i < ci.WordObstaclePointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    wordObstaclePoints = o;
                }
                return wordObstaclePoints;
            }
        }

        Point4D[] cameraPoints;
        public Point4D[] CameraPoints
        {
            get
            {
                if (cameraPoints == null)
                {
                    var ci = ComputeInfo;
                    float[] f = new float[ci.CameraPointsCount * 4];
                    Marshal.Copy(ci.CameraPointsPtr, f, 0, ci.CameraPointsCount * 4);
                    Point4D[] o = new Point4D[ci.CameraPointsCount];
                    for (int i = 0; i < ci.CameraPointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    cameraPoints = o;
                }
                return cameraPoints;
            }
        }

        private AggregateItem[,] aggregateItems;
        public AggregateItem[,] AggregateItems
        {
            get
            {
                if (aggregateItems == null)
                {
                    var ci = ComputeInfo;
                    var size = Marshal.SizeOf(typeof(AggregateItem));

                    aggregateItems = new AggregateItem[ci.Width, ci.Height];

                    for (int x = 0; x < ci.Width; x++)
                    {
                        for (int y = 0; y < ci.Height; y++)
                        {
                            IntPtr ins = new IntPtr(ci.AggregatesPtr.ToInt64() + (x + y * ci.Width) * size);
                            aggregateItems[x, y] = Marshal.PtrToStructure<AggregateItem>(ins);
                        }
                    }
                }
                return aggregateItems;
            }
        }

        public AggregateItem? GetAggregateItem(int x, int y)
        {
            var ci = ComputeInfo;
            x += ci.xOff;
            y += ci.yOff;
            if (x < 0 || y < 0 || x >= ci.Width || y >= ci.Height)
                return null;
            int idx = x + y * ci.Width;
            return (AggregateItem)Marshal.PtrToStructure(IntPtr.Add(ci.AggregatesPtr, idx * Marshal.SizeOf(typeof(AggregateItem))), typeof(AggregateItem));
        }
        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="maxPoints">Pocet pixelu depth kamery</param>
        /// <param name="width">sirka agrekacniho pole</param>
        /// <param name="height">vyska agregacniho pole</param>
        /// <param name="xOff">x posun stredu agregacniho pole, typicky width/2</param>
        /// <param name="yOff">y posun stredu agregacniho pole, typicky height/2</param>
        /// <param name="aggregateResolution">rozliseni agregacniho pole v metrech</param>
        /// <param name="backProject"></param>
        public NativeComputeUnit(int maxPoints, int width, int height, int xOff, int yOff, float aggregateResolution, BackProject backProject)
        {
            AggregateResolution = aggregateResolution;
            computeInfoPtr = NativeMethods.ComputeAlloc(maxPoints, width, height, xOff, yOff, aggregateResolution);
            BackProjectData = backProject;
            dist2Cnt.Clear();
            dist2Cnt.Add(0, 12);
            dist2Cnt.Add(1, 12);
            dist2Cnt.Add(2, 12);
            dist2Cnt.Add(3, 12);
            dist2Cnt.Add(4, 12);
            dist2Cnt.Add(5, 12);
            dist2Cnt.Add(6, 12);    
            dist2Cnt.Add(7, 12);
            dist2Cnt.Add(8, 11);
            dist2Cnt.Add(9, 10);
            dist2Cnt.Add(10, 9);
            dist2Cnt.Add(11, 8);
            dist2Cnt.Add(12, 7);
            dist2Cnt.Add(13, 6);
            dist2Cnt.Add(14, 5);
            dist2Cnt.Add(15, 5);
            dist2Cnt.Add(16, 4);
            dist2Cnt.Add(17, 4);
            dist2Cnt.Add(18, 4);
            dist2Cnt.Add(19, 4);
            dist2Cnt.Add(20, 4);
            dist2Cnt.Add(21, 3);
            dist2Cnt.Add(22, 3);
            dist2Cnt.Add(23, 3);
            dist2Cnt.Add(24, 3);
            dist2Cnt.Add(25, 3);
            dist2Cnt.Add(26, 3);
            dist2Cnt.Add(27, 2);
            dist2Cnt.Add(28, 2);
            dist2Cnt.Add(29, 2);
            dist2Cnt.Add(30, 2);
            dist2Cnt.Add(31, 2);
            dist2Cnt.Add(32, 2);
            dist2Cnt.Add(33, 2);
            dist2Cnt.Add(34, 2);
            dist2Cnt.Add(35, 2);
            dist2Cnt.Add(36, 2);
            dist2Cnt.Add(37, 2);
            dist2Cnt.Add(38, 2);
            dist2Cnt.Add(39, 2);
            dist2Cnt.Add(40, 2);
            dist2Cnt.Add(41, 2);
            dist2Cnt.Add(42, 2);
            dist2Cnt.Add(43, 1);
            dist2Cnt.Add(44, 1);
            dist2Cnt.Add(45, 1);
            dist2Cnt.Add(46, 1);
            dist2Cnt.Add(47, 1);
            dist2Cnt.Add(48, 1);
            dist2Cnt.Add(49, 1);
        }

        public static float[] Transformation(System.Numerics.Matrix4x4 m)
        {
            float[] l = new float[16];

            l[0] = (float)m.M11;
            l[1] = (float)m.M12;
            l[2] = (float)m.M13;
            l[3] = (float)m.M14;

            l[4] = (float)m.M21;
            l[5] = (float)m.M22;
            l[6] = (float)m.M23;
            l[7] = (float)m.M24;

            l[8] = (float)m.M31;
            l[9] = (float)m.M32;
            l[10] = (float)m.M33;
            l[11] = (float)m.M34;

            l[12] = (float)m.M41;
            l[13] = (float)m.M42;
            l[14] = (float)m.M43;
            l[15] = (float)m.M44;

            return l;
        }

        /// <summary>
        /// Hleda prekazky v hloubkovem obraze.
        /// </summary>
        /// <param name="leftImage">Levy hloubkovy obraz</param>
        /// <param name="leftProjection">Leva hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.</param>
        /// <param name="rightImage">Pravy hloubkovy obraz</param>
        /// <param name="rightProjection">Prava hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.</param>
        /// <param name="globalTransform">Finalni pootoceni do svetovych souradnic.</param>
        public void Segment(Image<Gray16> leftImage, IDepthCameraProjection leftProjection, Image<Gray16> rightImage, IDepthCameraProjection rightProjection, System.Numerics.Matrix4x4 globalTransform)
        {
            float[] lt = Transformation(leftProjection.Transformation);
            Point2DF[,] lct = leftProjection.Camera2DToCamera3D;

            float[] rt = Transformation(rightProjection.Transformation);
            Point2DF[,] rct = rightProjection.Camera2DToCamera3D;

            float[] gt = Transformation(globalTransform);

            if(leftImage?.Data!=null)
                NativeMethods.Segment2(computeInfoPtr, leftImage?.Data, lt, lct, rightImage?.Data, rt, rct, gt, leftImage.Width * leftImage.Height, 0.1f);
            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;
        }

        /// <summary>
        /// Hleda prekazky v hloubkovem obraze.
        /// </summary>
        /// <param name="image">Hloubkovy obraz</param>
        /// <param name="projection">Hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.
        /// Rotace kolem svisle osy je oddelena do globalTransform.
        /// </param>
        /// <param name="globalTransform">Finalni pootoceni do svetovych souradnic.</param>
        public void Segment(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform)
        {
             // extrakce ppomoci agregacniho pole
            float[] lt = Transformation(projection.Transformation);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            float[] gt = Transformation(globalTransform);

            if (image?.Data != null)
                NativeMethods.Segment2(computeInfoPtr, image?.Data, lt, lct, null, lt, lct, gt, image.Width * image.Height, 0.1f);
            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;
        }


        public void SegmentNew(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform)
        {
            float minZ2 = 0.01f * 0.01f;
            Common.Point[] nn = new Common.Point[4]
            {
                new Common.Point(-1, 0),
                new Common.Point(0, 1),
                new Common.Point(1, 0),
                new Common.Point(0, -1)
            };

            float[] lt = Transformation(projection.Transformation * globalTransform);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            //            float[] gt = Transformation(globalTransform);

            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;

            wordPoints = new Point4D[image.Width * image.Height];
            if (image?.Data != null)
                NativeMethods.DepthTransform2Impl(wordPoints, lct, lt, image?.Data, image.Width * image.Height);

            float resolution = 0.1f;
            int w = 160;
            int h = 160;
            var aa = new AggregateItem[w, h];
            aggregateItems = aa;

            int x1, y1;

            Point4D p;
            for (int x = 10; x < image.Width-10; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    int idx = x + image.Width * y;
                    p = wordPoints[idx];
                    if (p.A == 1)
                    {
                        x1 = (int)(p.X / resolution + 0.5) + w / 2;
                        y1 = (int)(p.Y / resolution + 0.5) + h / 2;
                        if (x1 >= 0 && x1 < w && y1 >= 0 && y1 < h)
                        {
                            aa[x1, y1].Count++;
                            aa[x1, y1].SumX += p.X;
                            aa[x1, y1].SumY += p.Y;
                            aa[x1, y1].SumZ += p.Z;
                            aa[x1, y1].SumZ2 += p.Z*p.Z;
                        }
                    }
                }
            }

            var l = new List<Point4D>();
            int r = 6;
            AggregateItem a1, a2;
            double max = 0, v;
            List<Point4D> pts = new List<Point4D>();
            for (int x = w/2-r; x <= w/2+r; x++)
            {
                for (int y = h/2-r; y <= h/2+r; y++)
                {
                    a1 = aa[x, y];
                    if (a1.Count > 0)
                    {
                        var pp1 = a1.ToPoint4D();
                        if(Math.Abs(pp1.Z)<0.05)
                            pts.Add(pp1);
                    }
                }
            }

            PlaneParams pPars = new PlaneParams(pts);

            for (int x = 1; x < w-1; x++)
            {
                for (int y = 1; y < h-1; y++)
                {
                    a1 = aa[x, y];
                    if (a1.Count > 0)
                    {
                        var pp1 = a1.ToPoint4D();
                        pp1.Z = pp1 * pPars.v; // vzdalenost od roviny, neni to uplne presny
                        var d = Math.Sqrt(pp1.X * pp1.X + pp1.Y * pp1.Y);
                        if(Math.Abs(pp1.Z)>0.02+d/20)
                            l.Add(pp1);
/*                        else if (a1.Count > 20)
                        {
                            max = 0;
                            foreach (var pp in nn)
                            {
                                x1 = x + pp.X;
                                y1 = y + pp.Y;
                                a2 = aa[x1, y1];
                                if (a2.Count > 20)
                                {
                                    var pp2 = a2.ToPoint4D();
                                    pp2.Z = pp2 * pPars.v; // vzdalenost od roviny, neni to uplne presny
                                    pp2 = pp1 - pp2;
                                    v = Math.Abs(pp2.Z) / Math.Sqrt(Math.Pow(pp2.X, 2) + Math.Pow(pp2.Y, 2)); // stoupani
                                    if (v > max)
                                        max = v;
                                }
                            }

                            if (max > 0.2)
                                l.Add(pp1);
                        }*/
                    }
                    else
                    {
//                        l.Add(new Point4D() {X= (float)(x - w / 2 - 0.5) * resolution, Y= (float)(y - h / 2 - 0.5) * resolution, Z=0, A=1 });
                    }
                }
            }
            wordObstaclePoints = l.ToArray();
        }

        // segmentace stoupani mezi pixely vzdalenymi aspon 10cm
        public void SegmentNew1(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform)
        {
            float[] lt = Transformation(projection.Transformation * globalTransform);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            //            float[] gt = Transformation(globalTransform);

            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;

            wordPoints = new Point4D[image.Width * image.Height];
            if (image?.Data != null)
            {
                NativeMethods.DepthTransform2Impl(wordPoints, lct, lt, image?.Data, image.Width * image.Height);
            }

//            var s = String.Join("\r\n", wordPoints.Where(xx => xx.A == 1).Select(xx => $"{xx.X}\t{xx.Y}\t{xx.Z}"));

            var l = new List<Point4D>();
            Point4D p, p1;
            double dx, dy, dz, d, d1;
            double cnt=0;
            for (int x = 0; x < image.Width; x++)
            {
                for (int y = image.Height - 1, y1 = image.Height - 1; y >= 0; y--)
                {
                    int idx = x + image.Width * y;
                    p = wordPoints[idx];
                    if (p.A == 0)
                        continue;
                    d = p.X * p.X + p.Y * p.Y;
/*                    if (d > 100)
                        break;
*/
                    while (y1-y > 1)
                    {
                        idx = x + image.Width * y1;

                        p1 = wordPoints[idx];
                        if (p1.A == 0)
                        {
                            y1--;
                        }
                        else
                        {
                            d1 = p1.X * p1.X + p1.Y * p1.Y;

                            dx = p.X - p1.X;
                            dy = p.Y - p1.Y;
                            dz = p.Z - p1.Z;

                            dx *= dx;
                            dy *= dy;
                            dz *= dz;

                            dx = dx + dy + dz;
                            if (dx > 0.01)
                            {
                                y1--;
                                if (dz / dx > 0.04)
                                {
                                    cnt++;
                                    if (d1 < d)
                                        l.Add(p1);
                                    else
                                        l.Add(p);
                                }
                            }
                            else
                                break;
                        }
                    }
                }
            }
            wordObstaclePoints = l.ToArray();
        }
        // segmentace zalozena na rozdilu z dvou pixelu, ktere maji vzdalenost v z>konst. Pak jeste je nutna smernice >konst
        public void SegmentNew2(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform)
        {
            float zStepDown = -0.1F;
            float minZ1_2 = 0.02f;
            float minZ2_2 = 0.02f;
            float c1 = 0.25f;
            float r2 = 25;
            minZ1_2 = minZ1_2 * minZ1_2;
            minZ2_2 = minZ2_2 * minZ2_2;

            float[] lt = Transformation(projection.Transformation * globalTransform);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            //            float[] gt = Transformation(globalTransform);

            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;

            wordPoints = new Point4D[image.Width * image.Height];
            if (image?.Data != null)
                NativeMethods.DepthTransform2Impl(wordPoints, lct, lt, image?.Data, image.Width * image.Height);

            var l = new List<Point4D>();
            Point4D p=new Point4D();
            Point4D pLast;
            Point4D p1;
            float dz;
            float dz2;
            float dx;
            float dy;
            int y;
            int idx=0;
            int idx1;
            int idxMax = image.Height * image.Width;

            for (int x = 10; x < image.Width-10; x++)
            {
                for (idx=x; idx < idxMax; idx += image.Width)
                {
                    p = wordPoints[idx];
                    if (p.A == 1 || p.X * p.X + p.Y * p.Y > r2)
                    {
                        break;
                    }
                }
                pLast = p;
                p1 = p;
                for (idx1=idx; idx < idxMax; idx += image.Width)
                {
                    p = wordPoints[idx];
                    if (p.A == 1)
                    {
                        if (p.Z - pLast.Z < zStepDown)
                        {
                            l.Add(pLast);
                            break;
                        }
                        else
                        {
repeat_y1:
                            dx = p.X - p1.X;
                            dy = p.Y - p1.Y;
                            dz = p.Z - p1.Z;
                            dx *= dx;
                            dy *= dy;
                            dz *= dz;
                            if (dz >= minZ1_2)
                            {
                                dx += dy + dz;
                                if (dz / dx > c1)
                                {
                                    l.Add(p1);
                                    break;
                                }
                                do
                                {
                                    p1 = wordPoints[idx1];
                                    idx1 += image.Width;
                                }
                                while (p1.Z == 0);
                                goto repeat_y1;
                            }
                        }
                        if (p.X * p.X + p.Y * p.Y > r2)
                            break;
                        pLast = p;
                    }
                }
            }
            wordObstaclePoints = l.GroupBy(v => new { X = (int)(v.X * 10), Y = (int)(v.Y * 10) }).Where(g =>
            {
                var d = (int)Math.Sqrt(g.Key.X * g.Key.X + g.Key.Y + g.Key.Y);
                if (dist2Cnt.ContainsKey(d))
                    return g.Count() >= dist2Cnt[d];
                return false;
            }).Select(g => g.First()).ToArray();
        }

        //tohle je aktualne pouzivana detekce prekazek z 3d kamer
        //ma testovaci obdobu na Depth3DProfileTool, mely se upravovat spolecne
        public void SegmentNew3(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform)
        {
            float minZ = 0.04f;
            float minS = 0.2f;
            float rMax = 9;

            float[] lt = Transformation(projection.Transformation * globalTransform);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;

            wordPoints = new Point4D[image.Width * image.Height];
            if (image?.Data != null)
                NativeMethods.DepthTransform2Impl(wordPoints, lct, lt, image?.Data, image.Width * image.Height);


            var l = new List<Point4D>();
            Point4D p1;
            Point4D p2;
            Point4D p;
            Point4D d;
            float s;
            float az;
            float r;
            float rMin;
            int idxMax = (image.Height-1) * image.Width;
            int w = image.Width;

            for (int x = 10; x < image.Width - 10; x++)
            {
                p1 = wordPoints[x];
                p2 = p1;
                p = new Point4D();
                d = new Point4D();

//                r = p1.X * p1.X + p1.Y * p1.Y;
                r = p2.X * p2.X + p2.Y * p2.Y;
                rMin = rMax;

                for (int i1 = x, i2 = x; i1 < idxMax;)
                {
                    if (p1.A == 1 && p2.A == 1)
                    {
                        if (r < rMin)
                        {
                            d = p1 - p2;
                            az = Math.Abs(d.Z);
                            if (az > minZ)
                            {
                                //                            i2 = i1;
                                //                          p2 = p1;
                                s = az / d.Length;
                                if (s > minS)
                                {
                                    rMin = r;
                                    p = p2;
                                }
                                i2 += w;
                                p2 = wordPoints[i2];
                                r = p2.X * p2.X + p2.Y * p2.Y;
                            }
                            else
                            {
                                i1 += w;
                                p1 = wordPoints[i1];
//                                r = p1.X * p1.X + p1.Y * p1.Y;
                            }
                        }
                        else
                        {
                            i1 += w;
                            p1 = wordPoints[i1];
//                            r = p1.X * p1.X + p1.Y * p1.Y;
                        }
                    }
                    else
                    {
                        if (p1.A == 0)
                        {
                            i1 += w;
                            p1 = wordPoints[i1];
//                            r = p1.X * p1.X + p1.Y * p1.Y;
                        }
                        if (p2.A == 0)
                        {
                            i2 += w;
                            p2 = wordPoints[i2];
                            r = p2.X * p2.X + p2.Y * p2.Y;
                        }
                    }
                }
                if (rMin < rMax)
                    l.Add(p);
            }
            wordObstaclePoints = l.ToArray();

            //wordObstaclePoints = l.GroupBy(v => new { X = (int)(v.X * 10), Y = (int)(v.Y * 10) }).Where(g =>
            //{
            //    var d1 = (int)Math.Sqrt(g.Key.X * g.Key.X + g.Key.Y + g.Key.Y);
            //    if (dist2Cnt.ContainsKey(d1))
            //        return g.Count() >= dist2Cnt[d1];
            //    return false;
            //}).Select(g => g.First()).ToArray();
            wordObstaclePoints = l.GroupBy(v => new { X = (int)(v.X * 10), Y = (int)(v.Y * 10) }).Where(g =>
            {
                    return g.Count() >= 4;
            }).Select(g => g.First()).ToArray();
        }

        public void SegmentNew4(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform)
        {
            float minZ2 = 0.01f * 0.01f;

            float[] lt = Transformation(projection.Transformation * globalTransform);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            //            float[] gt = Transformation(globalTransform);

            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;

            wordPoints = new Point4D[image.Width * image.Height];
            if (image?.Data != null)
                NativeMethods.DepthTransform2Impl(wordPoints, lct, lt, image?.Data, image.Width * image.Height);

            var l = new List<Point4D>();
            Point4D p;
            for (int x = 0; x < image.Width; x++)
            {
                Point4D? p1 = null;

                for (int y = image.Height - 1; y >= 0; y--)
                {
                    int idx = x + image.Width * y;
                    p = wordPoints[idx];
                    if (p.A == 1)
                    {
                        if (p1 != null)
                        {
                            if (p.X * p.X + p.Y * p.Y < 25)
                            {
                                float dx = p.X - p1.Value.X;
                                float dy = p.Y - p1.Value.Y;
                                float dz = p.Z - p1.Value.Z;
                                dz *= dz;
                                if (dz > minZ2 || Math.Abs(p.Z) > 0.1)
                                {
                                    dx *= dx;
                                    dy *= dy;
                                    dx += dy;
                                    // bacha tohle jsou kvadraty 
                                    // 10% stoupani tj. 0.1^2 tj. 0.01
                                    if (dz / dx > 0.01)
                                    {
                                        l.Add(p1.Value);
                                    }
                                }
                            }
                        }
                        p1 = p;
                    }
                }
            }
            wordObstaclePoints = l.ToArray();
        }

        /// <summary>
        /// Za prekazky jsou pokladany body s z>zLimit a ve vzdalenosti r od robota, pouzito pro RoboOrientiering
        /// </summary>
        /// <param name="image"></param>
        /// <param name="projection"></param>
        /// <param name="globalTransform"></param>
        /// <param name="zLimit"></param>
        /// <param name="r2">kvadrat vzdalenosti ve kteme musi byt prekazka</param>
        public void SegmentNew5(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform, float zLimit, float r2)
        {
            float[] lt = Transformation(projection.Transformation * globalTransform);
            Point2DF[,] lct = projection.Camera2DToCamera3D;

            //            float[] gt = Transformation(globalTransform);

            computeInfo = null;
            obstaclePoints = null;
            wordPoints = null;
            wordObstaclePoints = null;
            cameraPoints = null;

            wordPoints = new Point4D[image.Width * image.Height];
            if (image?.Data != null)
                NativeMethods.DepthTransform2Impl(wordPoints, lct, lt, image?.Data, image.Width * image.Height);

            var l = new List<Point4D>();
            Point4D p;
            for (int x = 0; x < image.Width; x++)
            {
                for (int y = image.Height - 1; y >= 0; y--)
                {
                    int idx = x + image.Width * y;
                    p = wordPoints[idx];
                    if (p.A == 1)
                    {
                        if (p.X * p.X + p.Y * p.Y < r2)
                        {
                            if (p.Z* p.Z > zLimit)
                            {
                                l.Add(p);
                            }
                        }
                    }
                }
            }
            wordObstaclePoints = l.GroupBy(v => new { X = (int)(v.X * 10), Y = (int)(v.Y * 10) }).Where(g =>
            {
                return g.Count() >= 6;
            }).Select(g => g.First()).ToArray();
        }


        public void BackProject(Image<Gray> probability, Image<BGR> img, BackProject backProject)
        {
            if (probability.Width == img.Width && probability.Height == img.Height)
                NativeMethods.BackProject(probability.Data, img.Data, backProject.Data, probability.Width * probability.Height);
            else
                throw new Exception("Rozmery probability a img musibyt stejny.");
        }

        public void BackProject(Image<Gray> probability, Image<ARBot.Common.Common.BGR32> img, BackProject backProject)
        {
            if (probability.Width == img.Width && probability.Height == img.Height)
                NativeMethods.BackProjectBGR32(probability.Data, img.Data, backProject.Data, probability.Width * probability.Height);
            else
                throw new Exception("Rozmery probability a img musi byt stejny.");
        }

        public void BackProject(Image<Gray> probability, Image<ARBot.Common.Common.BGR32> img, byte[] backProject)
        {
            if (probability.Width == img.Width && probability.Height == img.Height)
                NativeMethods.BackProjectBGR32(probability.Data, img.Data, backProject, probability.Width * probability.Height);
            else
                throw new Exception("Rozmery probability a img musi byt stejny.");
        }


        public void Process(Image<Common.BGR32> srcImg, Image<Gray> destImg)
        {
            BackProject(destImg, srcImg, this.BackProjectData);
        }

        /// <summary>
        /// Hleda hranice cesty v pravdepodobnostnim obrazku
        /// </summary>
        /// <param name="image"></param>
        /// <param name="scaleX"></param>
        /// <param name="scaleY"></param>
        /// <returns></returns>
        public List<PathEdge> PathEdges(Image<Gray> image, double scaleX, double scaleY)
        {
            PathEdgeItem[] dst = new PathEdgeItem[image.Height];
            int cnt = NativeMethods.FindPathEdge(dst, image.Data, image.Width, image.Height);

            var l = new List<PathEdge>();

            for (int i = 0; i < cnt; i++)
                l.Add(new PathEdge() { Y =(int)(dst[i].Y*scaleY), Left = dst[i].Left != -1 ? (int)(dst[i].Left*scaleX) : (int?)null, Right = dst[i].Right != -1 ? (int)(dst[i].Right*scaleX) : (int?)null });
            return l;
        }

        ~NativeComputeUnit()
        {
            NativeMethods.ComputeFree(computeInfoPtr);
        }

        public void Test()
        {
            Int64 len = 1000000;
            Int64 cnt = 1000;
            byte[] i = new byte[len];
            byte[] o = new byte[len];

            IntPtr i1 = NativeMethods.Alloc((int)len +100)+8;
            IntPtr o1 = NativeMethods.Alloc((int)len +100)+8;

            StringBuilder sb = new StringBuilder();
            Stopwatch sw = new Stopwatch();

            for (int mode = 1; mode < 16; mode *= 2)
            {
                sw.Restart();
                for(int j=0;j<cnt;j++)
                    NativeMethods.TestCopy(i, o, mode, (int)len /mode);
                sw.Stop();
                sb.AppendLine(string.Format("mode={0}, ts={1}", mode, cnt*len / mode / sw.Elapsed.TotalSeconds ));

                sw.Restart();
                for (int j = 0; j < cnt; j++)
                    NativeMethods.TestCopy2(i1, o1, mode, (int)len / mode);
                sw.Stop();
                sb.AppendLine(string.Format("mode={0}, ts={1}", mode, cnt*len / mode / sw.Elapsed.TotalSeconds));

            }


            Debug.Write(sb.ToString());
        }
        public Size Size(int width, int height)
        {
            return new Size(width, height);
        }
    }
}
