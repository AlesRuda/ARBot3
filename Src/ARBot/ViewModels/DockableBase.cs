using System;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dockable (dokument/nástroj), který zná typ svého prezentačního controlu.
    /// <see cref="ARBot.ViewLocator"/> podle něj vytvoří view; díky samostatnému
    /// <c>UserControl</c> je možný i design-time náhled.
    /// </summary>
    public interface IViewProvider
    {
        /// <summary>Typ Avalonia controlu, který tento dockable zobrazuje.</summary>
        Type ViewType { get; }
    }

    /// <summary>Společný předek dokovacích dokumentů s odkazem na prezentační control.</summary>
    public abstract class DocumentBase : Document, IViewProvider
    {
        public abstract Type ViewType { get; }
    }

    /// <summary>Společný předek dokovacích nástrojů s odkazem na prezentační control.</summary>
    public abstract class ToolBase : Tool, IViewProvider
    {
        public abstract Type ViewType { get; }
    }
}
