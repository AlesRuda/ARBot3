using ARBot.Common.Logs;
using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.LocalMaps
{
    public class GraphMapBase
    {
        public int MaxIteration = 100;
        /// <summary>
        /// Stavove body
        /// </summary>
        public List<ICPStatePoint> States { get; protected set; } = new List<ICPStatePoint>();

        /// <summary>
        /// Aktualizuje mapu novymi daty
        /// </summary>
        /// <param name="data"></param>
        public virtual void Solve(List<ICPObservationPoint> data)
        {
        }

        public virtual ICPMsg ToLogMessage()
        {
            ICPMsg m = new ICPMsg("GraphMap",
                States.Select(i => new ICPMsg.ICPPoint()
                {
                    X = i.Point.X,
                    Y = i.Point.Y,
                    Generace = i.Generace,
                    Iterace = i.Iterace,
                    LastMatch = i.LastMatch,
                    IsMain = i.IsMain,
                    Type=i.Type,
                    SubType = i.SubType,
                    P = new Common.Matrix(1, 1),
                    Orientation=i.Orientation,
                }).ToList(),
                0, 0, 0);
            return m;
        }


        /// <summary>
        /// Aktualizuje mapu podle pohybu robota - predikcni krok
        /// </summary>
        /// <param name="dx">O kolik se maji posunot stavy</param>
        /// <param name="dy">O kolik se maji posunot stavy</param>
        /// <param name="alfa">O kolik se maji pootocit mereni pred pripocitani ke stavum</param>
        public virtual void Update(double dx, double dy, double alfa)
        {
            var ss = States;
            if (ss != null)
            {
                Point2D d = new Point2D(dx, dy);
//                var rot = new ARBot.Common.Common.Matrix(new double[,] { { Math.Cos(alfa), -Math.Sin(alfa) }, { Math.Sin(alfa), Math.Cos(alfa) } });
                foreach (var s in ss)
                {
                    s.Iterace++;
                    // posunuto do dalsiho kroku
                    s.Point += d;
                }
                States = ss.Where(i => i.Iterace < MaxIteration).ToList();
            }
        }
    }
}
