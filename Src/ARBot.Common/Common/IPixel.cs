using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Windows.Media;
using System.Xml.Serialization;

namespace ARBot.Common.Common
{
/*    [XmlInclude(typeof(YUV))]
    [XmlInclude(typeof(RGB))]
    [XmlInclude(typeof(GrayPixel))]
    [XmlInclude(typeof(HSV))]*/
    /// <summary>
    /// Rozhrani pixelu obrazku
    /// </summary>
    public interface IPixel
    {
        /// <summary>
        /// Format pixelu
        /// </summary>
//        PixelFormat Format { get; }
        /// <summary>
        /// Pocet byte na jeden pixel
        /// </summary>
        int Count {get;}
        /// <summary>
        /// Pole bajtu reprezentujici data obrazku pocinaje pixelem [0, 0]
        /// </summary>
        byte[] Data
        {
            get;
            set;
        }
        /// <summary>
        /// Index pixelu v obrazku tj. x+y*Width
        /// </summary>
        int Index
        {
            get;
            set;
        }
        /// <summary>
        /// Pole int reprezentujici pixel n pozici Index.
        /// Pozor - kvuli rychlosti je pole instancni
        /// </summary>
        int[] Values
        {
            get;
            set;
        }
        /// <summary>
        /// Barva pixelu na pozici Index
        /// </summary>
        Color Color
        {
            get;
            set;
        }
    }
}
