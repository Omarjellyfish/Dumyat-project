using UnityEngine;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class PenTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [Tooltip("How far past the tip the detection ray reaches.")]
        [SerializeField] private float _rayDistance = 0.05f;
        [Tooltip("How far behind the tip the ray starts. A tracked hand regularly pushes " +
                 "the tip slightly through the surface, and a ray that starts inside a " +
                 "collider reports no hit - this backs the origin off so it still does.")]
        [SerializeField] private float _rayBackOffset = 0.02f;
        [SerializeField] private PaintBrush _paintBrush;
        [SerializeField] private LineDrawing _lineDraw;
        [SerializeField] private Color _penColor = Color.black;
        [SerializeField] private float _brushSize = 0.012f;
        [SerializeField] private float _brushSizeLine = 0.012f;
        private float _lastDrawnTime;
        [SerializeField] private float _timeInterval = 0.01f;
        [Tooltip("Largest UV gap that still counts as one continuous stroke. Beyond this " +
                 "the tip has jumped to a different part of the unwrap, so joining the two " +
                 "points would smear a line across unrelated parts of the board.")]
        [SerializeField] private float _maxStrokeUvGap = 0.15f;

        [Header("Mode Toggles")]
        [Tooltip("Temporarily disable free draw entirely while focusing on line drawing.")]
        [SerializeField] private bool _freeDrawEnabled = true;

        [HideInInspector] public PenMode _currentMode;
        public PenMode CurrentMode
        {
            get => _currentMode;
            set => _currentMode = value;
        }

        private Vector3 _pointAWorld;
        private Vector2 _pointAUV;
        private bool _pointASet = false;

        // Last point the line preview actually reached. Kept so releasing the trigger
        // commits the line the player saw, even if the tip lost contact on that frame.
        private Vector3 _pointBWorld;
        private Vector2 _pointBUV;
        private bool _pointBSet = false;

        private Vector2 _lastPaintUV;
        private bool _lastPaintUVSet;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        private WoodPaintSurface _currentWood;
        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        private void Awake()
        {
            _currentMode = PenMode.FreeDraw;
            if (_paintBrush == null)
                _paintBrush = GetComponent<PaintBrush>();
        }

        private void Update()
        {
            bool onWood = TryDetectWood(out RaycastHit hit, out WoodPaintSurface wood, out Vector2 uv);

            switch (_currentMode)
            {
                case PenMode.FreeDraw:
                    if (!onWood || !_freeDrawEnabled)
                    {
                        // Lifting the pen breaks the stroke, so the next contact starts a
                        // fresh dot instead of being joined back to where we left off.
                        _lastPaintUVSet = false;
                        break;
                    }

                    FreeDraw(wood, uv);
                    break;

                case PenMode.LineDrawing:
                    if (!onWood)
                        break;

                    if (!_pointASet)
                    {
                        _pointAWorld = hit.point;
                        _pointAUV = uv;
                        _pointASet = true;
                        _currentWood = wood;
                    }

                    // Only the surface the line started on can receive it.
                    if (wood != _currentWood)
                        break;

                    _pointBWorld = hit.point;
                    _pointBUV = uv;
                    _pointBSet = true;

                    if (_lineDraw != null)
                        _lineDraw.UpdateLinePositions(_pointAWorld, hit.point);
                    break;
            }
        }

        private void FreeDraw(WoodPaintSurface wood, Vector2 uv)
        {
            if (_paintBrush == null)
                return;

            if (wood != _currentWood)
            {
                _currentWood = wood;
                _lastPaintUVSet = false;
            }

            if (Time.time - _lastDrawnTime < _timeInterval)
                return;

            // Joining up to the previous stamp is what keeps this reading as a line: at
            // headset frame rates the tip covers far more ground between frames than a
            // mouse does in the simulator, so isolated stamps come out as a dotted trail.
            if (_lastPaintUVSet && Vector2.Distance(_lastPaintUV, uv) <= _maxStrokeUvGap)
                _paintBrush.PaintStroke(wood, _lastPaintUV, uv, _penColor, _brushSize);
            else
                _paintBrush.PaintAt(wood, uv, _penColor, _brushSize);

            _eventService?.Publish(new PaintAppliedEvent(wood, uv, _penColor, _brushSize));

            _lastPaintUV = uv;
            _lastPaintUVSet = true;
            _lastDrawnTime = Time.time;
        }

        public void FinalizeLine()
        {
            if (!_pointASet || !_pointBSet || _currentWood == null || _paintBrush == null)
            {
                ResetLineState();
                return;
            }

            _paintBrush.PaintStroke(_currentWood, _pointAUV, _pointBUV, _penColor, _brushSizeLine);

            _eventService?.Publish(new PaintAppliedEvent(_currentWood, _pointBUV, _penColor, _brushSizeLine));
            _eventService?.Publish(
                new PaintLineCompletedEvent(_currentWood, _pointAUV, _pointBUV, _brushSizeLine));

            ResetLineState();
        }

        private void ResetLineState()
        {
            _pointASet = false;
            _pointBSet = false;
            _lastPaintUVSet = false;
            _currentMode = PenMode.FreeDraw;
        }

        /// <summary>
        /// Finds the nearest wood surface in front of the tip that can actually report a
        /// UV. Deliberately looks at every hit along the ray rather than just the first:
        /// the plank prefabs carry a BoxCollider on the same object and layer as their
        /// painting MeshCollider, and whichever one the ray happened to land on decided
        /// whether a mark went where the player drew it or to UV (0,0) - which is what
        /// scattered the stroke into stray dots.
        /// </summary>
        private bool TryDetectWood(out RaycastHit hit, out WoodPaintSurface wood, out Vector2 uv)
        {
            hit = default;
            wood = null;
            uv = Vector2.zero;

            Vector3 direction = -transform.forward;
            Vector3 origin = transform.position - direction * _rayBackOffset;
            float distance = _rayDistance + _rayBackOffset;

            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, distance, _woodLayer);
            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = _hitBuffer[i];

                // UVs only resolve against a non-convex MeshCollider. Anything else
                // reports (0,0) rather than failing, so it has to be skipped outright.
                if (!(candidate.collider is MeshCollider meshCollider) || meshCollider.convex)
                    continue;

                WoodPaintSurface surface = candidate.collider.GetComponent<WoodPaintSurface>();
                if (surface == null || candidate.distance >= nearest)
                    continue;

                nearest = candidate.distance;
                hit = candidate;
                wood = surface;
            }

            if (wood == null)
                return false;

            // Use the second UV channel (unique/non-overlapping unwrap), NOT
            // hit.textureCoord (UV0), which is the trim sheet's overlapping UVs.
            uv = hit.textureCoord2;
            return true;
        }

        private void OnDrawGizmos()
        {
            Vector3 direction = -transform.forward;
            Vector3 origin = transform.position - direction * _rayBackOffset;

            Gizmos.color = Color.red;
            Gizmos.DrawRay(origin, direction * (_rayDistance + _rayBackOffset));
        }
    }
}
