using ARBot.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Diagnostics.CodeAnalysis;

namespace ARBot
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            // Dockable, ktery zna typ sveho controlu (DocumentBase/ToolBase).
            if (param is IViewProvider vp)
                return Activator.CreateInstance(vp.ViewType) as Control
                       ?? new TextBlock { Text = "Not a Control: " + vp.ViewType.FullName };

            // Jinak konvence nazvu: ...ViewModel -> ...View.
            var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is IViewProvider || data is ViewModelBase;
        }
    }
}
