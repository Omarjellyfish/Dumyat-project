using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class VertexPainterPropertyInjector : MonoBehaviour
    {
        public enum MaterialCountEnum
        {
            Two = 0,
            Three = 1,
            // Four = 2,
            // Five = 3
        }

        [Header("Editor Sync")]
        [Tooltip("Master switch. When off, no syncing happens at all — safe to leave on the GameObject through a build.")]
        [SerializeField] private bool _enableEditorSync = true;

        [Header("Shader Graph Keyword Control")]
        [SerializeField] private MaterialCountEnum _materialCount = MaterialCountEnum.Two;

        [Header("Materials")]
        [SerializeField] private Material _materialA;
        [SerializeField] private Material _materialB;
        [SerializeField] private Material _materialC;
        // [SerializeField] private Material _materialD;
        // [SerializeField] private Material _materialE;

        [Header("Target Blend Material")]
        [SerializeField] private Material _targetBlendMaterial;

        // ======================== EDITOR-ONLY SYNC TRIGGERS ========================

        // This is a normal MonoBehaviour, so it exists in builds — but the body of Update()
        // is entirely #if UNITY_EDITOR, so in a build this method compiles to an empty stub
        // that does nothing and costs nothing.
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
            if (_targetBlendMaterial == null) return;

            // 1. Update the active shader keyword (e.g., _MATERIALCOUNT_TWO vs _MATERIALCOUNT_THREE)
            ApplyMaterialCountKeyword(_targetBlendMaterial);

            // 2. Sync only the materials that are actually used based on the dropdown selection.
            //    This prevents wasting CPU syncing Mat3_ properties when the shader ignores them.
            if (_materialA != null)
                SyncWithPrefix(_materialA, _targetBlendMaterial, "Mat1_");

            if (_materialCount >= MaterialCountEnum.Three && _materialB != null)
                SyncWithPrefix(_materialB, _targetBlendMaterial, "Mat2_");

            if (_materialCount >= MaterialCountEnum.Three && _materialC != null)
                SyncWithPrefix(_materialC, _targetBlendMaterial, "Mat3_");

            #endif
        }

        // ======================== CORE SYNC LOGIC (Editor-only) ========================

        #if UNITY_EDITOR

        private void ApplyMaterialCountKeyword(Material target)
        {
            string baseKeyword = "_MATERIALCOUNT";

            // Clean active states for this specific keyword group
            target.DisableKeyword($"{baseKeyword}_TWO");
            target.DisableKeyword($"{baseKeyword}_THREE");

            // Toggle the exact one selected in your inspector
            switch (_materialCount)
            {
                case MaterialCountEnum.Two:
                    target.SetFloat(baseKeyword, 0f);
                    target.EnableKeyword($"{baseKeyword}_TWO");
                    break;
                case MaterialCountEnum.Three:
                    target.SetFloat(baseKeyword, 1f);
                    target.EnableKeyword($"{baseKeyword}_THREE");
                    break;
            }
        }

        private void SyncWithPrefix(Material source, Material target, string prefix)
        {
            Shader sourceShader = source.shader;
            int propertyCount = sourceShader.GetPropertyCount();

            for (int i = 0; i < propertyCount; i++)
            {
                string sourcePropName = sourceShader.GetPropertyName(i);

                // Clean the source name: if it starts with "_", remove it so we don't double-underscore.
                string cleanSourceName = sourcePropName.StartsWith("_")
                    ? sourcePropName.Substring(1)
                    : sourcePropName;

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

                        // Ignore any other property types (e.g., Int) to be safe.
                        default:
                            break;
                    }
                }
            }
        }

        #endif
    }
}