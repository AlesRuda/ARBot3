using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

/// <summary>
/// <b>Délky dočasných hran z cílového splitu.</b>
///
/// <para><c>InsertGoal</c> rozřízne hranu nejbližší cíli a vloží dočasný uzel; obě půlky dostávají
/// <b>traversal cost</b> úměrný podílu <c>t</c>. Do 26. 8. 2026 ale dostávaly
/// <see cref="Edge.LengthMeters"/> rovnou <b>nule</b> — tedy hrana s reálnou geometrickou délkou
/// o sobě tvrdila, že je nulová.</para>
///
/// <para><b>Proč to vadilo:</b> délka trasy se počítá jako součet <c>LengthMeters</c> hran
/// (<c>GlobalNavMsg.RouteLengthM</c> i zkouška dosažitelnosti pro misi Robotour), takže poslední
/// úsek k cíli se do ní <b>nezapočítal vůbec</b> — a chyba rostla s délkou rozříznuté hrany.
/// Nahlásil autor 26. 8. 2026 při dotazu, jak dosažitelnost funguje.</para>
/// </summary>
public class GoalFieldSplitLengthTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    /// <summary>Obousměrná linka n1-n2-n3 po 100 m.</summary>
    private static (RoadNetwork net, Node n1, Node n2, Node n3) Line100()
    {
        var b = new RoadNetwork.Builder();
        var n1 = N(1, 50.0, 14.0000);
        var n2 = N(2, 50.0, 14.0010);
        var n3 = N(3, 50.0, 14.0020);
        Edge E(Node x, Node y) => b.AddEdge(x, y, 100, 1, 100);
        var all = new[] { E(n1, n2), E(n2, n1), E(n2, n3), E(n3, n2) };
        foreach (var i in all)
            foreach (var o in all)
                if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId))
                    b.AddTurn(i, o, 0);
        return (b.Build(), n1, n2, n3);
    }

    /// <summary>
    /// Půlky rozříznuté hrany mají délky <c>t·L</c> a <c>(1−t)·L</c>, a <b>dohromady dají původní
    /// délku</b>. Virtuální smyčka cíle (<c>From == To</c>) zůstává nulová — ta žádnou geometrii nemá.
    /// </summary>
    [Test]
    public void PulkyRozriznuteHrany_MajiDelkyPodleDelicihoPomeru()
    {
        var (net, _, n2, n3) = Line100();

        // Cíl v 30 % úseku n2->n3 (úsek je 100 m, takže 30 m a 70 m).
        var goal = LLA.FromDegrees(50.0, 14.0013);
        var field = new GoalField(net, goal);

        // Dočasné hrany poznáme podle WayId = -1 (originály mají 1).
        var temps = field.Nodes.Where(e => e.WayId == -1 && e.From.Id != e.To.Id).ToList();

        Assert.That(temps, Is.Not.Empty, "split musi vyrobit docasne hrany");
        Assert.Multiple(() =>
        {
            Assert.That(temps.All(e => e.LengthMeters > 0), Is.True,
                        "hrana s realnou geometrii nesmi tvrdit, ze je nulova");

            // Půlka od n2 k dělicímu bodu ~30 m, od dělicího bodu k n3 ~70 m (v obou směrech).
            foreach (var e in temps)
                Assert.That(e.LengthMeters, Is.EqualTo(30).Within(2).Or.EqualTo(70).Within(2),
                            $"delka pulky {e.From.Id}->{e.To.Id} neodpovida delicimu pomeru");
        });
    }

    /// <summary>
    /// <b>Součet délek trasy dosáhne až k cíli.</b> Robot u <c>n1</c>, cíl 30 m za <c>n2</c>:
    /// trasa je 100 m (n1→n2) + 30 m (půlka) = 130 m. Se starou nulovou délkou vycházelo 100 m.
    /// </summary>
    [Test]
    public void DelkaTrasy_ZapocitaIPosledniUsekKCili()
    {
        var (net, n1, _, _) = Line100();
        var goal = LLA.FromDegrees(50.0, 14.0013);      // 30 % úseku n2->n3
        var field = new GoalField(net, goal);

        var route = new Router(field).Plan(n1.Location);
        double length = route.Sum(e => e.LengthMeters);

        Assert.That(route, Is.Not.Empty, "trasa ma existovat");
        Assert.That(length, Is.EqualTo(130).Within(3),
                    "100 m n1->n2 + 30 m posledni pulka; drive vychazelo 100 m");
    }
}
