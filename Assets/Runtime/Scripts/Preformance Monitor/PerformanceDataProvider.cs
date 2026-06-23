using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace DumyatRuntime
{
    public class PerformanceDataProvider : MonoBehaviour
    {
        #region Private Fields
        private ProfilerRecorder _systemMemoryRecorder;
        private ProfilerRecorder _gcMemoryRecorder;
        private ProfilerRecorder _mainThreadTimeRecorder;
        private ProfilerRecorder _trianglesCountRecorder;
        private ProfilerRecorder _verticesCountRecorder;
        private ProfilerRecorder _drawCallsCountRecorder;
        private ProfilerRecorder _batchCountRecorder;
        private ProfilerRecorder _setPassCallsRecorder;
        private ProfilerRecorder _savedByBatchingRecorder;
        private ProfilerRecorder _cpuTimeRecorder;
        private ProfilerRecorder _gpuTimeRecorder;

        private static List<ProfilerRecorderSample> _sampleBuffer = new List<ProfilerRecorderSample>(15);
        #endregion

        #region Properties
        public double FrameTimeMs => GetRecorderFrameAverage(_mainThreadTimeRecorder) * 1e-6f;
        public double Fps => FrameTimeMs > 0 ? 1000.0 / FrameTimeMs : 0;
        public long GcMemoryMb => _gcMemoryRecorder.LastValue / (1024 * 1024);
        public long SystemMemoryMb => _systemMemoryRecorder.LastValue / (1024 * 1024);
        public long DrawCalls => _drawCallsCountRecorder.LastValue;
        public long Batches => _batchCountRecorder.LastValue;
        public long SetPassCalls => _setPassCallsRecorder.LastValue;
        //public long SavedByBatching => _savedByBatchingRecorder.LastValue;
        public double CpuTimeMs => GetRecorderFrameAverage(_cpuTimeRecorder) * 1e-6f;
        public double GpuTimeMs => GetRecorderFrameAverage(_gpuTimeRecorder) * 1e-6f;

        public double TrianglesCount => _trianglesCountRecorder.LastValue;
        public double VerticesCount => _verticesCountRecorder.LastValue;
        #endregion

        #region Unity Callbacks
        void OnEnable()
        {
            InitializeRecorders();
        }

        void OnDisable()
        {
            DisposeRecorders();
        }
        #endregion

        #region Setup & Cleanup
        private void InitializeRecorders()
        {
            _systemMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            _gcMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            _mainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _drawCallsCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batchCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPassCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _trianglesCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _verticesCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            // _savedByBatchingRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Saved by Batching Count");
            _cpuTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "CPU Total Frame Time", 15);
            _gpuTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 15);
        }

        private void DisposeRecorders()
        {
            _systemMemoryRecorder.Dispose();
            _gcMemoryRecorder.Dispose();
            _mainThreadTimeRecorder.Dispose();
            _drawCallsCountRecorder.Dispose();
            _batchCountRecorder.Dispose();
            _setPassCallsRecorder.Dispose();
            _trianglesCountRecorder.Dispose();
            _verticesCountRecorder.Dispose();
            //_savedByBatchingRecorder.Dispose();
            _cpuTimeRecorder.Dispose();
            _gpuTimeRecorder.Dispose();
        }
        #endregion

        #region Private Methods
        private static double GetRecorderFrameAverage(ProfilerRecorder recorder)
        {
            var samplesCount = recorder.Capacity;
            if (samplesCount == 0 || !recorder.Valid)
                return 0;

            _sampleBuffer.Clear();
            recorder.CopyTo(_sampleBuffer);
            
            double r = 0;
            for (var i = 0; i < _sampleBuffer.Count; ++i)
                r += _sampleBuffer[i].Value;
            
            return r / _sampleBuffer.Count;
        }
        #endregion
    }
}