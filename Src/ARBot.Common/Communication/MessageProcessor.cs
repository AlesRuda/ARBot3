using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Vypocetni stupen pipeline - zaroven cil (konzumuje vstupni zpravy) i zdroj
    /// (produkuje odvozene zpravy). "Pocitany senzor": napr. fuze nad merenimi nebo
    /// vize nad snimky. Odvozene zpravy jdou ven pres <see cref="Output"/>.
    /// </summary>
    public abstract class MessageProcessor : MessageTarget
    {
        private readonly RelaySource output = new RelaySource();

        /// <param name="policy">Chovani vstupni fronty.</param>
        /// <param name="capacity">Kapacita vstupni fronty; &lt;=0 = neomezena.</param>
        protected MessageProcessor(OverflowPolicy policy = OverflowPolicy.Block, int capacity = 0)
            : base(policy, capacity)
        {
        }

        /// <summary>Zdroj odvozenych (mezivysledkovych) zprav.</summary>
        public MessageSource Output => output;

        /// <summary>Vysle odvozenou zpravu odberatelum <see cref="Output"/>.</summary>
        protected void EmitDerived(Message msg) => output.Publish(msg);

        private sealed class RelaySource : MessageSource
        {
            public void Publish(Message msg) => Emit(msg);
            public override void Start() { }
            public override void Stop() { }
        }
    }
}
