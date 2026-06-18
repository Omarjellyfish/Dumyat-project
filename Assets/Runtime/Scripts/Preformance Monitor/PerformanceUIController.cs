using System.Text;
using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    [RequireComponent(typeof(PerformanceDataProvider))]
    public class PerformanceUIController : MonoBehaviour
    {
        #region Fields & Dependencies
        private PerformanceDataProvider dataProvider;
        private StringBuilder sb = new StringBuilder(500);

        [SerializeField] private TextMeshProUGUI textMeshProUGUI;
        
        [Header("Settings")]
        [SerializeField] private float updateInterval = 0.3f; 
        private float nextUpdateTime;
        #endregion

        #region Unity Lifecycle
        void Awake()
        {
            dataProvider = GetComponent<PerformanceDataProvider>();
        }

        void Update()
        {
            if (Time.unscaledTime < nextUpdateTime) 
                return;
                
            nextUpdateTime = Time.unscaledTime + updateInterval;

            RenderMetricsDisplay();
        }
        #endregion

        #region Rendering Logic
        private void RenderMetricsDisplay()
        {
            sb.Clear();
            
            sb.AppendLine($"FPS: {dataProvider.Fps:F1}");
            sb.AppendLine($"Frame Time: {dataProvider.FrameTimeMs:F1} ms");

            sb.AppendLine($"Triangles: {dataProvider.TrianglesCount}");
            sb.AppendLine($"Vertices: {dataProvider.VerticesCount}");
            
            sb.AppendLine($"Batches: {dataProvider.Batches}");
            sb.AppendLine($"Set Pass Calls: {dataProvider.SetPassCalls}");

            sb.AppendLine($"CPU Time: {dataProvider.CpuTimeMs:F1} ms");
            sb.AppendLine($"GPU Time: {dataProvider.GpuTimeMs:F1} ms");

            sb.AppendLine($"GC Memory: {dataProvider.GcMemoryMb} MB");
            sb.AppendLine($"System Memory: {dataProvider.SystemMemoryMb} MB");
            
            if (textMeshProUGUI != null)
            {
                textMeshProUGUI.text = sb.ToString();
            }
        }
        #endregion
    }
}
