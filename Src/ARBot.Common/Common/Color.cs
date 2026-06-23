using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class Color
    {
        public static Color Black = new Color(0, 0, 0);
        public static Color White = new Color(255, 255, 255);
        public static Color Yellow = new Color(255, 255, 0);
        public static Color Red = new Color(255, 0, 0);
        public static Color Green = new Color(0, 255, 0);
        public static Color Blue = new Color(0, 0, 255);

        public byte R, G, B;
        public Color()
        {
        }
        public Color(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public override int GetHashCode()
        {
            return R << 16 + G << 8 + B;
        }

        public override bool Equals(object obj)
        {
            var c = obj as Color;
            if (c != null)
                return c.R == R && c.G == G && c.B == B;
            return base.Equals(obj);
        }
    }
}
