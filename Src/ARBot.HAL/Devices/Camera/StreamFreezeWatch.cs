using System;
using ARBot.Common.Common;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// <b>Hlídka zamrzlého streamu kamery.</b> Sleduje razítka jednotlivých streamů (barva,
    /// hloubka) a řekne, když některé z nich stojí dýl než <see cref="LimitSec"/>.
    ///
    /// <para><b>Nač to je (6. 9. 2026):</b> zásek nemusí znamenat, že snímky přestanou chodit.
    /// Na zařízení se ukázalo, že <b>frameset chodí dál 10 Hz, hloubka se mění, ale BARVA je pořád
    /// tatáž</b> — librealsense vrací v každém framesetu tentýž barevný snímek. Čítač timeoutů
    /// v driveru to nechytí (žádný timeout není) a nic jiného to nesledovalo: <c>IsError</c> hlásil
    /// OK, stránka náhledu svítila zeleně se stářím 12 ms, a přitom „cesta z RGB", occupancy grid
    /// i mise jely nad nehybnou fotkou. Záznam <c>20260906-082403.rec</c>: jeden různý barevný
    /// obraz ze 100, hloubka 100 různých — a totéž razítko od začátku do konce 11 minut.</para>
    ///
    /// <para><b>Hlídá se razítko, ne obsah obrazu.</b> Porovnávat pixely by stálo výkon při každém
    /// snímku a nic navíc by to neřeklo: zamrzlý snímek má zamrzlé i razítko (ověřeno nad tím
    /// záznamem). Čas se měří přes <see cref="TimeBase"/>, aby práh neskákal při synchronizaci
    /// hodin (pravidlo v CLAUDE.md).</para>
    ///
    /// <para><b>Společné pro obě platformy</b> — `HALWindows` i `HALArmbian` mají vlastní kopii
    /// <c>D435Camera</c>, ale tahle logika je jedna a je otestovaná.</para>
    /// </summary>
    public sealed class StreamFreezeWatch
    {
        /// <summary>
        /// Jak dlouho smí razítko streamu stát, než se to považuje za zásek [s].
        ///
        /// <para><b>Proč 5 s:</b> pomalá barva je legitimní — automatická expozice v šeru srazí
        /// snímkovou frekvenci pod periodu čtení, takže se opakované snímky běžně objevují — ale
        /// razítko se při tom pořád hýbe. Pět sekund je ~50 čtení; to už žádná expozice nevysvětlí.
        /// Tatáž konstanta jako u T265 („5 s bez pózy → restart pipeline"), ať má projekt jednu
        /// konvenci.</para>
        /// </summary>
        public const double DefaultLimitSec = 5.0;

        private readonly Func<DateTime> hodiny;
        private double posledniBarva, posledniHloubka;
        private DateTime barvaZmenaAt, hloubkaZmenaAt;

        /// <param name="clock">Zdroj času; <c>null</c> = <see cref="TimeBase.Now"/> (testy si ho
        /// podvrhnou, aby nemusely čekat pět sekund).</param>
        public StreamFreezeWatch(double limitSec = DefaultLimitSec, Func<DateTime> clock = null)
        {
            LimitSec = limitSec;
            hodiny = clock ?? (() => TimeBase.Now);
        }

        /// <summary>Práh, po kterém se stojící razítko považuje za zásek [s].</summary>
        public double LimitSec { get; }

        /// <summary>Kolikrát hlídka ohlásila zásek — diagnostika, rostoucí číslo = opakuje se to.</summary>
        public int Detections { get; private set; }

        /// <summary>Začít znovu (po přestavění pipeline mají streamy nová razítka).</summary>
        public void Reset()
        {
            barvaZmenaAt = default;
            hloubkaZmenaAt = default;
        }

        /// <summary>
        /// Zapíše razítka právě přijatého snímku a vrátí <b>popis poruchy</b>, když některý stream
        /// stojí dýl než <see cref="LimitSec"/>; jinak <c>null</c>.
        /// </summary>
        /// <param name="colorStamp">Razítko barevného snímku z driveru; <c>null</c> = stream není.</param>
        /// <param name="depthStamp">Razítko hloubkového snímku; <c>null</c> = stream není.</param>
        public string Check(double? colorStamp, double? depthStamp)
        {
            var ted = hodiny();

            double barvaS = Sleduj(colorStamp, ref posledniBarva, ref barvaZmenaAt, ted);
            double hloubkaS = Sleduj(depthStamp, ref posledniHloubka, ref hloubkaZmenaAt, ted);

            // Barva se hlasi prednostne: zamrzla barva je zakernejsi, protoze z ni vede „cesta
            // z RGB" do occupancy gridu, takze robot jede podle nehybne fotky.
            if (colorStamp.HasValue && barvaS > LimitSec)
            {
                Detections++;
                return $"BARVA zamrzla ({barvaS:F1} s stejne razitko, hloubka {hloubkaS:F1} s)";
            }

            if (depthStamp.HasValue && hloubkaS > LimitSec)
            {
                Detections++;
                return $"HLOUBKA zamrzla ({hloubkaS:F1} s stejne razitko, barva {barvaS:F1} s)";
            }

            return null;
        }

        /// <summary>Jak dlouho stojí jedno razítko [s]; první snímek jen ukotví výchozí stav.</summary>
        private static double Sleduj(double? razitko, ref double posledni, ref DateTime zmenaAt, DateTime ted)
        {
            if (!razitko.HasValue) return 0;

            if (zmenaAt == default || razitko.Value != posledni)
            {
                posledni = razitko.Value;
                zmenaAt = ted;
                return 0;
            }

            return (ted - zmenaAt).TotalSeconds;
        }
    }
}
