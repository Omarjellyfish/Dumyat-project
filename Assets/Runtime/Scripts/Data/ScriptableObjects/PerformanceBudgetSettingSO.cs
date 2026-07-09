using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "PerformanceBudgetSettings", menuName = "Dumyat/Performance/Performance Budget Settings")]
    public class PerformanceBudgetSettingSO : ScriptableObject
    {
        [Header("Performance Threshold Budgets")]
        [Tooltip("The minimum target FPS for the application.")]
        [SerializeField] private int _targetMinFps = 60;

        [Tooltip("The maximum number of triangles allowed before performance is considered poor.")]
        [SerializeField] private int _maxTriangles = 500000;

        [Tooltip("The maximum number of draw calls allowed before performance is considered poor.")]
        [SerializeField] private int _maxDrawCalls = 150;

        [Header("Visual Bar Max Caps")]
        
        [Tooltip("The maximum graph value representing 100% width of the track element for FPS.")]
        [SerializeField] private float _maxGraphFpsScale = 120f;

        [Tooltip("The maximum graph value representing 100% width of the track element for triangles.")]
        [SerializeField] private float _maxGraphTrianglesScale = 1000000f;
        
        [Tooltip("The maximum graph value representing 100% width of the track element for draw calls.")]
        [SerializeField] private float _maxGraphDrawCallsScale = 300f;

        public float TargetMinFps => _targetMinFps;
        public float MaxTriangles => _maxTriangles;
        public float MaxDrawCalls => _maxDrawCalls;
        public float MaxGraphFpsScale => _maxGraphFpsScale;
        public float MaxGraphTrianglesScale => _maxGraphTrianglesScale;
        public float MaxGraphDrawCallsScale => _maxGraphDrawCallsScale;
    }
}
