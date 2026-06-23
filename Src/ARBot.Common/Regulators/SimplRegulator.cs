using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Models;

namespace ARBot.Common.Regulators
{
    public class SimplRegulator : IRegulator
    {
        public int MaxWayPoints { get { return 1; } }
        public double maxSpeed, acceleration, rozchod, maxOrientationSpeed, stability = 2;

        public SimplRegulator(double maxSpeed, double maxOrientationSpeed, double acceleration, double rozchod)
        {
            this.maxSpeed = maxSpeed;
            this.maxOrientationSpeed = maxOrientationSpeed;
            this.acceleration = acceleration;
            this.rozchod = rozchod;
        }

        public double Speed2Dist(double startSpeed, double endSpeed)
        {
            double s = Math.Abs(startSpeed - endSpeed);
            return s * s / (2 * acceleration);
        }

        private RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed, double maxSpeed, double acceleration)
        {
            double d = Math.Abs(dist);
            d = Math.Sign(dist) * Math.Min((Math.Sqrt(4 * acceleration * d)) / 2, maxSpeed);
            return new RegulatorResult() { Speed = d, RegulationTime = Math.Sqrt(Math.Abs(dist) / (4 * acceleration)) };
        }

        public RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed)
        {
            double dist = beta * rozchod / 2.0;

            double d = Math.Abs(dist);
            d = Math.Sign(dist) * Math.Min((Math.Sqrt(4 * acceleration * d)) / 2, maxSpeed);
            return new RegulatorResult() { RotationSpeed = d / rozchod, RegulationTime = Math.Sqrt(Math.Abs(dist) / (4 * acceleration)) };
        }

        public RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed)
        {
            return Dist2Speed(dist, startSpeed, endSpeed, maxSpeed, acceleration);
        }

        private double Time(double dist, double speed, double startSpeed, double endSpeed, double acceleration)
        {
            double d = Math.Sqrt(Math.Abs(dist) / (4 * acceleration));
            return d;
        }
        public RegulatorResult Control(IModelState state, RegulatorWayPoint[] points)
        {
            if (points.Length != MaxWayPoints)
                throw new Exception("Nepodporovana delka");

            RegulatorWayPoint p = points[0];

            double dx = p.X - state.X;
            double dy = p.Y - state.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double beta;
            if (d > p.MaxPositionError || p.Speed > 0)
                beta = Conversions.NormalizeOrientation(Math.Atan2(dy, dx) - state.Orientation);
            else
            {
                beta = Conversions.NormalizeOrientation(p.Orientation.GetValueOrDefault(state.Orientation) - state.Orientation);
                d = 0;
            }

            var rotRes = Dist2Speed(beta * rozchod / 2.0, state.OrientationVelocity * rozchod, 0, maxOrientationSpeed * rozchod / 2.0, acceleration);

            //            Debug.WriteLine(string.Format("dx={0}, dy={1}, d={2}, beta={3}, sRot={4}", dx, dy, d, beta, sRot));

            var res = Dist2Speed(d, state.Velocity, p.Speed, maxSpeed, acceleration);

            double s = SpeedLimit(res.Speed, d, rotRes);
            return new RegulatorResult() { Speed = s, RotationSpeed = rotRes.Speed, RegulationTime = Math.Max(res.RegulationTime, rotRes.RegulationTime) };
        }
        /// <summary>
        /// Omezi doprednou rychlost na zaklade tychlosti rotace
        /// </summary>
        /// <param name="speed">dopredna rychlost </param>
        /// <param name="d">vzdalenost na ktere musi dojit k otoceni</param>
        /// <param name="rotationResul">Vysledek vypoctu rotacni rychlosti</param>
        /// <returns></returns>
        public double SpeedLimit(double speed, double d, RegulatorResult rotationResul)
        {
            if (rotationResul.RegulationTime != 0)
                return Math.Min(speed, d / (stability * rotationResul.RegulationTime));
            return speed;
        }
    }
}
