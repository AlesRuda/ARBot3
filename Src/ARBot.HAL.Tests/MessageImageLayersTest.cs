using System;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Rozklad zprav na pojmenovane vrstvy (sjednoceni Blob + CameraFrame).
    /// </summary>
    public class MessageImageLayersTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Blob_Probability_OneGrayLayer()
        {
            var img = new Image<Gray>(8, 6);
            var blob = Blob.FromImage("sjizdnost", img);   // Type=Probability
            blob.TimeStamp = T0;

            var layers = MessageImageLayers.Extract(blob).ToList();

            Assert.That(layers.Count, Is.EqualTo(1));
            Assert.That(layers[0].Name, Is.EqualTo("sjizdnost"));
            Assert.That(layers[0].Kind, Is.EqualTo(LayerKind.Probability));
            Assert.That(layers[0].Gray, Is.Not.Null);
            Assert.That(layers[0].TimeStamp, Is.EqualTo(T0));
        }

        [Test]
        public void Blob_Jpeg_OneColorLayer()
        {
            var rgb = new Image<BGR32>(16, 16);
            var blob = Blob.FromImage("rgb", rgb, compress: true);   // Type=Jpeg

            var layers = MessageImageLayers.Extract(blob).ToList();

            Assert.That(layers.Count, Is.EqualTo(1));
            Assert.That(layers[0].Kind, Is.EqualTo(LayerKind.Color));
            Assert.That(layers[0].Color, Is.Not.Null);
        }

        [Test]
        public void CameraFrame_NamedLayers()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                ImageRGB = new Image<BGR32>(8, 6),
                ImageProbability = new Image<Gray>(8, 6),
                ImageDepth = new Image<Gray16>(8, 6)
            };

            var layers = MessageImageLayers.Extract(frame).ToList();

            Assert.That(layers.Select(l => l.Name),
                Is.EquivalentTo(new[] { "Left/RGB", "Left/Probability", "Left/Depth" }));
            Assert.That(layers.Single(l => l.Name == "Left/RGB").Kind, Is.EqualTo(LayerKind.Color));
            Assert.That(layers.Single(l => l.Name == "Left/Probability").Kind, Is.EqualTo(LayerKind.Probability));
            Assert.That(layers.Single(l => l.Name == "Left/Depth").Kind, Is.EqualTo(LayerKind.Depth));
        }

        [Test]
        public void CameraFrame_NullName_FallsBackToCamera()
        {
            var frame = new CameraFrame { ImageRGB = new Image<BGR32>(4, 4) };

            var layers = MessageImageLayers.Extract(frame).ToList();

            Assert.That(layers.Count, Is.EqualTo(1));
            Assert.That(layers[0].Name, Is.EqualTo("Camera/RGB"));
        }
    }
}
