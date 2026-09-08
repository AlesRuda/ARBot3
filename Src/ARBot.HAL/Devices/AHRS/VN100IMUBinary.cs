using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Models;
using VectorNav.Devices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ARBot.HAL.Tests")]

namespace ARBot.HAL.Devices.AHRS
{
    /// <summary>
    /// VN-100 přes BINÁRNÍ výstup. Oproti ASCII <see cref="VN100IMU"/> umí navíc přečíst
    /// odhad nejistoty orientace (YprU = attitude uncertainty, 1σ), který se uloží do
    /// <see cref="IMUState.OrientationUncertainty"/> a slouží jako zdroj kovariance měření R
    /// pro fúzní filtr.
    ///
    /// Konfigurace výstupu se sestaví přes <see cref="BinaryOutputConfig"/> (skupiny
    /// Imu: Accel/Gyro/Mag/UncompMag; Attitude: Ypr/YprU/YprRate). Pakety se rámují a dekódují ručně
    /// podle nakonfigurovaného layoutu (velikosti polí z BinaryOutputInfoAttribute).
    /// Orientace se bere z VN Ypr (yaw = azimut z magnetometru) a ukládá jako ENU/matematická
    /// orientace (viz Azimut2Orientation), takže Rotation odpovídá konvenci projektu.
    ///
    /// PŘEDPOKLAD FRAME: na VN100 je nastavena reference frame rotation diag(-1,1,-1), takže
    /// výstup je robotem zarovnaný FRD (X vpřed, Y vpravo, Z dolů) / NED. Surové vektory se pak
    /// převádějí na projektové FLU (negace Y, Z). Pokud se montáž/nastavení VN změní, uprav zde.
    /// </summary>
    public class VN100IMUBinary : UartSensorBase<IMUState>, IIMU, IMagneticModel
    {
        private const byte Sync = 0xFA;

        // Pořadí skupin podle bitů v "groups" byte (bit0..bit5).
        private static readonly Type[] GroupEnums =
        {
            typeof(BinaryOutputConfig.CommonGroupOptions),
            typeof(BinaryOutputConfig.TimeGroupOptions),
            typeof(BinaryOutputConfig.ImuGroupOptions),
            typeof(BinaryOutputConfig.GpsGroupOptions),
            typeof(BinaryOutputConfig.AttitudeGroupOptions),
            typeof(BinaryOutputConfig.InsGroupOptions),
        };

        public VN100IMUBinary(IUart uart) : base(uart)
        {
            Configure();
            Start();
        }

        public override string Name => "VN100 IMU (binary)";

        /// <summary>Vypne ASCII async výstup a zapne binární výstup 1 s požadovanými skupinami.</summary>
        private void Configure()
        {
            var cfg = new BinaryOutputConfig
            {
                AsyncMode = BinaryOutputConfig.AsyncModeOption.SerialPort1,
                RateDivisor = 8,   // IMU rate / 8  (~100 Hz při 800 Hz)
                ImuGroup = BinaryOutputConfig.ImuGroupOptions.Accel
                           | BinaryOutputConfig.ImuGroupOptions.Gyro
                           | BinaryOutputConfig.ImuGroupOptions.Mag
                           // NEKOMPENZOVANE pole (pred registrem 23) kvuli kalibraci
                           // magnetometru — viz doc/plan-vn100-kalibrace.md.
                           | BinaryOutputConfig.ImuGroupOptions.UncompMag,
                AttitudeGroup = BinaryOutputConfig.AttitudeGroupOptions.Ypr
                                | BinaryOutputConfig.AttitudeGroupOptions.YprU
                                | BinaryOutputConfig.AttitudeGroupOptions.YprRate,
            };

            WriteCommand("VNWRG,06,0");                            // ADOR = 0 → vypnout ASCII async
            WriteCommand("VNWRG,75," + cfg.ConvertToCommand());    // binární výstup 1
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>Tady to dosud <b>vubec nebylo</b> — metoda existovala jen v ASCII verzi
        /// driveru, zatimco na robotu bezi tahle. Sestaveni prikazu je proto ve spolecnem
        /// <see cref="VnCommands"/>; duvody a opravene chyby viz tam.</para>
        ///
        /// <para><b>Konstruktor to nevola zamerne.</b> Model pole potrebuje POLOHU, a tu driver
        /// pri startu nezna — smysl to ma az po prvnim kvalitnim fixu GPS. Volani si tedy musi
        /// zaridit runtime; driver jen umi prikaz poslat.</para>
        /// </remarks>
        public void SetModelParams(LLA lla)
        {
            WriteCommand(VnCommands.ReferenceVectorConfig(lla, TimeBase.Now));
        }

        /// <summary>Poste prikaz oramovany do <c>$telo*XX</c> (spolecne s ASCII driverem).</summary>
        private void WriteCommand(string body) => uart.WriteLine(VnCommands.Frame(body));

        // --- Cteni a zapis registru (kalibrace magnetometru) --------------------------------
        //
        // ⚠️ Cely tenhle blok existuje proto, ze driver drzi port VYHRADNE. Skript zvenci
        // (deploy/vnprobe.sh) se s senzorem domluvi jen pri ZASTAVENE sluzbe, takze kalibraci
        // by nesla odjet z telefonu — musel by se do pole nosit notebook. Viz
        // doc/plan-vn100-kalibrace.md, rozhodnuti 2.

        /// <summary>Kolik bajtu odposlechu se drzi. Pri ~9 kB/s je to necela pulsekunda.</summary>
        private const int OdposlechLen = 4096;

        private readonly object odposlechLock = new object();
        private readonly byte[] odposlech = new byte[OdposlechLen];
        private int odposlechPos;
        private bool odposlechZapnut;

        /// <summary>
        /// <b>Odbocka bajtoveho toku pro ASCII odpovedi.</b> Vola se z <see cref="ReadExact"/>,
        /// tedy z TEHOZ misto, kudy tecou bajty do skladace binarnich ramcu.
        ///
        /// <para>⚠️ <b>Nezakladat druhy odber z portu.</b> Dva ctenari jednoho UARTu si data
        /// rozeberou a binarni ramce se zacnou ztracet — proto odbocka, ne dalsi
        /// <c>uart.Read</c>.</para>
        ///
        /// <para>Sbira se jen kdyz na odpoved nekdo ceka; jinak by se zbytecne kopirovalo
        /// ~9 kB/s po celou dobu behu.</para>
        /// </summary>
        private void Odposlechni(byte[] data, int offset, int count)
        {
            lock (odposlechLock)
            {
                if (!odposlechZapnut) return;
                for (int i = 0; i < count; i++)
                    odposlech[odposlechPos++ % OdposlechLen] = data[offset + i];
            }
        }

        private void ZacniSbirat()
        {
            lock (odposlechLock)
            {
                Array.Clear(odposlech, 0, odposlech.Length);
                odposlechPos = 0;
                odposlechZapnut = true;
            }
        }

        private void PrestanSbirat()
        {
            lock (odposlechLock) odposlechZapnut = false;
        }

        private byte[] Snapshot()
        {
            lock (odposlechLock)
            {
                int n = Math.Min(odposlechPos, OdposlechLen);
                var kopie = new byte[n];
                Array.Copy(odposlech, kopie, n);
                return kopie;
            }
        }

        /// <summary>
        /// <b>Precte registr</b> — posle <c>VNRRG</c> a hleda odpoved v prichozim BINARNIM toku.
        /// <c>null</c> = nepodarilo se (zapsano do <see cref="Trace"/>).
        ///
        /// <para>⚠️ <b>Neni to rychla operace</b> (az ~1 s na pokus, tri pokusy) a vola se
        /// z jineho vlakna nez cteci smycka. Volat jen pri zakladani a ukonceni mise,
        /// <b>nikdy v ridici smycce</b>.</para>
        ///
        /// <para>Odpoved se hleda kazdych 20 ms, ne az na konci timeoutu: kruhovy odposlech se
        /// pri ~9 kB/s prepise za necelou pulsekundu, takze cekat cely timeout by znamenalo
        /// odpoved prehlednout.</para>
        /// </summary>
        public double[] ReadRegister(int reg, int pokusu = 3)
        {
            for (int p = 0; p < pokusu; p++)
            {
                ZacniSbirat();
                try
                {
                    WriteCommand(VnCommands.ReadRegister(reg));
                    var az = TimeBase.Now.AddSeconds(1);
                    while (TimeBase.Now < az)
                    {
                        if (VnCommands.TryParseResponse(Snapshot(), reg, out var v)) return v;
                        Thread.Sleep(20);
                    }
                }
                finally { PrestanSbirat(); }

                Trace.WriteLine($"VN100: registr {reg} neodpovedel (pokus {p + 1}/{pokusu}).");
            }
            return null;
        }

        /// <summary>
        /// Zapise registr a <b>overi to zpetnym ctenim</b>; <c>true</c> = zapis potvrzen.
        ///
        /// <para>⚠️ Zpetne cteni cte z <b>RAM</b>, ne z flash — o trvalosti nerika nic.</para>
        /// </summary>
        /// <param name="telo">Telo prikazu z <see cref="VnCommands"/> (bez ramovani).</param>
        /// <param name="reg">Registr, ktery se ma zpetne precist.</param>
        /// <param name="ocekavano">Hodnoty k porovnani; <c>null</c> = jen ze odpovi.</param>
        public bool WriteRegister(string telo, int reg, double[] ocekavano = null)
        {
            if (telo == null) throw new ArgumentNullException(nameof(telo));

            WriteCommand(telo);
            Thread.Sleep(400);

            var zpet = ReadRegister(reg);
            if (zpet == null)
            {
                Trace.WriteLine($"VN100: zapis registru {reg} nelze overit - neodpovida.");
                return false;
            }
            if (ocekavano == null) return true;

            for (int i = 0; i < Math.Min(zpet.Length, ocekavano.Length); i++)
                if (Math.Abs(zpet[i] - ocekavano[i]) > 1e-3)
                {
                    Trace.WriteLine($"VN100: registr {reg} po zapisu nesouhlasi na pozici {i}:"
                                    + $" {zpet[i]} != {ocekavano[i]}.");
                    return false;
                }
            return true;
        }

        /// <summary>
        /// Posle prikaz bez zpetneho cteni a pocka. Pro <c>VNWNV</c> (ulozeni do flash), ktere
        /// trva ~1 s a senzor pritom neodpovida — a hlavne <b>nema co zpetne cist</b>.
        /// </summary>
        public void SendCommand(string telo, TimeSpan cekani)
        {
            if (telo == null) throw new ArgumentNullException(nameof(telo));
            WriteCommand(telo);
            Thread.Sleep(cekani);
        }

        protected override IMUState GetMeasurement()
        {
            // 1) synchronizace na 0xFA
            byte[] one;
            do
            {
                one = ReadExact(1);
                if (one == null)
                    return null;
            } while (one[0] != Sync);

            // 2) groups byte + masky přítomných skupin
            var groupsB = ReadExact(1);
            if (groupsB == null)
                return null;
            byte groups = groupsB[0];
            if (groups >> GroupEnums.Length != 0)
            {
                Debug.WriteLine("VN100IMUBinary: neznámá skupina v paketu, přeskočeno.");
                return null;
            }

            // hlavička (groups + masky) se počítá do CRC spolu s payloadem
            var header = new System.Collections.Generic.List<byte> { groups };
            var masks = new ushort[GroupEnums.Length];
            for (int g = 0; g < GroupEnums.Length; g++)
            {
                if (((groups >> g) & 1) == 0)
                    continue;
                var mb = ReadExact(2);
                if (mb == null)
                    return null;
                masks[g] = (ushort)(mb[0] | (mb[1] << 8));
                header.Add(mb[0]);
                header.Add(mb[1]);
            }

            // 3) délka payloadu z velikostí polí
            int payloadLen = PayloadLength(groups, masks);
            if (payloadLen < 0)
            {
                Debug.WriteLine("VN100IMUBinary: neznámé pole, paket zahozen.");
                return null;
            }

            var payload = ReadExact(payloadLen);
            if (payload == null)
                return null;
            var crcB = ReadExact(2);
            if (crcB == null)
                return null;

            // 4) kontrola CRC16 (přes groups+masky+payload)
            ushort crcCalc = Crc16(header.ToArray(), 0, header.Count);
            crcCalc = Crc16(payload, 0, payload.Length, crcCalc);
            ushort crcRecv = (ushort)((crcB[0] << 8) | crcB[1]);
            if (crcCalc != crcRecv)
            {
                Debug.WriteLine("VN100IMUBinary: chybné CRC, paket zahozen.");
                return null;
            }

            // 5) dekódování polí
            var state = DecodePacket(groups, masks, payload);
            if (state == null)
                return null;
            state.TimeStamp = TimeBase.Now;
            state.Name = Name;

            if (double.IsNaN(state.Rotation.Value.Z))
            {
                Debug.WriteLine("VN100IMUBinary: NaN v orientaci, paket zahozen.");
                return null;
            }
            return state;
        }

        /// <summary>Délka payloadu [B] daná nastavenými poli, nebo -1 při neznámém poli.</summary>
        internal static int PayloadLength(byte groups, ushort[] masks)
        {
            int len = 0;
            for (int g = 0; g < GroupEnums.Length; g++)
            {
                if (((groups >> g) & 1) == 0 || masks[g] == 0)
                    continue;
                for (int bit = 0; bit < 16; bit++)
                {
                    if (((masks[g] >> bit) & 1) == 0)
                        continue;
                    int sz = FieldSize(GroupEnums[g], 1 << bit);
                    if (sz < 0)
                        return -1;
                    len += sz;
                }
            }
            return len;
        }

        /// <summary>
        /// Dekóduje pole payloadu do <see cref="IMUState"/> (bez časové značky). Vrací null,
        /// když chybí orientace (Ypr). Oddělené kvůli testovatelnosti (bez UARTu/HW).
        /// </summary>
        internal static IMUState DecodePacket(byte groups, ushort[] masks, byte[] payload)
        {
            Vector3? yprDeg = null;   // VN Ypr [yaw, pitch, roll] ve stupních (azimut)
            Vector3? gyro = null, accel = null, mag = null, magRaw = null, ypru = null;

            int off = 0;
            for (int g = 0; g < GroupEnums.Length; g++)
            {
                if (((groups >> g) & 1) == 0 || masks[g] == 0)
                    continue;
                for (int bit = 0; bit < 16; bit++)
                {
                    if (((masks[g] >> bit) & 1) == 0)
                        continue;
                    int fieldVal = 1 << bit;
                    int sz = FieldSize(GroupEnums[g], fieldVal);
                    string name = Enum.GetName(GroupEnums[g], fieldVal);

                    if (GroupEnums[g] == typeof(BinaryOutputConfig.ImuGroupOptions))
                    {
                        if (name == "Mag") mag = Vec3(payload, off);
                        // Nekompenzovane pole (pred registrem 23) kvuli kalibraci magnetometru.
                        else if (name == "UncompMag") magRaw = Vec3(payload, off);
                        else if (name == "Accel") accel = Vec3(payload, off);
                        else if (name == "Gyro") gyro = Vec3(payload, off);
                    }
                    else if (GroupEnums[g] == typeof(BinaryOutputConfig.AttitudeGroupOptions))
                    {
                        if (name == "Ypr") yprDeg = Vec3(payload, off);
                        else if (name == "YprU") ypru = Vec3(payload, off);
                    }
                    off += sz;
                }
            }

            if (yprDeg == null)
                return null;

            // VN dává yaw jako azimut (0 = sever, +po směru hod. ručiček). Uložíme orientaci v
            // ENU/matematické konvenci (0 = východ, +proti směru hod.), aby Rotation odpovídalo
            // konvenci projektu; převod azimut→orientace přes Conversions.
            var d = yprDeg.Value;
            var ypr = new YawPitchRoll(
                (float)Conversions.Azimut2Orientation(Conversions.Deg2Rad(d.X)),
                (float)Conversions.Deg2Rad(d.Y),
                (float)Conversions.Deg2Rad(d.Z));
            var state = new IMUState(ypr.ToQuaternion(YawPitchRoll.Euler.zxy));
            // VN výstup je (díky reference frame rotation na VN100) FRD: X vpřed, Y vpravo, Z dolů.
            // Projekt používá body FLU: X vpřed, Y vlevo, Z nahoru → převod FRD→FLU = negace Y, Z.
            // Tím je AngularVelocity.Z rovnou ENU yaw rate (CCW+) a Acceleration.Z je +g nahoru.
            state.AngularVelocity = FrdToFlu(gyro);
            state.Acceleration = FrdToFlu(accel);
            state.Magnetometer = FrdToFlu(mag);
            // Tyz prevod ramce: reference frame rotation (reg 26) plati na kompenzovane
            // i nekompenzovane pole, takze obe musi skoncit ve stejnych osach.
            state.MagnetometerRaw = FrdToFlu(magRaw);
            // YprU je [yaw, pitch, roll] ve stupních (1σ) → radiány
            if (ypru is Vector3 u)
                state.OrientationUncertainty = new Vector3(
                    (float)Conversions.Deg2Rad(u.X),
                    (float)Conversions.Deg2Rad(u.Y),
                    (float)Conversions.Deg2Rad(u.Z));
            state.Confidence = 1;
            return state;
        }

        /// <summary>Převod vektoru z VN body frame FRD (X vpřed, Y vpravo, Z dolů) na projektové FLU.</summary>
        private static Vector3? FrdToFlu(Vector3? v) =>
            v == null ? (Vector3?)null : new Vector3(v.Value.X, -v.Value.Y, -v.Value.Z);

        private static Vector3 Vec3(byte[] p, int off) =>
            new Vector3(
                BitConverter.ToSingle(p, off),
                BitConverter.ToSingle(p, off + 4),
                BitConverter.ToSingle(p, off + 8));

        private byte[] ReadExact(int n)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = uart.Read(buf, got, n - got);
                if (r <= 0)
                    return null;
                // Odbocka pro ASCII odpovedi na VNRRG: tohle je JEDINE misto, kudy bajty
                // z portu tecou, takze druhy ctenar neni potreba (a byl by skodlivy).
                Odposlechni(buf, got, r);
                got += r;
            }
            return buf;
        }

        // --- pomůcky ---

        private static int FieldSize(Type enumType, int fieldVal)
        {
            string name = Enum.GetName(enumType, fieldVal);
            if (name == null)
                return -1;
            var fi = enumType.GetField(name);
            var at = fi?.GetCustomAttribute<BinaryOutputInfoAttribute>();
            return at != null ? at.DataSize : -1;
        }

        /// <summary>CRC16-CCITT (stejný algoritmus jako VN ASCII), nad bajty.</summary>
        private static ushort Crc16(byte[] data, int start, int len, ushort crc = 0)
        {
            for (int i = start; i < start + len; i++)
            {
                crc = (ushort)((crc >> 8) | (crc << 8));
                crc ^= data[i];
                crc ^= (ushort)((crc & 0xFF) >> 4);
                crc ^= (ushort)(crc << 8 << 4);
                crc ^= (ushort)((crc & 0xFF) << 4 << 1);
            }
            return crc;
        }
    }
}
