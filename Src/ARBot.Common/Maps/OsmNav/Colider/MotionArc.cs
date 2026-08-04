using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>Výsledek promítnutí bodu na osu úseku dráhy.</summary>
/// <param name="Lateral">Kolmá (radiální) vzdálenost bodu k ose úseku [m].</param>
/// <param name="ArcLength">Kumulativní vzdálenost podél dráhy v nejbližším bodě [m].</param>
/// <param name="Time">Čas v nejbližším bodě [s].</param>
/// <param name="Sigma">Poziční nejistota (1σ) v nejbližším bodě [m].</param>
public readonly record struct ArcProjection(double Lateral, double ArcLength, double Time, double Sigma);

/// <summary>
/// Jeden úsek dráhy s konstantní křivostí — buď rovný (kapsle), nebo kruhový oblouk
/// (výsek mezikruží). Umožňuje analytické promítnutí bodu (O(1)) bez vzorkování:
/// zametená oblast robota/koridoru je množina bodů do vzdálenosti <c>w</c> od osy úseku,
/// takže test „bod je v koridoru" = <c>Project(p).Lateral ≤ w</c>. Konce jsou přirozeně
/// zaoblené (promítnutí se ořízne na koncové póze).
///
/// Pozice (<see cref="Start"/>/<see cref="End"/>/<see cref="Center"/>) jsou sdílený
/// <see cref="Point2D"/> (float). Mezivýpočty posunů, rotací a vzdáleností se drží
/// v lokálních <c>double</c>, takže analytika zůstává přesná i bez alokací (žádný
/// referenční vektorový typ v hot-path).
/// </summary>
public readonly struct MotionArc
{
    private const double Eps = 1e-9;

    public bool IsStraight { get; }
    public Point2D Start { get; }
    public double StartHeading { get; }
    public Point2D End { get; }
    public double EndHeading { get; }
    public double Length { get; }
    public double StartDistance { get; }
    public double StartTime { get; }
    public double EndTime { get; }
    public double StartSigma { get; }
    public double EndSigma { get; }

    // jen pro oblouk
    public Point2D Center { get; }
    public double Radius { get; }
    private readonly double _sweep;

    private MotionArc(bool isStraight, Point2D start, double startHeading, Point2D end, double endHeading,
        double length, double startDistance, double startTime, double endTime,
        double startSigma, double endSigma, Point2D center, double radius, double sweep)
    {
        IsStraight = isStraight;
        Start = start;
        StartHeading = startHeading;
        End = end;
        EndHeading = endHeading;
        Length = length;
        StartDistance = startDistance;
        StartTime = startTime;
        EndTime = endTime;
        StartSigma = startSigma;
        EndSigma = endSigma;
        Center = center;
        Radius = radius;
        _sweep = sweep;
    }

    public static MotionArc Straight(Point2D start, double heading, double length,
        double startDistance, double startTime, double endTime, double startSigma, double endSigma)
    {
        // konec = start posunutý o length ve směru headingu
        var end = Offset(start, Math.Cos(heading) * length, Math.Sin(heading) * length);
        return new MotionArc(true, start, heading, end, heading, length,
            startDistance, startTime, endTime, startSigma, endSigma, default, 0, 0);
    }

    /// <param name="radiusSigned">Poloměr se znaménkem = v/ω; kladné → střed vlevo (zatáčka CCW).</param>
    /// <param name="sweepAngle">Změna headingu podél úseku [rad] = ω·Δt.</param>
    public static MotionArc Curved(Point2D start, double startHeading, double radiusSigned, double sweepAngle,
        double startDistance, double startTime, double endTime, double startSigma, double endSigma)
    {
        // levá normála headingu; střed = start + normála·radiusSigned
        double nx = -Math.Sin(startHeading), ny = Math.Cos(startHeading);
        var center = Offset(start, nx * radiusSigned, ny * radiusSigned);
        double radius = Math.Abs(radiusSigned);
        // konec = střed + rotace vektoru (start − střed) o sweep
        var (ex, ey) = Rotate(start.X - center.X, start.Y - center.Y, sweepAngle);
        var end = Offset(center, ex, ey);
        double endHeading = startHeading + sweepAngle;
        double length = radius * Math.Abs(sweepAngle);
        return new MotionArc(false, start, startHeading, end, endHeading, length,
            startDistance, startTime, endTime, startSigma, endSigma, center, radius, sweepAngle);
    }

    /// <summary>Bod na ose úseku v poměrné vzdálenosti <paramref name="fraction"/> ∈ [0,1].</summary>
    public Point2D PointAt(double fraction)
    {
        if (IsStraight)
        {
            double L = Length * fraction;
            return Offset(Start, Math.Cos(StartHeading) * L, Math.Sin(StartHeading) * L);
        }
        var (rx, ry) = Rotate(Start.X - Center.X, Start.Y - Center.Y, _sweep * fraction);
        return Offset(Center, rx, ry);
    }

    /// <summary>Promítne bod <paramref name="p"/> na osu úseku (nejbližší bod, ořez na konce).</summary>
    public ArcProjection Project(Point2D p)
    {
        double frac;
        double lateral;

        if (IsStraight)
        {
            double ux = Math.Cos(StartHeading), uy = Math.Sin(StartHeading);
            double relX = p.X - Start.X, relY = p.Y - Start.Y;
            double t = Length > Eps ? Math.Clamp(relX * ux + relY * uy, 0, Length) : 0;
            double closestX = Start.X + ux * t, closestY = Start.Y + uy * t;
            lateral = Hypot(p.X - closestX, p.Y - closestY);
            frac = Length > Eps ? t / Length : 0;
        }
        else
        {
            double v0X = Start.X - Center.X, v0Y = Start.Y - Center.Y;
            double vpX = p.X - Center.X, vpY = p.Y - Center.Y;
            double phi0 = Math.Atan2(v0Y, v0X);
            double phiP = Math.Atan2(vpY, vpX);
            double d = Hypot(vpX, vpY);
            double absSweep = Math.Abs(_sweep);
            double s = Math.Sign(_sweep);
            double alpha = Mod2Pi((phiP - phi0) * s);   // offset ve směru jízdy, [0,2π)

            if (alpha <= absSweep + Eps)
            {
                frac = absSweep > Eps ? alpha / absSweep : 0;
                lateral = Math.Abs(d - Radius);
            }
            else
            {
                // mimo úhlový rozsah → ořez na bližší konec (přes „obtočení")
                double beyondEnd = alpha - absSweep;
                double beforeStart = 2 * Math.PI - alpha;
                if (beforeStart < beyondEnd)
                {
                    frac = 0;
                    lateral = Hypot(p.X - Start.X, p.Y - Start.Y);
                }
                else
                {
                    frac = 1;
                    lateral = Hypot(p.X - End.X, p.Y - End.Y);
                }
            }
        }

        return new ArcProjection(
            lateral,
            StartDistance + frac * Length,
            Lerp(StartTime, EndTime, frac),
            Lerp(StartSigma, EndSigma, frac));
    }

    /// <summary>Bod posunutý o (<paramref name="dx"/>,<paramref name="dy"/>) [m]; posun v double, pozice ve float.</summary>
    private static Point2D Offset(Point2D p, double dx, double dy) => new Point2D(p.X + dx, p.Y + dy);

    /// <summary>Rotace vektoru (<paramref name="vx"/>,<paramref name="vy"/>) o úhel <paramref name="a"/> [rad], CCW.</summary>
    private static (double X, double Y) Rotate(double vx, double vy, double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return (vx * c - vy * s, vx * s + vy * c);
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static double Mod2Pi(double a)
    {
        double m = a % (2 * Math.PI);
        return m < 0 ? m + 2 * Math.PI : m;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
