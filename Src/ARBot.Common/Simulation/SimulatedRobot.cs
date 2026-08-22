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
    /// <b>Rampa je v (dopredna, rozdil), ne po kolech</b> - tak to dela skutecny ridici SW motoru
    /// (<c>Src/RoboRun/RizeniDiffPodvozku.mbs</c>, tentyz skript je v komentari u
    /// <c>SDC2160Ex</c>): kazda slozka ma svou rampu a <b>pri saturaci rychlosti kola ustupuje
    /// dopredna rychlost, rotace se drzi</b>. Kdyby se rampovalo po kolech, pri soucasnem brzdeni
    /// obou kol na dorazu by se rozdil kol zmrazil a robot by jel rovne, i kdyz se poruci zatacka
    /// (nalezeno 18. 8. 2026 rozborem zaznamu - viz doc/virtual-hw.md).
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
        private readonly double maxWheelSpeed;

        private DateTime time;
        private double acceleration = 1.0;

        // Stav v tychz velicinach, jake dostava radic: dopredna rychlost a POLOVICNI rozdil kol
        // (vL = forward - dif, vR = forward + dif).
        private double targetForward, targetDif;
        private double speedForward, speedDif;

        private double x, y, theta;
        private double encoderLeft, encoderRight;

        // Prokluz kol: nasobek mezi tim, co kolo NAMERI (enkoder), a tim, oc se robot SKUTECNE
        // posune. 1.0 = ideal. Viz LeftWheelSlip.
        private double slipLeft = 1.0, slipRight = 1.0;

        /// <param name="wheelBase">Rozchod kol [m].</param>
        /// <param name="startTime">Cas, ke kteremu plati pocatecni stav.</param>
        /// <param name="maxWheelSpeed">Nejvyssi mozna rychlost jednoho kola [m/s]; pri jejim
        /// dosazeni ustupuje dopredna rychlost, aby se rotace zachovala (u skutecneho driveru
        /// je to <c>maxPossibleSpeed</c>). Vychozi = bez omezeni.</param>
        public SimulatedRobot(double wheelBase, DateTime startTime,
                              double maxWheelSpeed = double.PositiveInfinity)
        {
            if (wheelBase <= 0) throw new ArgumentOutOfRangeException(nameof(wheelBase));
            if (maxWheelSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(maxWheelSpeed));

            this.wheelBase = wheelBase;
            this.maxWheelSpeed = maxWheelSpeed;
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
        /// Prokluz LEVEHO kola: nasobek mezi tim, co kolo <b>namerí</b> (enkoder, odometrie),
        /// a tim, oc se robot <b>skutecne</b> posune. <c>1.0</c> = ideal (vychozi),
        /// <c>0.98</c> = kolo se otoci o 2 % vic, nez ujede.
        ///
        /// <para><b>K cemu to je.</b> Bez prokluzu je odometrie <i>presna</i>, takze chyba odhadu
        /// fuze je jen bily sum GPS/IMU - nulova stredni hodnota, ohranicena, nikam nedriftuje.
        /// Realny pripad, ktery ma hranova lokalizace lecit (pomalu rostouci chyba polohy a kurzu),
        /// tak v simulaci vubec nevznikne. Ruzny prokluz vlevo/vpravo vyrobi <b>drift kurzu</b>,
        /// stejny na obou kolech <b>chybu merítka drahy</b>. Viz doc/virtual-hw.md.</para>
        ///
        /// <para><b>Co je nominalni a co skutecne.</b> <see cref="LeftWheelSpeed"/>,
        /// <see cref="RightWheelSpeed"/> a enkodery hlasi <b>nominalni</b> hodnoty (to, co kolo
        /// udela) - to je vstup odometrie. <see cref="Speed"/>, <see cref="AngularSpeed"/> a poloha
        /// jsou <b>skutecne</b> (po prokluzu) - to meri GPS a gyro. Rozdil mezi nimi je prave ta
        /// chyba, kterou ma fuze najit.</para>
        /// </summary>
        public double LeftWheelSlip
        {
            get { lock (gate) return slipLeft; }
            set { lock (gate) slipLeft = value; }
        }

        /// <summary>Prokluz PRAVEHO kola - viz <see cref="LeftWheelSlip"/>.</summary>
        public double RightWheelSlip
        {
            get { lock (gate) return slipRight; }
            set { lock (gate) slipRight = value; }
        }

        /// <summary>Prokluji kola jinak nez idealne? (Zvyrazneni v UI - snadno se zapomene vypnout.)</summary>
        public bool HasWheelSlip
        {
            get { lock (gate) return slipLeft != 1.0 || slipRight != 1.0; }
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
                targetForward = forwardSpeed;
                targetDif = difSpeed;
            }
        }

        /// <summary>Omezeni zrychleni [m/s^2]. Plati na doprednou i rotacni slozku zvlast -
        /// skutecny driver posila tutez hodnotu do obou (<c>VAR 1</c> a <c>VAR 2</c>).</summary>
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
            double forwardBefore = speedForward, difBefore = speedDif;

            // Obe slozky maji SVOU rampu a jsou na sobe nezavisle - doraz zrychleni v dopredne
            // slozce nesmi zdrzet ustaveni rotace (to byla chyba rampy po kolech).
            speedForward = Ramp(speedForward, targetForward, dt);
            speedDif = Ramp(speedDif, targetDif, dt);

            // Saturace kola: ustoupi DOPREDNA rychlost, rotace zustava. Tvar i poradi podminek
            // je stejne jako v ridicim skriptu motoru (rotace se zamerne nekrati - kdyz je sama
            // vetsi nez maximum kola, dopredna vyjde zaporna, presne jako tam).
            double bound = maxWheelSpeed - Math.Abs(speedDif);
            if (speedForward > bound) speedForward = bound;
            if (speedForward < -bound) speedForward = -bound;

            double leftBefore = forwardBefore - difBefore, rightBefore = forwardBefore + difBefore;
            double left = speedForward - speedDif, right = speedForward + speedDif;

            // Lichobeznikova integrace: pri rampe se rychlost behem kroku meni, takze se
            // integruje PRUMEREM pres krok, ne koncovou hodnotou.
            double v = 0.5 * (forwardBefore + speedForward);
            double omega = (difBefore + speedDif) / wheelBase;

            // Prokluz: enkodery dal pocitaji NOMINALNI drahu (kolo se opravdu otocilo), ale robot
            // se posune min / jinak. Bez prokluzu se vetev preskakuje zamerne - vzorec pres kola
            // je v realnych cislech totozny, ale ne bit po bitu, a deterministicke testy se opiraji
            // o dosavadni hodnoty. Viz LeftWheelSlip.
            if (slipLeft != 1.0 || slipRight != 1.0)
            {
                double leftAvg = 0.5 * (leftBefore + left) * slipLeft;
                double rightAvg = 0.5 * (rightBefore + right) * slipRight;

                v = 0.5 * (leftAvg + rightAvg);
                omega = (rightAvg - leftAvg) / wheelBase;
            }

            // Poloha se posouva ve smeru uprostred kroku (presnejsi pri soucasnem otaceni).
            double thetaMid = theta + 0.5 * omega * dt;

            x += v * Math.Cos(thetaMid) * dt;
            y += v * Math.Sin(thetaMid) * dt;

            theta = Normalize(theta + omega * dt);

            encoderLeft += 0.5 * (leftBefore + left) * dt;
            encoderRight += 0.5 * (rightBefore + right) * dt;
        }

        /// <summary>Nominalni rychlost leveho kola [m/s] - to, co hlasi odometrie (bez prokluzu).</summary>
        public double LeftWheelSpeed { get { lock (gate) return speedForward - speedDif; } }

        /// <summary>Nominalni rychlost praveho kola [m/s] - to, co hlasi odometrie (bez prokluzu).</summary>
        public double RightWheelSpeed { get { lock (gate) return speedForward + speedDif; } }

        /// <summary>
        /// Skutecna dopredna rychlost [m/s] - <b>po prokluzu</b>, tedy to, co meri GPS.
        /// Bez prokluzu je to presne nominalni dopredna rychlost.
        /// </summary>
        public double Speed
        {
            get
            {
                lock (gate)
                {
                    if (slipLeft == 1.0 && slipRight == 1.0) return speedForward;
                    return 0.5 * ((speedForward - speedDif) * slipLeft + (speedForward + speedDif) * slipRight);
                }
            }
        }

        /// <summary>
        /// Skutecna uhlova rychlost [rad/s], matematicky (+CCW) - <b>po prokluzu</b>, tedy to,
        /// co meri gyro. Bez prokluzu je to presne <c>2*dif/rozchod</c>.
        /// </summary>
        public double AngularSpeed
        {
            get
            {
                lock (gate)
                {
                    if (slipLeft == 1.0 && slipRight == 1.0) return 2 * speedDif / wheelBase;
                    return ((speedForward + speedDif) * slipRight - (speedForward - speedDif) * slipLeft) / wheelBase;
                }
            }
        }

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
                leftSpeed = speedForward - speedDif; rightSpeed = speedForward + speedDif;
                leftEncoder = encoderLeft; rightEncoder = encoderRight;
            }
        }

        /// <summary>
        /// Skutecny stav jako zprava do zaznamu (konvence: doménovy objekt si vyrabi svou
        /// log-zpravu sam - viz CLAUDE.md). Cte se atomicky, aby zprava nebyla slozena
        /// z nekolika ruznych okamziku.
        /// </summary>
        /// <param name="timeStamp">Cas, ke kteremu se stav hlasi (tik ridici smycky) - musi byt
        /// tentyz jako u <c>RobotStateMsg</c>, jinak by rozdil obou zprav nebyl chyba odhadu.</param>
        public Logs.GroundTruthMsg ToLogMessage(DateTime timeStamp)
        {
            lock (gate)
            {
                bool ideal = slipLeft == 1.0 && slipRight == 1.0;
                double left = speedForward - speedDif, right = speedForward + speedDif;

                return new Logs.GroundTruthMsg
                {
                    X = x,
                    Y = y,
                    Theta = theta,
                    V = ideal ? speedForward : 0.5 * (left * slipLeft + right * slipRight),
                    Omega = ideal ? 2 * speedDif / wheelBase
                                  : (right * slipRight - left * slipLeft) / wheelBase,
                    LeftEncoder = encoderLeft,
                    RightEncoder = encoderRight,
                    LeftWheelSlip = slipLeft,
                    RightWheelSlip = slipRight,
                    TimeStamp = timeStamp,
                };
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
