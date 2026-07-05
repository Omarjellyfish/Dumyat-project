using UnityEngine;
using UnityEngine.Rendering;

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class VertexPainterPropertyInjector : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] private Material _materialA;
        [SerializeField] private Material _materialB;
        [SerializeField] private Material _materialC;
        [SerializeField] private Material _materialD;
        [SerializeField] private Material _materialE;

        [Header("Target Blend Material")]
        [SerializeField] private Material _targetBlendMaterial;

        private void OnValidate()
        {
            if (_targetBlendMaterial == null || _materialA == null || _materialB == null || _materialC == null || _materialD == null || _materialE == null) return;
            SyncAll();
        }

        void Update()
        {
            SyncAll();
        }

        public void SyncAll()
        {
            if (_targetBlendMaterial == null) return;

            if (_materialA != null)
            {
                SyncWithPrefix(_materialA, _targetBlendMaterial, "Mat1_");
            }

            if (_materialB != null)
            {
                SyncWithPrefix(_materialB, _targetBlendMaterial, "Mat2_");
            }

            if (_materialC != null)
            {
                SyncWithPrefix(_materialC, _targetBlendMaterial, "Mat3_");
            }

            if (_materialD != null)
            {
                SyncWithPrefix(_materialD, _targetBlendMaterial, "Mat4_");
            }

            if (_materialE != null)
            {
                SyncWithPrefix(_materialE, _targetBlendMaterial, "Mat5_");
            }

            Debug.Log("<color=cyan>[VertexPainterPropertyInjectorSync]</color> Synced prefixes successfully!");
        }

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
    }
}
