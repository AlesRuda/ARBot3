using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Sdilene vykresleni pudorysneho tvaru robota (telo + 4 kola; prevzato z puvodni WPF appky).
    /// Pouziva ho robot-centricky pohled (orientace „vpred = nahoru") i budouci world view
    /// (robot na sve pozici a se svou orientaci). Tvar je definovan v METRECH v lokalnim ramci
    /// (lx = vpravo, ly = vpred/spicka); vola se s meritkem (px/m), stredem [px] a orientaci.
    /// </summary>
    public static class RobotGlyph
    {
        // Obrys v metrech (lx vpravo, ly vpred). FillRule EvenOdd vytvori z prekryvu "diry" kol.
        private static readonly (double lx, double ly)[] Outline =
        {
            (-0.15, -0.15), (0.15, -0.15), (0.15, 0.00), (0.20, 0.00), (0.20, -0.10),
            (0.25, -0.10), (0.25, 0.10), (0.20, 0.10), (0.20, 0.00), (0.15, 0.00),
            (0.15, 0.20), (0.05, 0.30), (-0.05, 0.30), (-0.15, 0.20), (-0.15, 0.00),
            (-0.20, 0.00), (-0.20, 0.10), (-0.25, 0.10), (-0.25, -0.10), (-0.20, -0.10),
            (-0.20, 0.00), (-0.15, 0.00), (-0.15, -0.15),
        };

        /// <summary>Obrys robota v metrech (lx = vpravo, ly = vpřed) — jediný zdroj tvaru; využívá ho
        /// i world view k vykreslení robota jako metrického polygonu na mapě (mimo Avalonia render).</summary>
        public static IReadOnlyList<(double lx, double ly)> OutlineMeters => Outline;

        /// <summary>
        /// Prevede bod obrysu z lokalniho ramce robota do sveta ENU (east, north) pro danou orientaci.
        /// <para><b>Pozor na konvenci obrysu:</b> <see cref="Outline"/> je v puvodni WPF konvenci
        /// s osou <b>Y DOLU</b>, takze vpred je <c>-ly</c>. Prevod je proto soucasti teto metody -
        /// drive byl rozkopirovany ve <see cref="Draw"/> a ve world view, kde se pri kopirovani
        /// ztratil a robot se kreslil otoceny o 180 stupnu.</para>
        /// </summary>
        /// <param name="lx">Lokalni souradnice vpravo [m].</param>
        /// <param name="ly">Lokalni souradnice v konvenci obrysu (Y dolu) [m].</param>
        /// <param name="orientationRad">Orientace v matematickem smyslu (0 = vychod, +CCW).</param>
        public static (double East, double North) ToWorld(double lx, double ly, double orientationRad)
        {
            double s = Math.Sin(orientationRad), c = Math.Cos(orientationRad);
            return ToWorld(lx, ly, s, c);
        }

        /// <summary>Varianta s predpocitanym sinem a kosinem (pro smycky pres cely obrys).</summary>
        public static (double East, double North) ToWorld(double lx, double ly, double sin, double cos)
        {
            double lym = -ly;                       // WPF Y dolu -> matematicke "vpred"
            return (lx * sin + lym * cos,           // east
                    -lx * cos + lym * sin);         // north
        }

        /// <summary>Dosah tvaru od počátku (osy otáčení) v metrech — robot NENÍ symetrický
        /// (dozadu delší než dopředu). Pro layout, aby se celý robot vešel do pohledu.</summary>
        public static readonly double ForwardExtentMeters;
        public static readonly double RearExtentMeters;
        public static readonly double SideExtentMeters;

        static RobotGlyph()
        {
            double f = 0, r = 0, s = 0;
            foreach (var (lx, ly) in Outline)
            {
                double lym = -ly;                 // stejny prevod jako pri kresleni (WPF Y dolu -> math)
                if (lym > f) f = lym;
                if (-lym > r) r = -lym;
                double ax = Math.Abs(lx);
                if (ax > s) s = ax;
            }
            ForwardExtentMeters = f; RearExtentMeters = r; SideExtentMeters = s;
        }

        /// <summary>
        /// Vykresli robota. <paramref name="cx"/>/<paramref name="cy"/> je stred (pocatek robotu)
        /// v obrazovych px (volajici si ho spocte z pozice robota). <paramref name="pxPerMeter"/>
        /// je meritko. <paramref name="orientationRad"/> je orientace v matematickem smyslu
        /// (0 = vychod/+X, +CCW); pro „vpred = nahoru" predej <c>PI/2</c>. Osy sveta: X vpravo,
        /// Y nahoru (screen Y se prevraci).
        /// </summary>
        public static void Draw(DrawingContext ctx, double cx, double cy, double pxPerMeter,
                                double orientationRad, IBrush fill = null, IPen stroke = null,
                                bool drawCenterCross = true)
        {
            double s = Math.Sin(orientationRad), c = Math.Cos(orientationRad);

            // Obrys je v puvodni WPF konvenci (osa Y DOLU); prevedeme na matematickou (Y NAHORU)
            // pres lym = -ly. Jinak by byl robot otoceny vzhuru nohama (spravne: predni sirsi strana
            // dopredu). Pak: lokalni (lx vpravo, lym vpred) -> svet (rotace o orientaci) -> obrazovka.
            Point P(double lx, double ly)
            {
                var (wx, wy) = ToWorld(lx, ly, s, c);
                return new Point(cx + wx * pxPerMeter, cy - wy * pxPerMeter);
            }

            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.SetFillRule(FillRule.EvenOdd);
                gc.BeginFigure(P(Outline[0].lx, Outline[0].ly), true);
                for (int i = 1; i < Outline.Length; i++)
                    gc.LineTo(P(Outline[i].lx, Outline[i].ly));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(fill ?? Brushes.Yellow, stroke ?? new Pen(Brushes.Black, 1), geo);

            if (drawCenterCross)
            {
                var cross = new Pen(Brushes.Red, 1);
                ctx.DrawLine(cross, new Point(cx - 10, cy), new Point(cx + 10, cy));
                ctx.DrawLine(cross, new Point(cx, cy - 10), new Point(cx, cy + 10));
            }
        }
    }
}
