using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Jednoduchý kompas / ukazatel kurzu. Kreslí pevnou růžici (N nahoře, po směru
    /// hodinových ručiček) a ručičku otočenou na hodnotu <see cref="Heading"/> (ve stupních,
    /// 0 = sever, roste po směru hodinových ručiček).
    /// </summary>
    public class CompassControl : Control
    {
        public static readonly StyledProperty<double> HeadingProperty =
            AvaloniaProperty.Register<CompassControl, double>(nameof(Heading));

        public double Heading
        {
            get => GetValue(HeadingProperty);
            set => SetValue(HeadingProperty, value);
        }

        static CompassControl()
        {
            AffectsRender<CompassControl>(HeadingProperty);
        }

        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            double w = b.Width, h = b.Height;
            if (w <= 2 || h <= 2)
                return;

            var c = new Point(w / 2.0, h / 2.0);
            double r = Math.Min(w, h) / 2.0 - 6;
            if (r <= 0)
                return;

            var face = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
            ctx.DrawEllipse(face, new Pen(Brushes.Gray, 2), c, r, r);

            // značky po 30°
            var tick = new Pen(Brushes.DimGray, 1);
            for (int a = 0; a < 360; a += 30)
            {
                var dir = Dir(a);
                ctx.DrawLine(tick,
                    new Point(c.X + dir.X * r, c.Y + dir.Y * r),
                    new Point(c.X + dir.X * (r - 8), c.Y + dir.Y * (r - 8)));
            }

            // světové strany
            Label(ctx, "N", c, r - 20, 0, Brushes.OrangeRed);
            Label(ctx, "E", c, r - 20, 90, Brushes.Silver);
            Label(ctx, "S", c, r - 20, 180, Brushes.Silver);
            Label(ctx, "W", c, r - 20, 270, Brushes.Silver);

            // ručička na kurz
            var nd = Dir(Heading);
            var tip = new Point(c.X + nd.X * (r - 12), c.Y + nd.Y * (r - 12));
            var tail = new Point(c.X - nd.X * (r * 0.5), c.Y - nd.Y * (r * 0.5));
            ctx.DrawLine(new Pen(Brushes.OrangeRed, 3), c, tip);
            ctx.DrawLine(new Pen(Brushes.Silver, 3), c, tail);
            ctx.DrawEllipse(Brushes.White, null, c, 3, 3);

            // číselný kurz dole
            double hn = ((Heading % 360) + 360) % 360;
            var ft = Fmt(hn.ToString("F0", CultureInfo.InvariantCulture) + "°", 14, Brushes.White);
            ctx.DrawText(ft, new Point(c.X - ft.Width / 2, h - ft.Height - 2));
        }

        /// <summary>Jednotkový směr pro úhel ve stupních (0 = nahoru/sever, po směru hod. ručiček).</summary>
        private static Point Dir(double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point(Math.Sin(rad), -Math.Cos(rad));
        }

        private static void Label(DrawingContext ctx, string s, Point c, double dist, double angleDeg, IBrush brush)
        {
            var dir = Dir(angleDeg);
            var ft = Fmt(s, 13, brush);
            ctx.DrawText(ft, new Point(c.X + dir.X * dist - ft.Width / 2, c.Y + dir.Y * dist - ft.Height / 2));
        }

        private static FormattedText Fmt(string text, double size, IBrush brush) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("sans-serif"), size, brush);
    }
}
