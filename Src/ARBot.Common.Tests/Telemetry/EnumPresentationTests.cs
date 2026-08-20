using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;
using ARBot.Common.Telemetry;

namespace ARBot.Common.Tests.Telemetry;

/// <summary>
/// Testy prevodu vyctove hodnoty sloupce na text (viz doc/telemetry-view.md).
///
/// <para>Vznikly z REALNE vyjimky za behu 19. 8. 2026: sloupec "korel duvod" nese
/// <see cref="MapCorrelationReason"/>, ktery je <c>: byte</c>, a puvodni prevod predaval
/// <see cref="System.Enum.IsDefined(System.Type, object)"/> vzdy <c>int</c>. To vyzaduje shodu
/// s PODKLADOVYM typem vyctu, takze to spadlo na ArgumentException. Vsechny starsi vyctove sloupce
/// maji standardni <c>int</c> podklad, proto to zadny test ani zadna review neodhalily - vada se
/// projevila teprve tim, ze si autor zkusil telemetrii zobrazit.</para>
/// </summary>
public class EnumPresentationTests
{
    [Test]
    public void ByteVycet_VratiJmenoHodnoty()
    {
        // REGRESNI TEST na tu vyjimku. MapCorrelationReason je ": byte".
        Assert.That(EnumPresentation.Text<MapCorrelationReason>((int)MapCorrelationReason.Ambiguous),
                    Is.EqualTo(nameof(MapCorrelationReason.Ambiguous)));
    }

    [Test]
    public void ByteVycet_VsechnyHodnoty_MajiJmeno()
    {
        // Cely vycet, aby se nedalo projit jen na jedne stastne hodnote.
        foreach (MapCorrelationReason r in System.Enum.GetValues<MapCorrelationReason>())
            Assert.That(EnumPresentation.Text<MapCorrelationReason>((int)r), Is.EqualTo(r.ToString()),
                        $"Hodnota {r} se neprevedla na jmeno.");
    }

    [Test]
    public void IntVycet_VratiJmenoHodnoty()
    {
        // Starsi sloupce se standardnim podkladem musi fungovat dal.
        Assert.That(EnumPresentation.Text<GlobalNavStatus>((int)GlobalNavStatus.NoRoute),
                    Is.EqualTo(nameof(GlobalNavStatus.NoRoute)));
        Assert.That(EnumPresentation.Text<LocalPlanStatus>((int)LocalPlanStatus.NoRoute),
                    Is.EqualTo(nameof(LocalPlanStatus.NoRoute)));
    }

    [Test]
    public void NeznamaHodnota_VratiCislo()
    {
        // 99 do MapCorrelationReason nepatri (ma 6 hodnot), ale do byte se vejde.
        Assert.That(EnumPresentation.Text<MapCorrelationReason>(99), Is.EqualTo("99"));
    }

    [Test]
    public void HodnotaMimoPodkladovyTyp_VratiCislo()
    {
        // 300 se do byte nevejde vubec - nesmi to vyhodit, jen vypsat cislo.
        Assert.That(EnumPresentation.Text<MapCorrelationReason>(300), Is.EqualTo("300"));
        Assert.That(EnumPresentation.Text<MapCorrelationReason>(-1), Is.EqualTo("-1"));
    }
}
