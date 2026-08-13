using ARBot.Common.Configuration;
using ARBot.Common.Fusion;

namespace ARBot.Common.Tests.Fusion;

/// <summary>
/// Vychozi hodnoty fuzni konfigurace, ktere musi odpovidat skutecnemu robotu.
/// </summary>
public class FusionConfigDefaultsTests
{
    /// <summary>
    /// Rozchod ve fuzi musi odpovidat <see cref="Profile.Rozchod"/> - z odometrie se pocita
    /// <c>omega = (vR - vL) / WheelBase</c>, takze nesouhlas znamena systematickou chybu
    /// uhlove rychlosti (pri 0,5 vs 0,41 by to bylo -18 %). Konfigurace se v provozu nikde
    /// neprepisuje, takze musi byt spravna uz jako default.
    /// </summary>
    [Test]
    public void WheelBase_MatchesRobotProfile()
    {
        Assert.That(new FusionConfig().WheelBase, Is.EqualTo(Profile.Rozchod).Within(1e-9));
    }
}
