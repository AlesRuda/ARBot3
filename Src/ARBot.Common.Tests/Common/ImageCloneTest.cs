using System;
using ARBot.Common.Common;

namespace ARBot.Common.Tests.Common
{
    /// <summary>Ověřuje, že <see cref="Image{T}.Clone"/> dělá hlubokou (nezávislou) kopii dat.</summary>
    public class ImageCloneTest
    {
        [Test]
        public void Clone_IsDeepCopy_IndependentData()
        {
            var orig = new Image<Gray>(2, 2);
            for (int i = 0; i < orig.Data.Length; i++)
                orig.Data[i] = (byte)(i + 1);   // 1,2,3,4

            var copy = orig.Clone();

            // stejné rozměry a hodnoty, ale JINÝ buffer
            Assert.That(copy.Width, Is.EqualTo(orig.Width));
            Assert.That(copy.Height, Is.EqualTo(orig.Height));
            Assert.That(copy.Data, Is.EqualTo(orig.Data));                  // shodné hodnoty
            Assert.That(ReferenceEquals(copy.Data, orig.Data), Is.False);   // ne stejná reference

            // změna v kopii neovlivní originál
            copy.Data[0] = 99;
            Assert.That(orig.Data[0], Is.EqualTo(1));
        }

        [Test]
        public void ICloneable_ReturnsImage()
        {
            var orig = new Image<BGR32>(1, 1);
            object clone = ((ICloneable)orig).Clone();
            Assert.That(clone, Is.InstanceOf<Image<BGR32>>());
        }
    }
}
