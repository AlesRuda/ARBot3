using System;
using System.Diagnostics;

namespace ARBot.ViewModels
{
    /// <summary>
    /// TraceListener presmerovavajici Debug/Trace vystup do callbacku
    /// (napr. do panelu Debug output v UI).
    /// </summary>
    internal sealed class RelayTraceListener : TraceListener
    {
        private readonly Action<string> write;

        public RelayTraceListener(Action<string> write) => this.write = write;

        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
                write(message);
        }

        public override void WriteLine(string? message)
        {
            write((message ?? string.Empty) + Environment.NewLine);
        }
    }
}
