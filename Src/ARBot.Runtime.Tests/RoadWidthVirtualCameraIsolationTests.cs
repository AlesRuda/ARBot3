using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Runtime.Tests;

/// <summary>
/// ⚠️ Scena, ze ktere renderuje <b>VIRTUALNI KAMERA</b>, nesmi dostat naucenou sirku.
///
/// <para>Kdyby ji dostala, simulace by renderovala cestu podle odhadu a koridor by meril
/// <b>SAM SEBE</b> — tataz past jako <c>camerapose=fusion</c>, kterou projekt uz jednou nasel
/// (22. 8. 2026). Proto to nesmi byt vec opatrnosti pri psani kodu, ale vec toho, ze ta cesta
/// NEEXISTUJE. Viz doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 3.</para>
/// </summary>
public class RoadWidthVirtualCameraIsolationTests
{
    [Test]
    public void ScenaRendereru_prekryvNedostane()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);

        // Presne tak, jak scenu stavi ARBotHW (bez prekryvu).
        var rendererScena = new RoadScene(net, o);
        // A takhle ta, kterou dostane korelator po nauceni sirky.
        var korelatorScena = new RoadScene(net, o, RoadWidthOverrides.Build(net, _ => 6.0));

        Assert.That(korelatorScena.IsRoad(0, 2.5), Is.True, "korelator uz naucenou sirku ma");
        Assert.That(rendererScena.IsRoad(0, 2.5), Is.False,
                    "renderer musi zustat na MAPOVE sirce - jinak by koridor meril sam sebe");
    }

    [Test]
    public void ArbotHwStaviScenuBezPrekryvu()
    {
        // Strazny test proti tomu, aby nekdo prekryv do ARBotHW dopsal. RepoPaths zije
        // v ARBot.Common/Configuration, takze je odsud dostupny pres ARBot.Runtime -> ARBot.Common.
        string root = ARBot.Common.Configuration.RepoPaths.RootOrBase();
        string cesta = System.IO.Path.Combine(root, "Src", "ARBot.Runtime", "Robot", "ARBotHW.cs");
        if (!System.IO.File.Exists(cesta)) Assert.Ignore("Bezi bez repa - neni co kontrolovat.");

        string zdroj = System.IO.File.ReadAllText(cesta);

        Assert.That(zdroj, Does.Contain("new RoadScene(options.Network, options.Origin)"),
                    "ARBotHW musi stavet scenu BEZ prekryvu - viz rozhodnuti 3 ve specu");
    }
}
