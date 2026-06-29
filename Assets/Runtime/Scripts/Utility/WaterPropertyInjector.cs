using System.Runtime.Serialization;
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

        [Header("Prefix Names")]
        [SerializeField] private string riverPrefix = "River_";
        [SerializeField] private string oceanPrefix = "Ocean_";

        private void OnValidate()
        {
            SyncAll();
        }

        public void SyncAll()
        {
            if (barzakhMaterial == null) return;

            // Sync River properties and tell the script to look for the "River_" prefix
            if (riverMaterial != null)
            {
                SyncWithPrefix(riverMaterial, barzakhMaterial, riverPrefix);
            }

            // Sync Ocean properties and tell the script to look for the "Ocean_" prefix
            if (oceanMaterial != null)
            {
                SyncWithPrefix(oceanMaterial, barzakhMaterial, oceanPrefix);
            }
            
            Debug.Log("<color=cyan>[Water Sync]</color> Synced prefixes successfully!");
        }

        private void SyncWithPrefix(Material source, Material target, string prefix)
        {
            Shader sourceShader = source.shader;
            int propertyCount = sourceShader.GetPropertyCount();

            for (int i = 0; i < propertyCount; i++)
            {
                string sourcePropName = sourceShader.GetPropertyName(i); // e.g., "_WaveSpeed"
                
                // Clean up any leading underscores from the source property name
                string cleanSourceName = sourcePropName.TrimStart('_'); 

                // Construct the target name matching your Barzakh Blackboard IDs (e.g., "_River_WaveSpeed")
                string targetPropName = "_" + prefix + cleanSourceName;

                // If the Barzakh shader has this prefixed property, copy the data over!
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
