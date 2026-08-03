namespace DumyatRuntime
{
    /// <summary>
    /// Owns the VR blackout used to hide the world during scene loads and travel.
    ///
    /// The implementation fades itself in and out around every scene load automatically, so systems
    /// that just want a faded load do not need this interface at all — they only need to go through
    /// <see cref="ISceneLoadService"/>. Inject this when something needs to black the view out for
    /// its own reasons (a cutscene, a blink-comfort teleport).
    /// </summary>
    public interface IScreenFadeService
    {
        /// <summary>Current blackout opacity, 0 (clear) to 1 (fully black).</summary>
        float Alpha { get; }

        /// <summary>Eases the view to black over <paramref name="seconds"/>.</summary>
        void FadeToBlack(float seconds = -1f);

        /// <summary>Eases the view back from black over <paramref name="seconds"/>.</summary>
        void FadeFromBlack(float seconds = -1f);

        /// <summary>Jumps to <paramref name="alpha"/> with no ease — for arriving already blacked out.</summary>
        void SetAlphaImmediate(float alpha);
    }
}
