using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Draws the visual attention a <see cref="TutorialStepSO"/> asks for on every
    /// <see cref="TutorialAnchor"/> named by <c>PrimaryAnchorId</c> / <c>HighlightIds</c>.
    ///
    /// Those two fields have existed on the step asset since the tutorial system was written, and
    /// every authored step sets <c>HighlightPrimaryAnchor</c> — but nothing read them, so the
    /// instruction card told the player to "grab the saw" and then left them to find it. This is the
    /// missing half.
    ///
    /// Two shapes, because the two things being pointed at are not alike:
    ///  * A <b>beacon</b> — a vertical beam with a disc at its foot — for a PLACE or a loose object.
    ///    A beam is the one form that still reads from across a courtyard, which is what the walk to
    ///    the workshop needed.
    ///  * A <b>frame</b> — an outline around the whole panel — for a UI window. A beam through the
    ///    middle of a crafting board says "something is here" when the panel already fills the view;
    ///    an outline says "this window, all of it".
    ///
    /// Lives beside <see cref="TutorialRunner"/>, which owns the step lifecycle and calls
    /// <see cref="Apply"/> / <see cref="Clear"/>.
    /// </summary>
    [AddComponentMenu("Dumyat/Tutorial/Tutorial Highlighter")]
    public class TutorialHighlighter : MonoBehaviour
    {
        [Header("Look")]
        [Tooltip("Highlight colour. Delta cyan by default, matching the tutorial card's accent.")]
        [SerializeField] private Color _color = new Color(0.404f, 0.867f, 0.910f, 1f);   // #67DDE8

        [Header("Beacon (places and objects)")]
        [Tooltip("Height (m) of the vertical beam. Tall enough to clear furniture and be seen across " +
                 "a room; the entrance beacon relies on this.")]
        [SerializeField, Min(0.2f)] private float _beamHeight = 2.6f;

        [Tooltip("Radius (m) of the beam. Keep it thin — this is a marker, not an obstacle.")]
        [SerializeField, Min(0.005f)] private float _beamRadius = 0.06f;

        [Tooltip("Radius (m) of the disc at the beacon's base. Set to 0 for a beam with no base.")]
        [SerializeField, Min(0f)] private float _discRadius = 0.4f;

        [Header("Frame (UI windows)")]
        [Tooltip("Thickness (m) of the outline bars drawn around a panel.")]
        [SerializeField, Min(0.002f)] private float _frameThickness = 0.045f;

        [Tooltip("Gap (m) between the panel's edge and the outline, so the frame sits around the " +
                 "window rather than on top of its content.")]
        [SerializeField, Min(0f)] private float _frameMargin = 0.03f;

        [Tooltip("Fallback frame size (m) when a panel cannot be measured and no override is set.")]
        [SerializeField] private Vector2 _frameFallbackSize = new Vector2(1.2f, 0.8f);

        [Header("Reaching a highlight")]
        [Tooltip("Beacons fade out and vanish once the player's head comes within this distance (m), " +
                 "measured horizontally — a marker you are standing on has done its job. Set to 0 to " +
                 "keep beacons up for the whole step.")]
        [SerializeField, Min(0f)] private float _vanishWithin = 1.5f;

        [Tooltip("Also vanish FRAMES on approach. Off by default: you walk up to a UI window in order " +
                 "to use it, and losing the outline at the moment you arrive is the opposite of helpful.")]
        [SerializeField] private bool _framesVanishToo;

        [Header("Pulse")]
        [Tooltip("Pulses per second. Slow enough not to be irritating over a long step.")]
        [SerializeField, Min(0.05f)] private float _pulseSpeed = 0.7f;

        [Tooltip("Alpha at the dimmest point of the pulse.")]
        [SerializeField, Range(0f, 1f)] private float _minAlpha = 0.20f;

        [Tooltip("Alpha at the brightest point of the pulse.")]
        [SerializeField, Range(0f, 1f)] private float _maxAlpha = 0.65f;

        private readonly List<TutorialHighlight> _active = new List<TutorialHighlight>();

        // Reused across steps; Collect rewrites it in full each time.
        private readonly List<TutorialAnchor> _targets = new List<TutorialAnchor>();

        /// <summary>Clears any previous highlights and raises the ones this step asks for.</summary>
        public void Apply(TutorialStepSO step)
        {
            Clear();
            if (step == null) return;

            // Which anchors to draw on is TutorialHighlightTargets' answer, shared with TutorialRunner
            // so the beacon and the anchor's own OnHighlightChanged hook can never point at different
            // objects. It also owns the de-duplication. Quiet, because TutorialRunner resolves the
            // same step immediately after this and is the one that reports a missing anchor.
            TutorialHighlightTargets.Collect(step, _targets, this, warnOnMissing: false);

            foreach (TutorialAnchor anchor in _targets)
            {
                if (anchor == null) continue;

                TutorialHighlight highlight = Build(anchor);
                if (highlight != null) _active.Add(highlight);
            }
        }

        /// <summary>Tears every highlight down. Safe to call when none are up.</summary>
        public void Clear()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i] != null) Destroy(_active[i].gameObject);
            }
            _active.Clear();
        }

        private void OnDisable() => Clear();

        private TutorialHighlight Build(TutorialAnchor anchor)
        {
            bool frame = ResolveUsesFrame(anchor);

            var settings = new TutorialHighlight.Settings
            {
                Color = _color,
                PulseSpeed = _pulseSpeed,
                MinAlpha = Mathf.Min(_minAlpha, _maxAlpha),
                MaxAlpha = Mathf.Max(_minAlpha, _maxAlpha),
                BeamHeight = _beamHeight,
                BeamRadius = _beamRadius,
                DiscRadius = _discRadius,
                FrameThickness = _frameThickness,
                FrameMargin = _frameMargin,
                // A frame keeps its place while the player uses the window unless told otherwise.
                VanishWithin = (frame && !_framesVanishToo) ? 0f : _vanishWithin
            };

            if (!frame) return TutorialHighlight.CreateBeacon(anchor.transform, settings);

            Vector2 size = ResolveFrameSize(anchor);
            return TutorialHighlight.CreateFrame(anchor.transform, size, settings);
        }

        /// <summary>
        /// Whether this anchor should be outlined rather than beaconed. <c>Auto</c> asks the anchor
        /// what it is: anything carrying a UI panel gets a frame.
        /// </summary>
        private static bool ResolveUsesFrame(TutorialAnchor anchor)
        {
            switch (anchor.HighlightStyle)
            {
                case TutorialHighlightStyle.Frame: return true;
                case TutorialHighlightStyle.Beacon: return false;
                default:
                    // Deliberately the anchor's OWN components, not its children. The XR rig's
                    // controllers carry UI panels as children (the wrist menu), and a child search
                    // would decide the "Hands" anchor was a window and draw a frame round the
                    // player's hand. An anchor that IS a panel has the panel component on itself —
                    // which is how both crafting boards are built.
                    return anchor.GetComponent<UIDocument>() != null
                        || anchor.GetComponent<Canvas>() != null
                        || anchor.GetComponent<RectTransform>() != null;
            }
        }

        /// <summary>
        /// The panel's world size, in metres. Explicit override first, then measurement, then a
        /// fallback that at least puts something on screen rather than nothing.
        /// </summary>
        private Vector2 ResolveFrameSize(TutorialAnchor anchor)
        {
            if (anchor.FrameSizeOverride.x > 0f && anchor.FrameSizeOverride.y > 0f)
                return anchor.FrameSizeOverride;

            if (TryMeasurePanel(anchor.transform, out Vector2 measured)) return measured;

            Debug.LogWarning($"[{nameof(TutorialHighlighter)}] Could not measure the panel on " +
                             $"'{anchor.name}' — using the fallback frame size. Set FrameSizeOverride " +
                             "on its TutorialAnchor to fit it properly.", anchor);
            return _frameFallbackSize;
        }

        /// <summary>
        /// Measures a world-space UI panel.
        ///
        /// For a UI Toolkit <see cref="UIDocument"/>, <c>rootVisualElement.worldBound</c> is already
        /// in the panel's own units — a 900 x 620 px document reports 9.00 x 6.20 — so multiplying by
        /// the document transform's lossy scale gives metres. That is measured, not assumed: the
        /// crafting boards report 9.00 x 6.20 at scale 0.30, i.e. 2.70 m x 1.86 m. The rect is centred
        /// on the transform origin, which is what lets the frame be built symmetrically around it.
        ///
        /// uGUI canvases fall through to the RectTransform, which carries the same information.
        /// </summary>
        private static bool TryMeasurePanel(Transform anchor, out Vector2 size)
        {
            size = Vector2.zero;
            Vector3 scale = anchor.lossyScale;

            var document = anchor.GetComponent<UIDocument>();
            if (document == null) document = anchor.GetComponentInChildren<UIDocument>(true);
            if (document != null && document.rootVisualElement != null)
            {
                Rect bound = document.rootVisualElement.worldBound;
                if (bound.width > 0f && bound.height > 0f)
                {
                    size = new Vector2(bound.width * Mathf.Abs(scale.x), bound.height * Mathf.Abs(scale.y));
                    return true;
                }
            }

            var rect = anchor.GetComponent<RectTransform>();
            if (rect == null) rect = anchor.GetComponentInChildren<RectTransform>(true);
            if (rect != null && rect.rect.width > 0f && rect.rect.height > 0f)
            {
                Vector3 rectScale = rect.lossyScale;
                size = new Vector2(rect.rect.width * Mathf.Abs(rectScale.x),
                                   rect.rect.height * Mathf.Abs(rectScale.y));
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// One highlight — either a beacon or a frame — built from primitives and a runtime material, so
    /// a designer adding an anchor gets it for free with nothing to wire and no art to import.
    /// </summary>
    public sealed class TutorialHighlight : MonoBehaviour
    {
        internal struct Settings
        {
            public Color Color;
            public float PulseSpeed;
            public float MinAlpha;
            public float MaxAlpha;
            public float BeamHeight;
            public float BeamRadius;
            public float DiscRadius;
            public float FrameThickness;
            public float FrameMargin;
            public float VanishWithin;
        }

        private const float FadeOutSeconds = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Material _material;
        private Settings _settings;

        private Camera _camera;
        private float _fade = 1f;
        private bool _fadingOut;

        /// <summary>True once the player has arrived and this highlight is on its way out.</summary>
        public bool IsVanishing => _fadingOut;

        internal static TutorialHighlight CreateBeacon(Transform anchor, Settings settings)
        {
            TutorialHighlight h = CreateRoot(anchor, settings);

            // Unity's cylinder is 2 units tall, so a half-height y scale gives the requested metres.
            h.AddPart(PrimitiveType.Cylinder, "Beam",
                new Vector3(settings.BeamRadius * 2f, settings.BeamHeight * 0.5f, settings.BeamRadius * 2f),
                new Vector3(0f, settings.BeamHeight * 0.5f, 0f), Quaternion.identity);

            if (settings.DiscRadius > 0f)
            {
                h.AddPart(PrimitiveType.Cylinder, "Disc",
                    new Vector3(settings.DiscRadius * 2f, 0.004f, settings.DiscRadius * 2f),
                    new Vector3(0f, 0.01f, 0f), Quaternion.identity);
            }

            return h;
        }

        /// <summary>
        /// Builds a rectangular outline of <paramref name="size"/> in the anchor's local XY plane,
        /// centred on its origin — which is where a world-space UI panel sits.
        ///
        /// The bars are pushed OUTSIDE the panel edge by the margin rather than laid on top of it.
        /// That is not only tidier: a bar coplanar with the panel would z-fight with it, and nudging
        /// the frame forward instead would put it on the wrong side the moment the player walked
        /// round the back.
        /// </summary>
        internal static TutorialHighlight CreateFrame(Transform anchor, Vector2 size, Settings settings)
        {
            TutorialHighlight h = CreateRoot(anchor, settings);

            float t = settings.FrameThickness;
            float halfW = size.x * 0.5f + settings.FrameMargin + t * 0.5f;
            float halfH = size.y * 0.5f + settings.FrameMargin + t * 0.5f;

            // Horizontal bars run the full width including the corners, so the outline closes.
            float barWidth = halfW * 2f + t;
            const float depth = 0.012f;

            h.AddPart(PrimitiveType.Cube, "FrameTop", new Vector3(barWidth, t, depth),
                new Vector3(0f, halfH, 0f), Quaternion.identity);
            h.AddPart(PrimitiveType.Cube, "FrameBottom", new Vector3(barWidth, t, depth),
                new Vector3(0f, -halfH, 0f), Quaternion.identity);
            h.AddPart(PrimitiveType.Cube, "FrameLeft", new Vector3(t, halfH * 2f, depth),
                new Vector3(-halfW, 0f, 0f), Quaternion.identity);
            h.AddPart(PrimitiveType.Cube, "FrameRight", new Vector3(t, halfH * 2f, depth),
                new Vector3(halfW, 0f, 0f), Quaternion.identity);

            return h;
        }

        private static TutorialHighlight CreateRoot(Transform anchor, Settings settings)
        {
            var root = new GameObject("Tutorial Highlight");

            // Parented to the anchor so the highlight rides along with anything that moves — the saw
            // and the chisel are picked up and carried while their step is still on screen.
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            // Cancel the anchor's scale, so everything below is authored in real metres.
            //
            // This is not a nicety. The crafting boards sit at a transform scale of 0.30, so a frame
            // built from the panel's measured 2.70 m width came out at 0.85 m — a third of the window
            // it was supposed to surround. Anchors carry whatever scale their art needed; a highlight
            // must not inherit it.
            root.transform.localScale = InverseScale(anchor.lossyScale);

            var highlight = root.AddComponent<TutorialHighlight>();
            highlight._settings = settings;
            highlight._material = CreateMaterial(settings.Color);
            return highlight;
        }

        /// <summary>
        /// Componentwise reciprocal, guarding the degenerate axes. A zero-scaled anchor would
        /// otherwise produce an infinite scale and a highlight that renders as garbage.
        /// </summary>
        private static Vector3 InverseScale(Vector3 scale)
        {
            const float minimum = 0.0001f;
            return new Vector3(
                1f / (Mathf.Abs(scale.x) < minimum ? minimum : scale.x),
                1f / (Mathf.Abs(scale.y) < minimum ? minimum : scale.y),
                1f / (Mathf.Abs(scale.z) < minimum ? minimum : scale.z));
        }

        private void AddPart(PrimitiveType type, string partName, Vector3 scale, Vector3 localPosition,
            Quaternion localRotation)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;

            // A marker the player can bump into or grab by accident is worse than no marker. The
            // primitive ships with a collider, so it has to go.
            //
            // Disabled as well as destroyed: Destroy only takes effect at the end of the frame, and
            // for that one frame a beam collider would be sitting on top of whatever is being
            // highlighted. Several highlight targets are grabbable — the fish, the saw, the chisel —
            // and an unregistered collider in front of one can swallow the interactor's cast.
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            part.transform.SetParent(transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = scale;

            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// An unlit transparent material. Unlit on purpose: a highlight that dims because it is
        /// standing in shadow is not doing its job.
        /// </summary>
        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            var material = new Material(shader) { name = "TutorialHighlight (Runtime)" };

            // URP Unlit defaults to opaque; these are the properties its transparent mode sets.
            // Harmless on the fallback shaders, which simply do not have them.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            material.SetColor(BaseColorId, color);
            material.SetColor(ColorId, color);
            return material;
        }

        private void Update()
        {
            if (_material == null) return;

            if (!_fadingOut && HasPlayerArrived()) _fadingOut = true;

            if (_fadingOut)
            {
                _fade -= Time.unscaledDeltaTime / FadeOutSeconds;
                if (_fade <= 0f)
                {
                    // Destroyed rather than merely hidden: a marker the player has already walked to
                    // should not come back if they happen to step away again.
                    Destroy(gameObject);
                    return;
                }
            }

            // Unscaled: a tutorial can be on screen while the game is paused, and a highlight frozen
            // mid-pulse looks like the tutorial has hung.
            float t = (Mathf.Sin(Time.unscaledTime * _settings.PulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(_settings.MinAlpha, _settings.MaxAlpha, t) * _fade;

            Color c = _settings.Color;
            c.a = alpha;

            _material.SetColor(BaseColorId, c);
            _material.SetColor(ColorId, c);
        }

        /// <summary>
        /// Whether the player is close enough for this highlight to have served its purpose.
        ///
        /// Distance is measured HORIZONTALLY from the head. A beacon rises from the floor, so the
        /// full 3D distance to a standing player never drops much below their eye height, and a
        /// straight distance check would mean "reached" never fires for a marker you are standing on.
        /// </summary>
        private bool HasPlayerArrived()
        {
            if (_settings.VanishWithin <= 0f) return false;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return false;

            Vector3 a = transform.position;
            Vector3 b = _camera.transform.position;
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b) <= _settings.VanishWithin;
        }

        private void OnDestroy()
        {
            // The material is created per highlight, so it leaks if it is not cleaned up with it.
            if (_material != null) Destroy(_material);
        }
    }
}
