using System;
using ARBot.ViewModels;
using ARBot.Views.Controls;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ARBot.Views
{
    /// <summary>
    /// Graf telemetrickych rad. Samotne kresleni je v <see cref="TelemetryChartControl"/>;
    /// tady se jen propojuje s ViewModelem to, co pres binding nejde (klik do grafu = skok
    /// v prehravani, zruseni priblizeni). Viz doc/telemetry-view.md.
    /// </summary>
    public partial class TelemetryChartDocumentView : UserControl
    {
        private TelemetryChartDocument boundVm;

        public TelemetryChartDocumentView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Rebind();

            var chart = this.FindControl<TelemetryChartControl>("Chart");
            if (chart != null)
                chart.TimePicked += (_, ticks) => boundVm?.SeekToTime(ticks);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void Rebind()
        {
            if (boundVm != null)
                boundVm.ResetViewRequested -= OnResetViewRequested;

            boundVm = DataContext as TelemetryChartDocument;

            if (boundVm != null)
                boundVm.ResetViewRequested += OnResetViewRequested;
        }

        /// <summary>Priblizeni si drzi control (je to stav zobrazeni, ne dat) - VM ho jen pozada.</summary>
        private void OnResetViewRequested(object sender, EventArgs e)
            => this.FindControl<TelemetryChartControl>("Chart")?.ResetView();
    }
}
