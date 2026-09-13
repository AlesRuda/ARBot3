using System;
using ARBot.Common.Calibration;
using ARBot.Common.Models;
using MathNet.Numerics.LinearAlgebra;

using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// <b>Generator synteticke otacky robotem</b> — pole a gravitace pro danou pozu, se ZNAMYM
    /// tvrdym i mekkym zelezem. Sdili ho <see cref="MagCalCollectorTests"/> (proveruje samotne
    /// prolozeni) a <c>MagCalMissionTests</c> (potrebuje se dostat do stavu <c>Usable</c>, aby
    /// slo overit chovani mise po USPESNEM zapisu).
    ///
    /// <para>Zelezo je zamerne to skutecne, zmerene na robotu 6. 9. 2026 — test, ktery projde
    /// jen na hezkych cislech, nerika o senzoru nic.</para>
    /// </summary>
    internal static class MagCalSamples
    {
        internal const double Bref = 0.4818;
        internal const double SklonRad = 1.0638;
        internal static readonly DateTime T0 = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

        internal static readonly Matrix<double> C = Matrix<double>.Build.DenseOfArray(new[,] {
            { 1.222, 0.005, 0.010 }, { 0.005, 1.175, -0.012 }, { 0.010, -0.012, 1.081 } });
        internal static readonly Vector<double> Bias =
            Vector<double>.Build.DenseOfArray(new[] { -0.274, -0.058, 0.076 });

        /// <summary>
        /// Pole i gravitace pro danou pozu robota — <b>obojí z JEDNÉ rotace</b>.
        ///
        /// <para>⚠️ Je to zamer, ne styl. Kdyz se pole a gravitace vyrabeji dvema nezavislymi
        /// vzorci, snadno se rozejdou (naklon v jednom a ne v druhem, nebo obraceny znak) a fit
        /// pak spravne hlasi rozptyl sklonu v desitkach stupnu — chyba je ale v testu. Presne
        /// tohle se tady jednou stalo (sd(sklonu) 18,7° pri perfektni kalibraci). Jedna rotace
        /// aplikovana na oba svetove vektory tu tridu chyby odstranuje.</para>
        /// </summary>
        internal static (Vector3 Mag, Vector3 Acc) Poza(double yaw, double naklon, double smerRad)
        {
            // Svetove vektory: pole se sklonem SklonRad k severu, gravitace dolu.
            var mWorld = new Vector3((float)(Bref * Math.Cos(SklonRad)), 0f,
                                     (float)(-Bref * Math.Sin(SklonRad)));
            var gWorld = new Vector3(0f, 0f, -9.81f);

            // Svet -> teleso: nejdriv kurz kolem svislice, pak naklon kolem vodorovne osy.
            var osa = new Vector3((float)Math.Cos(smerRad + Math.PI / 2),
                                  (float)Math.Sin(smerRad + Math.PI / 2), 0f);
            var qYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-yaw);
            var qTilt = Quaternion.CreateFromAxisAngle(osa, (float)-naklon);

            Vector3 DoTelesa(Vector3 v) => Vector3.Transform(Vector3.Transform(v, qYaw), qTilt);

            var m = DoTelesa(mWorld);
            var x = Vector<double>.Build.DenseOfArray(new double[] { m.X, m.Y, m.Z });
            var r = C.Inverse() * x + Bias;
            return (new Vector3((float)r[0], (float)r[1], (float)r[2]), DoTelesa(gWorld));
        }

        internal static IMUState Vzorek(double t, double omega, Vector3 mag, Vector3 acc)
            => new IMUState
            {
                Name = "VN100 IMU",
                HasAbsoluteHeading = true,
                MagnetometerRaw = mag,
                Acceleration = acc,
                AngularVelocity = new Vector3(0f, 0f, (float)omega),
                TimeStamp = T0.AddSeconds(t),
            };

        /// <summary>Jeden obrat o 360° pri danem naklonu; 100 Hz, 0,5 rad/s (tedy ~12,6 s).</summary>
        internal static void Obrat(MagCalCollector c, double naklon, double smerRad, ref double t)
            => Obrat(v => c.Add(v), naklon, smerRad, ref t);

        /// <summary>
        /// Tyz obrat, ale vzorky jdou kamkoli — sberaci primo, nebo <c>Post</c>em do mise.
        /// Mise ma vlastni sberac uvnitr, takze se k nemu jinak nez pres zpravy nedostaneme.
        /// </summary>
        internal static void Obrat(Action<IMUState> cil, double naklon, double smerRad, ref double t)
        {
            const double omega = 0.5, dt = 0.01;
            int n = (int)(2 * Math.PI / omega / dt);
            for (int i = 0; i < n; i++)
            {
                double yaw = omega * dt * i;
                var p = Poza(yaw, naklon, smerRad);
                cil(Vzorek(t, omega, p.Mag, p.Acc));
                t += dt;
            }
        }
    }
}
