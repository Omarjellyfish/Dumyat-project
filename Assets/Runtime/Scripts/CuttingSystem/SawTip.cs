using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public class SawTip : MonoBehaviour
    {
        [SerializeField] private SawBladePoint _bladeTip;
        [SerializeField] private SawBladePoint _bladeMid;
        [SerializeField] private SawBladePoint _bladeBase;
        [SerializeField] private Transform _bladeDirectionReference;
        [SerializeField] private SurfaceConstraint _surfaceConstraint;
        [SerializeField] private WoodSlicer _woodSlicer;

        [SerializeField] private float _minimumMoveDistance = 0.005f;
        [SerializeField] private int _strokesNeededToCut = 15;

        [Header("Floating Text")]
        [SerializeField] private bool _showFloatingText = true;
        [SerializeField] private Vector3 _floatingTextOffset = new Vector3(0f, 0.12f, 0f);

        private Vector3 _lastFramePosition;
        private Vector3 _currentPosition;
        private Vector3 _currentDirection;
        private Vector3 _previousDirection;

        private bool _hasValidPreviousFrame;
        private int _strokeCount;
private FloatingText _activeFloatingText;
        private List<Vector3> _hitPoints;
        private List<Vector3> _hitNormals;

        private void Start()
        {
            _hitPoints = new List<Vector3>();
            _hitNormals = new List<Vector3>();
        }

        private void FixedUpdate()
        {
            _bladeTip.CheckContact();
            _bladeMid.CheckContact();
            _bladeBase.CheckContact();

            if (_surfaceConstraint == null || !_surfaceConstraint.IsConstrained)
            {
                ResetCutProgress();
                return;
            }

            bool touchingNow =
                _bladeTip._isTouchingWood ||
                _bladeMid._isTouchingWood ||
                _bladeBase._isTouchingWood;

            if (!touchingNow)
            {
                ResetStrokeTracking();
                return;
            }

            if (_bladeDirectionReference == null)
            {
                Debug.LogError("SawTip: Blade Direction Reference is not assigned.");
                ShowFloatingText("Missing blade ref");
                ResetStrokeTracking();
                return;
            }

            if (!_hasValidPreviousFrame)
            {
                _currentPosition = _bladeMid.transform.position;
                _currentDirection = Vector3.zero;
                _hasValidPreviousFrame = true;
                return;
            }

            _lastFramePosition = _currentPosition;
            _currentPosition = _bladeMid.transform.position;

            Vector3 rawDelta = _currentPosition - _lastFramePosition;

            // Use the real blade axis only, so VR grab jitter does not create fake strokes.
            Vector3 bladeAxis = _bladeDirectionReference.right.normalized;
            float strokeAmount = Vector3.Dot(rawDelta, bladeAxis);

            if (Mathf.Abs(strokeAmount) < _minimumMoveDistance)
                return;

            _previousDirection = _currentDirection;
            _currentDirection = Mathf.Sign(strokeAmount) * bladeAxis;

            if (_previousDirection == Vector3.zero)
                return;

            float dot = Vector3.Dot(_currentDirection, _previousDirection);

            if (dot < 0f)
            {
                _strokeCount++;
                AddCurrentHitPoints();

                ShowFloatingText($"Sawing {_strokeCount}/{_strokesNeededToCut}");
            }

            if (_strokeCount >= _strokesNeededToCut)
            {
                GameObject woodToCut = GetCurrentWoodObject();

                if (woodToCut != null && _hitPoints.Count > 0)
                {
                    _woodSlicer.PerformCut(_hitPoints, _currentDirection, woodToCut);
                    ShowFloatingText("Cut!");
                }

                _strokeCount = 0;
                _hitPoints.Clear();
                _hitNormals.Clear();
            }
        }

        private void AddCurrentHitPoints()
        {
            // Important: clear old points so the cut does not average old accidental touches.
            _hitPoints.Clear();
            _hitNormals.Clear();

            if (_bladeTip._isTouchingWood)
            {
                _hitPoints.Add(_bladeTip._hitPoint);
                _hitNormals.Add(_bladeTip._hitNormal);
            }

            if (_bladeMid._isTouchingWood)
            {
                _hitPoints.Add(_bladeMid._hitPoint);
                _hitNormals.Add(_bladeMid._hitNormal);
            }

            if (_bladeBase._isTouchingWood)
            {
                _hitPoints.Add(_bladeBase._hitPoint);
                _hitNormals.Add(_bladeBase._hitNormal);
            }
        }

        private GameObject GetCurrentWoodObject()
        {
            if (_bladeMid._isTouchingWood)
                return _bladeMid._hitObject;

            if (_bladeTip._isTouchingWood)
                return _bladeTip._hitObject;

            if (_bladeBase._isTouchingWood)
                return _bladeBase._hitObject;

            return null;
        }

        private void ResetCutProgress()
        {
            _hasValidPreviousFrame = false;
            _currentDirection = Vector3.zero;
            _previousDirection = Vector3.zero;
            _strokeCount = 0;
            _hitPoints.Clear();
            _hitNormals.Clear();
            HideFloatingText();
        }
private void HideFloatingText()
{
    if (_activeFloatingText == null)
        return;

    if (FloatingUIManager.Instance != null)
        FloatingUIManager.Instance.Hide(_activeFloatingText);

    _activeFloatingText = null;
}
        private void ResetStrokeTracking()
        {
            _hasValidPreviousFrame = false;
            _currentDirection = Vector3.zero;
            _previousDirection = Vector3.zero;
        }

private void ShowFloatingText(string text)
{
    if (!_showFloatingText)
        return;

    if (FloatingUIManager.Instance == null)
        return;

    Vector3 position = _bladeMid != null
        ? _bladeMid.transform.position + _floatingTextOffset
        : transform.position + _floatingTextOffset;

    if (_activeFloatingText == null || !_activeFloatingText.gameObject.activeSelf)
    {
        _activeFloatingText = FloatingUIManager.Instance.Show(position, text);
        return;
    }

    _activeFloatingText.SetPosition(position, Vector3.zero);
    _activeFloatingText.SetText(text);
}
    }
}