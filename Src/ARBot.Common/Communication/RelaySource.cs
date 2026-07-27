using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Pruchozi (relay) uzel: zaroven <see cref="MessageSource"/> (odberatele se pripoji
    /// pres <see cref="MessageSource.Connect"/>) i <see cref="IMessageSink"/> (producenti
    /// do nej vkladaji pres <see cref="Post"/>). Nema vlastni frontu ani vlakno - kazdou
    /// vlozenou zpravu rovnou rozbocuje (fan-out) vsem aktualnim odberatelum na vlakne
    /// volajiciho. Slouzi jako sdileny bod grafu (napr. <c>ARBotRuntime.Stream</c> nebo
    /// vstup zpracovani), na ktery se odberatele pripojuji a producenti do nej publikuji.
    ///
    /// <para><b>Pravidlo neblokovani:</b> protoze <see cref="Post"/> bezi na vlakne
    /// producenta, musi byt odberatele neblokujici (typicky <see cref="MessageTarget"/>
    /// s vlastni frontou), jinak se zpetny tlak prenese na producenta.</para>
    /// </summary>
    public sealed class RelaySource : MessageSource, IMessageSink
    {
        /// <inheritdoc/>
        public void Post(Message msg) => Emit(msg);

        /// <summary>Synonym pro <see cref="Post"/> (publikace zpravy odberatelum).</summary>
        public void Publish(Message msg) => Emit(msg);

        /// <inheritdoc/>
        public override void Start() { }

        /// <inheritdoc/>
        public override void Stop() { }
    }
}
