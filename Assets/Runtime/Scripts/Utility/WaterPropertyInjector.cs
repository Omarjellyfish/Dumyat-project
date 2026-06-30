using UnityEngine;
using UnityEngine.Rendering;

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class WaterPropertyInjector : MonoBehaviour
    {
        [Header("Source Materials")]
        public Material riverMaterial;
        public Material oceanMaterial;

        [Header("Target Blend Material")]
        public Material barzakhMaterial;

        private void OnValidate()
        {
            if (barzakhMaterial == null || riverMaterial == null || oceanMaterial == null) return;
            SyncAll();
        }

        void Update()
        {
            SyncAll();
        }

        public void SyncAll()
        {
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
