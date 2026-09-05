using System.IO;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration;

/// <summary>
/// Datový adresář (<c>dataroot=</c>): proti čemu se řeší relativní cesty.
///
/// <para><b>Proč to je:</b> na zařízení se nasazuje <b>stínovou kopií</b> — binárky běží z kopie
/// bokem (běžící .NET binárku přepsat nejde), ale záznamy, logy a profily musí zůstat v původním
/// adresáři, jinak by se s každou novou kopií ztrácely. Viz doc/plan-headless-provoz.md, návrh F.</para>
/// </summary>
/// <remarks><see cref="RepoPaths"/> i <see cref="ParamStore.Current"/> jsou statické, proto
/// <c>NonParallelizable</c> a návrat hodnot v <c>TearDown</c>.</remarks>
[NonParallelizable]
public class DataRootTests
{
    [TearDown]
    public void Vrat()
    {
        ParamStore.Build(new string[0]);   // vrátí i RepoPaths.DataRoot na null
    }

    [Test]
    public void BezDataroot_SeCestyResiJakoDosud()
    {
        ParamStore.Build(new[] { "ARBot.exe" });

        Assert.Multiple(() =>
        {
            Assert.That(RepoPaths.DataRoot, Is.Null);
            Assert.That(RepoPaths.Resolve("records/a.rec"),
                        Is.EqualTo(Path.GetFullPath(Path.Combine(RepoPaths.RootOrBase(), "records/a.rec"))));
        });
    }

    [Test]
    public void SDataroot_SeRelativniCestyResiProtiNemu()
    {
        string data = Path.Combine(Path.GetTempPath(), "arbot-data-test");

        ParamStore.Build(new[] { "ARBot.exe", "dataroot=" + data });

        Assert.Multiple(() =>
        {
            Assert.That(RepoPaths.DataRoot, Is.EqualTo(Path.GetFullPath(data)));
            Assert.That(RepoPaths.Resolve("records/a.rec"),
                        Is.EqualTo(Path.GetFullPath(Path.Combine(data, "records/a.rec"))));
            // Absolutni cesta se nemeni ani s datovym adresarem.
            Assert.That(RepoPaths.Resolve(Path.Combine(data, "x.rec")),
                        Is.EqualTo(Path.Combine(data, "x.rec")));
        });
    }

    [Test]
    public void ParametrCestyCteNovyZaklad()
    {
        string data = Path.Combine(Path.GetTempPath(), "arbot-data-test2");

        ParamStore.Build(new[] { "ARBot.exe", "dataroot=" + data, "map=OSM/x.osm" });

        // PathParam.Value jde pres RepoPaths.Resolve - tady se pozna, ze se to propsalo VSUDE,
        // ne jen do zaznamu.
        Assert.That(ParamRegistry.Map.Value, Is.EqualTo(Path.GetFullPath(Path.Combine(data, "OSM/x.osm"))));
    }

    [Test]
    public void DatarootVProfilu_JeChybaPriStartu()
    {
        // Profil se hleda AZ PODLE dataroot, takze v profilu by hodnota prisla pozde a cesty by
        // se resily jinam, nez clovek napsal. Tiche ignorovani je horsi nez hlaska.
        string profil = Path.Combine(Path.GetTempPath(), "arbot-dataroot-" + Path.GetRandomFileName() + ".cfg");
        File.WriteAllText(profil, "dataroot=/tmp/necos\n");
        try
        {
            var ex = Assert.Throws<ParamFileException>(
                () => ParamStore.Build(new[] { "ARBot.exe", "config=" + profil }));

            Assert.That(ex.Message, Does.Contain("prikazovou radku"));
        }
        finally { File.Delete(profil); }
    }

    [Test]
    public void PrazdnyDataroot_NicNemeni()
    {
        ParamStore.Build(new[] { "ARBot.exe", "dataroot=" });

        Assert.That(RepoPaths.DataRoot, Is.Null);
    }
}
