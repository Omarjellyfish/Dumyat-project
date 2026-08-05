using System;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Buries whatever is under it in sawdust, and lets the player sweep it off by hand.
    ///
    /// The credits are the one screen in the game nobody has to read, which is exactly why they
    /// should be the one you have to DO something to see. The gesture is the one this game has been
    /// teaching the whole time — put your hand on the bench and wipe.
    ///
    /// HOW IT CLEARS. A grid of small dust patches, each shrinking away when a hand passes near it,
    /// rather than a render-texture mask painted by a brush. The mask is the "proper" technique and
    /// it needs a custom shader, a second camera or a command buffer, and a per-platform check that
    /// it survives the Quest's tiler. A hundred quads that scale to zero needs none of that, costs
    /// one Update over a fixed array, and at this patch size the difference is not visible.
    ///
    /// It deliberately finishes the job for the player: past <see cref="_revealedAt"/> the remaining
    /// patches clear themselves. Hunting the last three specks of dust is not a puzzle anybody wants.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SawdustReveal : MonoBehaviour
    {
        [Header("Drift")]
        [Tooltip("Size of the covered area in metres, on this object's local X and Y.")]
        [SerializeField] private Vector2 _areaSize = new Vector2(0.95f, 0.58f);

        [Tooltip("Patches across and down. More reads finer and costs more; 11 x 6 is about the " +
                 "point where individual squares stop being legible as squares.")]
        [SerializeField] private Vector2Int _grid = new Vector2Int(11, 6);

        [Tooltip("Width of one patch in metres. Wants to be comfortably MORE than the spacing so " +
                 "neighbours overlap — a grid with gaps reads as tiles, not as a drift.")]
        [SerializeField] private float _patchSize = 0.14f;

        [Tooltip("Random offset per patch, in metres. Breaks the grid up so it never reads as rows.")]
        [SerializeField] private float _scatter = 0.022f;

        [Tooltip("Fixed so the drift falls the same way every time the credits are opened.")]
        [SerializeField] private int _seed = 3;

        [Header("Look")]
        [Tooltip("Unlit transparent material for the patches. One shared material — patches fade by " +
                 "SCALE, not by alpha, so none of them needs an instance of their own.")]
        [SerializeField] private Material _dustMaterial;

        [Header("Sweeping")]
        [Tooltip("What counts as a hand. Leave as Everything and the hammer sweeps it too, which is " +
                 "usually what a player tries first.")]
        [SerializeField] private LayerMask _sweepLayers = ~0;

        [Tooltip("How wide a swathe one pass clears, in metres.")]
        [SerializeField] private float _brushRadius = 0.085f;

        [Tooltip("How long a disturbed patch takes to shrink away.")]
        [SerializeField] private float _clearSeconds = 0.28f;

        [Tooltip("Fraction of the drift that has to go before the rest clears itself.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float _revealedAt = 0.62f;

        [Header("Nudge")]
        [Tooltip("Shown when the player has looked at a dusty bench for this long without touching " +
                 "it. Without this, someone who does not think to wipe just sees no credits at all.")]
        [SerializeField] private float _nudgeAfterSeconds = 6f;

        [Tooltip("Hint object — a caption, a hand prompt. Hidden again the moment sweeping starts.")]
        [SerializeField] private GameObject _nudge;

        /// <summary>Raised once the drift is judged cleared. Fires again after a re-open.</summary>
        public event Action Revealed;

        /// <summary>0 when untouched, 1 when every patch is gone.</summary>
        public float ClearedFraction => _patches.Length == 0 ? 1f : (float)_cleared / _patches.Length;

        private struct Patch
        {
            public Transform Transform;
            public Vector2 Local;      // position on the covered plane, for distance tests
            public Vector3 BaseScale;

            /// <summary>Disturbed, and on its way out. Untouched patches are simply not sweeping.</summary>
            public bool Sweeping;

            /// <summary>
            /// Seconds since it was disturbed. Starts NEGATIVE for the auto-clear at the end, which
            /// is how the last of the drift is staggered instead of blinking out together — hence a
            /// separate <see cref="Sweeping"/> flag rather than using a negative value as "untouched".
            /// </summary>
            public float Clearing;
        }

        private Patch[] _patches = Array.Empty<Patch>();
        private int _cleared;
        private bool _revealed;
        private float _idleSeconds;
        private BoxCollider _bounds;

        private void Awake()
        {
            _bounds = GetComponent<BoxCollider>();
            _bounds.isTrigger = true;
            _bounds.size = new Vector3(_areaSize.x, _areaSize.y, Mathf.Max(0.06f, _brushRadius * 2f));

            // Triggers need a Rigidbody on one side of the pair. The player's hands may or may not
            // have one depending on how the rig is configured, so this side guarantees it.
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            Build();
        }

        // The credits panel is shown and hidden by IMenuService, so the drift has to come BACK when
        // it is reopened — the gesture is the point, and a bench that stays swept only works once.
        private void OnEnable() => Restore();

        private void Build()
        {
            if (_dustMaterial == null)
                Debug.LogWarning($"{nameof(SawdustReveal)} on '{name}' has no dust material — the " +
                                 "patches will render with Unity's default magenta.", this);

            var scatter = new System.Random(_seed);
            int count = Mathf.Max(1, _grid.x) * Mathf.Max(1, _grid.y);
            _patches = new Patch[count];

            float stepX = _areaSize.x / _grid.x;
            float stepY = _areaSize.y / _grid.y;
            int i = 0;

            for (int y = 0; y < _grid.y; y++)
            {
                for (int x = 0; x < _grid.x; x++)
                {
                    float px = -_areaSize.x * 0.5f + stepX * (x + 0.5f)
                               + (float)(scatter.NextDouble() * 2 - 1) * _scatter;
                    float py = -_areaSize.y * 0.5f + stepY * (y + 0.5f)
                               + (float)(scatter.NextDouble() * 2 - 1) * _scatter;

                    _patches[i] = MakePatch(new Vector2(px, py), i, scatter);
                    i++;
                }
            }
        }

        private Patch MakePatch(Vector2 local, int index, System.Random scatter)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Dust_{index}";
            quad.layer = gameObject.layer;

            // The patches are decoration; a collider on each would be a hundred needless entries in
            // the physics scene, and the sweep test is done in this component's own space anyway.
            Destroy(quad.GetComponent<Collider>());

            var renderer = quad.GetComponent<MeshRenderer>();
            if (_dustMaterial != null) renderer.sharedMaterial = _dustMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Transform t = quad.transform;
            t.SetParent(transform, false);
            t.localPosition = new Vector3(local.x, local.y, 0f);

            // Spun at random so the same patch texture never reads as a repeated stamp.
            t.localRotation = Quaternion.Euler(0f, 0f, (float)scatter.NextDouble() * 360f);

            float size = _patchSize * (0.8f + (float)scatter.NextDouble() * 0.4f);
            t.localScale = new Vector3(size, size, 1f);

            return new Patch
            {
                Transform = t,
                Local = local,
                BaseScale = t.localScale,
                Sweeping = false,
                Clearing = 0f
            };
        }

        /// <summary>Puts every patch back, ready to be swept again.</summary>
        public void Restore()
        {
            for (int i = 0; i < _patches.Length; i++)
            {
                if (_patches[i].Transform == null) continue;

                _patches[i].Sweeping = false;
                _patches[i].Clearing = 0f;
                _patches[i].Transform.localScale = _patches[i].BaseScale;
                _patches[i].Transform.gameObject.SetActive(true);
            }

            _cleared = 0;
            _revealed = false;
            _idleSeconds = 0f;
            if (_nudge != null) _nudge.SetActive(false);
        }

        private void OnTriggerStay(Collider other)
        {
            if (((1 << other.gameObject.layer) & _sweepLayers) == 0) return;

            // Where on the sweeping thing to clear from. A SawdustBrush marks the business end
            // explicitly, which matters most for the hammer: one collider covers the whole tool, so
            // its centre is halfway down the handle. Without a brush, fall back to the nearest point
            // on the collider's bounds — cruder, but it never throws the way Collider.ClosestPoint
            // does on a non-convex mesh.
            SawdustBrush brush = other.GetComponentInChildren<SawdustBrush>();
            if (brush == null) brush = other.GetComponentInParent<SawdustBrush>();

            if (brush != null)
                Sweep(brush.transform.position, brush.RadiusScale);
            else
                Sweep(other.ClosestPointOnBounds(ProjectOntoDrift(other.bounds.center)), 1f);
        }

        /// <summary>The point on the drift's own plane directly under <paramref name="world"/>.</summary>
        private Vector3 ProjectOntoDrift(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            local.z = 0f;
            return transform.TransformPoint(local);
        }

        /// <summary>Clears everything within a brush of <paramref name="worldPoint"/>.</summary>
        public void Sweep(Vector3 worldPoint) => Sweep(worldPoint, 1f);

        /// <summary>
        /// Clears within <paramref name="radiusScale"/> times the configured brush radius — a hammer
        /// head sweeps a wider swathe than a fingertip.
        /// </summary>
        public void Sweep(Vector3 worldPoint, float radiusScale)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            var point = new Vector2(local.x, local.y);
            float radius = _brushRadius * Mathf.Max(0.05f, radiusScale);
            float radiusSquared = radius * radius;

            for (int i = 0; i < _patches.Length; i++)
            {
                if (_patches[i].Sweeping) continue;
                if ((_patches[i].Local - point).sqrMagnitude > radiusSquared) continue;

                _patches[i].Sweeping = true;
                _patches[i].Clearing = 0f;
                _cleared++;
                _idleSeconds = 0f;
                if (_nudge != null) _nudge.SetActive(false);
            }
        }

        private void Update()
        {
            AdvanceClearing();

            if (_revealed) return;

            if (ClearedFraction >= _revealedAt)
            {
                SweepTheRest();
                _revealed = true;
                Revealed?.Invoke();
                return;
            }

            if (_nudge == null || _cleared > 0) return;

            _idleSeconds += Time.deltaTime;
            if (_idleSeconds >= _nudgeAfterSeconds) _nudge.SetActive(true);
        }

        private void AdvanceClearing()
        {
            for (int i = 0; i < _patches.Length; i++)
            {
                if (!_patches[i].Sweeping) continue;
                if (_patches[i].Transform == null) continue;
                if (!_patches[i].Transform.gameObject.activeSelf) continue;

                _patches[i].Clearing += Time.deltaTime;

                // Still inside its stagger delay.
                if (_patches[i].Clearing < 0f) continue;

                float t = Mathf.Clamp01(_patches[i].Clearing / Mathf.Max(0.01f, _clearSeconds));

                if (t >= 1f)
                {
                    _patches[i].Transform.gameObject.SetActive(false);
                    continue;
                }

                // Shrink rather than fade: scale needs no per-patch material, and dust being pushed
                // away should get smaller, not more transparent.
                _patches[i].Transform.localScale = _patches[i].BaseScale * (1f - t);
            }
        }

        private void SweepTheRest()
        {
            for (int i = 0; i < _patches.Length; i++)
            {
                if (_patches[i].Sweeping) continue;

                // Staggered by position so the last of it drifts off rather than blinking out.
                _patches[i].Sweeping = true;
                _patches[i].Clearing = -Mathf.Abs(_patches[i].Local.x) * 0.35f;
                _cleared++;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.28f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_areaSize.x, _areaSize.y, 0.01f));
        }
#endif
    }
}
