using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Coordinates
{
    public class GreatCircle
    {
        /// <summary>
        /// Polomer zemekoule v m
        /// </summary>
        private double R = 6371000;
        public double Distance(LLA from, LLA to)
        {
            var a = Math.Pow(Math.Sin((to.Latitude- from.Latitude) / 2), 2) + Math.Cos(from.Latitude) * Math.Cos(to.Latitude) * Math.Pow(Math.Sin((to.Longitude- from.Longitude) / 2), 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0-a));
            return R * c;
        }
        public double Bearing(LLA from, LLA to)
        {
            return Math.Atan2(Math.Sin( to.Longitude- from.Longitude) * Math.Cos(to.Latitude), Math.Cos(from.Latitude) * Math.Sin(to.Latitude) - Math.Sin(from.Latitude) * Math.Cos(to.Latitude) * Math.Cos(to.Longitude- from.Longitude));
        }
    }
}
