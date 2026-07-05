using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class SprayTip : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private LayerMask _woodLayer;
        [SerializeField] private float _rayDistance = 0.15f; // spray reaches a bit further than the pen tip

        [Header("Painting")]
        [SerializeField] private PaintBrush _paintBrush;
        [SerializeField] private Color _sprayColor = Color.black;
        [SerializeField] private float _brushSize = 0.08f; // noticeably bigger than the pen's 0.02f
        [SerializeField] private float _timeInterval = 0.02f;

        [Header("Input")]
        [SerializeField] private XRGrabInteractable _grabInteractable;

        private float _lastDrawnTime;
        private bool _isSpraying;

        private void Awake()
        {
            if (_grabInteractable == null)
                _grabInteractable = GetComponent<XRGrabInteractable>();
        }

        private void OnEnable()
        {
            if (_grabInteractable != null)
            {
                _grabInteractable.activated.AddListener(OnSprayStart);
                _grabInteractable.deactivated.AddListener(OnSprayEnd);
            }
        }

        private void OnDisable()
        {
            if (_grabInteractable != null)
            {
                _grabInteractable.activated.RemoveListener(OnSprayStart);
                _grabInteractable.deactivated.RemoveListener(OnSprayEnd);
            }
        }

        private void OnSprayStart(ActivateEventArgs args) => _isSpraying = true;
        private void OnSprayEnd(DeactivateEventArgs args) => _isSpraying = false;

        private void Update()
        {
            if (!_isSpraying)
                return;

            if (Time.time - _lastDrawnTime < _timeInterval)
                return;

            if (Physics.Raycast(transform.position, -transform.forward,
                out RaycastHit hit, _rayDistance, _woodLayer))
            {
                WoodPaintSurface wood = hit.collider.GetComponent<WoodPaintSurface>();
                if (wood == null) return;

                Vector2 uv = hit.textureCoord2;
                _paintBrush.PaintAt(wood, uv, _sprayColor, _brushSize);
                _lastDrawnTime = Time.time;
            }
        }

        /// <summary>Called by a ColorSwatch (or any other color-picker UI) to change the active spray color.</summary>
        public void SetColor(Color color)
        {
            _sprayColor = color;
        }

        // If a trigger collider sits on this object (or forwards here), touching a
        // ColorSwatch picks up its color - a simple, VR-native way to choose color
        // without needing a screen-space UI.
        private void OnTriggerEnter(Collider other)
        {
            ColorSwatch swatch = other.GetComponent<ColorSwatch>();
            if (swatch != null)
            {
                SetColor(swatch.SwatchColor);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _sprayColor;
            Gizmos.DrawRay(transform.position, -transform.forward * _rayDistance);
        }
    }
}