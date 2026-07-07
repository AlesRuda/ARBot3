using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    /// <summary>
    /// Uart abstraction
    /// </summary>
    public interface IUart
    {
        /// <summary>
        /// Zda je uart otevren
        /// </summary>
        bool IsOpen { get; }
        /// <summary>
        /// Read timeout in ms
        /// </summary>
        int ReadTimeout { get; set; }
        /// <summary>
        /// Reads data from uart
        /// </summary>
        /// <param name="buffer">Buffer to store data</param>
        /// <param name="offset">Offset to the buffer</param>
        /// <param name="count">Max read bytes</param>
        /// <returns>Readed bytes</returns>
        int Read(byte[] buffer, int offset, int count);
        /// <summary>
        /// Reads data from uart
        /// </summary>
        /// <param name="count">Bytes to read</param>
        /// <returns>Readed bytes</returns>
        Task<byte[]> ReadAsync(int count);
        /// <summary>
        /// Reads data from uart
        /// </summary>
        /// <param name="count">Bytes to read</param>
        /// <returns>Readed bytes</returns>
        byte[] Read(int count);
        /// <summary>
        /// Read line
        /// </summary>
        /// <returns></returns>
        string ReadLine();
        /// <summary>
        /// Read all
        /// </summary>
        /// <returns></returns>
        string ReadAll();
        /// <summary>
        /// Read line async
        /// </summary>
        /// <returns></returns>
        Task<string> ReadLineAsync();
        /// <summary>
        /// Writes bytes to uart
        /// </summary>
        /// <param name="buffer"></param>
        void Write(byte[] buffer);
        /// <summary>
        /// Writes line
        /// </summary>
        /// <param name="txt"></param>
        void WriteLine(string txt);
    }
}
