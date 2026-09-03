using System;
using System.Globalization;
using ARBot.Common.Occupancy;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Maly graf rychlostni obalky lokalniho planu: osa X = vzdalenost od robota po drahe [m],
    /// osa Y = strop rychlosti [m/s]. Kresli se vlastnim <see cref="Render"/> (stejne jako
    /// <see cref="TelemetryChartControl"/>) do prekryvu v rohu World pohledu, aby byla obalka videt
    /// bez najizdeni mysi na useky planu - ty se pohybuji a tooltip nad nimi je neucinny.
    ///
    /// <para>Co je videt: lomena cara stropu po uzlech, useky obarvene stejnou skalou jako plan
    /// v mape (<see cref="SpeedPalette"/>), tecky v uzlech, carkovana cara stropu rizeni a znacka
    /// aktualni rychlosti robota v <c>s = 0</c>. Kdyz draha konci zastavenim, cara spadne na nulu.
    /// Hlavicka nese stav planu, delku a nejmensi odstup - tedy to, co by jinak bylo jen v tooltipu.</para>
    /// </summary>
    public class PlanSpeedProfileControl : Control
    {
        /// <summary>Profil ke kresleni; null = zadny plan (kresli se jen hlaska).</summary>
        public static readonly StyledProperty<PlanSpeedProfile> ProfileProperty =
            AvaloniaProperty.Register<PlanSpeedProfileControl, PlanSpeedProfile>(nameof(Profile));

        public PlanSpeedProfile Profile
        {
            get => GetValue(ProfileProperty);
            set => SetValue(ProfileProperty, value);
        }

        private const double PadLeft = 34;    // osa Y s cisly
        private const double PadRight = 8;
        private const double PadTop = 18;     // hlavicka
        private const double PadBottom = 16;  // popisky vzdalenosti

        private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 1);
        private static readonly Pen AxisPen = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), 1);
        private static readonly Pen MaxPen = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), 1,
                                                    new DashStyle(new double[] { 3, 3 }, 0));
        private static readonly IBrush RobotBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));   // zluta jako znacka robota v mape

        static PlanSpeedProfileControl()
        {
            AffectsRender<PlanSpeedProfileControl>(ProfileProperty);
        }

        public PlanSpeedProfileControl()
        {
            ClipToBounds = true;
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= PadLeft + PadRight + 10 || h <= PadTop + PadBottom + 10) return;

            var p = Profile;
            if (p == null || p.Count < 2)
            {
                DrawText(ctx, "Rychlostní profil: žádný plán", new Point(6, 2), DimBrush, 11);
                return;
            }

            // Rozsahy os. X konci na delce drahy (min 1 m, aby kratky plan nebyl roztazeny do nesmyslu),
            // Y na stropu rizeni zaokrouhlenem nahoru na desetiny.
            double sMax = Math.Max(1.0, NiceCeil(p.LengthM));
            double vMax = Math.Max(0.1, Math.Ceiling(p.VMax * 10) / 10);
            double x0 = PadLeft, x1 = w - PadRight, y0 = h - PadBottom, y1 = PadTop;
            double X(double s) => x0 + (x1 - x0) * Math.Clamp(s / sMax, 0, 1);
            double Y(double v) => y0 - (y0 - y1) * Math.Clamp(v / vMax, 0, 1);

            // Hlavicka: stav, delka, nejmensi odstup - kontext, bez ktereho cisla nic nerikaji.
            string header = string.Format(CultureInfo.InvariantCulture,
                "{0} · {1:F2} m · min. odstup {2:F2} m", p.Status, p.LengthM, p.MinClearanceM);
            DrawText(ctx, header, new Point(4, 1), TextBrush, 11);

            // Mrizka a osy.
            ctx.DrawLine(AxisPen, new Point(x0, y0), new Point(x1, y0));
            ctx.DrawLine(AxisPen, new Point(x0, y0), new Point(x0, y1));
            foreach (double v in new[] { vMax / 2, vMax })
            {
                ctx.DrawLine(v >= vMax ? MaxPen : GridPen, new Point(x0, Y(v)), new Point(x1, Y(v)));
                var ft = Text(v.ToString("0.0", CultureInfo.InvariantCulture), 10, DimBrush);
                ctx.DrawText(ft, new Point(x0 - ft.Width - 3, Y(v) - ft.Height / 2));
            }
            DrawText(ctx, "m/s", new Point(2, y1 - 2), DimBrush, 9);
            foreach (double s in new[] { sMax / 2, sMax })
            {
                ctx.DrawLine(GridPen, new Point(X(s), y0), new Point(X(s), y1));
                var ft = Text(s.ToString("0.#", CultureInfo.InvariantCulture) + " m", 10, DimBrush);
                // Popisek centrovany na cárku, ale krajni se stahne dovnitr - jinak ho okraj urizne.
                double lx = Math.Min(X(s) - ft.Width / 2, w - ft.Width - 2);
                ctx.DrawText(ft, new Point(lx, y0 + 2));
            }

            // Obalka: kazdy usek svou barvou podle stropu, ze ktereho se z uzlu odjizdi (tataz
            // hodnota, kterou ma usek v mape).
            for (int k = 0; k < p.Count - 1; k++)
            {
                var (r, g, b) = SpeedPalette.Rgb(p.Normalized(p.SegmentV(k)));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(r, g, b)), 2);
                ctx.DrawLine(pen, new Point(X(p.S[k]), Y(p.V[k])), new Point(X(p.S[k + 1]), Y(p.V[k + 1])));
            }
            for (int k = 0; k < p.Count; k++)
            {
                var (r, g, b) = SpeedPalette.Rgb(p.Normalized(p.V[k]));
                ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(r, g, b)), null,
                                new Point(X(p.S[k]), Y(p.V[k])), 2.5, 2.5);
            }

            // Aktualni rychlost robota v s = 0: zluta znacka jako robot v mape. Rozdil proti
            // prvnimu uzlu je "o kolik planu robot zaostava / o kolik ho plan brzdi".
            if (!double.IsNaN(p.RobotV))
            {
                double yv = Y(Math.Abs(p.RobotV));
                ctx.DrawEllipse(RobotBrush, null, new Point(x0, yv), 4, 4);
                var ft = Text(Math.Abs(p.RobotV).ToString("0.00", CultureInfo.InvariantCulture), 10, RobotBrush);
                ctx.DrawText(ft, new Point(x0 + 6, yv - ft.Height - 1));
            }
        }

        /// <summary>Horni "hezka" mez osy vzdalenosti: 0,5 m kroky do 3 m, pak cele metry.</summary>
        private static double NiceCeil(double s)
            => s <= 3 ? Math.Ceiling(s * 2) / 2 : Math.Ceiling(s);

        private static FormattedText Text(string text, double size, IBrush brush)
            => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                 Typeface.Default, size, brush);

        private static void DrawText(DrawingContext ctx, string text, Point at, IBrush brush, double size)
            => ctx.DrawText(Text(text, size, brush), at);
    }
}
