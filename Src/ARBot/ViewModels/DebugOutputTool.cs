using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci nastroj zobrazujici Debug/Trace vystup aplikace.
    /// V konstruktoru zaregistruje <see cref="RelayTraceListener"/>, ktery presmeruje
    /// vystup ze <c>System.Diagnostics.Debug</c>/<c>Trace</c> do tohoto panelu.
    ///
    /// Vystup je drzen jako kolekce radku (<see cref="Lines"/>) a zobrazovan virtualizovanym
    /// listem - na rozdil od jednoho velkeho <c>string</c> v <c>TextBox</c> (ten se pri kazde
    /// aktualizaci cely znovu skladal/vykresloval a pri zaplave logu shazoval responzivitu).
    /// </summary>
    public partial class DebugOutputTool : ToolBase
    {
        public override System.Type ViewType => typeof(ARBot.Views.DebugOutputToolView);

        /// <summary>Maximalni pocet drzenych radku (starsi se orezavaji zepredu).</summary>
        private const int MaxLines = 5000;
        /// <summary>Hystereze orezu: orezavame az pri prekroceni o tuto rezervu (min. CollectionChanged).</summary>
        private const int TrimSlack = 512;

        /// <summary>Radky debug vystupu (bindovane na virtualizovany list v UI).</summary>
        public ObservableCollection<string> Lines { get; } = new();

        private readonly object gate = new();
        private readonly List<string> pending = new();   // hotove radky cekajici na preneseni do UI
        private readonly StringBuilder current = new();   // rozdelana (jeste neuzavrena) radka
        private volatile bool updateQueued;

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
            {
                pending.Clear();
                current.Clear();
            }
            Lines.Clear();
        }

        /// <summary>
        /// Prijme kus vystupu (muze obsahovat 0..N koncu radku). Rozdeli ho na radky, nedokoncenou
        /// radku si drzi v <see cref="current"/> do prichodu <c>\n</c>. UI aktualizace je koalescovana
        /// (nejvyse jedna naplanovana davka), aby zaplava zprav neutopila dispatcher.
        /// </summary>
        private void Append(string message)
        {
            lock (gate)
            {
                foreach (char c in message)
                {
                    if (c == '\n')
                    {
                        pending.Add(current.ToString());
                        current.Clear();
                    }
                    else if (c != '\r')
                    {
                        current.Append(c);
                    }
                }
            }

            if (updateQueued)
                return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush);
        }

        /// <summary>Prenese nahromadene radky do <see cref="Lines"/> a orizne na <see cref="MaxLines"/>.</summary>
        private void Flush()
        {
            updateQueued = false;

            List<string> batch;
            lock (gate)
            {
                if (pending.Count == 0)
                    return;
                batch = new List<string>(pending);
                pending.Clear();
            }

            foreach (var line in batch)
                Lines.Add(line);

            // Orez zepredu s hysterezi - jen kdyz pretece o rezervu, aby se RemoveAt(0)
            // nedelal kazdou davku.
            if (Lines.Count > MaxLines + TrimSlack)
            {
                int remove = Lines.Count - MaxLines;
                for (int i = 0; i < remove; i++)
                    Lines.RemoveAt(0);
            }
        }
    }
}
