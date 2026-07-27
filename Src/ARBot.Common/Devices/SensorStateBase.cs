using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Predek pro mereni senzoru.
    ///
    /// Je zaroven <see cref="Message"/> - kazde mereni je tim primo prenositelne a
    /// zaznamenatelne v pipeline (record/replay) bez wrapperu a bez konverze. Konkretni
    /// potomek, ktery chce byt (de)serializovatelny, prepise <see cref="ToData"/>,
    /// <see cref="FromData"/> a <see cref="Message.Build"/>; ostatni pouziji vychozi
    /// impl., ktera vyhodi <see cref="NotSupportedException"/>. Metadata ramce lze
    /// (de)serializovat pomocnymi <see cref="WriteMeta"/> / <see cref="ReadMeta"/>.
    /// </summary>
    public abstract class SensorStateBase : Message, IHasCaptureTime, IPrimaryMessage
    {
        /// <summary>
        /// Nazev zpravy = jmeno konkretniho typu (napr. "IMUState"). Potomek MUSI predat verzi
        /// formatu serializace (typicky svou konstantu <c>FormatVersion</c>): pri kazde zmene
        /// obsahu zpravy se verze zvedne a <see cref="Message.FromData(BinaryReader)"/> vetvi
        /// podle <see cref="Message.Verze"/> (viz doc/record-replay.md → Verzovani zprav).
        /// </summary>
        protected SensorStateBase(int verze) : base(string.Empty, verze)
        {
            MsgName = GetType().Name;
        }

        /// <summary>
        /// Poradi vrozku
        /// </summary>
        public uint FrameNum;

        /// <summary>
        /// Pocet preskocenych vzorku pred timto a predchozim vyzvednutym
        /// </summary>
        public uint DropedOutNum;

        /// <summary>
        /// Doba od prichodu predchoziho frejmu v s
        /// </summary>
        public TimeSpan FrameReceivePeriod;
        /// <summary>
        /// Doba od vyzvednuti predchoziho frejmu v s
        /// </summary>
        public TimeSpan FramePickupPeriod;

        /// <summary>
        /// okamzik vzorku
        /// </summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        /// <summary>Zapise spolecna metadata ramce (FrameNum, DropedOutNum, periody, TimeStamp).</summary>
        protected void WriteMeta(BinaryWriter bw) => Write(bw, this);

        /// <summary>Nacte spolecna metadata ramce (musi presne zrcadlit <see cref="WriteMeta"/>).</summary>
        protected void ReadMeta(BinaryReader br)
        {
            FrameNum = br.ReadUInt32();
            DropedOutNum = br.ReadUInt32();
            FrameReceivePeriod = new TimeSpan(br.ReadInt64());
            FramePickupPeriod = new TimeSpan(br.ReadInt64());
            TimeStamp = new DateTime(br.ReadInt64());
        }

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
            => throw new NotSupportedException($"{GetType().Name} nepodporuje serializaci (ToData).");

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
            => throw new NotSupportedException($"{GetType().Name} nepodporuje deserializaci (FromData).");

        /// <inheritdoc/>
        public override Message Build()
            => throw new NotSupportedException($"{GetType().Name} nepodporuje Build().");
    }
}
