using System;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci nastroj zobrazujici Debug/Trace vystup aplikace.
    /// V konstruktoru zaregistruje <see cref="RelayTraceListener"/>, ktery presmeruje
    /// vystup ze <c>System.Diagnostics.Debug</c>/<c>Trace</c> do tohoto panelu.
    /// </summary>
    public partial class DebugOutputTool : ToolBase
    {
        public override Type ViewType => typeof(ARBot.Views.DebugOutputToolView);

        /// <summary>Maximalni pocet znaku v bufferu (starsi vystup se orezava).</summary>
        private const int MaxChars = 100_000;

        private readonly StringBuilder buffer = new();
        private readonly object gate = new();

        /// <summary>Nahromadeny text debug vystupu.</summary>
        [ObservableProperty]
        private string text = string.Empty;

        public DebugOutputTool()
        {
            Id = "DebugOutput";
            Title = "Debug output";

            if (!Design.IsDesignMode)
                Trace.Listeners.Add(new RelayTraceListener(Append));
        }

        /// <summary>Vymaze obsah panelu.</summary>
        [RelayCommand]
        private void Clear()
        {
            lock (gate)
                buffer.Clear();
            Text = string.Empty;
        }

        private void Append(string message)
        {
            string snapshot;
            lock (gate)
            {
                buffer.Append(message);
                if (buffer.Length > MaxChars)
                    buffer.Remove(0, buffer.Length - MaxChars);
                snapshot = buffer.ToString();
            }

            // Aktualizace bindovane vlastnosti musi probehnout na UI vlakne.
            if (Dispatcher.UIThread.CheckAccess())
                Text = snapshot;
            else
                Dispatcher.UIThread.Post(() => Text = snapshot);
        }
    }
}
