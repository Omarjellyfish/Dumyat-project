using System.Collections;
using System.Collections.Generic;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>
    /// Loads every demo stage additively at boot, warms them, then starts the deck.
    ///
    /// Phase 0 measured all six scenes resident at 1696 MB of texture memory against roughly 3400 MB
    /// usable on the presenting machine, so <see cref="StageResidencyPolicy.AllResidentAtBoot"/> is the
    /// shipping policy: the whole world loads once, before anyone is watching, and every subsequent
    /// stage switch is instant. Boot is the price — expect tens of seconds.
    ///
    /// The deck does not start until loading finishes, which is why the director's
    /// "show first slide on start" is off in the presentation scene.
    /// </summary>
    public sealed class PresentationBootstrapper : MonoBehaviour
    {
        [Header("Stages")]
        [SerializeField, Tooltip("Demo scene names to load additively, in deck order. They must all be " +
                                 "in the build profile's scene list or loading fails in a player.")]
        private string[] _demoSceneNames = new string[0];

        [Header("Rig")]
        [SerializeField] private PresentationDirector _director;
        [SerializeField] private StageRegistry _registry;
        [SerializeField] private StagePrewarmer _prewarmer;

        [Header("Behaviour")]
        [SerializeField, Tooltip("Skip stages already loaded — true when testing from the Editor with " +
                                 "scenes dragged into the hierarchy.")]
        private bool _skipAlreadyLoaded = true;

        [SerializeField, Tooltip("Prewarm each stage for a couple of frames so the first cut to it has " +
                                 "no shader-compile hitch.")]
        private bool _prewarmStages = true;

        [SerializeField, Range(5f, 300f),
         Tooltip("Give up on a single stage after this long and present the rest of the deck. Shipyard " +
                 "is 62 MB of scene, so allow generous headroom.")]
        private float _perSceneTimeoutSeconds = 120f;

        private IEventService _events;

        [Inject]
        private void Inject(IEventService events) => _events = events;

        private void Awake()
        {
            if (_director == null) _director = FindFirstObjectByType<PresentationDirector>();
            if (_registry == null) _registry = FindFirstObjectByType<StageRegistry>();
            if (_prewarmer == null) _prewarmer = FindFirstObjectByType<StagePrewarmer>();
        }

        private void Start() => StartCoroutine(BootRoutine());

        private IEnumerator BootRoutine()
        {
            var loaded = new List<string>();
            var failed = new List<string>();

            foreach (string sceneName in _demoSceneNames)
            {
                if (string.IsNullOrWhiteSpace(sceneName)) continue;

                if (_skipAlreadyLoaded && SceneManager.GetSceneByName(sceneName).isLoaded)
                {
                    loaded.Add(sceneName + " (already open)");
                    continue;
                }

                AsyncOperation op = null;

                // Checked BEFORE calling, because LoadSceneAsync LOGS a red error and returns null for
                // an unregistered scene rather than throwing — so a try/catch cannot suppress it, and
                // two console errors at boot read as a broken deck.
                bool inBuildList = IsInBuildList(sceneName);

                if (inBuildList)
                {
                    op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                }
#if UNITY_EDITOR
                else
                {
                    // Editor fallback: load straight from the asset path. The Build Settings list is
                    // shared with the game, so registering six demo scenes there would bloat every game
                    // build — Phase 7's dedicated presentation build profile is where they belong. This
                    // keeps Editor iteration working without touching anything the game depends on.
                    string path = FindScenePathInEditor(sceneName);
                    if (!string.IsNullOrEmpty(path))
                        op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                            path, new LoadSceneParameters(LoadSceneMode.Additive));
                }
#endif

                if (op == null)
                {
                    failed.Add(sceneName + (inBuildList ? " (load returned null)" : " (not in the build list)"));
                    continue;
                }

                // Timed rather than an open-ended wait. A stage that never finishes must not hang boot
                // forever with a black screen and no explanation — better to report it and present the
                // rest of the deck.
                float waited = 0f;
                while (!op.isDone && waited < _perSceneTimeoutSeconds)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!op.isDone)
                {
                    failed.Add($"{sceneName} (timed out after {_perSceneTimeoutSeconds:F0}s at " +
                               $"{op.progress:P0})");
                    continue;
                }
                loaded.Add(sceneName);
            }

            if (failed.Count > 0)
                Debug.LogError($"[PresentationBootstrapper] {failed.Count} stage(s) failed to load: " +
                               $"{string.Join(", ", failed)}. Add them to the build profile's scene list.", this);

            Debug.Log($"[PresentationBootstrapper] loaded {loaded.Count} stage scene(s): " +
                      $"{string.Join(", ", loaded)}", this);

            // Stages exist now, so the registry can see them.
            if (_registry != null) _registry.Rescan();

            if (_prewarmStages && _prewarmer != null) yield return _prewarmer.PrewarmAll();

            // Every stage asleep, then the deck opens on its first slide.
            if (_registry != null) _registry.DeactivateAll();

            if (_director != null) _director.Restart();
            else Debug.LogWarning("[PresentationBootstrapper] no director — nothing will present.", this);
        }

        /// <summary>
        /// Whether a scene name appears in the active build list. Compared by file name because the
        /// build list stores full asset paths.
        /// </summary>
        private static bool IsInBuildList(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Asset path of a scene by name, searched under the presentation folder first.</summary>
        private static string FindScenePathInEditor(string sceneName)
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets($"t:SceneAsset {sceneName}"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) return path;
            }
            return null;
        }
#endif
    }
}
