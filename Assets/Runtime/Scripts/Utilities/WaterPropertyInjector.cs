using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class WaterPropertyInjector : MonoBehaviour
    {
        [Header("Editor Sync")]
        [Tooltip("Master switch. When off, no syncing happens at all — safe to leave on the GameObject through a build.")]
        [SerializeField] private bool _enableEditorSync = true;

        [Header("Source Materials")]
        public Material riverMaterial;
        public Material oceanMaterial;

        [Header("Target Blend Material")]
        public Material barzakhMaterial;

        // ======================== EDITOR-ONLY SYNC TRIGGERS ========================

        // Normal MonoBehaviour — exists in builds. But the body of both callbacks below is
        // entirely #if UNITY_EDITOR, so in a build they compile to empty stubs: no cost, no risk.
        private void OnValidate()
        {
            #if UNITY_EDITOR
            if (!_enableEditorSync) return;
            if (Application.isPlaying) return;
            if (barzakhMaterial == null || riverMaterial == null || oceanMaterial == null) return;
            SyncAll();
            #endif
        }

        private void Update()
        {
            #if UNITY_EDITOR
            if (!_enableEditorSync) return;
            if (Application.isPlaying) return;
            SyncAll();
            #endif
        }

        // Manual sync: Right-click the component header in the Inspector and select "Sync Now".
        [ContextMenu("Sync Now")]
        public void SyncAll()
        {
            #if UNITY_EDITOR
            if (!_enableEditorSync) return;
            if (Application.isPlaying) return;
            if (barzakhMaterial == null) return;

            if (riverMaterial != null)
            {
                SyncWithPrefix(riverMaterial, barzakhMaterial, "River_");
            }

            if (oceanMaterial != null)
            {
                SyncWithPrefix(oceanMaterial, barzakhMaterial, "Ocean_");
            }

            Debug.Log("<color=cyan>[Water Sync]</color> Synced prefixes successfully!");
            #endif
        }

        // ======================== CORE SYNC LOGIC (Editor-only) ========================

        #if UNITY_EDITOR

        private void SyncWithPrefix(Material source, Material target, string prefix)
        {
            Shader sourceShader = source.shader;

            // Unity 6 Native replacement for ShaderUtil.GetPropertyCount
            int propertyCount = sourceShader.GetPropertyCount();

            for (int i = 0; i < propertyCount; i++)
            {
                // Unity 6 Native replacement for ShaderUtil.GetPropertyName
                string sourcePropName = sourceShader.GetPropertyName(i);
                string cleanSourceName = sourcePropName.TrimStart('_');
                string targetPropName = "_" + prefix + cleanSourceName;

                if (target.HasProperty(targetPropName))
                {
                    // Unity 6 Native replacement for ShaderUtil.GetPropertyType
                    // (returns UnityEngine.Rendering.ShaderPropertyType)
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
                        case ShaderPropertyType.Range: // Natively handled by ShaderPropertyType enum
                            target.SetFloat(targetPropName, source.GetFloat(sourcePropName));
                            break;
                        case ShaderPropertyType.Texture: // Unity 6 uses ShaderPropertyType.Texture instead of TexEnv
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