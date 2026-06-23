using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Pomocne vypocty pro Ray a RayEx
    /// </summary>
    public static class RayExtensions
    {
        /// <summary>
        /// Konvertuje na list RayEx.
        /// Provadi korekce na otoceni robotu v case podle zaznamu v historii stavu.
        /// </summary>
        /// <param name="rays">Mereni lidaru</param>
        /// <param name="current">Aktualni stav robota</param>
        /// <param name="history">Historie stavu</param>
        /// <param name="angleOff">Mereni lidaru</param>
        /// <returns></returns>
        public static IList<RayEx> ToRayEx(this IList<Ray> rays, IModelState current, ModelStateHistory history, Vector3D lidarOffset)
        {
            int cnt = rays.Count();
            List<RayEx> ret = new List<RayEx>();
            for (int i = 0; i < cnt; i++)
            {
                Ray r = rays[i];
                RayEx re = new RayEx() { Ray = r };

                double angle = Math.PI/2;

                Vector3D robotOrigin = new Vector3D(0, 0, 0);
                angle = 0;

                if (history != null)
                {
                    IModelState ms = history[r.TimeStamp];
//                    Debug.WriteLine(ms.Orientation);
                    angle = ms.Orientation;
                }

                var q = new Quaternion(new Vector3D(0, 0, 1), Conversions.Rad2Deg(angle));
                var m = Matrix3D.Identity;
                m.Rotate(q);
                var v = m.Transform(lidarOffset);

                re.OriginalLidar = robotOrigin + v;

                if (r.Distance != null)
                {
                    Vector3D vv= new Vector3D(r.Distance.Value * Math.Cos(angle + r.Angle), r.Distance.Value * Math.Sin(angle + r.Angle), 0);
                    re.Target = re.OriginalLidar + vv;
                    re.LocalTarget = re.Target - robotOrigin;
                    double x = re.Target.Value.X - current?.X??0;
                    double y = re.Target.Value.Y - current?.Y??0;
                    re.Distance = Math.Sqrt(x*x+y*y);
                    re.Angle = Math.Atan2(y, x);
                    re.LocalAngle = re.Angle - current?.Orientation ?? (Math.PI / 2);
                }
                else
                    re.Target = null;
                ret.Add(re);
            }

            return ret;
        }

        /// <summary>
        /// Konvertuje kolekci RayEx na kolekci VFHPlusItem
        /// </summary>
        /// <param name="rays"></param>
        /// <returns></returns>
        public static IEnumerable<VFHPlusItem> ToVFHPlus(this IList<RayEx> rays)
        {
            return rays.Where((i) => i.Distance != null).Select((i) => new VFHPlusItem() { Coeficient = 1, Beta = i.Angle-Math.PI/2, Distance = i.Distance.Value });
        }

        /// <summary>
        /// Konvertuje kolekci Ray na kolekci VFHPlusItem
        /// </summary>
        /// <param name="rays"></param>
        /// <param name="off">Pootoceni lidaru v matematickem smeru. 0 pred lidarem.</param>
        /// <returns></returns>
        public static IEnumerable<VFHPlusItem> ToVFHPlus(this IList<Ray> rays, double off)
        {
            return rays.Where((i) => i.Distance != null).Select((i) => new VFHPlusItem() { Coeficient = 1, Beta = i.Angle + off, Distance = i.Distance.Value });
        }

        /// <summary>
        /// Konvertuje na log message.
        /// </summary>
        /// <param name="rays"></param>
        /// <returns></returns>
        public static Lidar ToLogMessage(this IList<Ray> rays)
        {
            Lidar lidar = new Lidar("Ray");
            lidar.Count = rays.Count;
            lidar.Angle = rays.Select((r)=>r.Angle).ToArray();
            lidar.Distance = rays.Select((r)=>r.Distance).ToArray();

            return lidar;
        }

        /// <summary>
        /// Konvertuje na log message.
        /// </summary>
        /// <param name="rays"></param>
        /// <returns></returns>
        public static Lidar ToLogMessage(this IList<RayEx> rays)
        {
            Lidar lidar = new Lidar("RayEx");
            lidar.Count = rays.Count;
            lidar.Angle = rays.Select((r) => r.Angle-Math.PI/2).ToArray();
            lidar.Distance = rays.Select((r) => r.Distance).ToArray();

            return lidar;
        }


        /// <summary>
        /// Pocita konvoluci v pode pos
        /// </summary>
        /// <param name="rays">Mereni lidaru</param>
        /// <param name="core">Konvolucni jadro</param>
        /// <param name="off">Index prvku konvolucniho jaadra, ktery poresponduje s prvkem pos mereni lidaru</param>
        /// <param name="pos">Pozice v merenich lidaru pro kterou se pocita konvoluce</param>
        /// <param name="nullAsZero"></param>
        /// <returns>Hodnota konvoluce</returns>
        public static double? Conv(this IList<RayEx> rays, double[] core, int off, int pos, bool nullAsZero)
        {
            int cnt = rays.Count;
            double d = 0;
            for (int i = 0; i < core.Length; i++)
            {
                int j = i + pos - off;
                if (j < 0)
                    j += cnt;
                j = j % cnt;
                var v = (j >= 0 ? rays[j] : rays[j + cnt]).Distance;
                if (v == null)
                    if (nullAsZero)
                        v = 0;
                    else
                        return null;
                d += core[i] * v.Value;
            }
            return d;
        }

        /// <summary>
        /// konvoluce
        /// </summary>
        /// <param name="rays"></param>
        /// <param name="core"></param>
        /// <param name="off"></param>
        /// <param name="diffIgnoreNull"></param>
        /// <returns></returns>
        public static Dictionary<RayEx, double?> Conv(this IList<RayEx> rays, double[] core, int off, bool diffIgnoreNull)
        {
            int i = 0;
            return rays.ToDictionary((r) => r, (r)=>Conv(rays, core, off, i++, diffIgnoreNull));
        }

    }
}
