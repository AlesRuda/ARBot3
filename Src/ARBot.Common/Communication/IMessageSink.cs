using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Cil, do ktereho lze vlozit zpravu. <see cref="Post"/> je thread-safe a
    /// (podle politiky cile) zpravidla neblokujici.
    /// </summary>
    public interface IMessageSink
    {
        /// <summary>Vlozi zpravu ke zpracovani.</summary>
        void Post(Message msg);
    }
}
