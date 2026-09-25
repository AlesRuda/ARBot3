using System;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// <see cref="RoadCorridorMsg"/> verze 6: skore prirazeni k hrane.
///
/// <para><b>Nacpak to je v zaznamu:</b> prah jednoznacnosti (<c>assocmargin=</c>) a strop
/// (<c>assocchi2=</c>) jdou jen tak proladit <b>offline</b> — jinak by se hadaly. Proto chodi
/// hodnoty i u zamitnutych cyklu; prave ty rikaji, kde byla mapa nejednoznacna.</para>
/// </summary>
public class RoadCorridorMsgAssocTests
{
    private static RoadCorridorMsg RoundTrip(RoadCorridorMsg msg)
    {
        var enc = Encoding.UTF8;
        var ms = new MemoryStream();
        var w = new MessageWriter(ms, enc);
        w.Write(msg);
        w.Flush();

        var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
        var reader = new MessageReader(new MemoryStream(ms.ToArray()), enc, map);
        return reader.Read() as RoadCorridorMsg;
    }

    [Test]
    public void Verze_je7()
    {
        // Verze 7 (24. 9. 2026) = merenie z jedne hrany; round-trip je v CorridorSingleEdgeTests.
        Assert.That(new RoadCorridorMsg().Verze, Is.EqualTo(7));
    }

    [Test]
    public void RoundTrip_nesePrirazeni()
    {
        var src = new RoadCorridorMsg
        {
            TimeStamp = new DateTime(2026, 9, 16, 16, 49, 26, DateTimeKind.Utc),
            Width = 3.2,
            AssocChi2 = 1.37,
            AssocChi2Second = 42.5,
            AssocCandidates = 3,
            FixReason = (byte)CorridorFixReason.Ok,
        };

        var back = RoundTrip(src);

        Assert.That(back, Is.Not.Null);
        Assert.That(back!.AssocChi2, Is.EqualTo(1.37).Within(1e-9));
        Assert.That(back.AssocChi2Second, Is.EqualTo(42.5).Within(1e-9));
        Assert.That(back.AssocCandidates, Is.EqualTo(3));
    }

    [Test]
    public void NepocitanePrirazeni_jeNaN_aPrezijeZapis()
    {
        // NaN rika "nepocitalo se", coz je JINA informace nez nula - ta by znamenala dokonalou
        // shodu. Starsi zaznamy (verze < 6) se proto musi nacist jako NaN, ne jako 0.
        var back = RoundTrip(new RoadCorridorMsg { TimeStamp = DateTime.UtcNow });

        Assert.That(double.IsNaN(back!.AssocChi2), Is.True);
        Assert.That(double.IsNaN(back.AssocChi2Second), Is.True);
    }

    [Test]
    public void NoveDuvody_majiVlastniHodnoty()
    {
        // Vycet se serializuje jako byte, takze hodnoty jsou soucast formatu zaznamu.
        Assert.That((byte)CorridorFixReason.EdgeMismatch, Is.EqualTo(10));
        Assert.That((byte)CorridorFixReason.AmbiguousEdge, Is.EqualTo(11));
    }
}
