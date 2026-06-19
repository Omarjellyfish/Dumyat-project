using UnityEngine;

namespace DumyatRuntime
{
    public class WoodPaintSurface : MonoBehaviour
    {
        RenderTexture _renderTexture;
        public int width;
        public int height;
        public RenderTexture PaintTexture => _renderTexture;
        public Vector2 GetUV(RaycastHit hit) => hit.textureCoord;
        void Awake()
        {
            Renderer render = GetComponent<Renderer>();
            _renderTexture = new RenderTexture(width, height, 0);
            _renderTexture.Create();
            ClearRenderTexture(_renderTexture);
            render.material.SetTexture("_RenderTexture", _renderTexture);
        }

        private void ClearRenderTexture(RenderTexture rt)
        {
            // Cache the current active texture to avoid breaking Unity's rendering pipeline
            RenderTexture previousActive = RenderTexture.active;

            // Set your texture active and clear it
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);

            // Restore the previous active texture
            RenderTexture.active = previousActive;
        }

    }
}
