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
                if (d.DefaultFromCode || d.Default == null) continue;
                Assert.That(d.IsValidValue(d.Default), Is.True,
                            $"{d.Name}: vychozi hodnota '{d.Default}' neprojde vlastni validaci");
            }
        }

        [Test]
        public void DefaultFromCode_MaPopisVychoziHodnoty()
        {
            foreach (var d in ParamRegistry.All)
            {
                if (!d.DefaultFromCode) continue;
                Assert.That(d.DefaultDescription, Is.Not.Null.And.Not.Empty,
                            $"{d.Name}: DefaultFromCode bez popisu, panel by nemel co ukazat");
            }
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
