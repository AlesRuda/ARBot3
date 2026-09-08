using System;
using System.Diagnostics;
using System.Globalization;
using ARBot.Common.Missions;

namespace ARBot.HAL.Devices.AHRS
{
    /// <summary>
    /// <b><see cref="IMagCalControl"/> pro simulaci</b> — registry 21/23/44/47 drzene v pameti.
    ///
    /// <para><b>Nacpak.</b> <c>mission=magcal</c> se zakladala jen s <see cref="VN100IMUBinary"/>,
    /// takze v simulaci <b>nevznikla vubec</b> a cely retez mise → <c>MagCalMsg</c> → zaznam →
    /// <c>ARBot.Analyze magcal</c> nikdy neprobehl od zacatku do konce. S timhle uloziste jde
    /// proceduru <b>proklikat</b> a hlavne automaticky overit, ze mise vrati prave to zelezo,
    /// ktere se do <see cref="VirtualSensorOptions.MagHardIronG"/> vlozilo.</para>
    ///
    /// <para><b>Registr 21 se hlasi podle simulovaneho pole</b>
    /// (<see cref="VirtualSensorOptions.MagFieldG"/>), ne podle hodnoty ze skutecneho senzoru —
    /// jinak by mise normovala na jine <c>|B|</c>, nez jake simulace vyrabi, a vysledek by se
    /// nedal porovnat se vstupem.</para>
    ///
    /// <para>⚠️ <b>Neni to model senzoru.</b> Zapis se jen zapamatuje; nic se tim v simulovanem
    /// poli nezmeni (virtualni magnetometr zadnou palubni kompenzaci nedela). Overuje se tim NAS
    /// retez, ne chovani VN100 — to jde zmerit jedine na zeleze.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md a doc/virtual-hw.md.</para>
    /// </summary>
    public sealed class VirtualMagCalControl : IMagCalControl
    {
        private readonly VirtualSensorOptions options;
        private readonly object gate = new object();

        /// <summary>Kompenzace „v senzoru" — vychozi jednotkova, jako po <c>--clearmag</c>.</summary>
        private double[] reg23 = { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };

        /// <summary>Registr 44: HSIMode, HSIOutput, ConvergeRate.</summary>
        private double[] reg44 = { 0, 1, 5 };

        public VirtualMagCalControl(VirtualSensorOptions options)
            => this.options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>Co bylo naposled zapsano do registru 23 (diagnostika a testy).</summary>
        public string LastWritten { get; private set; }

        /// <summary>Kolikrat se „ukladalo do flash" (diagnostika a testy).</summary>
        public int FlashSaves { get; private set; }

        /// <inheritdoc/>
        public double[] ReadRegister(int reg)
        {
            lock (gate)
            {
                switch (reg)
                {
                    case IMagCalControl.RegReference:
                        // Referencni vektor pole (NED) + gravitace, jako registr 21 skutecneho VN.
                        double b = options.MagFieldG, s = options.MagInclinationRad;
                        return new[] { b * Math.Cos(s), 0.0, b * Math.Sin(s), 0.0, 0.0, -9.79375 };

                    case IMagCalControl.RegCompensation: return (double[])reg23.Clone();
                    case IMagCalControl.RegCalControl: return (double[])reg44.Clone();

                    // Registr 47 je kalibrace, kterou spocital SAM senzor. Simulace zadny vlastni
                    // HSI algoritmus nema, takze poctive hlasi "nic" - vymyslet cislo by znamenalo
                    // predstirat nezavislou kontrolu, ktera neexistuje.
                    case IMagCalControl.RegCalculatedHsi: return null;

                    default: return null;
                }
            }
        }

        /// <inheritdoc/>
        public bool WriteMagCompensation(string dvanactCisel)
        {
            if (string.IsNullOrWhiteSpace(dvanactCisel)) return false;
            var casti = dvanactCisel.Split(',');
            if (casti.Length != 12) return false;

            var nove = new double[12];
            for (int i = 0; i < 12; i++)
                if (!double.TryParse(casti[i], NumberStyles.Float, CultureInfo.InvariantCulture,
                                     out nove[i]))
                    return false;

            lock (gate) { reg23 = nove; LastWritten = dvanactCisel; }
            Trace.WriteLine("VirtualMagCal: registr 23 (v pameti) = " + dvanactCisel);
            return true;
        }

        /// <inheritdoc/>
        public bool SetOnboardHsi(bool run)
        {
            lock (gate) reg44 = new double[] { run ? 1 : 0, 1, 5 };
            return true;
        }

        /// <inheritdoc/>
        public bool SaveToFlash()
        {
            lock (gate) FlashSaves++;
            Trace.WriteLine("VirtualMagCal: ulozeni do flash (v simulaci jen zaznamenano).");
            return true;
        }
    }
}
