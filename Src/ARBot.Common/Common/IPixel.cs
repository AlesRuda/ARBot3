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

        /// <summary>
        /// Cervena slozka pixelu na pozici <see cref="Index"/>.
        ///
        /// <para><b>Kanaly R/G/B jsou jediny slibeny zpusob, jak se dostat k barve</b> nezavisle na
        /// pixel typu. <see cref="Values"/> to neni: tohle rozhrani u nej neslibuje ani delku, ani
        /// poradi slozek, takze algoritmus, ktery z nej barvu cte, se opira o nahodu (dnesni typy
        /// ho plni z pojmenovanych vlastnosti, tedy vzdy <c>[R,G,B]</c>) — a pixel typ s jinou
        /// reprezentaci barvy, treba YUV, by mu ticho podstrcil <c>[Y,U,V]</c>.</para>
        ///
        /// <para>Proti <see cref="Color"/> maji tu vyhodu, ze <b>nealokuji</b>: <see cref="Color"/>
        /// je <c>class</c>, takze jeho cteni v cyklu pres obraz znamena alokaci na kazdy pixel
        /// (u 640x480 tri sta tisic na snimek).</para>
        ///
        /// <para>Jednoslozkove (sede) pixel typy vraci svou hodnotu ve vsech treh kanalech. Typy
        /// sirsi nez bajt (<see cref="Gray16"/>, <see cref="Gray32"/>) vraci <b>nejvyssi bajt</b>,
        /// tedy hodnotu <b>skalovanou</b> do rozsahu bajtu — stejnou konvenci, jakou uz ma
        /// <see cref="Color"/>. Saturace na 255 by byla horsi ve dvou ohledech: tentyz pixel by
        /// hlasil jinou barvu pres <c>R</c> a jinou pres <c>Color.R</c>, a z hloubkoveho obrazu
        /// v milimetrech (hodnoty v tisicich) by udelala bilou placku.</para>
        /// </summary>
        byte R { get; }

        /// <summary>Zelena slozka pixelu na pozici <see cref="Index"/>; viz <see cref="R"/>.</summary>
        byte G { get; }

        /// <summary>Modra slozka pixelu na pozici <see cref="Index"/>; viz <see cref="R"/>.</summary>
        byte B { get; }
    }
}
