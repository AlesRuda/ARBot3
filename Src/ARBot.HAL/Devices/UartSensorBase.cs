using ARBot.Common.Devices;
using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Trida pro senzory, ktere komunikujou pres UART
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public abstract class UartSensorBase<TState> :SensorBase<TState> where TState : class
    {
        /// <summary>
        /// Rozhrani pro komunikaci pres UART
        /// </summary>
        protected readonly IUart uart;
        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="uart"></param>
        public UartSensorBase(IUart uart)
        {
            if (uart == null)
                throw new ArgumentNullException("urat");
            this.uart = uart;
        }

        /// <summary>
        /// Zda je senzor v chybovem stavu
        /// </summary>
        public override bool IsError => base.IsError || !uart.IsOpen;
    }
}
