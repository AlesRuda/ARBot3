using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Umělý horizont (attitude indicator). Kreslí oblohu/zem rozdělenou horizontem,
    /// který se posouvá podle <see cref="Pitch"/> a naklání podle <see cref="Roll"/>
    /// (oba ve stupních). Pevný střed (žlutá značka letadla) a kruhový rám zůstávají.
    /// </summary>
    public class ArtificialHorizonControl : Control
    {
        public static readonly StyledProperty<double> PitchProperty =
            AvaloniaProperty.Register<ArtificialHorizonControl, double>(nameof(Pitch));

        public static readonly StyledProperty<double> RollProperty =
            AvaloniaProperty.Register<ArtificialHorizonControl, double>(nameof(Roll));

        public double Pitch
        {
            get => GetValue(PitchProperty);
            set => SetValue(PitchProperty, value);
        }

        public double Roll
        {
            get => GetValue(RollProperty);
            set => SetValue(RollProperty, value);
        }

        static ArtificialHorizonControl()
        {
            AffectsRender<ArtificialHorizonControl>(PitchProperty, RollProperty);
        }

        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            double w = b.Width, h = b.Height;
            if (w <= 2 || h <= 2)
                return;

            var c = new Point(w / 2.0, h / 2.0);
            double r = Math.Min(w, h) / 2.0 - 4;
            if (r <= 0)
                return;

            var sky = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5));
            var ground = new SolidColorBrush(Color.FromRgb(0x7A, 0x53, 0x2A));
            double pxPerDeg = r / 45.0;   // ±45° pitchu vyplní poloměr

            var clip = new EllipseGeometry(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
            using (ctx.PushGeometryClip(clip))
            {
                // rotace obsahu kolem středu podle náklonu
                var m = Matrix.CreateTranslation(-c.X, -c.Y)
                        * Matrix.CreateRotation(-Roll * Math.PI / 180.0)
                        * Matrix.CreateTranslation(c.X, c.Y);
                using (ctx.PushTransform(m))
                {
                    double horizonY = c.Y + Pitch * pxPerDeg;
                    double ext = 2 * r;
                    ctx.DrawRectangle(sky, null, new Rect(c.X - ext, c.Y - 2 * ext, 2 * ext, horizonY - (c.Y - 2 * ext)));
                    ctx.DrawRectangle(ground, null, new Rect(c.X - ext, horizonY, 2 * ext, (c.Y + 2 * ext) - horizonY));
                    ctx.DrawLine(new Pen(Brushes.White, 2), new Point(c.X - ext, horizonY), new Point(c.X + ext, horizonY));

                    // žebřík sklonu (±10, ±20, ±30°)
                    var lp = new Pen(Brushes.White, 1);
                    for (int d = -30; d <= 30; d += 10)
                    {
                        if (d == 0)
                            continue;
                        double y = c.Y + (Pitch - d) * pxPerDeg;
                        double half = 22;
                        ctx.DrawLine(lp, new Point(c.X - half, y), new Point(c.X + half, y));
                        var ft = Fmt(Math.Abs(d).ToString(CultureInfo.InvariantCulture), 10, Brushes.White);
                        ctx.DrawText(ft, new Point(c.X + half + 3, y - ft.Height / 2));
                    }
                }
            }

            // pevný rám a značka letadla
            ctx.DrawEllipse(null, new Pen(Brushes.Gray, 2), c, r, r);
            var acp = new Pen(Brushes.Yellow, 3);
            ctx.DrawLine(acp, new Point(c.X - 30, c.Y), new Point(c.X - 10, c.Y));
            ctx.DrawLine(acp, new Point(c.X + 10, c.Y), new Point(c.X + 30, c.Y));
            ctx.DrawLine(acp, new Point(c.X, c.Y - 6), new Point(c.X, c.Y + 6));
        }

        private static FormattedText Fmt(string text, double size, IBrush brush) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("sans-serif"), size, brush);
    }
}
