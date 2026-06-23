using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class ModelState : Matrix, IHistoryItem<ModelState>, IModelState
    {
        private double b;
        private double rozchod;
        public double Roll { get; set; }
        public double Pitch { get; set; }
        public Matrix3D Rotation => Conversions.WordToWordTransform(Orientation, Pitch, Roll, new Vector3D(0, 0, 0));
        public Matrix3D Trasnformation => Conversions.WordToWordTransform(Orientation, Pitch, Roll, new Vector3D(X, Y, 0));
        public ModelState(double rozchod)
            : base(5, 1)
        {
            this.rozchod = rozchod;
            b = 0.5 / rozchod;
        }
        public double LeftWheelVelocity { get { return this[0, 0]; } set { this[0, 0] = value; } }
        public double RightWheelVelocity { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// Pozice robotu smerem na vychod od referencniho bodu
        /// </summary>
        public double X { get { return this[2, 0]; } set { this[2, 0] = value; } }
        /// <summary>
        /// Pozice robotu smerem na sever od referencniho bodu
        /// </summary>
        public double Y { get { return this[3, 0]; } set { this[3, 0] = value; } }
        /// <summary>
        /// Svetova orientace robotu v radianech. Tj. 0 na sever, 90 na vychod
        /// </summary>
        public double Azimut { get { return this[4, 0]; } set { this[4, 0] = value; } }
        /// <summary>
        /// Matematicka orientace robotu v radianech. 0 na vychod, PI/2 na sever
        /// </summary>
        public double Orientation { get { return Conversions.Azimut2Orientation(Azimut); } set { Azimut = Conversions.Orientation2Azimut(value); } }
        /// <summary>
        /// Dopredna rychlost
        /// </summary>
        public double Velocity { get { return (LeftWheelVelocity + RightWheelVelocity) / 2.0; } }
        /// <summary>
        /// Uhlova rychlost otaceni
        /// </summary>
        public double OrientationVelocity { get { return (RightWheelVelocity - LeftWheelVelocity) * b; } }
        /// <summary>
        /// Cas vzorku
        /// </summary>
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// Kopie stavoveho vektoru
        /// </summary>
        /// <returns></returns>
        public ModelState Clone()
        {
            ModelState n = new ModelState(rozchod);
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
        public ModelState Interpolate(ModelState prev, ModelState next, double d)
        {
            ModelState s = next.Clone();
            s.X = prev.X + d * (next.X - prev.X);
            s.Y = prev.Y + d * (next.Y - prev.Y);
            s.Azimut = prev.Azimut + d * Conversions.NormalizeOrientation(Conversions.NormalizeOrientation(next.Azimut) - Conversions.NormalizeOrientation(prev.Azimut));
            s.Roll = prev.Roll + d * Conversions.NormalizeOrientation(Conversions.NormalizeOrientation(next.Roll) - Conversions.NormalizeOrientation(prev.Roll));
            s.Pitch = prev.Pitch + d * Conversions.NormalizeOrientation(Conversions.NormalizeOrientation(next.Pitch) - Conversions.NormalizeOrientation(prev.Pitch));
            s.LeftWheelVelocity = prev.LeftWheelVelocity + d * (next.LeftWheelVelocity - prev.LeftWheelVelocity);
            s.RightWheelVelocity = prev.RightWheelVelocity + d * (next.RightWheelVelocity - prev.RightWheelVelocity);
            return s;
        }

        IModelState IModelState.Clone()
        {
            return Clone();
        }

        IModelState IModelState.Interpolate(IModelState prev, IModelState next, double d)
        {
            return Interpolate(prev as ModelState, next as ModelState, d);
        }

        IModelState IHistoryItem<IModelState>.Interpolate(IModelState prev, IModelState next, double d)
        {
            return Interpolate(prev as ModelState, next as ModelState, d);
        }
    }
}
