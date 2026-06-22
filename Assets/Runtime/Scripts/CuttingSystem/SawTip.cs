using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public class SawTip : MonoBehaviour
    {
        [SerializeField] private SawBladePoint _bladeTip;
        [SerializeField] private SawBladePoint _bladeMid;
        [SerializeField] private SawBladePoint _bladeBase;
        [SerializeField] private float _minimumMoveDistance = 0.005f;

        private Vector3 _lastFramPosition;
        private Vector3 _currentPosition;
        private Vector3 _currentDirection;
        private Vector3 _previousDirection;
        private bool _hasValidPreviousFrame = false;
        private bool _isAnyTouching = false;
        private float _dot;
        int _strokeCount;
        List<Vector3> _hitPoints;
        List<Vector3> _hitNormal;

        void Start()
        {
            _hitPoints = new List<Vector3>();
            _hitNormal = new List<Vector3>();
        }
        private void FixedUpdate()
        {
            _bladeTip.CheckContact();
            _bladeMid.CheckContact();
            _bladeBase.CheckContact();

            bool touchingNow = _bladeTip._isTouchingWood || _bladeMid._isTouchingWood || _bladeBase._isTouchingWood;

            if (!touchingNow)
            {
                _isAnyTouching = false;
                _hasValidPreviousFrame = false;
                return;
            }

            _isAnyTouching = true;

            if (!_hasValidPreviousFrame)
            {
                _currentPosition = _bladeMid.transform.position;
                _currentDirection = Vector3.zero;
                _hasValidPreviousFrame = true;
                return;
            }

            _lastFramPosition = _currentPosition;
            _currentPosition = _bladeMid.transform.position;

            float movedDistance = Vector3.Distance(_currentPosition, _lastFramPosition);
            if (movedDistance < _minimumMoveDistance)
                return;

            _previousDirection = _currentDirection;
            _currentDirection = (_currentPosition - _lastFramPosition).normalized;

            if (_previousDirection == Vector3.zero)
                return;

            _dot = Vector3.Dot(_currentDirection, _previousDirection);

            if (_dot < 0)
            {
                _strokeCount++;
                if (_bladeTip._isTouchingWood)
                {
                    _hitPoints.Add(_bladeTip._hitPoint);
                    _hitNormal.Add(_bladeTip._hitNormal);
                }
                if (_bladeMid._isTouchingWood)
                {
                    _hitPoints.Add(_bladeMid._hitPoint);
                    _hitNormal.Add(_bladeMid._hitNormal);
                }
                if (_bladeBase._isTouchingWood)
                {
                    _hitPoints.Add(_bladeBase._hitPoint);
                    _hitNormal.Add(_bladeBase._hitNormal);
                }
            }
            if (_strokeCount == 15)
            {
                _strokeCount = 0;
                _hitPoints.Clear();
                _hitNormal.Clear();
            }
        }
    }

}