using System;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy vytazeni dukaznich bunek ze zpravy gridu (viz doc/map-correlation-localization.md).
/// Konvence znamenka: LRoad kladne = "mimo cestu", zaporne = "cesta".
/// </summary>
public class EvidenceCloudTests
{
    /// <summary>Prazdna zprava gridu 8 x 8 po 0,5 m s pocatkem v (0,0).</summary>
    private static OccupancyGridMsg Grid()
        => new OccupancyGridMsg
        {
            Size = 8,
            Resolution = 0.5,
            OriginX = 0,
            OriginY = 0,
            Scale = 0.05f,
            BlockedThreshold = 1.0f,
            FreeThreshold = -1.0f,
            Occ = new sbyte[64],
            Road = new sbyte[64],
            TimeStamp = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
        };

    /// <summary>Zapise do bunky (i,j) log-odds hodnotu (prepocte se na fixed-point).</summary>
    private static void SetRoad(OccupancyGridMsg msg, int i, int j, float logOdds)
        => msg.Road[i + j * msg.Size] = (sbyte)Math.Round(logOdds / msg.Scale);

    [Test]
    public void FromGrid_SlabeBunkyVynecha()
    {
        var msg = Grid();
        SetRoad(msg, 1, 1, 0.2f);   // pod prahem
        SetRoad(msg, 2, 2, -0.2f);  // pod prahem (i v absolutni hodnote)

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(0));
    }

    [Test]
    public void FromGrid_VezmeObeZnamenka()
    {
        var msg = Grid();
        SetRoad(msg, 1, 1, 1.0f);   // mimo cestu
        SetRoad(msg, 2, 3, -1.0f);  // cesta

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(2));
    }

    [Test]
    public void FromGrid_SouradniceJsouStredyBunekVeSvete()
    {
        var msg = Grid();
        SetRoad(msg, 3, 2, -1.0f);

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(1));
        // Origin (0,0), 0,5 m bunka => stred bunky (3,2) je (1,75 ; 1,25).
        Assert.That(cloud.X[0], Is.EqualTo(1.75).Within(1e-9));
        Assert.That(cloud.Y[0], Is.EqualTo(1.25).Within(1e-9));
    }

    [Test]
    public void FromGrid_VahaJeLogOddsVcetneZnamenka()
    {
        var msg = Grid();
        SetRoad(msg, 1, 1, -1.0f);

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.W[0], Is.EqualTo(-1.0f).Within(0.03f));
    }

    [Test]
    public void FromGrid_IgnorujeKanalOcc()
    {
        var msg = Grid();
        msg.Occ[1 + 1 * msg.Size] = 100;  // silna geometricka prekazka

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        // Occ se korelace neucastni - jsou v nem veci, ktere v mape nejsou.
        Assert.That(cloud.Count, Is.EqualTo(0));
    }

    [Test]
    public void FromGrid_ChybejiciKanalRoad_DaPrazdnyOblak()
    {
        var msg = Grid();
        msg.Road = null;

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(0));
    }
}
