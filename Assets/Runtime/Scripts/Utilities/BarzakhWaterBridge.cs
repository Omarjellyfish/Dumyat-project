using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    /// <summary>
    /// Bridges two water systems (River & Ocean) by copying material properties
    /// and injecting their live wave ComputeBuffers into a unified Barzakh blend material.
    /// </summary>
    [ExecuteAlways]
    public class BarzakhWaterBridge : MonoBehaviour
    {
        [Header("Editor Sync")]
        [Tooltip("Master switch. When off, property copying is disabled.")]
        [SerializeField] private bool _enableEditorSync = true;

        [Header("River System (Water A)")]
        public WaterVFX riverVFX;
        public Material riverMaterial;

        [Header("Ocean System (Water B)")]
        public WaterVFX oceanVFX;
        public Material oceanMaterial;

        [Header("Barzakh Blend Target")]
        public Material barzakhMaterial;

        private void OnEnable()
        {
            SyncComputeBuffers();
        }

        private void Update()
        {
            if (barzakhMaterial == null) return;

            // Keep compute buffers active at both runtime and in-editor
            SyncComputeBuffers();

            #if UNITY_EDITOR
            if (!_enableEditorSync || Application.isPlaying) return;
            SyncMaterialProperties();
            #endif
        }

        private void OnValidate()
        {
            #if UNITY_EDITOR
            if (!_enableEditorSync || Application.isPlaying || barzakhMaterial == null) return;
            SyncMaterialProperties();
            #endif
        }

        // ======================== COMPUTE BUFFER INJECTION ========================

        public void SyncComputeBuffers()
        {
            if (barzakhMaterial == null) return;

            // Bind River ComputeBuffer -> _WavesA
            if (riverVFX != null)
            {
                ComputeBuffer riverBuffer = riverVFX.WaveBuffer;
                if (riverBuffer != null && riverBuffer.IsValid())
                {
                    barzakhMaterial.SetBuffer("_WavesA", riverBuffer);
                    if (riverVFX.settings != null)
                    {
                        barzakhMaterial.SetInt("_WaveCountA", riverVFX.settings.waveCount);
                    }
                }
            }

            // Bind Ocean ComputeBuffer -> _WavesB
            if (oceanVFX != null)
            {
                ComputeBuffer oceanBuffer = oceanVFX.WaveBuffer;
                if (oceanBuffer != null && oceanBuffer.IsValid())
                {
                    barzakhMaterial.SetBuffer("_WavesB", oceanBuffer);
                    if (oceanVFX.settings != null)
                    {
                        barzakhMaterial.SetInt("_WaveCountB", oceanVFX.settings.waveCount);
                    }
                }
            }
        }

        // ======================== MANUAL SYNC ========================

        [ContextMenu("Sync All Now")]
        public void SyncAll()
        {
            if (barzakhMaterial == null) return;

            SyncComputeBuffers();

            #if UNITY_EDITOR
            SyncMaterialProperties();
            #endif
        }

        // ======================== MATERIAL PROPERTY SYNC ========================

        #if UNITY_EDITOR

        private void SyncMaterialProperties()
        {
            if (riverMaterial != null)
            {
                SyncWithPrefix(riverMaterial, barzakhMaterial, "River_");
            }

            if (oceanMaterial != null)
            {
                SyncWithPrefix(oceanMaterial, barzakhMaterial, "Ocean_");
            }
        }

        private void SyncWithPrefix(Material source, Material target, string prefix)
        {
            Shader sourceShader = source.shader;
            int propertyCount = sourceShader.GetPropertyCount();

            for (int i = 0; i < propertyCount; i++)
            {
                string sourcePropName = sourceShader.GetPropertyName(i);
                string cleanSourceName = sourcePropName.TrimStart('_');
                string targetPropName = "_" + prefix + cleanSourceName;

                if (target.HasProperty(targetPropName))
                {
                    ShaderPropertyType type = sourceShader.GetPropertyType(i);

                    switch (type)
                    {
                        case ShaderPropertyType.Color:
                            target.SetColor(targetPropName, source.GetColor(sourcePropName));
                            break;
                        case ShaderPropertyType.Vector:
                            target.SetVector(targetPropName, source.GetVector(sourcePropName));
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            target.SetFloat(targetPropName, source.GetFloat(sourcePropName));
                            break;
                        case ShaderPropertyType.Texture:
                            target.SetTexture(targetPropName, source.GetTexture(sourcePropName));
                            target.SetTextureScale(targetPropName, source.GetTextureScale(sourcePropName));
                            target.SetTextureOffset(targetPropName, source.GetTextureOffset(sourcePropName));
                            break;
                    }
                }
            }
        }

        #endif
    }
}