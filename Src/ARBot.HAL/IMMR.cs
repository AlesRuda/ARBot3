using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Pristup k memory maped registrum
    /// </summary>
    public interface IMMR
    {
        /// <summary>
        /// Nacteni 8 bitoveho slova
        /// </summary>
        /// <param name="adr">Adresa registru</param>
        /// <returns></returns>
        uint Get8(int adr);
        /// <summary>
        /// Cte 16bitovou hodnotu z adr
        /// </summary>
        /// <param name="adr">Adresa wordu. Word s adresou 1 je pristupny jako dva byte s adresou 2 a 3.</param>
        /// <returns></returns>
        uint Get16(int adr);
        /// <summary>
        /// Cte 32bitovou hodnotu z adr
        /// </summary>
        /// <param name="adr">Adresa dwordu. DWord s adresou 1 je pristupny jako dva wordy s adresou 2 a 3.</param>
        /// <returns></returns>
        uint Get32(int adr);
        /// <summary>
        /// Nastaveni 8 bitoveho registru
        /// </summary>
        /// <param name="adr">Adresa registru.</param>
        /// <param name="val">Hodnota registru, pouziva se spodnich 8 bitu</param>
        void Set8(int adr, uint val);
        /// <summary>
        /// Nastaveni 16 bitoveho registru
        /// </summary>
        /// <param name="adr">Adresa wordu. Word s adresou 1 je pristupny jako dva byte s adresou 2 a 3.</param>
        /// <param name="val">Hodnota registru, pouziva se spodnich 16 bitu</param>
        void Set16(int adr, uint val);
        /// <summary>
        /// Nastaveni 32 bitoveho registru
        /// </summary>
        /// <param name="adr">Adresa dwordu. DWord s adresou 1 je pristupny jako dva wordy s adresou 2 a 3.</param>
        /// <param name="val">Hodnota registru, pouziva se spodnich 32 bitu</param>
        void Set32(int adr, uint val);
    }
}
