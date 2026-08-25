using System;
using System.IO;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Korelace hlasi ve sve zprave, kolik jejich korekci fuze zahodila jako starsi nez okno historie.
///
/// <para><b>Proc:</b> 21. 8. 2026 se nad zaznamem ukazalo, ze fuze zahodila 12 korekci z 5 cyklu,
/// a telemetrie u vsech dal hlasila <c>Reason = Ok</c> — presne past, kterou popisuje
/// <see cref="AsyncFusionEngine.DroppedTooOld"/>. Pocitadlo ve zprave to ukaze bez hrabani
/// v <c>Info</c> hlaskach a bez zapnute diagnostiky merenii.
/// Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class MapCorrelatorDropFeedbackTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ToLogMessage_NeseZahozeniFuzi()
    {
        var r = MapCorrelationResult.From(T0, new ScanResult { Score = 0.9, CoarseStrideScoreAtPeak = 0.9 },
                                          CorrelationCovariance.ForTest(0.12, 2.4, Math.PI / 2, 0.018),
                                          evidenceCells: 5000, rivalAlongTight: 0.1, rivalAlongLoose: 0.1,
                                          new MapCorrelatorConfig());
        r.DroppedByFusion = 7;

        var msg = r.ToLogMessage();

        Assert.That(msg.DroppedByFusion, Is.EqualTo(7));
    }

    [Test]
    public void ZahozeniPrezijeSerializaci()
    {
        var original = new MapCorrelationMsg { TimeStamp = T0, DroppedByFusion = 12 };

        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(loaded.DroppedByFusion, Is.EqualTo(12));
    }

    [Test]
    public void StaryZaznamBezPocitadla_SeCteDal()
    {
        // Verze 1 pocitadlo nenesla; stary zaznam musi jit precist a hlasit 0, ne spadnout.
        var v1 = new MapCorrelationMsg { TimeStamp = T0, Dx = 0.5, DroppedByFusion = 3 };
        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v1.ToDataV1ForTest(bw);

        buffer.Position = 0;
        var loaded = new MapCorrelationMsg { Verze = 1 };
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(loaded.Dx, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(loaded.DroppedByFusion, Is.EqualTo(0));
    }

    /// <summary>
    /// Informativni dukaz prezije serializaci - a hodnota z <b>verze 3</b> se ZAHODI.
    ///
    /// <para>Verze 3 (25. 8. 2026, jediny den) nesla na temze miste SUROVY vazeny POCET bunek.
    /// Bajty jsou stejne, jednotky ne: cist starou hodnotu jako m²·log-odds by znamenalo srovnavat
    /// necislo (rozdil je plocha bunky, tedy 400x pri 5 cm). Radeji nula nez tichy nesmysl.</para>
    /// </summary>
    [Test]
    public void InformativniDukaz_PrezijeSerializaci_AVerze3SeZahodi()
    {
        var original = new MapCorrelationMsg { TimeStamp = T0, InformativeEvidence = 37.5 };
        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var v4 = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v4.FromData(br);
        Assert.That(v4.InformativeEvidence, Is.EqualTo(37.5).Within(1e-9));

        // Tytez bajty, jen hlaseny jako verze 3 - hodnota musi zmizet, ne se prevzit.
        buffer.Position = 0;
        var v3 = new MapCorrelationMsg { Verze = 3 };
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v3.FromData(br);
        Assert.That(v3.InformativeEvidence, Is.EqualTo(0.0),
                    "hodnota verze 3 je v jinych jednotkach - musi se zahodit");
        Assert.That(v3.TimeStamp, Is.EqualTo(T0), "zbytek zpravy se precte dal");
    }

    /// <summary>
    /// <b>Poza, proti ktere se korelovalo, musi cestovat ve zprave</b> (verze 5).
    ///
    /// <para><c>Dx</c>/<c>Dy</c> je posun proti TE poze, takze bez ni nejde poznat, jestli je
    /// nenulovy posun chybou korelatoru, nebo chybou pozy, kterou korelator SPRAVNE nasel. Presne
    /// na tom se 25. 8. 2026 spletlo meridlo: dohledavalo odhad z <c>RobotStateMsg</c> podle razitka
    /// a chybu FUZE ucetlovalo korelatoru (vychyleni 0,191 m proti skutecnym 0,018 m).</para>
    /// </summary>
    [Test]
    public void PozaProtiKtereSeKorelovalo_JdeDoZpravy_AStaryZaznamJiNema()
    {
        var original = new MapCorrelationMsg
        {
            TimeStamp = T0, PoseX = 12.5, PoseY = -3.25, PoseTheta = 0.75, HasPose = true,
        };
        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.HasPose, Is.True);
            Assert.That(loaded.PoseX, Is.EqualTo(12.5).Within(1e-9));
            Assert.That(loaded.PoseY, Is.EqualTo(-3.25).Within(1e-9));
            Assert.That(loaded.PoseTheta, Is.EqualTo(0.75).Within(1e-9));
        });

        // Tytez bajty hlasene jako verze 4: poza tam jeste nebyla, takze se nesmi tvarit, ze je.
        buffer.Position = 0;
        var v4 = new MapCorrelationMsg { Verze = 4 };
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v4.FromData(br);
        Assert.That(v4.HasPose, Is.False, "stary zaznam pozu nenese - nula neni poza");
    }

    [Test]
    public void SendCorrections_lzeVypnout_prepinacemKonfigurace()
    {
        // A/B "stejna zatez, jen bez korekci" stoji na tomhle prepinaci: vypocet bezi dal
        // (zprava se emituje), do fuze se ale neposila nic.
        Assert.That(new MapCorrelatorConfig().SendCorrections, Is.True, "vychozi stav je posilat");
        Assert.That(new MapCorrelatorConfig { SendCorrections = false }.SendCorrections, Is.False);
    }
}
