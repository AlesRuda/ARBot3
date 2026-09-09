using System;
using ARBot.Common.Missions;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// Testy cteni souboru <c>*.track</c> (viz doc/track-mission.md).
///
/// <para>Nejdulezitejsi vlastnost, kterou hlidaji: <b>vadny radek je CHYBA, ne tiche
/// preskoceni</b>. Kdyby se preskocil, robot by objel jinou trasu, nez clovek zadal — a poznalo
/// by se to jen tim, co v ni NENI.</para>
/// </summary>
public class TrackPlanTests
{
    /// <summary>Priklad ze zadani mise.</summary>
    private static readonly string[] Priklad =
    {
        "50.0337431,14.5257403",
        "50.0336719,14.5253072",
        "50.0338847,14.5261453",
        "repeat",
    };

    [Test]
    public void Priklad_ZeZadani_DaTriBodyADokola()
    {
        var plan = TrackPlan.Parse(Priklad);

        Assert.That(plan.Count, Is.EqualTo(3));
        Assert.That(plan.Repeat, Is.True);
        // Souradnice se prevadeji na RADIANY uz pri cteni: soubor je okraj systemu, dal se
        // v projektu nese jen radian (viz CLAUDE.md).
        Assert.That(plan.Points[0].Latitude, Is.EqualTo(50.0337431 * Math.PI / 180.0).Within(1e-12));
        Assert.That(plan.Points[0].Longitude, Is.EqualTo(14.5257403 * Math.PI / 180.0).Within(1e-12));
        Assert.That(plan.Points[2].Latitude, Is.EqualTo(50.0338847 * Math.PI / 180.0).Within(1e-12));
    }

    [Test]
    public void BezRepeat_SeJedeJenJednou()
    {
        var plan = TrackPlan.Parse(new[] { "50.03,14.52", "50.04,14.53" });

        Assert.That(plan.Repeat, Is.False);
        Assert.That(plan.Count, Is.EqualTo(2));
    }

    [Test]
    public void PrazdneRadkyAKomentare_SePreskakuji()
    {
        var plan = TrackPlan.Parse(new[]
        {
            "# trasa kolem rybnika",
            "",
            "50.03,14.52",
            "   ",
            "50.04,14.53   ",
            "# konec",
        });

        Assert.That(plan.Count, Is.EqualTo(2));
        Assert.That(plan.SourceLines, Has.Count.EqualTo(2), "do zaznamu jdou jen skutecne radky");
    }

    [Test]
    public void NesrozumitelnyRadek_JeChyba_NeTichePreskoceni()
    {
        // TOHLE JE TEN DULEZITY TEST: tri hodnoty misto dvou, text, chybejici oddelovac.
        Assert.That(() => TrackPlan.Parse(new[] { "50.03,14.52", "50.04,14.53,100" }),
                    Throws.TypeOf<FormatException>());
        Assert.That(() => TrackPlan.Parse(new[] { "50.03,14.52", "u rybnika" }),
                    Throws.TypeOf<FormatException>());
        Assert.That(() => TrackPlan.Parse(new[] { "50.03,14.52", "50.04" }),
                    Throws.TypeOf<FormatException>());
    }

    [Test]
    public void HlaskaChyby_RikaCISLORADKU_ASoubor()
    {
        // Bez cisla radku by clovek hledal vadu v celem souboru rucne.
        var ex = Assert.Throws<FormatException>(
            () => TrackPlan.Parse(new[] { "# hlavicka", "50.03,14.52", "spatne" }, "abc.track"));

        Assert.That(ex.Message, Does.Contain("abc.track"));
        Assert.That(ex.Message, Does.Contain("radek 3"));
    }

    [Test]
    public void ProhozenaSirkaADelka_SePozna()
    {
        // Zamena je nejcastejsi omyl a v Cechach se NEPOZNA podle padu: 14,5 je platna sirka.
        // Chyti se to az na delce 50 > ... ne, 50 je platna delka. Proto se hlida rozsah sirky:
        // prohozene "14.52,50.03" projde, ale "200,50" uz ne. Test drzi aspon tu tvrdou hranici.
        Assert.That(() => TrackPlan.Parse(new[] { "95.0,14.5" }), Throws.TypeOf<FormatException>(),
                    "sirka nad 90 stupnu neexistuje");
        Assert.That(() => TrackPlan.Parse(new[] { "50.0,200.0" }), Throws.TypeOf<FormatException>(),
                    "delka nad 180 stupnu neexistuje");
    }

    [Test]
    public void RepeatUprostred_JeChyba()
    {
        // Radky za `repeat` by se nikdy neobjely, takze soubor by tvrdil neco jineho, nez robot
        // dela - a na to nic neupozorni.
        Assert.That(() => TrackPlan.Parse(new[] { "50.03,14.52", "repeat", "50.04,14.53" }),
                    Throws.TypeOf<FormatException>());
    }

    [Test]
    public void PrazdnySeznam_JeChyba()
    {
        Assert.That(() => TrackPlan.Parse(new[] { "# jen komentar", "" }),
                    Throws.TypeOf<FormatException>());
    }

    [Test]
    public void RepeatSJedinymBodem_JeChyba()
    {
        // Robot by dojel na to jedine misto a hlasil dojezd porad znovu.
        Assert.That(() => TrackPlan.Parse(new[] { "50.03,14.52", "repeat" }),
                    Throws.TypeOf<FormatException>());
    }

    [Test]
    public void DesetinnaCarka_JeChyba_NeJinyBod()
    {
        // "50,03,14,52" jsou ctyri cisla - kdyby se to tise vzalo jako dve, robot by jel
        // do Nigerie. Radeji chyba.
        Assert.That(() => TrackPlan.Parse(new[] { "50,03,14,52" }), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void StrednikIMezera_JsouTolerovane()
    {
        // Soubor pise clovek a kopiruje ho z mapy, kde je oddelovac podle nastroje jiny.
        var a = TrackPlan.Parse(new[] { "50.03;14.52" });
        var b = TrackPlan.Parse(new[] { "50.03 14.52" });

        Assert.That(a.Points[0].Latitude, Is.EqualTo(b.Points[0].Latitude).Within(1e-12));
        Assert.That(a.Points[0].Longitude, Is.EqualTo(b.Points[0].Longitude).Within(1e-12));
    }

    [Test]
    public void PointText_VracisStupne_ProCloveka()
    {
        var plan = TrackPlan.Parse(Priklad);

        Assert.That(plan.PointText(0), Does.StartWith("50.033743"));
        Assert.That(plan.PointText(99), Is.EqualTo("(mimo seznam)"), "mimo seznam se nepada");
    }
}
