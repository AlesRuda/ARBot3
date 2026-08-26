using System;
using ARBot.Common.Common;

namespace ARBot.Common.Vision.Synthetic
{
    /// <summary>
    /// <b>Svisla deska s texturou</b> ve scene virtualni kamery — typicky <b>QR kod na stojanu</b>.
    /// Viz doc/virtual-hw.md.
    ///
    /// <para><b>Nacpak to je:</b> aby se dal v simulaci projit krok mise Robotour, ve kterem robot
    /// cte QR kod. Simulace do 26. 8. 2026 zadny kod nerenderovala, takze servisni okno se nedalo
    /// dokoncit ani rucne — vedeny otevreny ukol z puvodniho navrhu mise.</para>
    ///
    /// <para><b>Kresli se jen do BARVY, ne do hloubky</b> (rozhodnuti): deska je <i>vizualni
    /// znacka</i>, ne fyzicky objekt. Kdyby psala i hloubku, stala by se prekazkou v occupancy gridu
    /// a mohla by ovlivnit detekci koridoru i planovani — tedy zkreslit prave to, co se v simulaci
    /// meri. Cena: v hloubkovem obrazu deska neni, takze se na ni neda merit vizualni dojezd. Az to
    /// bude potreba, je to samostatny krok (a bude chtit vlastni rozhodnuti, protoze pak uz to
    /// prekazka je).</para>
    ///
    /// <para><b>Geometrie:</b> stred <c>(CenterX, CenterY, CenterZ)</c> ve svete [m, ENU],
    /// <see cref="YawRad"/> je smer <b>normaly</b> (kam deska „koukа"), takze deska sama lezi ve
    /// svisle rovine na nej kolmé.</para>
    /// </summary>
    public sealed class SyntheticBillboard
    {
        /// <summary>Stred desky ve svete [m, ENU]; <c>CenterZ</c> je vyska nad vozovkou.</summary>
        public double CenterX, CenterY, CenterZ;

        /// <summary>Smer <b>normaly</b> desky [rad, matematicky] — kam deska koukа.</summary>
        public double YawRad;

        /// <summary>Rozmery desky [m].</summary>
        public double WidthM = 0.3, HeightM = 0.3;

        /// <summary>Textura (typicky vyrenderovany QR kod). <c>null</c> = deska se nekresli.</summary>
        public Image<BGR32> Texture;

        /// <summary>
        /// Protne paprsek <c>(ox,oy,oz) + t·(dx,dy,dz)</c> s deskou.
        ///
        /// <para><paramref name="t"/> je parametr podel paprsku — se stejnou parametrizaci, jakou
        /// pouziva <see cref="SyntheticFrameRenderer"/> pro hloubku, takze se da <b>primo
        /// porovnat</b> se vzdalenosti zasahu vozovky/travy a rozhodnout, co je bliz.</para>
        ///
        /// <para><paramref name="u"/>, <paramref name="v"/> jsou souradnice v texture v rozsahu
        /// [0;1]; <c>v = 0</c> je <b>horni</b> radek obrazu (jinak by se kod prevratil).</para>
        /// </summary>
        public bool TryIntersect(double ox, double oy, double oz, double dx, double dy, double dz,
                                 out double t, out double u, out double v)
        {
            t = 0; u = 0; v = 0;
            if (Texture == null || WidthM <= 0 || HeightM <= 0) return false;

            // Normala desky a jeji vodorovna osa (kolma na normalu, ve vodorovne rovine).
            double nx = Math.Cos(YawRad), ny = Math.Sin(YawRad);
            double ax = -ny, ay = nx;

            double denom = dx * nx + dy * ny;
            // Paprsek (skoro) rovnobezny s deskou - bez teto kontroly by delenim skoro nulou vysel
            // nesmyslny parametr.
            if (Math.Abs(denom) < 1e-12) return false;

            double hit = ((CenterX - ox) * nx + (CenterY - oy) * ny) / denom;
            if (hit <= 0) return false;   // deska je za pozorovatelem

            double hx = ox + hit * dx - CenterX;
            double hy = oy + hit * dy - CenterY;
            double hz = oz + hit * dz - CenterZ;

            double along = hx * ax + hy * ay;      // vodorovne od stredu desky
            if (Math.Abs(along) > WidthM * 0.5) return false;
            if (Math.Abs(hz) > HeightM * 0.5) return false;

            t = hit;
            u = along / WidthM + 0.5;
            // Svisle: vys na desce = MENSI v, protoze radek 0 textury je nahore.
            v = 0.5 - hz / HeightM;
            return true;
        }

        /// <summary>
        /// Vzorek textury pro souradnice <paramref name="u"/>, <paramref name="v"/> z
        /// <see cref="TryIntersect"/>. Nejblizsi soused, ne interpolace: QR je binarni vzor
        /// s ostrymi hranami a rozmazani je presne to, co dekoderu vadi.
        /// </summary>
        public (byte R, byte G, byte B) Sample(double u, double v)
        {
            var tex = Texture;
            if (tex == null) return (0, 0, 0);

            int x = (int)(u * tex.Width);
            int y = (int)(v * tex.Height);
            if (x < 0) x = 0; else if (x >= tex.Width) x = tex.Width - 1;
            if (y < 0) y = 0; else if (y >= tex.Height) y = tex.Height - 1;

            var p = new BGR32 { Data = tex.Data, Index = (y * tex.Width + x) * 4 };
            return (p.R, p.G, p.B);
        }
    }
}
