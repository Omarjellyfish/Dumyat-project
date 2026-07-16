using UnityEngine;

namespace DumyatRuntime
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class SetDecalShader : MonoBehaviour
    {
        [SerializeField] private Texture2D _texture;
        [SerializeField] private Color color = Color.red; 
        [SerializeField] private bool _invertTexture = false;

        [Tooltip("1 = Red, 2 = Green, 3 = Blue, 4 = Alpha")]
        [Range(1, 4)]
        [SerializeField] private int channelNumber = 3;

        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;

        // Cached Shader Property IDs for optimized performance
        private static readonly int ChannelNumberID = Shader.PropertyToID("_Channel_Number");
        private static readonly int ColorID = Shader.PropertyToID("_Color");
        private static readonly int PackedDecalTextureID = Shader.PropertyToID("_Packed_Decal_Texture");
        private static readonly int InvertTextureID = Shader.PropertyToID("_Invert_Mask");

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

    _renderer.GetPropertyBlock(_propBlock);

    // Map 1-4 slider to 0-3 index logic
    _propBlock.SetFloat(ChannelNumberID, channelNumber - 1);
    _propBlock.SetColor(ColorID, color);

    if (_texture != null)
        _propBlock.SetTexture(PackedDecalTextureID, _texture);

    _propBlock.SetFloat(InvertTextureID, _invertTexture ? 1.0f : 0.0f);

    _renderer.SetPropertyBlock(_propBlock);
}
    }
}
