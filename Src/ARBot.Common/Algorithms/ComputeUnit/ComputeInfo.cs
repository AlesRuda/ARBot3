using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.ComputeUnit
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ComputeInfo
    {
        public PlaneParams LeftCameraParams;
        //parametry prolozene roviny pravou kamerou 
        public PlaneParams RightCameraParams;
        // maximalni delka pole CameraPoints
        public int MaxCameraPoints;
        //xyz body z kamery (x - doleva, y - roste smerem dolu a z roste smerem od kemery)
        public IntPtr CameraPointsPtr;
        // pocet bodu v poli CameraPoints
        public int CameraPointsCount;
        //xyz body ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
        public IntPtr WordPointsPtr;
        // pocet bodu v poli WordPoints
        public int WordPointsCount;
        /// <summary>
        /// Body prekazek  - xyz body v orientaci kamer - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        public IntPtr ObstaclePointsPtr;
        // pocet bodu v poli ObstaclePoints
        public int ObstaclePointsCount;
        /// <summary>
        /// body prekazek  - xyz body ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        public IntPtr WordObstaclePointsPtr;
        // pocet bodu v poli WordObstaclePoints
        public int WordObstaclePointsCount;
        //Sirka
        public int Width;
        //Vyska
        public int Height;
        // posunuti v agregacnim poli v ose x
        public int xOff;
        // posunuti v agregacnim poli v ose y
        public int yOff;
        // rozliseni agregacniho pole
        public float Resolution;
        // agregacni pole o velikosti Width*Height
        public IntPtr AggregatesPtr;
        // pocet bodu v poli Aggregates
        public int AggregatesCount;
    }
}
