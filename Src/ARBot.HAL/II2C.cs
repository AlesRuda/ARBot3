using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// I2C bus interface
    /// </summary>
    public interface II2C
    {
        /// <summary>
        /// Writes data to adress.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="register"></param>
        /// <param name="data"></param>
        void Write(int address, byte register, byte[] data);
        /// <summary>
        /// Writes data to adress.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="register"></param>
        /// <param name="data"></param>
        void Write(int address, byte register, byte data);
        /// <summary>
        /// Reads 
        /// </summary>
        /// <param name="address"></param>
        /// <param name="register"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        byte[] Read(int address, byte register, int len);
    }
}
