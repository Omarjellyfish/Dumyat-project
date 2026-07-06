using UnityEngine;

namespace DumyatRuntime
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class SetDecalShader : MonoBehaviour
    {
        [Tooltip("1 = Red, 2 = Green, 3 = Blue, 4 = Alpha")]
        [Range(1, 4)]
        [SerializeField] private int channelNumber = 3; // Default to 3 (Blue)
        
        [SerializeField] private Color color = Color.red; 

        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;

        // OnValidate runs automatically in the editor whenever you change a value in the Inspector
        private void OnValidate()
        {
            UpdateProperties();
        }

        private void Start()
        {
            UpdateProperties();
        }

        private void UpdateProperties()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            // Fetch existing block data
            _renderer.GetPropertyBlock(_propBlock);
            
            // Subtract 1 so that:
            // Slider 1 -> Shader 0 (Red)
            // Slider 2 -> Shader 1 (Green)
            // Slider 3 -> Shader 2 (Blue)
            // Slider 4 -> Shader 3 (Alpha)
            _propBlock.SetFloat("_Channel_Number", channelNumber - 1);

            // Inject the color
            _propBlock.SetColor("_Color", color); 
            
            // Apply it back to the renderer
            _renderer.SetPropertyBlock(_propBlock);
        }
    }
}