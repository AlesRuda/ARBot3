using System;

namespace ARBot.Common.Simulation
{
    /// <summary>
    /// Ground truth simulovaneho robota (viz doc/virtual-hw.md): skutecna poza a rychlosti kol.
    /// Motory do nej pisou prikazy, senzory z nej ctou skutecnost.
    /// <para>
    /// Model je <b>idealni + rampa zrychleni</b>: kola se toci presne tak, jak jim rekne
    /// <see cref="Drive"/> (s omezenim <see cref="SetAcceleration"/>), zadny prokluz.
    /// </para>
    /// <para>
    /// Trida je <b>thread-safe</b>: motory do ni pisou z vlakna ridici smycky, senzory ctou
    /// kazdy ze sveho vlakna.
    /// </para>
    /// </summary>
    public sealed class SimulatedRobot
    {
        private readonly object gate = new object();
        private readonly double wheelBase;

        private DateTime time;
        private double acceleration = 1.0;

        private double targetLeft, targetRight;
        private double speedLeft, speedRight;

        private double x, y, theta;
        private double encoderLeft, encoderRight;

        /// <param name="wheelBase">Rozchod kol [m].</param>
        /// <param name="startTime">Cas, ke kteremu plati pocatecni stav.</param>
        public SimulatedRobot(double wheelBase, DateTime startTime)
        {
            if (wheelBase <= 0) throw new ArgumentOutOfRangeException(nameof(wheelBase));

            this.wheelBase = wheelBase;
            time = startTime;
        }

        /// <summary>Poloha na vychod od pocatku [m].</summary>
        public double X
        {
            get { lock (gate) return x; }
            set { lock (gate) x = value; }
        }

        /// <summary>Poloha na sever od pocatku [m].</summary>
        public double Y
        {
            get { lock (gate) return y; }
            set { lock (gate) y = value; }
        }

        /// <summary>Orientace [rad], matematicky (0 = vychod, +CCW).</summary>
        public double Theta
        {
            get { lock (gate) return theta; }
            set { lock (gate) theta = value; }
        }

        /// <summary>
        /// Nastavi pozadovanou rychlost. Rozklad na kola je <b>presna inverze odometrie</b>
        /// v <c>DefaultMeasurementMapper</c>: <c>vR = v + difSpeed</c>, <c>vL = v - difSpeed</c>,
        /// takze <c>omega = (vR-vL)/rozchod</c> vyjde presne to, ktere chtel regulator.
        /// Kladny <paramref name="difSpeed"/> = otaceni DOLEVA (CCW) - viz doc/virtual-hw.md.
        /// </summary>
        public void Drive(double forwardSpeed, double difSpeed)
        {
            lock (gate)
            {
                targetLeft = forwardSpeed - difSpeed;
                targetRight = forwardSpeed + difSpeed;
            }
        }

        /// <summary>Omezeni zrychleni kol [m/s^2].</summary>
        public void SetAcceleration(double acceleration)
        {
            lock (gate)
                this.acceleration = Math.Abs(acceleration);
        }

        /// <summary>
        /// Posune stav do zadaneho casu. Volani s casem v minulosti stav nemeni.
        /// </summary>
        public void Advance(DateTime now)
        {
            lock (gate)
            {
                double remaining = (now - time).TotalSeconds;
                if (remaining <= 0) return;
                time = now;

                // Integruje se po malych krocich. Jeden velky krok by byl spatne v obou smerech:
                // koncovou rychlosti prestreli rampu, prumerem ji naopak rozmaze pres cely interval
                // (i kdyz rampa dobehla hned). Kratky krok plati oboji. Zaroven to drzi presnost
                // v oblouku, i kdyz si senzor sahne jen 5x za sekundu.
                while (remaining > 0)
                {
                    double dt = Math.Min(MaxIntegrationStepSeconds, remaining);
                    remaining -= dt;
                    Step(dt);
                }
            }
        }

        /// <summary>Nejdelsi krok integrace [s].</summary>
        private const double MaxIntegrationStepSeconds = 0.005;

        /// <summary>Jeden krok integrace (vola se pod zamkem).</summary>
        private void Step(double dt)
        {
            double leftBefore = speedLeft, rightBefore = speedRight;
            speedLeft = Ramp(speedLeft, targetLeft, dt);
            speedRight = Ramp(speedRight, targetRight, dt);

            // Lichobeznikova integrace: pri rampe se rychlost behem kroku meni, takze se
            // integruje PRUMEREM pres krok, ne koncovou hodnotou.
            double v = 0.25 * (leftBefore + speedLeft + rightBefore + speedRight);
            double omega = 0.5 * ((speedRight - speedLeft) + (rightBefore - leftBefore)) / wheelBase;

            // Poloha se posouva ve smeru uprostred kroku (presnejsi pri soucasnem otaceni).
            double thetaMid = theta + 0.5 * omega * dt;

            x += v * Math.Cos(thetaMid) * dt;
            y += v * Math.Sin(thetaMid) * dt;

            theta = Normalize(theta + omega * dt);

            encoderLeft += 0.5 * (leftBefore + speedLeft) * dt;
            encoderRight += 0.5 * (rightBefore + speedRight) * dt;
        }

        /// <summary>Skutecna rychlost leveho kola [m/s].</summary>
        public double LeftWheelSpeed { get { lock (gate) return speedLeft; } }

        /// <summary>Skutecna rychlost praveho kola [m/s].</summary>
        public double RightWheelSpeed { get { lock (gate) return speedRight; } }

        /// <summary>Skutecna dopredna rychlost [m/s].</summary>
        public double Speed { get { lock (gate) return 0.5 * (speedLeft + speedRight); } }

        /// <summary>Skutecna uhlova rychlost [rad/s], matematicky (+CCW).</summary>
        public double AngularSpeed { get { lock (gate) return (speedRight - speedLeft) / wheelBase; } }

        /// <summary>Ujeta draha leveho kola [m] (integral, jako enkoder).</summary>
        public double LeftEncoder { get { lock (gate) return encoderLeft; } }

        /// <summary>Ujeta draha praveho kola [m] (integral, jako enkoder).</summary>
        public double RightEncoder { get { lock (gate) return encoderRight; } }

        /// <summary>
        /// Atomicky precte cely stav - senzory potrebuji konzistentni snimek, ne slozeny
        /// z nekolika zamku (mezi nimi by se stav mohl posunout).
        /// </summary>
        public void Read(out double x, out double y, out double theta,
                         out double leftSpeed, out double rightSpeed,
                         out double leftEncoder, out double rightEncoder)
        {
            lock (gate)
            {
                x = this.x; y = this.y; theta = this.theta;
                leftSpeed = speedLeft; rightSpeed = speedRight;
                leftEncoder = encoderLeft; rightEncoder = encoderRight;
            }
        }

        /// <summary>Uhel do intervalu (-pi, pi].</summary>
        private static double Normalize(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle <= -Math.PI) angle += 2 * Math.PI;
            return angle;
        }

        /// <summary>Posune rychlost k cili nejvyse o <c>acceleration * dt</c>.</summary>
        private double Ramp(double current, double target, double dt)
        {
            double maxStep = acceleration * dt;
            double diff = target - current;

            if (diff > maxStep) return current + maxStep;
            if (diff < -maxStep) return current - maxStep;
            return target;
        }
    }
}
