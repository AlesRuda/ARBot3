using System;
using System.IO;
using ARBot.HAL.Devices.Uart;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Které chyby čtení <see cref="Uart"/> hlásí do Trace (finální review driveru BMS, 8. 10. 2026):
    /// u BMS, která se jen dotazuje, je „do 100 ms nic nepřišlo" běžný stav, ne porucha portu —
    /// jinak by tichá (nebo jen pomalá) BMS psala do Trace každých 5 s celý zásobník.
    /// </summary>
    public class UartChybyCteniTests
    {
        [Test]
        public void Timeout_SeNehlasi_KdyzJeHlaseniTimeoutuVypnute()
            => Assert.That(Uart.HlasitChybuCteni(new TimeoutException(), hlasitTimeouty: false), Is.False);

        [Test]
        public void Timeout_SeHlasi_VeVychozimStavu()
            => Assert.That(Uart.HlasitChybuCteni(new TimeoutException(), hlasitTimeouty: true), Is.True,
                           "ostatni zarizeni (VN100) se nemeni");

        [Test]
        public void JinaChyba_SeHlasiVzdy()
            => Assert.That(Uart.HlasitChybuCteni(new IOException("port zmizel"), hlasitTimeouty: false), Is.True,
                           "odpojeny prevodnik musi byt videt i u BMS");
    }
}
