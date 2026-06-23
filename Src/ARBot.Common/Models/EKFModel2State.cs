using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModel2State : Matrix, IHistoryItem<EKFModel2State>
    {
        public EKFModel2State()
            : base(6, 1)
        {
        }
        /// <summary>
        /// Pozice robotu smerem na vychod od referencniho bodu
        /// </summary>
        public double X { get { return this[0, 0]; } set { this[0, 0] = value; } }
        /// <summary>
        /// Pozice robotu smerem na sever od referencniho bodu
        /// </summary>
        public double Y { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// Rychlost v m/s
        /// </summary>
        public double Speed { get { return this[2, 0]; } set { this[2, 0] = value; } }
        /// <summary>
        /// Matematicka orientace robotu v radianech. 0 na vychod, PI/2 na sever
        /// </summary>
        public double Orientation { get { return Conversions.NormalizeOrientation(this[3, 0]); } set { this[3, 0] = Conversions.NormalizeOrientation(value); } }
        /// <summary>
        /// Rotacni rychlost v rad/s
        /// </summary>
        public double RotationSpeed { get { return this[4, 0]; } set { this[4, 0] = value; } }
        /// <summary>
        /// Cas v sekundech, jen pro vypocty. Lepe je pouzivat TimeStamp
        /// </summary>
        public double TimeStampSecs { get { return this[5, 0]; } set { this[5, 0] = value; } }
        /// <summary>
        /// Cas vzorku
        /// </summary>
        public DateTime TimeStamp { get=>DateTime.FromOADate(TimeStampSecs); set { TimeStampSecs = value.ToOADate(); } }

        /// <summary>
        /// Kopie stavoveho vektoru
        /// </summary>
        /// <returns></returns>
        public EKFModel2State Clone()
        {
            EKFModel2State n = new EKFModel2State();
            n.in_Mat = (double[,])in_Mat.Clone();
            n.TimeStamp = TimeStamp;
            return n;
        }

        /// <summary>
        /// Interpoluje stav mezi prev a next
        /// </summary>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <param name="d"></param>
        /// <returns></returns>
        public EKFModel2State Interpolate(EKFModel2State prev, EKFModel2State next, double d)
        {
            EKFModel2State s = next.Clone();
            s.X = prev.X + d * (next.X - prev.X);
            s.Y = prev.Y + d * (next.Y - prev.Y);
            s.Speed = prev.Speed + d * (next.Speed - prev.Speed);
            s.Orientation = Conversions.NormalizeOrientation(prev.Orientation + d * Conversions.NormalizeOrientation(next.Orientation - prev.Orientation));
            s.RotationSpeed = prev.RotationSpeed + d * (next.RotationSpeed - prev.RotationSpeed);
            s.TimeStamp = prev.TimeStamp.AddSeconds( d * (next.TimeStamp - prev.TimeStamp).TotalSeconds);
            return s;
        }
    }
}
