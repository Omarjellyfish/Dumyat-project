using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    [RequireComponent(typeof(PerformanceDataProvider))]
    [RequireComponent(typeof(UIDocument))]
    public class PerformanceUIControllerUIToolkit : MonoBehaviour
    {
        #region Private Fields
        private PerformanceDataProvider _dataProvider;
        private UIDocument _uiDocument;

        // UI Rows
        private VisualElement _fpsRow;
        private VisualElement _frameTimeRow;
        private VisualElement _trianglesRow;
        private VisualElement _verticesRow;
        private VisualElement _batchesRow;
        private VisualElement _setPassRow;
        private VisualElement _cpuTimeRow;
        private VisualElement _gpuTimeRow;
        private VisualElement _gcMemoryRow;
        private VisualElement _systemMemoryRow;

        // Visual Budget Bars
        private VisualElement _fpsBarFill;
        private VisualElement _trisBarFill;
        private VisualElement _drawCallsBarFill;

        // Target Lines
        private VisualElement _fpsTargetLine;
        private VisualElement _trisTargetLine;
        private VisualElement _drawCallsTargetLine;

        [Header("Settings")]
        [SerializeField] private float _updateInterval = 0.2f; 
        [SerializeField] private float _lerpSpeed = 5f; 
        private float _nextUpdateTime;

        [Header("Performance Threshold Budgets")]
        [SerializeField] private PerformanceBudgetSettingSO _budgetSettings;
        [SerializeField] private float _targetMinFps = 90f;
        [SerializeField] private float _maxTrianglesLimit = 500000f;
        [SerializeField] private float _maxDrawCallsLimit = 150f;
        
        [Header("Visual Bar Max Caps")]
        [SerializeField] private float _maxGraphFpsScale = 120f;
        [SerializeField] private float _maxGraphTrianglesScale = 1000000f;
        [SerializeField] private float _maxGraphDrawCallsScale = 300f;

        // Current smoothing values (for width)
        private float _currentFpsRatio;
        private float _currentTrisRatio;
        private float _currentDrawCallsRatio;
        #endregion

        #region Unity Callbacks
        void Awake()
        {
            _dataProvider = GetComponent<PerformanceDataProvider>();
            _uiDocument = GetComponent<UIDocument>();
            LoadBudgetSettings();
        }

        void OnEnable()
        {
            if (_uiDocument == null) return;
            var root = _uiDocument.rootVisualElement;

            // Rows
            _fpsRow = root.Q("fps-row");
            _frameTimeRow = root.Q("frame-time-row");
            _trianglesRow = root.Q("triangles-row");
            _verticesRow = root.Q("vertices-row");
            _batchesRow = root.Q("batches-row");
            _setPassRow = root.Q("setpass-row");
            _cpuTimeRow = root.Q("cpu-time-row");
            _gpuTimeRow = root.Q("gpu-time-row");
            _gcMemoryRow = root.Q("gc-memory-row");
            _systemMemoryRow = root.Q("system-memory-row");

            // Bars
            _fpsBarFill = SetupBar(root, "fps-bar", out _fpsTargetLine);
            _trisBarFill = SetupBar(root, "tri-bar", out _trisTargetLine);
            _drawCallsBarFill = SetupBar(root, "batches-bar", out _drawCallsTargetLine); 

            InitializeTargetMarkers();
        }

        void OnValidate()
        {
            LoadBudgetSettings();
            InitializeTargetMarkers();
        }

        void Update()
        {
            if (_dataProvider == null) return;

            if (Time.unscaledTime >= _nextUpdateTime)
            {
                _nextUpdateTime = Time.unscaledTime + _updateInterval;
                UpdateTextReadouts();
            }

            UpdateBarsSmoothly();
        }
        #endregion

        #region UI Updating
        private void UpdateBarsSmoothly()
        {
            // 1. Calculate Target Width Ratios (Raw values from the provider)
            float targetFpsWidthRatio = (float)_dataProvider.Fps / _maxGraphFpsScale;
            float targetTrisWidthRatio = (float)_dataProvider.TrianglesCount / _maxGraphTrianglesScale;
            float targetDrawCallsWidthRatio = (float)_dataProvider.DrawCalls / _maxGraphDrawCallsScale;

            // 2. Smoothly Interpolate the Width Ratios
            _currentFpsRatio = Mathf.Lerp(_currentFpsRatio, targetFpsWidthRatio, Time.unscaledDeltaTime * _lerpSpeed);
            _currentTrisRatio = Mathf.Lerp(_currentTrisRatio, targetTrisWidthRatio, Time.unscaledDeltaTime * _lerpSpeed);
            _currentDrawCallsRatio = Mathf.Lerp(_currentDrawCallsRatio, targetDrawCallsWidthRatio, Time.unscaledDeltaTime * _lerpSpeed);

            // 3. BUDGET RATIOS (Now derived directly from the smoothed width ratios!)
            
            // For Triangles and Draw Calls, the budget ratio is just a re-scaling of the graph scale width
            float smoothedTrisCount = _currentTrisRatio * _maxGraphTrianglesScale;
            float trisBudgetRatio = smoothedTrisCount / _maxTrianglesLimit;

            float smoothedDrawCalls = _currentDrawCallsRatio * _maxGraphDrawCallsScale;
            float drawCallsBudgetRatio = smoothedDrawCalls / _maxDrawCallsLimit;
            
            // For FPS, use the smoothed FPS value derived from the smoothed ratio
            float smoothedFps = _currentFpsRatio * _maxGraphFpsScale;
            float fpsBudgetRatio;
            
            if (smoothedFps <= _targetMinFps)
            {
                fpsBudgetRatio = 1f; 
            }
            else
            {
                float range = _maxGraphFpsScale - _targetMinFps;
                if (range > 0f)
                {
                    float progressAboveMin = (smoothedFps - _targetMinFps) / range;
                    fpsBudgetRatio = 1f - Mathf.Clamp01(progressAboveMin);
                }
                else
                {
                    fpsBudgetRatio = 0f;
                }
            }

            // 4. Apply to UI (Widths and Colors are now perfectly in sync)
            SetBarVisuals(_fpsBarFill, _currentFpsRatio, fpsBudgetRatio);
            SetBarVisuals(_trisBarFill, _currentTrisRatio, trisBudgetRatio);
            SetBarVisuals(_drawCallsBarFill, _currentDrawCallsRatio, drawCallsBudgetRatio);
        }

        private void SetBarVisuals(VisualElement element, float widthRatio, float budgetRatio)
        {
            if (element == null) return;

            // 1. Update Width (Always based on Graph Scale)
            element.style.width = Length.Percent(Mathf.Clamp01(widthRatio) * 100f);

            // 2. Update Color (Based on Budget Ratio)
            // 0.0 is Green, 1.0 is Red
            float colorEval = Mathf.Clamp01(budgetRatio);
            
            Color targetColor;
            if (colorEval < 0.5f)
            {
                targetColor = Color.Lerp(Color.green, Color.yellow, colorEval * 2f);
            }
            else
            {
                targetColor = Color.Lerp(Color.yellow, Color.red, (colorEval - 0.5f) * 2f);
            }

            element.style.backgroundColor = targetColor;
        }

        private void UpdateTextReadouts()
        {
            SetRowValue(_fpsRow, $"{_dataProvider.Fps:F1}");
            SetRowValue(_frameTimeRow, $"{_dataProvider.FrameTimeMs:F1} ms");
            SetRowValue(_trianglesRow, $"{_dataProvider.TrianglesCount}");
            SetRowValue(_verticesRow, $"{_dataProvider.VerticesCount}");
            SetRowValue(_batchesRow, $"{_dataProvider.Batches}");
            SetRowValue(_setPassRow, $"{_dataProvider.SetPassCalls}");
            SetRowValue(_cpuTimeRow, $"{_dataProvider.CpuTimeMs:F1} ms");
            SetRowValue(_gpuTimeRow, $"{_dataProvider.GpuTimeMs:F1} ms");
            SetRowValue(_gcMemoryRow, $"{_dataProvider.GcMemoryMb:F0} MB");
            SetRowValue(_systemMemoryRow, $"{_dataProvider.SystemMemoryMb:F0} MB");
        }

        private void SetRowValue(VisualElement row, string text)
        {
            if (row == null) return;
            var label = row.Q<Label>("row-value");
            if (label != null) label.text = text;
        }
        #endregion

        #region Setup Helpers
        [ContextMenu("Initialize Target Markers")]
        private void InitializeTargetMarkers()
        {
            if (_maxGraphFpsScale > 0) SetTargetPosition(_fpsTargetLine, _targetMinFps / _maxGraphFpsScale);
            if (_maxGraphDrawCallsScale > 0) SetTargetPosition(_drawCallsTargetLine, _maxDrawCallsLimit / _maxGraphDrawCallsScale);
            if (_maxGraphTrianglesScale > 0) SetTargetPosition(_trisTargetLine, _maxTrianglesLimit / _maxGraphTrianglesScale);
        }

        private VisualElement SetupBar(VisualElement root, string containerName, out VisualElement targetLine)
        {
            targetLine = null;
            var container = root.Q(containerName);
            if (container == null) return null;

            targetLine = container.Q("bar-target-line");
            var fill = container.Q("bar-fill");
            return fill;
        }

        private void SetTargetPosition(VisualElement line, float ratio)
        {
            if (line != null) line.style.left = Length.Percent(Mathf.Clamp01(ratio) * 100f);
        }

        private void LoadBudgetSettings()
        {
            if (_budgetSettings != null)
            {
                _targetMinFps = _budgetSettings.TargetMinFps;
                _maxTrianglesLimit = _budgetSettings.MaxTriangles;
                _maxDrawCallsLimit = _budgetSettings.MaxDrawCalls;
                _maxGraphFpsScale = _budgetSettings.MaxGraphFpsScale;
                _maxGraphTrianglesScale = _budgetSettings.MaxGraphTrianglesScale;
                _maxGraphDrawCallsScale = _budgetSettings.MaxGraphDrawCallsScale;
            }
        }
        #endregion
    }
}
