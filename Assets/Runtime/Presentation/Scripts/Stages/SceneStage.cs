using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    /// <summary>
    /// One demo scene, presented as a stage the deck can switch to.
    ///
    /// This is where Phase 0's central finding lives. With every demo scene resident at once, the only
    /// thing that makes a scene's authored lighting apply is being the ACTIVE scene:
    /// <see cref="SceneManager.SetActiveScene"/> swaps skybox, ambient mode and intensity, and fog in
    /// the same frame — measured on Shipyard (ambient 2.010, blue fog) against EzbetElBorg (1.000,
    /// grey fog).
    ///
    /// Post-processing does NOT follow that rule. A global URP <see cref="Volume"/> is global to every
    /// loaded scene, so Shipyard's grade would bleed over El Manara. Each stage therefore owns its
    /// volumes and enables them only while it is live. Those two facts are handled together here
    /// because forgetting the second one produces a subtly wrong image that is very hard to attribute.
    /// </summary>
    public sealed class SceneStage : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField, Tooltip("Id referenced by SceneShowcaseSlideSO. Conventionally the scene " +
                                 "name without the _Demo suffix, e.g. 'Shipyard'.")]
        private string _stageId;

        [Header("Contents")]
        [SerializeField, Tooltip("Roots toggled with the stage. Leave empty to use every root in this " +
                                 "scene except this object, which is what most stages want.")]
        private GameObject[] _contentRoots = new GameObject[0];

        [SerializeField, Tooltip("Post-process volumes belonging to this stage. Leave empty to " +
                                 "auto-discover every Volume in the scene.")]
        private Volume[] _volumes = new Volume[0];

        [SerializeField] private CameraAngleSet _angles;

        private readonly List<GameObject> _resolvedRoots = new List<GameObject>();
        private bool _isLive;
        private bool _resolved;

        public string StageId => _stageId;
        public CameraAngleSet Angles => _angles;
        public bool IsLive => _isLive;
        public Scene Scene => gameObject.scene;

        private void Awake()
        {
            Resolve();
            // Stages start dormant. The deck decides which one is live, and the boot sequence would
            // otherwise render five scenes on top of each other for a frame.
            SetLive(false);
        }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            if (string.IsNullOrEmpty(_stageId))
            {
                _stageId = gameObject.scene.name.EndsWith("_Demo")
                    ? gameObject.scene.name.Substring(0, gameObject.scene.name.Length - 5)
                    : gameObject.scene.name;
            }

            _resolvedRoots.Clear();
            if (_contentRoots.Length > 0)
            {
                foreach (GameObject go in _contentRoots)
                    if (go != null) _resolvedRoots.Add(go);
            }
            else
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    if (root == gameObject) continue;   // never toggle the stage itself off
                    _resolvedRoots.Add(root);
                }
            }

            if (_volumes.Length == 0)
            {
                var found = new List<Volume>();
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    found.AddRange(root.GetComponentsInChildren<Volume>(true));
                _volumes = found.ToArray();
            }

            if (_angles == null) _angles = GetComponentInChildren<CameraAngleSet>(true);
            if (_angles == null)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    _angles = root.GetComponentInChildren<CameraAngleSet>(true);
                    if (_angles != null) break;
                }
            }
        }

        /// <summary>
        /// Brings the stage up: content on, volumes on, and — the part that matters — this scene made
        /// active so its authored environment applies. Returns the layers the rendering camera should
        /// exclude for the requested angle.
        ///
        /// <paramref name="allowBlend"/> should be true only when this stage was ALREADY live, i.e. the
        /// deck is moving between two angles of the same place. Blending into a stage from outside it
        /// would fly the camera across the 2500-unit gap between stages.
        /// </summary>
        public int Activate(string angleId, bool allowBlend)
        {
            Resolve();
            SetLive(true);

            // Order matters: the scene must be active BEFORE the first frame renders, or one frame
            // shows this stage's geometry under the previous stage's sky.
            if (Scene.IsValid() && Scene.isLoaded) SceneManager.SetActiveScene(Scene);

            return _angles != null ? _angles.Activate(angleId, allowBlend) : 0;
        }

        public void Deactivate()
        {
            Resolve();
            if (_angles != null) _angles.DeactivateAll();
            SetLive(false);
        }

        private void SetLive(bool live)
        {
            _isLive = live;

            foreach (GameObject go in _resolvedRoots)
                if (go != null) go.SetActive(live);

            // Disabling the component rather than the GameObject: a Volume on a deactivated root is
            // already inert, but stages whose volume sits on a root we do not own still need this.
            foreach (Volume v in _volumes)
                if (v != null) v.enabled = live;
        }

        /// <summary>
        /// Brings the stage up for a couple of frames without making it live, so shaders compile and
        /// textures upload before an audience is watching. Used by <see cref="StagePrewarmer"/>.
        /// </summary>
        public void PrewarmBegin() => SetLive(true);

        public void PrewarmEnd() => SetLive(false);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_stageId) && gameObject.scene.IsValid())
            {
                _stageId = gameObject.scene.name.EndsWith("_Demo")
                    ? gameObject.scene.name.Substring(0, gameObject.scene.name.Length - 5)
                    : gameObject.scene.name;
            }
        }
#endif
    }
}
