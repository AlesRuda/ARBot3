using ARBot.Common.Navigations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Lidar
{
    /// <summary>
    /// Pomocne vypocty pro lidar
    /// </summary>
    public static class LidarExtensions
    {
        /// <summary>
        /// Pocita konvoluci v pode pos
        /// </summary>
        /// <param name="rays">Mereni lidaru</param>
        /// <param name="core">Konvolucni jadro</param>
        /// <param name="off">Index prvku konvolucniho jaadra, ktery poresponduje s prvkem pos mereni lidaru</param>
        /// <param name="pos">Pozice v merenich lidaru pro kterou se pocita konvoluce</param>
        /// <returns></returns>
        public static float? Conv(this IList<Ray> rays, float[] core, int off, int pos)
        {
            int cnt = rays.Count;
            float d = 0;
            for (int i = 0; i < core.Length; i++)
            {
                int j = i + pos - off;
                if (j < 0)
                    j += cnt;
                j=j % cnt;
                var v = rays[j].Distance;
                if (v == null)
                    return null;
                d += core[i] * (j >= 0 ? rays[j] : rays[j + cnt]).Distance.Value;
            }
            return d;
        }
        /// <summary>
        /// Konvoluce
        /// </summary>
        /// <param name="rays"></param>
        /// <param name="core"></param>
        /// <param name="off"></param>
        /// <returns></returns>
        public static IList<Ray> Conv(this IList<Ray> rays, float[] core, int off)
        {
            int cnt = rays.Count;
            for (int i = 0; i < cnt; i++)
            {
                Ray r = rays[i];
                r.Diff = Conv(rays, core, off, i);
            }
            return rays;
        }
    }
}
