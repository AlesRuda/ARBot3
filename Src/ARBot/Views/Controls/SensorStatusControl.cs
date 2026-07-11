using System;
using System.Globalization;
using ARBot.Common.Devices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Znovupoužitelný indikátor stavu senzoru z <see cref="ISensor"/> (barevná tečka +
    /// jméno + stav). Určeno pro záhlaví dokumentů jednotlivých senzorů (IMU, kamera,
    /// GPS, motor, ...). <see cref="ISensor.IsError"/> se mění za běhu bez notifikace,
    /// proto se stav periodicky obnovuje časovačem.
    /// </summary>
    public class SensorStatusControl : Control
    {
        public static readonly StyledProperty<ISensor?> SensorProperty =
            AvaloniaProperty.Register<SensorStatusControl, ISensor?>(nameof(Sensor));

        public ISensor? Sensor
        {
            get => GetValue(SensorProperty);
            set => SetValue(SensorProperty, value);
        }

        private readonly DispatcherTimer timer;

        public SensorStatusControl()
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => InvalidateVisual();
        }

        static SensorStatusControl()
        {
            AffectsRender<SensorStatusControl>(SensorProperty);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            timer.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            timer.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            double w = b.Width, h = b.Height;
            if (w <= 0 || h <= 0)
                return;

            var s = Sensor;
            string name;
            string state;
            IBrush color;
            if (s == null)
            {
                name = "senzor";
                state = "—";
                color = Brushes.Gray;
            }
            else
            {
                name = s.Name;
                bool err = s.IsError;
                state = err ? "CHYBA" : "OK";
                color = err ? Brushes.OrangeRed : Brushes.LimeGreen;
            }

            double cy = h / 2;
            double r = 6;
            double x = r + 1;
            ctx.DrawEllipse(color, null, new Point(x, cy), r, r);

            double tx = x + r + 8;
            var ftName = Fmt(name + ":", 13, Brushes.Gainsboro);
            ctx.DrawText(ftName, new Point(tx, cy - ftName.Height / 2));

            var ftState = Fmt(state, 13, color);
            ctx.DrawText(ftState, new Point(tx + ftName.Width + 6, cy - ftState.Height / 2));
        }

        private static FormattedText Fmt(string text, double size, IBrush brush) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("sans-serif"), size, brush);
    }
}
