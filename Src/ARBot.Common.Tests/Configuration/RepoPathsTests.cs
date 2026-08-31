using System.IO;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Reseni cest proti koreni repa. Viz doc/configuration.md.
    /// </summary>
    public class RepoPathsTests
    {
        [Test]
        public void RootOrBase_ExistujiciSlozka()
        {
            Assert.That(Directory.Exists(RepoPaths.RootOrBase()), Is.True);
        }

        [Test]
        public void Resolve_AbsolutniCestuNechava()
        {
            string abs = Path.GetFullPath(Path.Combine(RepoPaths.RootOrBase(), "OSM"));
            Assert.That(RepoPaths.Resolve(abs), Is.EqualTo(abs));
        }

        [Test]
        public void Resolve_RelativniSpojiSKorenem()
        {
            Assert.That(RepoPaths.Resolve("OSM/x.osm"),
                        Is.EqualTo(Path.GetFullPath(Path.Combine(RepoPaths.RootOrBase(), "OSM/x.osm"))));
        }

        [Test]
        public void Resolve_NullNechava()
        {
            Assert.That(RepoPaths.Resolve(null), Is.Null);
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Resolve_PrazdneNechava(string vstup)
        {
            Assert.That(RepoPaths.Resolve(vstup), Is.EqualTo(vstup));
        }
    }
}
