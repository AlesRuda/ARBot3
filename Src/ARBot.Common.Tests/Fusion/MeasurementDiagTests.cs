using System;
using System.Collections.Generic;
using System.IO;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Fusion;

/// <summary>
/// Testy diagnostiky merenii ve fuzi: proc bylo merenie (ne)pouzite. Vzniklo 21. 8. 2026 pri
/// rozboru zaznamu, kde slo poznat, ze korekce z korelace fuze zahazuje, ale NE jestli ostatni
/// projdou gatingem - <c>MeasurementDiagMsg</c> byla v katalogu, ale nikdo ji nepublikoval.
/// Viz doc/map-correlation-localization.md.
/// </summary>
public class MeasurementDiagTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc);

    private static AsyncFusionEngine Engine(TimeSpan? window = null)
    {
        var cfg = new FusionConfig();
        var model = new EKFModel(cfg);
        return new AsyncFusionEngine(model, window ?? TimeSpan.FromSeconds(1));
    }

    private static ScalarStateMeasurement Speed(double v, DateTime t, string source = "Odo/speed")
        => ScalarStateMeasurement.Velocity(v, 0.05, t, source);

    // --- zprava -----------------------------------------------------------------------------

    [Test]
    public void ToLogMessage_OpisujeUdajeIDuvod()
    {
        var info = new AsyncFusionEngine.MeasurementInfo
        {
            Source = "MapCorr",
            Time = T0,
            Nis = 12.5,
            Accepted = false,
            Verdict = MeasurementVerdict.TooOld,
            Z = new[] { 3.5 },
            DiagR = new[] { 0.0121 },
        };

        var msg = info.ToLogMessage();

        Assert.That(msg.Source, Is.EqualTo("MapCorr"));
        Assert.That(msg.TimeStamp, Is.EqualTo(T0));
        Assert.That(msg.Nis, Is.EqualTo(12.5).Within(1e-9));
        Assert.That(msg.Accepted, Is.False);
        Assert.That(msg.Verdict, Is.EqualTo((byte)MeasurementVerdict.TooOld));
        Assert.That(msg.Z, Is.EqualTo(new[] { 3.5 }));
        Assert.That(msg.DiagR, Is.EqualTo(new[] { 0.0121 }));
        Assert.That(((IHasCaptureTime)msg).CaptureTime, Is.EqualTo(T0));
    }

    [Test]
    public void SerializaceJeObousmerna()
    {
        var original = new AsyncFusionEngine.MeasurementInfo
        {
            Source = "MapCorr", Time = T0, Nis = 3.25, Accepted = true,
            Verdict = MeasurementVerdict.Accepted, Z = new[] { 1.0, 2.0 }, DiagR = new[] { 0.1, 0.2 },
        }.ToLogMessage();

        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new MeasurementDiagMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(loaded.Source, Is.EqualTo(original.Source));
        Assert.That(loaded.TimeStamp, Is.EqualTo(original.TimeStamp));
        Assert.That(loaded.Nis, Is.EqualTo(original.Nis).Within(1e-9));
        Assert.That(loaded.Accepted, Is.EqualTo(original.Accepted));
        Assert.That(loaded.Verdict, Is.EqualTo(original.Verdict));
        Assert.That(loaded.Z, Is.EqualTo(original.Z));
        Assert.That(loaded.DiagR, Is.EqualTo(original.DiagR));
    }

    [Test]
    public void StaryZaznamBezVerdiktu_SeCteDal()
    {
        // Verze 1 nesla Verdict. Stary zaznam se musi precist a verdikt se dopocita z Accepted,
        // aby stara data ve telemetrii nevypadala jako "zahozeno pro stari".
        var v1 = new MeasurementDiagMsg { Source = "GPS/position", TimeStamp = T0, Nis = 1.5,
                                          Accepted = true, Z = new[] { 5.0 }, DiagR = new[] { 2.25 } };
        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v1.ToDataV1ForTest(bw);

        buffer.Position = 0;
        var loaded = new MeasurementDiagMsg { Verze = 1 };
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(loaded.Source, Is.EqualTo("GPS/position"));
        Assert.That(loaded.Accepted, Is.True);
        Assert.That(loaded.Verdict, Is.EqualTo((byte)MeasurementVerdict.Accepted));
    }

    [Test]
    public void JeOdvozenaZprava_NeniPrimarni()
    {
        Assert.That(new MeasurementDiagMsg() is IPrimaryMessage, Is.False);
    }

    [Test]
    public void JmenoInstanceJeZdrojMerenia()
    {
        // Bez toho by se v indexu zaznamu i v telemetrii michaly korekce z korelace s GPS.
        var msg = new MeasurementDiagMsg { Source = "MapCorr" };

        Assert.That(((INamedMessage)msg).Name, Is.EqualTo("MapCorr"));
    }

    // --- hlaseni z motoru fuze ---------------------------------------------------------------

    [Test]
    public void MerenieStarsiNezOkno_ohlasiTooOld_ihned()
    {
        var e = Engine(TimeSpan.FromSeconds(1));
        var seen = new List<AsyncFusionEngine.MeasurementInfo>();
        e.OnMeasurement = i => seen.Add(i);

        // Prah zahozeni je tBase, ne "nejnovejsi minus okno": tBase se posouva az na uzly, ktere
        // z okna VYPADLY. Rada v sekundovych krocich ho proto musi nejdriv posunout - jednorazovy
        // skok casu nestaci (merenie by se jen vlozilo a hned zapeklo do baze, coz je spravne).
        for (int i = 0; i <= 5; i++)
            e.Enqueue(Speed(1.0, T0.AddSeconds(i)));
        e.GetStateAt(T0.AddSeconds(5));
        seen.Clear();

        e.Enqueue(Speed(1.0, T0.AddSeconds(2.5), "MapCorr"));  // starsi nez tBase -> zahozeno

        Assert.That(seen, Has.Count.EqualTo(1), "zahozeni se hlasi hned, ne az pri prune");
        Assert.That(seen[0].Source, Is.EqualTo("MapCorr"));
        Assert.That(seen[0].Verdict, Is.EqualTo(MeasurementVerdict.TooOld));
        Assert.That(seen[0].Accepted, Is.False);
        Assert.That(seen[0].Time, Is.EqualTo(T0.AddSeconds(2.5)));
        Assert.That(e.DroppedTooOldBySource()["MapCorr"], Is.EqualTo(1), "pocitadlo jde ruku v ruce");
    }

    [Test]
    public void PrijateMerenie_ohlasiSeAzPriVypadnutiZOkna()
    {
        var e = Engine(TimeSpan.FromSeconds(1));
        var seen = new List<AsyncFusionEngine.MeasurementInfo>();
        e.OnMeasurement = i => seen.Add(i);

        e.Enqueue(Speed(1.0, T0));
        e.Enqueue(Speed(1.0, T0.AddMilliseconds(500)));
        e.GetStateAt(T0.AddMilliseconds(500));

        Assert.That(seen, Is.Empty, "dokud je merenie v okne, verdikt neni konecny");

        // Posun casu za okno -> nejstarsi uzly se zapecou do baze a tim je verdikt konecny.
        e.Enqueue(Speed(1.0, T0.AddSeconds(3)));
        e.GetStateAt(T0.AddSeconds(3));

        Assert.That(seen, Is.Not.Empty);
        Assert.That(seen[0].Time, Is.EqualTo(T0));
        Assert.That(seen[0].Verdict, Is.EqualTo(MeasurementVerdict.Accepted));
        Assert.That(seen[0].Accepted, Is.True);
    }

    [Test]
    public void GatingemZahozeneMerenie_ohlasiGatedOut()
    {
        var e = Engine(TimeSpan.FromSeconds(1));
        var seen = new List<AsyncFusionEngine.MeasurementInfo>();
        e.OnMeasurement = i => seen.Add(i);

        e.Enqueue(Speed(0.0, T0));
        // Nesmyslna rychlost s tvrdym prahem gatingu -> NIS ho prekroci a merenie se zahodi.
        e.Enqueue(new ScalarStateMeasurement(EKFModel.IV, 500.0, 0.01, T0.AddMilliseconds(100),
                                             "Odo/speed") { GateThreshold = 3.84 });
        e.Enqueue(Speed(0.0, T0.AddSeconds(3)));
        e.GetStateAt(T0.AddSeconds(3));

        var gated = seen.Find(i => i.Time == T0.AddMilliseconds(100));
        Assert.That(gated.Verdict, Is.EqualTo(MeasurementVerdict.GatedOut));
        Assert.That(gated.Accepted, Is.False);
        Assert.That(gated.Nis, Is.GreaterThan(3.84));
    }

    [Test]
    public void BezOdberatele_seNicNepocita()
    {
        // Vychozi stav: OnMeasurement je null a nesmi to nikde padnout ani nic stat.
        var e = Engine();
        Assert.That(e.OnMeasurement, Is.Null);
        Assert.DoesNotThrow(() =>
        {
            e.Enqueue(Speed(1.0, T0));
            e.Enqueue(Speed(1.0, T0.AddSeconds(5)));
            e.GetStateAt(T0.AddSeconds(5));
            e.Enqueue(Speed(1.0, T0.AddSeconds(1)));
        });
    }
}
