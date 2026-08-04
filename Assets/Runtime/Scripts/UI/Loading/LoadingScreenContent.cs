using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Everything the loading overlay draws, already resolved into one language.
    ///
    /// It exists so <see cref="LoadingScreenView"/> can stay dumb. The view is a persistent
    /// DontDestroyOnLoad object with no ContainerScope above it, so it cannot resolve
    /// <see cref="ISettingsService"/> to ask which language it is in; passing a finished set of
    /// strings keeps the language decision in <see cref="LoadingScreenPresenter"/>, where the
    /// settings service already is.
    /// </summary>
    public readonly struct LoadingScreenContent
    {
        /// <summary>The panel's own heading — "LOADING".</summary>
        public readonly string Title;

        /// <summary>Player-facing name of the scene being loaded.</summary>
        public readonly string Destination;

        /// <summary>The word beside the percentage — "PREPARING".</summary>
        public readonly string Status;

        /// <summary>Tips to cycle through, in the same language as the rest of this struct.</summary>
        public readonly IReadOnlyList<string> Tips;

        /// <summary>Whether to switch the Arabic text generator on for the whole panel.</summary>
        public readonly bool Arabic;

        public LoadingScreenContent(string title, string destination, string status,
                                    IReadOnlyList<string> tips, bool arabic)
        {
            Title = title;
            Destination = destination;
            Status = status;
            Tips = tips;
            Arabic = arabic;
        }
    }
}
