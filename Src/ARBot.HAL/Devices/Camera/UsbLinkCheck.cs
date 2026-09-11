using System;
using System.Globalization;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// <b>Posudek USB linky kamery</b> podle deskriptoru, který librealsense hlásí jako
    /// <c>CameraInfo.UsbTypeDescriptor</c> („3.2", „2.1", …). Řekne, jestli kamera naběhla na
    /// SuperSpeed, nebo spadla na USB 2.0 — a to druhé je u nás **porucha, ne zpomalení**.
    ///
    /// <para><b>Nač to je (11. 9. 2026):</b> driver ten údaj do té doby nečetl vůbec (z
    /// <c>Device.Info</c> se bralo jen <c>SerialNumber</c> a <c>Name</c>), takže kamera naběhlá na
    /// 480 Mbps vypadala jako porucha streamu, ne jako špatná linka. Stalo se to 2. 9. 2026: po
    /// restartu se obě D435 vyčetly jako <c>new high-speed USB device</c> (<c>speed=480</c>),
    /// přestože předchozí den jely na 5 Gbps a s kabely se nemanipulovalo — <b>nenaskočily
    /// SuperSpeed linky hub↔kamera</b>. Kernel si přitom ani jednou nestěžoval, takže se příčina
    /// hodinu hledala měřením zvenčí. Léčba je fyzický replug; tahle třída jen zajistí, že to
    /// driver <b>rovnou napíše</b> místo aby se to hledalo. Viz
    /// <c>OrangePi5Ultra/POSTUP.md</c> a <c>doc/hardware.md</c>.</para>
    ///
    /// <para><b>Proč je USB 2.0 porucha, ne zpomalení:</b> dvě D435 se tam nevejdou. Při našem
    /// nastavení (RGB 640×480 + hloubka 480×270, obojí 30 fps) potřebuje každá kamera ~210–283 Mbps,
    /// dvě tedy ~420–570 Mbps — proti 480 Mbps hrubě (reálně ~280–320) u USB 2.0. Na USB 3 je to
    /// naopak 13–18 % kapacity, tedy pohodlně. Proto se na USB 2.0 hlásí <b>varování</b>: kombinace
    /// hloubky a barvy je tam nesplnitelná a pipeline se buď nerozjede, nebo bude zahazovat snímky.
    /// (Přesně tak vypadala porucha 2. 9. 2026.)</para>
    ///
    /// <para><b>Společné pro obě platformy</b> — <c>HALWindows</c> i <c>HALArmbian</c> mají vlastní
    /// kopii <c>D435Camera</c>, ale tahle logika je jedna a je otestovaná; stejně jako
    /// <see cref="StreamFreezeWatch"/>.</para>
    /// </summary>
    public static class UsbLinkCheck
    {
        /// <summary>
        /// Rozhodne, jestli deskriptor znamená USB 2.x nebo starší (tedy pro dvě D435 nedostatečnou
        /// linku). Hodnota je řetězec typu „3.2" / „2.1"; bere se <b>hlavní číslo</b> před tečkou.
        ///
        /// <para><b>Neznámá hodnota není porucha.</b> <c>null</c> / prázdno / nesrozumitelný tvar
        /// vrací <c>false</c> — librealsense ten údaj u některých zařízení nehlásí (indexer
        /// <c>Info[]</c> vrací <c>null</c>, když ho zařízení nepodporuje) a hlásit poruchu kvůli
        /// chybějící diagnostice by znamenalo vyrobit falešný poplach z nástroje, který má poplachy
        /// vysvětlovat. Stejná konvence jako u brány kvality GPS („přijímač, který DOP nehlásí,
        /// projde").</para>
        /// </summary>
        /// <param name="usbTypeDescriptor">Hodnota <c>CameraInfo.UsbTypeDescriptor</c>, může být null.</param>
        public static bool JeUsb2(string usbTypeDescriptor)
        {
            return TryHlavniVerze(usbTypeDescriptor, out int verze) && verze < 3;
        }

        /// <summary>
        /// Sestaví větu o lince do logu připojení pipeline. Na USB 2.0 je v ní i <b>důvod a léčba</b>,
        /// protože právě tam se bez toho hledala příčina zvenčí.
        /// </summary>
        /// <param name="usbTypeDescriptor">Hodnota <c>CameraInfo.UsbTypeDescriptor</c>, může být null.</param>
        /// <returns>Text k připojení za hlášku „pipeline pripojena".</returns>
        public static string Popis(string usbTypeDescriptor)
        {
            if (string.IsNullOrWhiteSpace(usbTypeDescriptor))
                return "USB: neuvedeno (zarizeni deskriptor nehlasi)";

            string usb = usbTypeDescriptor.Trim();
            if (!JeUsb2(usb))
                return $"USB {usb}";

            return $"USB {usb} - POZOR, kamera nenabehla na SuperSpeed. Hloubka i barva se na USB 2.0 "
                 + "nevejdou (dve D435 potrebuji ~420-570 Mbps proti realnym ~280-320), takze snimky "
                 + "budou chybet nebo se pipeline nerozjede. Lecba je FYZICKE odpojeni a pripojeni "
                 + "kamery - viz OrangePi5Ultra/POSTUP.md, 2. 9. 2026.";
        }

        /// <summary>Vytáhne hlavní číslo verze („3.2" → 3). Vrací false, když to nejde přečíst.</summary>
        private static bool TryHlavniVerze(string usbTypeDescriptor, out int verze)
        {
            verze = 0;
            if (string.IsNullOrWhiteSpace(usbTypeDescriptor))
                return false;

            string s = usbTypeDescriptor.Trim();
            int tecka = s.IndexOf('.');
            string hlavni = tecka >= 0 ? s.Substring(0, tecka) : s;

            return int.TryParse(hlavni, NumberStyles.Integer, CultureInfo.InvariantCulture, out verze);
        }
    }
}
