using UnityEngine;

namespace DumyatRuntime
{
    public class DisableSRPBatching : MonoBehaviour
    {
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
             MeshRenderer renderer = GetComponent<MeshRenderer>();
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            
            // Even an empty or dummy property forces GPU Instancing over SRP
            mpb.SetFloat("_DummyProperty", 1.0f); 
            renderer.SetPropertyBlock(mpb);
        }

    }
}
