using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The one place that searches the loaded scenes for stages.
    ///
    /// <see cref="MakeOnlyLive"/> is deliberately "deactivate everything, then activate one" rather
    /// than "deactivate the previous one". Number-key jumps and backward navigation mean the deck can
    /// arrive at a stage slide from anywhere, and tracking the previous stage gets it wrong exactly
    /// once — after which two stages are lit and the frame is a mess of two places at once.
    /// </summary>
    public sealed class StageRegistry : MonoBehaviour, IStageRegistry
    {
        private readonly List<SceneStage> _stages = new List<SceneStage>();
        private readonly Dictionary<string, SceneStage> _byId =
            new Dictionary<string, SceneStage>(System.StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SceneStage> All => _stages;

        private void Awake() => Rescan();

        public void Rescan()
        {
            _stages.Clear();
            _byId.Clear();

            // Inactive included: stages deactivate their own content on Awake, and a stage object
            // itself may sit under a disabled root in a scene we did not author.
            foreach (SceneStage stage in FindObjectsByType<SceneStage>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None))
            {
                _stages.Add(stage);
                if (string.IsNullOrEmpty(stage.StageId))
                {
                    Debug.LogWarning($"[StageRegistry] stage in '{stage.Scene.name}' has no id.", stage);
                    continue;
                }
                if (_byId.ContainsKey(stage.StageId))
                {
                    Debug.LogWarning($"[StageRegistry] duplicate stage id '{stage.StageId}' — " +
                                     $"'{stage.Scene.name}' will be unreachable.", stage);
                    continue;
                }
                _byId[stage.StageId] = stage;
            }

            Debug.Log($"[StageRegistry] {_stages.Count} stage(s): {string.Join(", ", KnownIds())}", this);
        }

        public SceneStage Get(string stageId)
        {
            if (string.IsNullOrEmpty(stageId)) return null;
            return _byId.TryGetValue(stageId, out SceneStage stage) ? stage : null;
        }

        public SceneStage MakeOnlyLive(string stageId, string angleId, out int hiddenLayers)
        {
            hiddenLayers = 0;

            SceneStage target = Get(stageId);
            if (target == null)
            {
                Debug.LogWarning($"[StageRegistry] no stage '{stageId}'. Known: " +
                                 $"{string.Join(", ", KnownIds())}", this);
                return null;
            }

            // Read BEFORE anything is toggled: this is the difference between "the camera moves" and
            // "we are somewhere else now".
            bool sameStage = target.IsLive;

            foreach (SceneStage stage in _stages)
            {
                if (stage == null || ReferenceEquals(stage, target)) continue;
                if (stage.IsLive) stage.Deactivate();
            }

            hiddenLayers = target.Activate(angleId, allowBlend: sameStage);

            // A new place must SNAP. The stages sit 2500 units apart on a grid, so letting the brain
            // ease between two of them is a multi-second flight through empty space with the fog and the
            // wrong skybox — not a transition anybody would want mid-talk. Within one place the blend is
            // the point, so it is left alone.
            if (!sameStage) CutCameraBlend();

            return target;
        }

        /// <summary>
        /// Tells the Cinemachine brain to arrive instantly instead of blending.
        ///
        /// The brain is resolved through <see cref="Camera.main"/> on every call rather than cached: a
        /// stage swap changes which scene is active, and this project has been bitten before by holding
        /// on to a camera reference across that. It is one GetComponent on a slide change, not per frame.
        /// </summary>
        private static void CutCameraBlend()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            var brain = cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            if (brain == null) return;

            brain.ResetState();
        }

        /// <summary>Puts every stage to sleep — used when the deck moves to a non-scene slide.</summary>
        public void DeactivateAll()
        {
            foreach (SceneStage stage in _stages)
                if (stage != null && stage.IsLive) stage.Deactivate();
        }

        private IEnumerable<string> KnownIds()
        {
            foreach (SceneStage s in _stages)
                yield return string.IsNullOrEmpty(s.StageId) ? "<no id>" : s.StageId;
        }
    }
}
