using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// SKUTECNA poza simulovaneho robota (ground truth) - existuje jen pri virtualnim HW.
    ///
    /// <para><b>Proc to je zprava a ne jen udaj v UI.</b> Chyba lokalizace = skutecnost minus
    /// odhad. Odhad v zaznamu je (<see cref="RobotStateMsg"/>), skutecnost do ted nikde - takze
    /// se konvergence korekci dala posoudit jen tak, ze se chyba <b>predem vnutila</b> znamou
    /// hodnotou (<c>poseerror=</c>) a hledalo se, jestli ji korelator ohlasi. Jakmile kamery
    /// renderuji z ground truth (<c>camerapose=truth</c>) a chybu vyrabi sum a prokluz kol, zadna
    /// "znama odpoved" neexistuje a chybu jde spocitat <b>jen</b> proti teto zprave.
    /// Viz doc/virtual-hw.md.</para>
    ///
    /// <para>Emituje ji <c>ControlLoop</c> na temze tiku jako <see cref="RobotStateMsg"/> a se
    /// stejnym casovym razitkem, takze rozdil obou zprav v jednom taktu je primo chyba odhadu
    /// (neni potreba nic interpolovat).</para>
    ///
    /// <para>Role "odvozena" - pri replay se regeneruje, neni replay-vstup. Ve starsich zaznamech
    /// chybi; analyza si musi poradit s jeji nepritomnosti.</para>
    /// </summary>
    [Serializable()]
    public class GroundTruthMsg : Message, IHasCaptureTime
    {
        /// <summary>Skutecna poloha na vychod [m].</summary>
        public double X;
        /// <summary>Skutecna poloha na sever [m].</summary>
        public double Y;
        /// <summary>Skutecna orientace [rad], matematicky.</summary>
        public double Theta;
        /// <summary>Skutecna dopredna rychlost [m/s] (po prokluzu kol).</summary>
        public double V;
        /// <summary>Skutecna uhlova rychlost [rad/s], matematicky (+CCW).</summary>
        public double Omega;

        /// <summary>Ujeta draha leveho kola podle enkoderu [m] - NOMINALNI (co videla odometrie).</summary>
        public double LeftEncoder;
        /// <summary>Ujeta draha praveho kola podle enkoderu [m] - NOMINALNI (co videla odometrie).</summary>
        public double RightEncoder;

        /// <summary>Prokluz leveho kola [-], 1 = ideal (aby zaznam nesl i nastaveni experimentu).</summary>
        public double LeftWheelSlip = 1.0;
        /// <summary>Prokluz praveho kola [-], 1 = ideal.</summary>
        public double RightWheelSlip = 1.0;

        /// <summary>Cas, ke kteremu poza plati.</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public GroundTruthMsg() : base("GroundTruthMsg", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(X);
            bw.Write(Y);
            bw.Write(Theta);
            bw.Write(V);
            bw.Write(Omega);
            bw.Write(LeftEncoder);
            bw.Write(RightEncoder);
            bw.Write(LeftWheelSlip);
            bw.Write(RightWheelSlip);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            X = br.ReadDouble();
            Y = br.ReadDouble();
            Theta = br.ReadDouble();
            V = br.ReadDouble();
            Omega = br.ReadDouble();
            LeftEncoder = br.ReadDouble();
            RightEncoder = br.ReadDouble();
            LeftWheelSlip = br.ReadDouble();
            RightWheelSlip = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new GroundTruthMsg();

        public override string ToString()
            => string.Format("GroundTruthMsg X={0:F2} Y={1:F2} th={2:F3} v={3:F2} w={4:F3}", X, Y, Theta, V, Omega);
    }
}
