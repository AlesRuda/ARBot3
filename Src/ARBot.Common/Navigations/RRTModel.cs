using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public class RRTModel: TreeStateBase
    {
        /// <summary>
        /// Akcelerace v m/s^2
        /// </summary>
        public float acceleration = 1;
        private float speedLimit = 1;
        private float rotationSpeedLimit = .1f;
        public float LeftSpeed { get; set; } = 0;
        public float RightSpeed { get; set; } = 0;
        public float orientation = 0;
        public float Speed => (LeftSpeed + RightSpeed) / 2;

        //        public RectangleF Rectangle => new RectangleF(new PointF((float)X, (float)Y), new SizeF());

        /// <summary>
        /// Polovina rozchodu
        /// </summary>
        private float rozchod2 = 0.2f;

        public RRT RRT => Owner as RRT;

        public RRTModel(RRT rrt, float rozchod, float acceleration):base(rrt)
        {
            rozchod2 = rozchod / 2;
            this.acceleration = acceleration;
        }

        float Normalize(float o)
        {
            o = o % (2 * MathF.PI);
            if (o > MathF.PI)
                o -= 2 * MathF.PI;
            if (o <- MathF.PI)
                o += 2 * MathF.PI;
            return o;
        }

        public void Regulator(double x, double y, out double reqSpeed, out double reqRotationSpeed, out double t)
        {
            double r = Math.Sqrt(Math.Pow(y - this.Y, 2) + Math.Pow(x - this.X, 2));
            reqSpeed = Math.Min(speedLimit, Math.Sqrt(2 * r * acceleration));
            var or = Normalize(MathF.Atan2((float)(y - this.Y), (float)(x - this.X)));
            var ro = Normalize(or-this.orientation);
            if (Math.Abs(ro) > rotationSpeedLimit)
            {
                reqRotationSpeed = Math.Max(Math.Min(ro, rotationSpeedLimit), -rotationSpeedLimit);
                reqSpeed *= reqRotationSpeed / ro;
            }
            else
                reqRotationSpeed = ro;
            t = r / reqSpeed;
        }
        
        public void Update(double ts, double reqSpeed, double reqRotationSpeed)
        {
            double reqLeftSpeed = reqSpeed - reqRotationSpeed * rozchod2;
            double reqRightSpeed = reqSpeed + reqRotationSpeed * rozchod2;

            double max = Math.Max(reqLeftSpeed, reqRightSpeed);
            double min = Math.Min(reqLeftSpeed, reqRightSpeed);
            if (max > speedLimit)
            {
                reqLeftSpeed -= max - speedLimit;
                reqRightSpeed -= max - speedLimit;
            }
            if (min < -speedLimit)
            {
                reqLeftSpeed -= min + speedLimit;
                reqRightSpeed -= min + speedLimit;
            }
            reqLeftSpeed = Math.Max(Math.Min(reqLeftSpeed, speedLimit), -speedLimit);
            reqRightSpeed = Math.Max(Math.Min(reqRightSpeed, speedLimit), -speedLimit);

            double add = acceleration * ts * Math.Sign(reqLeftSpeed - LeftSpeed);
            if (add < 0 && add < reqLeftSpeed - LeftSpeed)
                add = reqLeftSpeed - LeftSpeed;
            if (add > 0 && add > reqLeftSpeed - LeftSpeed)
                add = reqLeftSpeed - LeftSpeed;

            double newLeftSpeed = LeftSpeed + add;

            add = acceleration * ts * Math.Sign(reqRightSpeed - RightSpeed);
            if (add < 0 && add < reqRightSpeed - RightSpeed)
                add = reqRightSpeed - RightSpeed;
            if (add > 0 && add > reqRightSpeed - RightSpeed)
                add = reqRightSpeed - RightSpeed;

            double newRightSpeed = RightSpeed + add;

            double speed = (newLeftSpeed + newRightSpeed) / 2;
            double b = (newRightSpeed - newLeftSpeed) / (2 * rozchod2);

            X += speed * ts * Math.Cos(orientation + b / 2);
            Y += speed * ts * Math.Sin(orientation + b / 2);

            orientation = Normalize(orientation + b);
            LeftSpeed = newLeftSpeed;
            RightSpeed = newRightSpeed;
        }



        public override bool Collision(GraphStateBase from, double safeZone)
        {
                double x = from.X;
                double y = from.Y;
                double r = Math.Sqrt(Math.Pow(x - X, 2) + Math.Pow(y - Y, 2));
                double dc = acceleration;
                double s = Speed;
                double t = s / dc + 0.1; // 0.1 je odhad dopravniho zpozdeny urceny merenim
                double l = r + (Math.Pow(t * t, 2) * dc / 2 + 0.2); // dalsi magicka konstanta 0.2, colider musi byt ovalny, aby pri otaceni doslo uhlove brzo k ukonceni kolize

            //                Debug.WriteLine(string.Format("Reflex: t={0}, l={1}, x={2}, s={3}", t, l, state.Model.CurrentState.X, s));
                Point2D f = new Point2D(from.X, from.Y);
                var c = new Collider2(f, f+Point2D.FromPolar(l, orientation + (s < 0 ? Math.PI : 0)), SafeZone - 0.1 + safeZone, SafeZone+ safeZone);

                return RRT.ObstaclesTree.NearestNeighbors(new double[] { x, y }, 1000, l + .4+ safeZone).Any(p =>
                {
                    return c.Inside(p);
                });
        }

        public override double MinDist2 => 0.25;
        public override double MinDist => 0.5;

        public override GraphStateBase NewState(double x, double y)
        {
            double reqSpeed, reqRotationSpeed, t, ts=0.2;

            Regulator(x, y, out reqSpeed, out reqRotationSpeed, out t);
            var newM = Clone() as RRTModel;

            double ts1 = Math.Abs(reqRotationSpeed) < 0.001 ? Math.Min(t, 10 * ts) : ts;
            newM.Update(ts1, reqSpeed, reqRotationSpeed);

            return newM;
        }

        public override GraphStateBase Clone()
        {
            var m = new RRTModel(RRT, 2 * rozchod2, acceleration);
            m.LeftSpeed = LeftSpeed;
            m.orientation = orientation;
            m.RightSpeed = RightSpeed;
            m.speedLimit = speedLimit;
            m.X = X;
            m.Y = Y;
            return m;
        }
    }
}
