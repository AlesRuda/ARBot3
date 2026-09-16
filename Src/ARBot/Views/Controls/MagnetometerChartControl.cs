using System;
using System.Globalization;
using ARBot.Common.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// <b>Průběh magnetického pole v čase</b> — pásový graf odchylek složek X/Y/Z a velikosti
    /// <c>|B|</c> od reference (nula, jinak průměr okna), v <b>miligaussech</b>.
    ///
    /// <para><b>Proč odchylky a ne absolutní hodnoty.</b> Složky se liší o řád (typicky 0,01 /
    /// −0,15 / −0,45 G), takže na společné ose by se hledaný efekt jednotek mG ztratil. Odečtením
    /// reference se všechny křivky vycentrují a osa může mít rozlišení, na kterém je krok vidět.
    /// Absolutní hodnoty jsou nad grafem číselně.</para>
    ///
    /// <para>⚠️ <b>Červený pruh dole = robot se točil</b> (<see cref="MagVzorek.Klid"/>). V těch
    /// úsecích křivky ukazují otáčení zemské složky, ne rušení — pootočení o 1° dá ve vodorovné
    /// složce ~3,5 mG. Bez toho pruhu by graf vypadal stejně přesvědčivě pro obojí.</para>
    ///
    /// <para>Překreslení se vyvolá nastavením <see cref="Snapshot"/> (nová instance = nový obsah;
    /// stopa se kopíruje, takže se pod kreslením nemění).</para>
    /// </summary>
    public class MagnetometerChartControl : Control
    {
        public static readonly StyledProperty<MagSnimek?> SnapshotProperty =
            AvaloniaProperty.Register<MagnetometerChartControl, MagSnimek?>(nameof(Snapshot));

        public MagSnimek? Snapshot
        {
            get => GetValue(SnapshotProperty);
            set => SetValue(SnapshotProperty, value);
        }

        static MagnetometerChartControl()
        {
            AffectsRender<MagnetometerChartControl>(SnapshotProperty);
        }

        private static readonly IBrush BarvaX = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x60));
        private static readonly IBrush BarvaY = new SolidColorBrush(Color.FromRgb(0x60, 0xD0, 0x60));
        private static readonly IBrush BarvaZ = new SolidColorBrush(Color.FromRgb(0x70, 0xA0, 0xF0));
        private static readonly IBrush BarvaB = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x40));
        private static readonly IBrush Pozadi = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        private static readonly IBrush Ramec = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
        private static readonly IBrush Popis = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));

        /// <summary>Nejmenší půlrozsah osy [mG] — jinak by šum v klidu vypadal jako bouře.</summary>
        private const double MinRozsahMg = 5.0;

        /// <summary>
        /// Naposledy použitý půlrozsah svislé osy [mG]. ⚠️ <b>Drží se mezi překresleními
        /// schválně:</b> kdyby se osa počítala pokaždé znovu z maxima v okně, přeskakovala by
        /// mezi stupni (10 → 20 → 10) pokaždé, když špička vstoupí do okna nebo z něj vypadne —
        /// a celá křivka by při tom skokem změnila výšku. Nahoru se pouští hned (jinak by se
        /// ořízla), dolů až s hysterezí (viz <see cref="Osa"/>).
        /// </summary>
        private double osaMg;

        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            double w = b.Width, h = b.Height;
            if (w <= 40 || h <= 30) return;

            ctx.FillRectangle(Pozadi, new Rect(0, 0, w, h));

            var s = Snapshot;
            const double levy = 46, pravy = 6, horni = 6, dolni = 20;
            var plocha = new Rect(levy, horni, Math.Max(1, w - levy - pravy),
                                  Math.Max(1, h - horni - dolni));
            ctx.DrawRectangle(null, new Pen(Ramec, 1), plocha);

            if (s == null || s.Vzorky.Count < 2)
            {
                Text(ctx, "čeká se na měření z magnetometru…", new Point(levy + 8, horni + 8), Popis);
                return;
            }

            // Rozsah osy: nejvetsi odchylka kterekoli krivky, pres hysterezi (viz osaMg).
            var r = s.Reference;
            double refVel = s.ReferenceVelikostG;
            double potreba = MinRozsahMg;
            foreach (var v in s.Vzorky)
            {
                potreba = Math.Max(potreba, Math.Abs((v.Pole.X - r.X) * 1000));
                potreba = Math.Max(potreba, Math.Abs((v.Pole.Y - r.Y) * 1000));
                potreba = Math.Max(potreba, Math.Abs((v.Pole.Z - r.Z) * 1000));
                potreba = Math.Max(potreba, Math.Abs((v.Velikost - refVel) * 1000));
            }
            double max = Osa(potreba);

            // ⚠️ Casova osa je vzdy CELE OKNO a kotvi se na nejnovejsi vzorek, ne na rozsah dat:
            // dokud se buffer plni, je dat min nez okno, takze mereni casu z (TDo - TOd) by se
            // pri plneni plynule menilo a po naplneni skokem ustalo - krivka by pri kazdem
            // prekresleni menila meritko. Takhle se jen posouva doleva.
            double okno = s.OknoSek > 0 ? s.OknoSek : 60;
            double t1 = s.TDo, t0 = t1 - okno;
            double X(double t) => plocha.X + (t - t0) / okno * plocha.Width;
            double Y(double mg) => plocha.Y + plocha.Height / 2 - mg / max * (plocha.Height / 2);

            // Vodorovne vodici cary a popisky osy [mG].
            var vodici = new Pen(Ramec, 1, new DashStyle(new double[] { 2, 3 }, 0));
            foreach (double podil in new[] { 1.0, 0.5, 0.0, -0.5, -1.0 })
            {
                double mg = max * podil;
                double y = Y(mg);
                if (podil == 0.0)
                    ctx.DrawLine(new Pen(Ramec, 1), new Point(plocha.X, y), new Point(plocha.Right, y));
                else
                    ctx.DrawLine(vodici, new Point(plocha.X, y), new Point(plocha.Right, y));
                Text(ctx, mg.ToString(max >= 10 ? "F0" : "F1", CultureInfo.InvariantCulture),
                     new Point(2, y - 8), Popis, 11);
            }

            // Pruh "robot se tocil" - useky, ktere pro mereni ruseni NEPLATI.
            var pohyb = new SolidColorBrush(Color.FromArgb(0xB0, 0xC0, 0x40, 0x40));
            double pruhY = plocha.Bottom - 4;
            double zacatek = double.NaN;
            for (int i = 0; i < s.Vzorky.Count; i++)
            {
                bool klid = s.Vzorky[i].Klid;
                if (!klid && double.IsNaN(zacatek)) zacatek = s.Vzorky[i].T;
                if ((klid || i == s.Vzorky.Count - 1) && !double.IsNaN(zacatek))
                {
                    double x0 = X(zacatek), x1 = X(s.Vzorky[i].T);
                    ctx.FillRectangle(pohyb, new Rect(x0, pruhY, Math.Max(1, x1 - x0), 4));
                    zacatek = double.NaN;
                }
            }

            // Svisla znacka v okamziku nulovani.
            if (s.NulaCas.HasValue && s.NulaCas.Value >= s.TOd && s.NulaCas.Value <= s.TDo)
            {
                double x = X(s.NulaCas.Value);
                ctx.DrawLine(new Pen(Brushes.White, 1, new DashStyle(new double[] { 3, 3 }, 0)),
                             new Point(x, plocha.Y), new Point(x, plocha.Bottom));
            }

            Krivka(ctx, s, X, Y, BarvaX, v => (v.Pole.X - r.X) * 1000);
            Krivka(ctx, s, X, Y, BarvaY, v => (v.Pole.Y - r.Y) * 1000);
            Krivka(ctx, s, X, Y, BarvaZ, v => (v.Pole.Z - r.Z) * 1000);
            Krivka(ctx, s, X, Y, BarvaB, v => (v.Velikost - refVel) * 1000);

            // Legenda a casovy rozsah.
            double lx = levy + 6;
            lx = Legenda(ctx, "X", BarvaX, lx, h - dolni + 3);
            lx = Legenda(ctx, "Y", BarvaY, lx, h - dolni + 3);
            lx = Legenda(ctx, "Z", BarvaZ, lx, h - dolni + 3);
            lx = Legenda(ctx, "|B|", BarvaB, lx, h - dolni + 3);
            Text(ctx, string.Format(CultureInfo.InvariantCulture, "{0:F0} s   [mG proti {1}]",
                                    okno, s.Nula.HasValue ? "nule" : "průměru okna"),
                 new Point(lx + 8, h - dolni + 3), Popis, 11);
        }

        private static void Krivka(DrawingContext ctx, MagSnimek s, Func<double, double> X,
                                   Func<double, double> Y, IBrush barva, Func<MagVzorek, double> hodnota)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool prvni = true;
                // Pri 60 s a 100 Hz je vzorku 6000 na ~600 px; kazdy druhy je porad 5x vic,
                // nez display rozlisi, a kreslit vsechny by bylo zbytecne.
                int krok = Math.Max(1, s.Vzorky.Count / 2000);
                for (int i = 0; i < s.Vzorky.Count; i += krok)
                {
                    var p = new Point(X(s.Vzorky[i].T), Y(hodnota(s.Vzorky[i])));
                    if (prvni) { g.BeginFigure(p, false); prvni = false; }
                    else g.LineTo(p);
                }
                if (!prvni) g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(barva, 1.4), geo);
        }

        private static double Legenda(DrawingContext ctx, string popis, IBrush barva, double x, double y)
        {
            ctx.DrawLine(new Pen(barva, 2), new Point(x, y + 8), new Point(x + 14, y + 8));
            var ft = Text(ctx, popis, new Point(x + 18, y), barva, 11);
            return x + 18 + ft + 10;
        }

        private static double Text(DrawingContext ctx, string s, Point p, IBrush brush, double size = 12)
        {
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                       new Typeface("Consolas"), size, brush);
            ctx.DrawText(ft, p);
            return ft.Width;
        }

        /// <summary>
        /// Půlrozsah osy s hysterezí. Nahoru <b>okamžitě</b> (křivka se nesmí oříznout), dolů
        /// teprve když se data vejdou pod <b>polovinu</b> současné osy — tedy o celý stupeň níž.
        /// Bez té asymetrie by osa u šumu, který se maxima jen dotýká, blikala tam a zpět.
        /// </summary>
        private double Osa(double potreba)
        {
            double chce = Zaokrouhli(potreba);
            if (osaMg <= 0 || chce > osaMg) osaMg = chce;
            else if (potreba < osaMg * 0.5) osaMg = chce;
            return osaMg;
        }

        /// <summary>Hezký půlrozsah osy (1/2/5 × mocnina deseti), aby popisky nebyly náhodná čísla.</summary>
        private static double Zaokrouhli(double v)
        {
            double rad = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double p = v / rad;
            double n = p <= 1 ? 1 : p <= 2 ? 2 : p <= 5 ? 5 : 10;
            return n * rad;
        }
    }
}
