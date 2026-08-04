using UnityEngine;
using UnityEngine.Rendering;

namespace DumyatRuntime
{
    /// <summary>
    /// The black shell that hides the world during a scene load: a small sphere hard-locked to the
    /// head, shaded on its inside face by the <c>Dumyat/ScreenBlinder</c> shader.
    ///
    /// This replaces the old <c>RenderMode.ScreenSpaceOverlay</c> canvas, which never worked in VR —
    /// screen-space overlay canvases render to the desktop mirror and are not composited into the
    /// HMD eye textures, so the "fade to black" was invisible in the headset.
    ///
    /// Single responsibility: it is a fade-able blackout and nothing else. It knows nothing about
    /// scene loading — <see cref="ScreenFadeService"/> decides when to fade it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenBlinder : MonoBehaviour
    {
        private const string ShaderResourcePath = "Shaders/ScreenBlinder";

        /// <summary>
        /// Shell radius (m). Must clear the loading panel's 1.2 m stand-off so the panel sits INSIDE
        /// the shell; the shader's later queue is what actually puts the panel on top.
        /// </summary>
        private const float ShellRadius = 1.6f;

        private static readonly int ColorId = Shader.PropertyToID("_BlinderColor");

        private Renderer _renderer;
        private Material _material;
        private Camera _camera;

        private float _alpha;
        private float _targetAlpha;
        private float _fadeRatePerSecond;

        /// <summary>Current opacity, 0 (clear) to 1 (fully black).</summary>
        public float Alpha => _alpha;

        /// <summary>True while the alpha is still travelling toward its target.</summary>
        public bool IsFading => !Mathf.Approximately(_alpha, _targetAlpha);

        /// <summary>
        /// Builds the blinder and marks it persistent. Returns null (having logged) if the shader is
        /// missing, so callers degrade to "no fade" rather than throwing on every load.
        /// </summary>
        public static ScreenBlinder Create()
        {
            var shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                Debug.LogWarning($"{nameof(ScreenBlinder)}: could not load 'Resources/{ShaderResourcePath}'. " +
                                 "Scene loads will not fade to black.");
                return null;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ScreenBlinder";

            // CreatePrimitive ships a collider; a blackout must never be hit by an XR ray or a
            // physics query.
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            go.transform.localScale = Vector3.one * (ShellRadius * 2f);

            var blinder = go.AddComponent<ScreenBlinder>();
            blinder.Initialize(shader);

            DontDestroyOnLoad(go);
            return blinder;
        }

        private void Initialize(Shader shader)
        {
            _renderer = GetComponent<Renderer>();

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            SetAlphaImmediate(0f);
        }

        /// <summary>Eases to <paramref name="target"/> over <paramref name="seconds"/>. Unscaled, so it still runs while paused.</summary>
        public void FadeTo(float target, float seconds)
        {
            _targetAlpha = Mathf.Clamp01(target);

            if (seconds <= 0f)
            {
                SetAlphaImmediate(_targetAlpha);
                return;
            }

            _fadeRatePerSecond = 1f / seconds;
            ApplyAlpha();
        }

        /// <summary>Jumps straight to <paramref name="alpha"/>, cancelling any fade in flight.</summary>
        public void SetAlphaImmediate(float alpha)
        {
            _alpha = _targetAlpha = Mathf.Clamp01(alpha);
            ApplyAlpha();
        }

        private void Update()
        {
            if (!IsFading) return;

            // Unscaled: a load started from the pause menu runs with Time.timeScale at 0, and a fade
            // that never advances would leave the player staring at a frozen half-black world.
            _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, _fadeRatePerSecond * Time.unscaledDeltaTime);
            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            if (_material == null) return;

            _material.SetColor(ColorId, new Color(0f, 0f, 0f, _alpha));

            // Fully clear costs nothing at all rather than a full-screen transparent draw.
            if (_renderer != null) _renderer.enabled = _alpha > 0.001f;
        }

        private void OnEnable()
        {
            AcquireCamera();

            // Re-place AFTER the XR pose drivers have written this frame's head pose, for the same
            // reason LazyFollowAnchor does it: positioning against last frame's pose makes the shell
            // lag the head, and a blackout that lags shows a sliver of the world at the edge.
            Application.onBeforeRender += ApplyPlacement;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyPlacement;
        }

        private void LateUpdate() => ApplyPlacement();

        private void ApplyPlacement()
        {
            if (_renderer == null || !_renderer.enabled) return;
            if (!AcquireCamera()) return;

            // Position only — a sphere has no meaningful orientation.
            transform.position = _camera.transform.position;
        }

        // Camera.main goes null for a frame or two mid-load, when the old scene's camera dies before
        // the new one wakes; re-acquiring every time keeps the shell locked across that gap.
        private bool AcquireCamera()
        {
            if (_camera != null) return true;

            _camera = Camera.main;
            return _camera != null;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
