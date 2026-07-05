using UnityEngine;
using UnityEngine.Rendering;

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class VertexPainterPropertyInjector : MonoBehaviour
    {
        public enum MaterialCountEnum
        {
            Two = 0,
            Three = 1,
            Four = 2,
            Five = 3
        }

        [Header("Shader Graph Keyword Control")]
        [SerializeField] private MaterialCountEnum _materialCount = MaterialCountEnum.Two;

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
            if (_targetBlendMaterial == null) return;
            SyncAll();
        }

        void Update()
        {
            SyncAll();
        }

        public void SyncAll()
        {
            if (_targetBlendMaterial == null) return;

            // 1. Update the main layout keyword directly
            ApplyMaterialCountKeyword(_targetBlendMaterial);

            // 2. Inject standard properties (Textures, Colors, Floats)
            if (_materialA != null) SyncWithPrefix(_materialA, _targetBlendMaterial, "Mat1_");
            if (_materialB != null) SyncWithPrefix(_materialB, _targetBlendMaterial, "Mat2_");
            if (_materialC != null) SyncWithPrefix(_materialC, _targetBlendMaterial, "Mat3_");
            if (_materialD != null) SyncWithPrefix(_materialD, _targetBlendMaterial, "Mat4_");
            if (_materialE != null) SyncWithPrefix(_materialE, _targetBlendMaterial, "Mat5_");
        }

        private void ApplyMaterialCountKeyword(Material target)
        {
            string baseKeyword = "_MATERIALCOUNT";
            
            // Clean active states for this specific keyword group
            target.DisableKeyword($"{baseKeyword}_TWO");
            target.DisableKeyword($"{baseKeyword}_THREE");
            target.DisableKeyword($"{baseKeyword}_FOUR");
            target.DisableKeyword($"{baseKeyword}_FIVE");

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
                case MaterialCountEnum.Four:
                    target.SetFloat(baseKeyword, 2f);
                    target.EnableKeyword($"{baseKeyword}_FOUR");
                    break;
                case MaterialCountEnum.Five:
                    target.SetFloat(baseKeyword, 3f);
                    target.EnableKeyword($"{baseKeyword}_FIVE");
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
    }
}