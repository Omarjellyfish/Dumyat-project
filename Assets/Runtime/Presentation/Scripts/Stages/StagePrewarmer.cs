using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Renders each stage for a couple of frames at boot so its shaders compile and its textures upload
    /// while the loading screen is still up.
    ///
    /// WHY THIS IS WORTH A COMPONENT:
    /// the first frame a stage appears is the frame Unity compiles its shader variants and uploads its
    /// textures — Phase 0 watched texture memory climb by 762 MB the moment EzbetElBorg was first
    /// rendered. Without a warm-up, that cost lands on the first slide that cuts to each stage, in
    /// front of an audience, as a visible hitch. Paying it during boot makes it invisible.
    ///
    /// The stage is brought up but NOT made active, so no environment swap happens and the deck's own
    /// first slide is unaffected.
    /// </summary>
    public sealed class StagePrewarmer : MonoBehaviour
    {
        [SerializeField] private StageRegistry _registry;

        [SerializeField, Tooltip("Camera used to force the render. Leave empty to use the main camera.")]
        private Camera _camera;

        [SerializeField, Range(1, 8), Tooltip("Frames rendered per stage. Two is usually enough; more " +
                                              "costs boot time and buys little.")]
        private int _framesPerStage = 2;

        private void Awake()
        {
            if (_registry == null) _registry = FindFirstObjectByType<StageRegistry>();
            if (_camera == null) _camera = Camera.main;
        }

        public IEnumerator PrewarmAll()
        {
            if (_registry == null)
            {
                Debug.LogWarning("[StagePrewarmer] no StageRegistry — skipping warm-up.", this);
                yield break;
            }

            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[StagePrewarmer] no camera — skipping warm-up.", this);
                yield break;
            }

            // Render off-screen so nothing flashes on the projector while this runs.
            var rt = new RenderTexture(256, 144, 16, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = cam.targetTexture;
            int previousMask = cam.cullingMask;
            int warmed = 0;

            // TRY/FINALLY IS NOT OPTIONAL HERE, AND THIS BUG COST A WHOLE PHASE.
            //
            // This routine points the MAIN camera at an off-screen texture and yields — twelve times
            // across six stages. Restoring afterwards on the happy path only is not enough: if the
            // coroutine is ever interrupted (a stopped coroutine, a domain reload, anything throwing
            // inside PrewarmBegin) the camera is left rendering into a 256x144 texture for the rest of
            // the session. The deck then shows its UI chrome over an empty frame and every scene slide
            // looks broken — while a direct cam.Render() to a fresh texture still works perfectly, which
            // is exactly how the cause stayed hidden.
            //
            // A finally block in an iterator runs when the iterator is disposed, including on
            // StopCoroutine, so the camera always gets its screen target back.
            try
            {
                cam.targetTexture = rt;
                cam.cullingMask = ~0;

                foreach (SceneStage stage in _registry.All)
                {
                    if (stage == null) continue;

                    stage.PrewarmBegin();
                    for (int f = 0; f < _framesPerStage; f++)
                    {
                        cam.Render();
                        yield return null;
                    }
                    stage.PrewarmEnd();
                    warmed++;
                }
            }
            finally
            {
                cam.targetTexture = previousTarget;
                cam.cullingMask = previousMask;
                if (rt != null) { rt.Release(); Destroy(rt); }
                Debug.Log($"[StagePrewarmer] warmed {warmed} stage(s); camera restored to " +
                          (previousTarget == null ? "the screen." : previousTarget.name + "."), this);
            }
        }
    }
}
