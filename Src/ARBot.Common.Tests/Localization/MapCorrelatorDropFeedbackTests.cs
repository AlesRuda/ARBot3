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

    [Test]
    public void SendCorrections_lzeVypnout_prepinacemKonfigurace()
    {
        // A/B "stejna zatez, jen bez korekci" stoji na tomhle prepinaci: vypocet bezi dal
        // (zprava se emituje), do fuze se ale neposila nic.
        Assert.That(new MapCorrelatorConfig().SendCorrections, Is.True, "vychozi stav je posilat");
        Assert.That(new MapCorrelatorConfig { SendCorrections = false }.SendCorrections, Is.False);
    }
}
