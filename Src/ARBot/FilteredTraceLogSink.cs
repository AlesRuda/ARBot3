using System.Diagnostics;
using System.Text;
using Avalonia.Logging;

namespace ARBot
{
    /// <summary>
    /// Avalonia log sink presmerovavajici zpravy do <see cref="System.Diagnostics.Trace"/>
    /// (nahrazuje vestavene <c>.LogToTrace()</c>), ale s moznosti vynechat vybrane oblasti.
    /// <para>
    /// Ve vychozim nastaveni je potlacena oblast <see cref="LogArea.Binding"/>: runtime
    /// binding warningy pochazeji temer vyhradne z Dock.Avalonia Fluent themy, ktera bindi
    /// na volitelne (bezne null) vlastnosti jako <c>DockCapabilityPolicy</c> /
    /// <c>DockCapabilityOverrides</c> / <c>OriginalOwner</c>. Vlastni XAML aplikace pouziva
    /// compiled bindings (<c>AvaloniaUseCompiledBindingsByDefault=true</c>), takze pripadne
    /// binding chyby se odhali uz pri kompilaci - runtime warningy jsou tedy jen sum.
    /// </para>
    /// </summary>
    internal sealed class FilteredTraceLogSink : ILogSink
    {
        private readonly LogEventLevel minimumLevel;
        private readonly string[] excludedAreas;

        public FilteredTraceLogSink(LogEventLevel minimumLevel, params string[] excludedAreas)
        {
            this.minimumLevel = minimumLevel;
            this.excludedAreas = excludedAreas;
        }

        public bool IsEnabled(LogEventLevel level, string area)
        {
            if (level < minimumLevel)
                return false;
            foreach (var excluded in excludedAreas)
                if (excluded == area)
                    return false;
            return true;
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (IsEnabled(level, area))
                Trace.WriteLine(Format(area, source, messageTemplate, null));
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            if (IsEnabled(level, area))
                Trace.WriteLine(Format(area, source, messageTemplate, propertyValues));
        }

        private static string Format(string area, object? source, string template, object?[]? values)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(area).Append("] ");

            if (values is null || values.Length == 0)
            {
                sb.Append(template);
            }
            else
            {
                // Nahrada tokenu {Xxx} po poradi hodnotami (obdoba vestaveneho TraceLogSink).
                int valueIndex = 0;
                for (int i = 0; i < template.Length; i++)
                {
                    char c = template[i];
                    if (c == '{' && i + 1 < template.Length && template[i + 1] != '{')
                    {
                        int end = template.IndexOf('}', i);
                        if (end > 0)
                        {
                            sb.Append(valueIndex < values.Length ? values[valueIndex]?.ToString() : string.Empty);
                            valueIndex++;
                            i = end;
                            continue;
                        }
                    }
                    sb.Append(c);
                }
            }

            if (source is not null)
                sb.Append(" (").Append(source.GetType().Name).Append(" #").Append(source.GetHashCode()).Append(')');

            return sb.ToString();
        }
    }
}
