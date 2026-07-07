using System;
using ARBot.Common.Common;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Konkretni EKF model pozemniho diferencialniho podvozku.
    /// Stav x = [X, Y, theta, v, omega]:
    ///   X, Y     - poloha [m] (vychod, sever)
    ///   theta    - orientace [rad] (matematicky)
    ///   v        - rychlost ve smeru orientace [m/s]
    ///   omega    - uhlova rychlost [rad/s]
    /// Predikce je near-constant-velocity (v a omega jsou nahodna prochazka, jejich
    /// nejistota je dana zrychlenim v Q). Poloha a orientace se integruji z v, omega.
    /// </summary>
    public class EKFModel : Ekf
    {
        public const int IX = 0, IY = 1, ITh = 2, IV = 3, IW = 4, N = 5;

        private readonly FusionConfig cfg;

        public EKFModel(FusionConfig config = null)
            : base(Vector<double>.Build.Dense(N), Matrix<double>.Build.DenseIdentity(N))
        {
            cfg = config ?? new FusionConfig();
        }

        public FusionConfig Config => cfg;

        protected override Vector<double> PredictState(Vector<double> x, double dt)
        {
            double th = x[ITh], v = x[IV], w = x[IW];
            double b = th + w * dt / 2.0;   // stredni orientace behem kroku (exaktnejsi oblouk)
            var n = x.Clone();
            n[IX] = x[IX] + v * Math.Cos(b) * dt;
            n[IY] = x[IY] + v * Math.Sin(b) * dt;
            n[ITh] = th + w * dt;
            n[IV] = v;
            n[IW] = w;
            return n;
        }

        protected override Matrix<double> JacobianF(Vector<double> x, double dt)
        {
            double v = x[IV], w = x[IW];
            double b = x[ITh] + w * dt / 2.0;
            double cb = Math.Cos(b), sb = Math.Sin(b);
            var F = Matrix<double>.Build.DenseIdentity(N);

            // X' = X + v cos(b) dt
            F[IX, ITh] = -v * sb * dt;
            F[IX, IW] = -v * sb * dt * dt / 2.0;
            F[IX, IV] = cb * dt;
            // Y' = Y + v sin(b) dt
            F[IY, ITh] = v * cb * dt;
            F[IY, IW] = v * cb * dt * dt / 2.0;
            F[IY, IV] = sb * dt;
            // theta' = theta + w dt
            F[ITh, IW] = dt;
            // v' = v, omega' = omega -> jednotkove (uz z identity)
            return F;
        }

        protected override Matrix<double> ProcessNoise(Vector<double> x, double dt)
        {
            // Spojity bily sum zrychleni (CWNA) mapovany pres aktualni orientaci.
            double th = x[ITh];
            double c = Math.Cos(th), s = Math.Sin(th);
            double sa2 = cfg.SigmaAccel * cfg.SigmaAccel;
            double sal2 = cfg.SigmaAngAccel * cfg.SigmaAngAccel;
            double dt2 = dt * dt, dt3 = dt2 * dt;

            var Q = Matrix<double>.Build.Dense(N, N);

            // linearni kanal: podelna poloha + v
            double qpp = sa2 * dt3 / 3.0;   // rozptyl polohy podel smeru jizdy
            double qpv = sa2 * dt2 / 2.0;   // krizovy clen poloha-rychlost
            double qvv = sa2 * dt;          // rozptyl rychlosti
            double floor = cfg.PositionNoiseFloor * dt;

            Q[IX, IX] = qpp * c * c + floor;
            Q[IY, IY] = qpp * s * s + floor;
            Q[IX, IY] = Q[IY, IX] = qpp * c * s;
            Q[IX, IV] = Q[IV, IX] = qpv * c;
            Q[IY, IV] = Q[IV, IY] = qpv * s;
            Q[IV, IV] = qvv;

            // uhlovy kanal: theta + omega
            Q[ITh, ITh] = sal2 * dt3 / 3.0;
            Q[ITh, IW] = Q[IW, ITh] = sal2 * dt2 / 2.0;
            Q[IW, IW] = sal2 * dt;

            return Q;
        }

        protected override void NormalizeState(Vector<double> x)
        {
            x[ITh] = Conversions.NormalizeOrientation(x[ITh]);
        }

        /// <summary>Vytvori typovany pohled na zadany (x, P) v case t.</summary>
        public RobotState ToRobotState(Vector<double> x, Matrix<double> P, DateTime t)
        {
            return new RobotState
            {
                X = x[IX],
                Y = x[IY],
                Theta = Conversions.NormalizeOrientation(x[ITh]),
                V = x[IV],
                Omega = x[IW],
                TimeStamp = t,
                Covariance = P
            };
        }

        /// <summary>Aktualni instancni stav jako RobotState.</summary>
        public RobotState Current(DateTime t) => ToRobotState(X, P, t);

        /// <summary>Nastavi pocatecni pozici a orientaci robota.</summary>
        public void SetPose(double x, double y, double theta)
        {
            X[IX] = x;
            X[IY] = y;
            X[ITh] = Conversions.NormalizeOrientation(theta);
        }
    }
}
