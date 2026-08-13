using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Finds a <see cref="SceneStage"/> by id.
    ///
    /// Stages live in additively-loaded demo scenes, so they cannot be serialized into a field in the
    /// presentation scene — something has to look them up at runtime. This interface exists so that
    /// lookup happens in exactly one place instead of every consumer doing its own
    /// <c>FindObjectsByType</c>, which is the pattern CLAUDE.md rules out.
    /// </summary>
    public interface IStageRegistry
    {
        /// <summary>Stage with this id, or null. Ids are case-insensitive.</summary>
        SceneStage Get(string stageId);

        /// <summary>Every stage discovered, in discovery order.</summary>
        IReadOnlyList<SceneStage> All { get; }

        /// <summary>Re-scans the loaded scenes. Called by the bootstrapper once loading finishes.</summary>
        void Rescan();

        /// <summary>
        /// Deactivates every stage but the named one. The renderer uses this rather than tracking the
        /// previous stage itself, so a jump straight into the middle of the deck cannot leave two
        /// stages lit.
        ///
        /// It also decides how the camera arrives, which it can do precisely because it is the only
        /// thing that knows whether the place changed: another angle of the stage that is already live
        /// gets a Cinemachine blend, a different stage gets a hard cut.
        /// </summary>
        SceneStage MakeOnlyLive(string stageId, string angleId, out int hiddenLayers);
    }
}
