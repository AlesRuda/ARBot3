using System;
using System.Collections.Generic;
using System.Device.Spi;
using System.Runtime.InteropServices;

namespace ARBot.HAL.Devices.NeoPixel
{
    /// <summary>
    /// Ovladac WS2812 (NeoPixel) pasku pro Armbian/Orange Pi pres SPI (spidev).
    /// <para>
    /// WS2812 se na Pi budi datovym vystupem SPI (MOSI): kazdy bit LED je zakodovan
    /// jako kratky/dlouhy puls slozeny z nekolika SPI "sub-bitu" (viz
    /// <see cref="SpiNeoPixelDriver.PulseConfig"/>). Zakladni trida sestavi cely buffer
    /// sub-bitu (MSB-first, stejne poradi jako posila SPI) a tato trida ho jednim
    /// zapisem posle na sbernici.
    /// </para>
    /// <para>
    /// SPI zarizeni vstupuje jako parametr - vlastnik (volajici) ho vytvori, nakonfiguruje
    /// (bus/CS, hodinovy kmitocet, rezim) a zodpovida i za jeho uvolneni (Dispose).
    /// Hodinovy kmitocet SPI musi odpovidat zvolenemu <see cref="SpiNeoPixelDriver.PulseConfig"/>
    /// tak, aby jeden sub-bit trval ~pozadovanou dobu (WS2812 bit ~1,25 us).
    /// </para>
    /// <para>
    /// Zapojeni (viz OrangePi5Ultra/POSTUP.md): DIN -> pin 13 = SPI4_MOSI_M2 (GPIO1_A1), GND -> GND;
    /// overlay <c>spi4-m2-cs0-spidev</c> zpristupni <c>/dev/spidev4.0</c> (max 50 MHz).
    /// Priklad vytvoreni SPI (tak ho zaklada ARBotHW):
    /// <code>
    /// var spi = SpiDevice.Create(new SpiConnectionSettings(4, 0)
    /// {
    ///     ClockFrequency = SpiNeoPixelDriver.OrangePiClockHz,   // 4 sub-bity/bit -> bit 1,28 us
    ///     Mode = SpiMode.Mode0,
    ///     DataBitLength = 8
    /// });
    /// var driver = new ArmbianSpiNeoPixelDriver(spi, SpiNeoPixelDriver.PulseConfig.OrangePi);
    /// </code>
    /// </para>
    /// </summary>
    public class ArmbianSpiNeoPixelDriver : SpiNeoPixelDriver
    {
        private readonly SpiDevice spi;

        /// <summary>
        /// Vytvori ovladac nad zadanym (jiz nakonfigurovanym) SPI zarizenim.
        /// </summary>
        /// <param name="spi">
        /// SPI zarizeni (napr. <c>/dev/spidev4.0</c>). Vlastnictvi zustava volajicimu -
        /// tato trida ho neuvolnuje.
        /// </param>
        /// <param name="config">
        /// Casovani pulsu WS2812 v poctu SPI sub-bitu; musi odpovidat hodinovemu kmitoctu SPI.
        /// </param>
        public ArmbianSpiNeoPixelDriver(SpiDevice spi, PulseConfig config) : base(config)
        {
            this.spi = spi ?? throw new ArgumentNullException(nameof(spi));
        }

        /// <summary>
        /// Posle sestaveny buffer sub-bitu na SPI jednim zapisem (MOSI drzi WS2812 timing).
        /// </summary>
        protected override void WriteData(List<byte> values)
        {
            if (values == null || values.Count == 0)
                return;

            // AsSpan bez kopie - values patri zakladni tride a behem zapisu se nemeni.
            spi.Write(CollectionsMarshal.AsSpan(values));
        }
    }
}
