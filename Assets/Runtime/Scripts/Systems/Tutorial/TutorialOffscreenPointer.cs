using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shows the player which way to turn when the tutorial card is behind them.
    ///
    /// The card is anchored to the object a step is about, so a step whose object is behind the player
    /// puts the card behind the player — correctly, but invisibly. Players read that as the tutorial
    /// having vanished, and stand still waiting for a card that is already up.
    ///
    /// This draws a small chevron floating at a comfortable distance in front of the player, banked
    /// toward whichever way is shorter to turn, and only while the card is outside the view cone. It
    /// disappears the moment the card comes into view, so it never competes with the card itself.
    ///
    /// Deliberately NOT a screen-space arrow: a HUD element pinned to the eye is uncomfortable in VR
    /// and reads as dirt on the lens. A world-space object at a fixed distance is something the eye
    /// can focus on.
    ///
    /// Single responsibility: it only points. It does not decide when a step starts, and it does not
    /// touch the card — attach it beside <see cref="TutorialHoverUI"/> and it works out the rest.
    /// </summary>
    public class TutorialOffscreenPointer : MonoBehaviour
    {
        [Tooltip("The card to point at. Auto-resolved from this GameObject or its children when empty.")]
        [SerializeField] private TutorialHoverUI _hoverUI;

        [Tooltip("The pointer only appears once the card is further off-centre than this, in degrees " +
                 "from where the player is looking. Roughly matches the headset's comfortable field of " +
                 "view, so it stays hidden while the card is merely off to one side but still readable.")]
        [SerializeField, Range(10f, 120f)] private float _hiddenWithinDegrees = 45f;

        [Tooltip("How far in front of the player the chevron floats, in metres.")]
        [SerializeField, Min(0.3f)] private float _distance = 1.1f;

        [Tooltip("How far below eye level it sits, in metres — low enough not to cover the world, high " +
                 "enough to be noticed.")]
        [SerializeField] private float _drop = 0.25f;

        [Tooltip("Size of the chevron in metres.")]
        [SerializeField, Min(0.02f)] private float _size = 0.09f;

        [Tooltip("Colour. Matches the tutorial accent by default.")]
        [SerializeField] private Color _colour = new Color(0.40f, 0.87f, 0.91f, 1f);

        [Tooltip("Pulses per second, so it reads as a prompt rather than scenery.")]
        [SerializeField, Min(0f)] private float _pulsesPerSecond = 1.4f;

        private readonly CameraBillboard _billboard = new CameraBillboard();
        private Transform _chevron;
        private Material _material;

        private void Awake()
        {
            if (_hoverUI == null) _hoverUI = GetComponentInChildren<TutorialHoverUI>(true);
        }

        private void LateUpdate()
        {
            if (!_billboard.TryResolveCamera(out Camera camera) || !TryGetCardPosition(out Vector3 card))
            {
                SetVisible(false);
                return;
            }

            Transform head = camera.transform;

            // Yaw-only: a card above or below the player is still findable, but one behind them is not.
            Vector3 toCard = card - head.position;
            toCard.y = 0f;
            Vector3 facing = head.forward;
            facing.y = 0f;
            if (toCard.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f)
            {
                SetVisible(false);
                return;
            }

            float offBy = Vector3.Angle(facing, toCard);
            if (offBy <= _hiddenWithinDegrees)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            Place(head, facing.normalized, toCard.normalized);
        }

        // Parks the chevron in front of the player, rolled toward the shorter way round to the card.
        private void Place(Transform head, Vector3 facing, Vector3 toCard)
        {
            Vector3 position = head.position + facing * _distance + Vector3.up * -_drop;
            _chevron.position = position;

            // Signed angle tells us which shoulder to send them over; the chevron rolls that way so a
            // glance is enough — no reading required.
            float signed = Vector3.SignedAngle(facing, toCard, Vector3.up);
            _billboard.Apply(_chevron, BillboardMode.YawOnly);
            _chevron.Rotate(0f, 0f, signed > 0f ? -90f : 90f, Space.Self);

            if (_material == null) return;
            float pulse = _pulsesPerSecond > 0f
                ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * _pulsesPerSecond))
                : 1f;
            Color c = _colour;
            c.a *= pulse;
            _material.color = c;
        }

        // The card's position, or false when there is no card up.
        private bool TryGetCardPosition(out Vector3 position)
        {
            position = default;
            if (_hoverUI == null || !_hoverUI.gameObject.activeInHierarchy) return false;

            position = _hoverUI.transform.position;
            return true;
        }

        private void SetVisible(bool visible)
        {
            if (!visible)
            {
                if (_chevron != null) _chevron.gameObject.SetActive(false);
                return;
            }

            if (_chevron == null) Build();
            _chevron.gameObject.SetActive(true);
        }

        // A flat triangle, built in code so the feature needs no art asset and no prefab wiring.
        private void Build()
        {
            var go = new GameObject("TutorialOffscreenPointer");
            go.transform.SetParent(null);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            var mesh = new Mesh { name = "TutorialChevron" };
            float h = _size;
            mesh.vertices = new[]
            {
                new Vector3(0f, h, 0f),
                new Vector3(-h * 0.72f, -h * 0.5f, 0f),
                new Vector3(0f, -h * 0.12f, 0f),
                new Vector3(h * 0.72f, -h * 0.5f, 0f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };   // both faces, so it reads from either side
            mesh.RecalculateNormals();
            filter.sharedMesh = mesh;

            // Unlit and always-on-top: a wayfinding cue is useless if the wall between you and the
            // card hides it too.
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _material = new Material(shader) { color = _colour };
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _chevron = go.transform;
        }

        private void OnDestroy()
        {
            if (_chevron != null) Destroy(_chevron.gameObject);
            if (_material != null) Destroy(_material);
        }
    }
}
