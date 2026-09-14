using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Missions;
using ARBot.Common.Rendering;
using NUnit.Framework;

namespace ARBot.Common.Tests.Rendering
{
    /// <summary>
    /// <see cref="GoalZones"/> — společný výběr zón, které mají být dosaženy.
    ///
    /// <para>Logika vznikla 12. 9. 2026 uvnitř <c>WebStatus</c>, tedy jen pro stránku náhledu;
    /// 14. 9. se vytáhla ven, aby ji mohl kreslit i World pohled v Avalonii. Testy jsou tady proto,
    /// že právě tahle pravidla si na sebe už jednou došlápla — <b>kreslí se místo PŘICHYCENÉ</b>
    /// na síť, a <b>zdroje se nemíchají</b>.</para>
    /// </summary>
    public class GoalZonesTests
    {
        private static TrackMsg Track(int pointIndex, double[] lat, double[] lon,
                                      double[] snapLat = null, double[] snapLon = null)
            => new TrackMsg
            {
                PointIndex = pointIndex,
                AllLatitudes = lat,
                AllLongitudes = lon,
                SnappedLatitudes = snapLat,
                SnappedLongitudes = snapLon,
            };

        /// <summary>Bez zpráv není co kreslit — a nesmí to spadnout na <c>null</c>.</summary>
        [Test]
        public void BezZprav_ZadneZony()
            => Assert.That(GoalZones.Select(null, null, null), Is.Empty);

        /// <summary>
        /// Seznam míst z <see cref="TrackMsg"/> dá zónu na každé místo; aktivní je ta, ke které se
        /// jede. Před odjezdem je <c>PointIndex</c> ještě −1, takže aktivní není žádná — a to je
        /// správně, robot zatím nikam nejede.
        /// </summary>
        [Test]
        public void TrackMsg_DaZonuNaKazdeMisto()
        {
            var t = Track(1,
                          new[] { 0.8731, 0.8732, 0.8733 },
                          new[] { 0.2536, 0.2537, 0.2538 });

            var z = GoalZones.Select(t, null, null);

            Assert.Multiple(() =>
            {
                Assert.That(z, Has.Count.EqualTo(3));
                Assert.That(z.Select(x => x.Label), Is.EqualTo(new[] { "1", "2", "3" }));
                Assert.That(z[1].Active, Is.True, "aktivni je misto, ke kteremu se jede");
                Assert.That(z[0].Active, Is.False);
                Assert.That(z[2].Active, Is.False);
            });
        }

        /// <summary>
        /// ⚠️ <b>Kreslí se místo PŘICHYCENÉ na síť</b>, ne surový bod ze souboru: robot jede na
        /// průmět a proti němu se měří dojezd. Nález autora 13. 9. 2026 — na surovém bodě ležela
        /// zóna vedle cesty a vypadalo to, že se nepřichycuje vůbec.
        /// </summary>
        [Test]
        public void TrackMsg_KresliPrichyceneMisto()
        {
            var t = Track(0,
                          new[] { 0.8731 }, new[] { 0.2536 },
                          snapLat: new[] { 0.8739 }, snapLon: new[] { 0.2544 });

            var z = GoalZones.Select(t, null, null);

            Assert.Multiple(() =>
            {
                Assert.That(z[0].LatRad, Is.EqualTo(0.8739).Within(1e-12), "prichycene, ne surove");
                Assert.That(z[0].LonRad, Is.EqualTo(0.2544).Within(1e-12));
            });
        }

        /// <summary>Bez přichycených souřadnic (starší záznam, verze zprávy &lt; 3) se vezmou surové.</summary>
        [Test]
        public void TrackMsg_BezPrichycenych_VezmeSurove()
        {
            var t = Track(0, new[] { 0.8731 }, new[] { 0.2536 });

            var z = GoalZones.Select(t, null, null);

            Assert.That(z[0].LatRad, Is.EqualTo(0.8731).Within(1e-12));
        }

        /// <summary>
        /// ⚠️ <b>Zdroje se NEMÍCHAJÍ.</b> Cíl navigace je totéž místo přichycené na síť, takže by
        /// vedle sebe vyšly dvě kružnice pár metrů od sebe a nikdo by nevěděl, která platí.
        /// </summary>
        [Test]
        public void MiseVyhravaNadCilemNavigace()
        {
            var t = Track(0, new[] { 0.8731 }, new[] { 0.2536 });
            var nav = new GlobalNavMsg { HasGoal = true, GoalLatDeg = 50.1, GoalLonDeg = 14.5 };

            var z = GoalZones.Select(t, null, nav);

            Assert.That(z, Has.Count.EqualTo(1), "jen misto mise, cil navigace ne");
        }

        /// <summary>Teprve když mise žádná místa nemá, kreslí se cíl globální navigace.</summary>
        [Test]
        public void BezMise_KresliCilNavigace()
        {
            var nav = new GlobalNavMsg { HasGoal = true, GoalLatDeg = 50.1, GoalLonDeg = 14.5 };

            var z = GoalZones.Select(null, null, nav);

            Assert.Multiple(() =>
            {
                Assert.That(z, Has.Count.EqualTo(1));
                Assert.That(z[0].Label, Is.EqualTo("cil"));
                Assert.That(z[0].Active, Is.True);
                Assert.That(z[0].LatRad, Is.EqualTo(Conversions.Deg2Rad(50.1)).Within(1e-12));
            });
        }

        /// <summary>Robotour: depo / nakládka / vykládka, aktivní podle fáze.</summary>
        [Test]
        public void MissionMsg_DaStanoviste()
        {
            var m = new MissionMsg
            {
                Phase = (int)RobotourPhase.DrivingToPickup,
                HasDepot = true, DepotLatDeg = 50.1, DepotLonDeg = 14.5,
                HasPickup = true, PickupLatDeg = 50.2, PickupLonDeg = 14.6,
            };

            var z = GoalZones.Select(null, m, null);

            Assert.Multiple(() =>
            {
                Assert.That(z.Select(x => x.Label), Is.EqualTo(new[] { "depo", "nakladka" }));
                Assert.That(z[0].Active, Is.False);
                Assert.That(z[1].Active, Is.True, "jede se na nakladku");
            });
        }

        /// <summary>
        /// Poloměr se bere <b>z dat</b> (<c>GlobalNavMsg.GoalRadiusM</c>), a dokud nedošel,
        /// z výchozího nastavení navigátoru — aby se to číslo neopisovalo na dvou místech.
        /// </summary>
        [Test]
        public void Polomer_ZDatJinakVychozi()
        {
            var t = Track(0, new[] { 0.8731 }, new[] { 0.2536 });

            var bezNav = GoalZones.Select(t, null, null);
            var sNav = GoalZones.Select(t, null, new GlobalNavMsg { GoalRadiusM = 7.5 });

            Assert.Multiple(() =>
            {
                Assert.That(bezNav[0].RadiusM,
                            Is.EqualTo(NavigatorOptions.DefaultArrivalRadiusMeters).Within(1e-12));
                Assert.That(sNav[0].RadiusM, Is.EqualTo(7.5).Within(1e-12));
            });
        }
    }
}
