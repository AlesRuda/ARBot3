using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Smerovac podle role zpravy (viz doc/record-replay.md, "Role zpravy").
    /// Napojuje se jako odberatel korenoveho zdroje. Kazdou zpravu posle na
    /// <see cref="Stream"/> (surovy passthrough, aby ji videly zaznam/UI); zpravu s
    /// markerem <see cref="IPrimaryMessage"/> (surova senzorova mereni + externi prikazy)
    /// navic posle do <see cref="Processing"/> (graf zpracovani: vize, fuze, rizeni).
    /// Odvozene zpravy (bez markeru, typicky prehravane ze souboru ve View/Simulate)
    /// jdou jen na <see cref="Stream"/> a zpracovani minou.
    ///
    /// <para>V rezimu Run senzory produkuji jen primarni zpravy, takze je router fakticky
    /// passthrough do obou vetvi; vetev "odvozene -&gt; jen Stream" se uplatni az v Simulate.</para>
    /// </summary>
    public sealed class RoleRouter : IMessageSink
    {
        private readonly IMessageSink stream;
        private readonly IMessageSink processing;

        /// <param name="stream">Vystupni fan-out (raw &cup; derived) - dostane kazdou zpravu.</param>
        /// <param name="processing">Vstup grafu zpracovani - dostane jen <see cref="IPrimaryMessage"/>.</param>
        public RoleRouter(IMessageSink stream, IMessageSink processing)
        {
            this.stream = stream;
            this.processing = processing;
        }

        /// <inheritdoc/>
        public void Post(Message msg)
        {
            if (msg == null) return;

            // Surovy passthrough: kazda zprava jde na Stream (zaznam/UI ji musi videt).
            stream?.Post(msg);

            // Primarni zprava jde navic do zpracovani.
            if (msg is IPrimaryMessage)
                processing?.Post(msg);
        }
    }
}
