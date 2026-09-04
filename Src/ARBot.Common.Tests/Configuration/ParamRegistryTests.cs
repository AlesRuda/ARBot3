using System;
using System.Collections.Generic;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Vnitrni konzistence registru parametru. Viz doc/configuration.md.
    /// </summary>
    public class ParamRegistryTests
    {
        [Test]
        public void TryGet_JeCaseInsensitive()
        {
            // Prikazova radka i profil se dosud porovnavaly bez ohledu na velikost pismen
            // (Program.GetParam pouzival ToLower) - registr to musi zachovat.
            Assert.That(ParamRegistry.TryGet("MAPCORR", out var def), Is.True);
            Assert.That(def.Name, Is.EqualTo("mapcorr"));
        }

        [Test]
        public void Jmena_JsouUnikatni_BezOhleduNaVelikost()
        {
            var videna = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in ParamRegistry.All)
                Assert.That(videna.Add(d.Name), Is.True, $"duplicitni parametr: {d.Name}");
        }

        [Test]
        public void KazdyParametrMaPopisAKategorii()
        {
            foreach (var d in ParamRegistry.All)
            {
                Assert.That(d.Description, Is.Not.Null.And.Not.Empty, $"{d.Name}: chybi popis");
                Assert.That(d.Category, Is.Not.Null.And.Not.Empty, $"{d.Name}: chybi kategorie");
            }
        }

        [Test]
        public void KonstantniDefault_JeSamPlatnouHodnotou()
        {
            foreach (var d in ParamRegistry.All)
            {
                if (d.Default == null) continue;
                Assert.That(d.IsValidValue(d.Default), Is.True,
                            $"{d.Name}: vychozi hodnota '{d.Default}' neprojde vlastni validaci");
            }
        }

        [Test]
        public void Vycty_MajiVychoziHodnotuMeziPovolenymi()
        {
            // Kdyby default nebyl ve vyctu, aplikace by se svou vlastni vychozi hodnotou
            // nenastartovala - a nikdo by to nezjistil driv nez za behu.
            foreach (var d in ParamRegistry.All)
            {
                if (d.AllowedValues == null || d.Default == null) continue;
                Assert.That(d.IsValidValue(d.Default), Is.True,
                            $"{d.Name}: vychozi '{d.Default}' neni mezi povolenymi hodnotami "
                            + string.Join(" | ", d.AllowedValues));
            }
        }

        [Test]
        public void Vycet_OdmitneNeznamouHodnotu_ADuvodJiVypise()
        {
            Assert.That(ParamRegistry.TryGet("mission", out var mission), Is.True);
            Assert.That(mission.IsValidValue("freerun"), Is.True);
            Assert.That(mission.IsValidValue("FREERUN"), Is.True);      // bez ohledu na velikost

            var vada = mission.Validate("robotur");                      // preklep
            Assert.That(vada.Ok, Is.False);
            Assert.That(vada.Error, Does.Contain("none").And.Contain("robotour"));
        }

        [Test]
        public void Canonical_SrovnaVelikostPismenNaTvarZVyctu()
        {
            // Validace vyctu je case-insensitive, ale rozbalovaci seznam v panelu porovnava
            // PRESNE - bez kanonizace by 'mission=NONE' z profilu nevybralo zadnou polozku,
            // seznam by ukazal prazdno a pri ulozeni by se hodnota ztratila.
            Assert.That(ParamRegistry.TryGet("mission", out var mission), Is.True);
            Assert.That(mission.Canonical("NONE"), Is.EqualTo("none"));
            Assert.That(mission.Canonical("FreeRun"), Is.EqualTo("freerun"));
            Assert.That(mission.Canonical(" robotour "), Is.EqualTo("robotour"));
        }

        [Test]
        public void Canonical_NechavaBezeZmenyCoNepatriDoVyctu()
        {
            Assert.That(ParamRegistry.TryGet("mission", out var mission), Is.True);
            Assert.That(mission.Canonical("necoJineho"), Is.EqualTo("necoJineho"));
            Assert.That(mission.Canonical(null), Is.Null);

            // Parametr bez vyctu se nekanonizuje vubec.
            Assert.That(ParamRegistry.TryGet("map", out var map), Is.True);
            Assert.That(map.Canonical("OSM/Neco.osm"), Is.EqualTo("OSM/Neco.osm"));
        }

        [Test]
        public void SlozenaHodnota_SeOveriParserem()
        {
            Assert.That(ParamRegistry.TryGet("start", out var start), Is.True);
            Assert.That(start.IsValidValue("50.1,14.5"), Is.True);
            Assert.That(start.IsValidValue("gps"), Is.True);
            Assert.That(start.IsValidValue("asd"), Is.False,
                        "presne tohle proslo pred zavedenim rozboru");

            Assert.That(ParamRegistry.TryGet("wheelslip", out var slip), Is.True);
            Assert.That(slip.IsValidValue("1.0,0.98"), Is.True);
            Assert.That(slip.IsValidValue("0,1"), Is.False, "prokluz musi byt kladny");
        }

        [Test]
        public void PrazdnaHodnotaProjde_IUParametruSVyctem()
        {
            // qrcamera= (prazdne) znamena VSECHNY kamery; prazdno nesmi spustit vycet ani rozbor.
            Assert.That(ParamRegistry.TryGet("qrcamera", out var qr), Is.True);
            Assert.That(qr.IsValidValue(string.Empty), Is.True);

            Assert.That(ParamRegistry.TryGet("mission", out var mission), Is.True);
            Assert.That(mission.IsValidValue(string.Empty), Is.True);
        }

        [Test]
        public void Validate_PrazdnySeznamKdyzJeVsePoradku()
        {
            var vady = ParamRegistry.Validate(new[]
            {
                new KeyValuePair<string, string>("mapcorr", "true"),
                new KeyValuePair<string, string>("roadwidth", "2.5"),
            });
            Assert.That(vady, Is.Empty);
        }

        [Test]
        public void Validate_VraciVSECHNYVadyNaraz()
        {
            // Vycet, ne prvni chyba: clovek ma profil opravit najednou, ne startovat mezi kazdou
            // opravou. Drzi to ParamStore.Build i panel Konfigurace.
            var vady = ParamRegistry.Validate(new[]
            {
                new KeyValuePair<string, string>("mapcor", "true"),     // preklep v klici
                new KeyValuePair<string, string>("mapcorr", "ano"),     // neplatna hodnota
                new KeyValuePair<string, string>("mission", "freerun"), // v poradku
            });

            Assert.That(vady, Has.Count.EqualTo(2));
            Assert.That(string.Join("|", vady), Does.Contain("mapcor").And.Contain("ano"));
        }

        [TestCase(ParamType.Bool, "true", true)]
        [TestCase(ParamType.Bool, "TRUE", true)]
        [TestCase(ParamType.Bool, "ano", false)]
        [TestCase(ParamType.Double, "0.05", true)]
        [TestCase(ParamType.Double, "-1", true)]
        [TestCase(ParamType.Double, "0,05", false)]   // desetinna carka neni InvariantCulture
        [TestCase(ParamType.Double, "x", false)]
        [TestCase(ParamType.String, "cokoliv", true)]
        [TestCase(ParamType.String, "", true)]
        [TestCase(ParamType.Path, "OSM/a.osm", true)]
        public void IsValidValue_PodleTypu(ParamType typ, string hodnota, bool ceka)
        {
            var def = new ParamDef { Name = "x", Type = typ, Description = "d", Category = "k" };
            Assert.That(def.IsValidValue(hodnota), Is.EqualTo(ceka));
        }
    }
}
