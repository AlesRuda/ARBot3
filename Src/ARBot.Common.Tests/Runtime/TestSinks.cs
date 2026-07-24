using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using System.Numerics;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>Cil volajici delegat pro kazdou zpravu (pro testy).</summary>
    internal sealed class DelegateTarget : MessageTarget
    {
        private readonly Action<Message> onMsg;
        public DelegateTarget(Action<Message> onMsg) : base(OverflowPolicy.Block) => this.onMsg = onMsg;
        protected override void Consume(Message msg) => onMsg(msg);
    }

    /// <summary>Sdilene pomocne funkce pro testy record/replay.</summary>
    internal static class TestHelpers
    {
        public static readonly Encoding Enc = Encoding.UTF8;

        /// <summary>Vytvori syntetickou IMU se zadanym kurzem (yaw) a uhlovou rychlosti.</summary>
        public static IMUState MakeImu(DateTime t, double yaw, double omega)
        {
            var q = new YawPitchRoll((float)yaw, 0f, 0f).ToQuaternion(YawPitchRoll.Euler.zxy);
            return new IMUState
            {
                TimeStamp = t,
                Rotation = q,
                AngularVelocity = new Vector3(0f, 0f, (float)omega),
                Confidence = 1.0
            };
        }

        /// <summary>Precte vsechny zpravy z bajtu zaznamu.</summary>
        public static List<Message> ReadMessages(byte[] bytes, MessageCatalog catalog)
        {
            var list = new List<Message>();
            var ms = new MemoryStream(bytes);
            var r = new MessageReader(ms, Enc, catalog.ToPrototypeMap());
            while (ms.Position < ms.Length)
            {
                Message m;
                try { m = r.Read(); }
                catch (EndOfStreamException) { break; }
                if (m != null) list.Add(m);
            }
            return list;
        }
    }
}
