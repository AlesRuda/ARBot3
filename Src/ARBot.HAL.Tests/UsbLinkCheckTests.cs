using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests;

/// <summary>
/// <see cref="UsbLinkCheck"/> — posudek USB linky kamery podle <c>CameraInfo.UsbTypeDescriptor</c>.
///
/// <para><b>Proč to vzniklo:</b> 2. 9. 2026 obě D435 po restartu naskočily jako
/// <c>high-speed</c> (480 Mbps) místo SuperSpeed, přestože se s kabely nemanipulovalo — a protože
/// driver ten údaj vůbec nečetl, vypadalo to jako porucha streamu a příčina se hodinu hledala
/// měřením zvenčí. Kernel si přitom nestěžoval ani jednou.</para>
///
/// <para>Testy drží dvě věci: že se USB 2.0 <b>pozná</b> (a v logu je i důvod a léčba), a že
/// <b>neznámá hodnota není poplach</b> — jinak by nástroj na vysvětlování poruch sám poruchy
/// vyráběl.</para>
/// </summary>
public class UsbLinkCheckTests
{
    [TestCase("2.0")]
    [TestCase("2.1")]
    [TestCase(" 2.1 ")]
    [TestCase("1.1")]
    public void Usb2_SePozna(string deskriptor)
    {
        Assert.That(UsbLinkCheck.JeUsb2(deskriptor), Is.True);
    }

    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public void SuperSpeed_NeniPoplach(string deskriptor)
    {
        Assert.That(UsbLinkCheck.JeUsb2(deskriptor), Is.False);
        // V popisu nesmí být varování — jinak by se hlásila porucha na zdravé lince.
        Assert.That(UsbLinkCheck.Popis(deskriptor), Does.Not.Contain("POZOR"));
        Assert.That(UsbLinkCheck.Popis(deskriptor), Does.Contain(deskriptor));
    }

    /// <summary>
    /// Chybějící nebo nesrozumitelný údaj <b>není</b> porucha: librealsense ho u některých
    /// zařízení nehlásí (indexer <c>Info[]</c> vrací <c>null</c>). Táž konvence jako u brány
    /// kvality GPS — „přijímač, který DOP nehlásí, projde".
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("neznamo")]
    public void NeznamaHodnota_NeniPoplach(string? deskriptor)
    {
        Assert.That(UsbLinkCheck.JeUsb2(deskriptor), Is.False);
        Assert.That(UsbLinkCheck.Popis(deskriptor), Does.Not.Contain("POZOR"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void ChybejiciHodnota_SeVLoguPriznaJakoNeuvedena(string? deskriptor)
    {
        // Ne mlčet: „neuvedeno" se od „USB 3.2" musí dát v logu rozeznat, jinak by chybějící
        // diagnostika vypadala jako zdravá linka.
        Assert.That(UsbLinkCheck.Popis(deskriptor), Does.Contain("neuvedeno"));
    }

    /// <summary>
    /// Na USB 2.0 musí být v logu <b>důvod i léčba</b>. Právě tohle 2. 9. 2026 chybělo: hláška
    /// „USB 2.1" sama o sobě nikomu neřekne, že se tam dvě kamery nevejdou a že pomůže replug.
    /// </summary>
    [Test]
    public void Usb2_VLoguJeDuvodILecba()
    {
        string popis = UsbLinkCheck.Popis("2.1");

        Assert.That(popis, Does.Contain("2.1"));
        Assert.That(popis, Does.Contain("POZOR"));
        Assert.That(popis, Does.Contain("SuperSpeed"));
        Assert.That(popis, Does.Contain("FYZICKE"));
    }
}
