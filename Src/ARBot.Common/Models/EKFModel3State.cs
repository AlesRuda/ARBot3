using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModel3State : Matrix, IHistoryItem<EKFModel3State>, IModelState
    {
        public EKFModel3State()
            : base(5, 1)
        {
        }
        /// <summary>
        /// Matematicka orientace robotu v radianech. 0 na vychod, PI/2 na sever
        /// </summary>
        public double Orientation { get { return Conversions.NormalizeOrientation(this[0, 0]); } set { this[0, 0] = Conversions.NormalizeOrientation(value); } }

        /// <summary>
        /// Rychlost otaceni robotu
        /// </summary>
        public double OrientationVelocity { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// Rychlost robotu
        /// </summary>
        public double Velocity { get { return this[2, 0]; } set { this[2, 0] = value; } }

        /// <summary>
        /// Pozice robotu smerem na vychod od referencniho bodu
        /// </summary>
        public double X { get { return this[3, 0]; } set { this[3, 0] = value; } }
        /// <summary>
        /// Pozice robotu smerem na sever od referencniho bodu
        /// </summary>
        public double Y { get { return this[4, 0]; } set { this[4, 0] = value; } }

        public DateTime TimeStamp { get; set; }


        public double Roll { get; set; }
        public double Pitch { get ; set ; }
        public Matrix3D Rotation => Conversions.WordToWordTransform(Orientation, Pitch, Roll, new Vector3D(0, 0, 0));
        public Matrix3D Trasnformation => Conversions.WordToWordTransform(Orientation, Pitch, Roll, new Vector3D(X, Y, 0));


        /// <summary>
        /// Kopie stavoveho vektoru
        /// </summary>
        /// <returns></returns>
        public EKFModel3State Clone()
        {
            EKFModel3State n = new EKFModel3State();
            n.in_Mat = (double[,])in_Mat.Clone();
            n.TimeStamp = TimeStamp;
            n.Pitch = Pitch;
            n.Roll = Roll;
            return n;
        }

        /// <summary>
        /// Interpoluje stav mezi prev a next
        /// </summary>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <param name="d"></param>
        /// <returns></returns>
        public EKFModel3State Interpolate(EKFModel3State prev, EKFModel3State next, double d)
        {
            EKFModel3State s = next.Clone();
            s.Orientation = Conversions.NormalizeOrientation(prev.Orientation + d * Conversions.NormalizeOrientation(next.Orientation - prev.Orientation));
            s.OrientationVelocity = prev.OrientationVelocity + d * (next.OrientationVelocity - prev.OrientationVelocity);
            s.Velocity = prev.Velocity + d * (next.Velocity - prev.Velocity);
            s.TimeStamp = TimeStamp.AddMilliseconds(d*(next.TimeStamp-prev.TimeStamp).TotalMilliseconds);
            s.Pitch = prev.Pitch + d * (next.Pitch - prev.Pitch);
            s.Roll = prev.Roll + d * (next.Roll - prev.Roll);
            return s;
        }

        IModelState IModelState.Clone()
        {
            return Clone();
        }

        IModelState IModelState.Interpolate(IModelState prev, IModelState next, double d)
        {
            return Interpolate(prev as EKFModel3State, next as EKFModel3State, d);
        }

        IModelState IHistoryItem<IModelState>.Interpolate(IModelState prev, IModelState next, double d)
        {
            return Interpolate(prev as EKFModel3State, next as EKFModel3State, d);
        }
    }
}
