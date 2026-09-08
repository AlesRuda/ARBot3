using System;
using System.Diagnostics;
using ARBot.Common.Missions;

namespace ARBot.HAL.Devices.AHRS
{
    /// <summary>
    /// <see cref="IMagCalControl"/> nad binarnim driverem VN100. <b>Zadna logika</b> — jen
    /// mapovani sevu na prikazy a registry, aby rozhodovani zustalo v misi a znalost protokolu
    /// v HAL.
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class VnMagCalControl : IMagCalControl
    {
        private readonly VN100IMUBinary imu;

        public VnMagCalControl(VN100IMUBinary imu)
            => this.imu = imu ?? throw new ArgumentNullException(nameof(imu));

        /// <inheritdoc/>
        public double[] ReadRegister(int reg) => imu.ReadRegister(reg);

        /// <inheritdoc/>
        public bool WriteMagCompensation(string dvanactCisel)
            => imu.WriteRegister(VnCommands.MagnetometerCompensation(dvanactCisel),
                                 VnCommands.RegMagCompensation);

        /// <inheritdoc/>
        public bool SetOnboardHsi(bool run)
            => imu.WriteRegister(VnCommands.MagCalControl(run), VnCommands.RegMagCalControl);

        /// <inheritdoc/>
        public bool SaveToFlash()
        {
            // VNWNV nema co zpetne cist (uklada RAM do flash), takze se jen posle a ceka.
            // ⚠️ Uspech tedy NENI overeny - skutecny test je az vypnuti a zapnuti robota.
            imu.SendCommand(VnCommands.SaveToFlash(), TimeSpan.FromSeconds(3));
            Trace.WriteLine("VN100: VNWNV posláno. Trvalost overi jen vypnuti a zapnuti robota.");
            return true;
        }
    }
}
