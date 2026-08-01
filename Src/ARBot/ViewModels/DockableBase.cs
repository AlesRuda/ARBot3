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

        private bool _active = true;

        /// <summary>
        /// Je tento dokument aktuálně viditelný = aktivní tab své <c>DocumentDock</c>? Nastavuje
        /// <see cref="DockFactory"/> z události <c>ActiveDockableChanged</c>. Vizualizace, jejichž
        /// render neběží přes Avalonia <c>Control.Render</c> (a tedy není frameworkem gatován
        /// viditelností - typicky tvorba <c>WriteableBitmap</c> ve ViewModelu, viz
        /// <see cref="ImageDocument"/>), podle toho gatují drahý render, aby skrytý tab nechrlil.
        /// Default <c>true</c> (dokud DockFactory nezavolá - bezpečné: renderuje jako dosud).
        /// </summary>
        public bool IsActive => _active;

        /// <summary>Nastaví aktivitu (volá <see cref="DockFactory"/>). Změna volá <see cref="OnActiveChanged"/>.</summary>
        internal void SetActive(bool value)
        {
            if (_active == value) return;
            _active = value;
            OnActiveChanged(value);
        }

        /// <summary>Hook: dokument se stal (ne)aktivním/viditelným. Výchozí implementace nic nedělá.</summary>
        protected virtual void OnActiveChanged(bool active) { }
    }

    /// <summary>Společný předek dokovacích nástrojů s odkazem na prezentační control.</summary>
    public abstract class ToolBase : Tool, IViewProvider
    {
        public abstract Type ViewType { get; }
    }
}
