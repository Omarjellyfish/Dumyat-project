using UnityEngine;

namespace DumyatRuntime
{
    public class WoodPaintSurface : MonoBehaviour
    {
        RenderTexture _renderTexture;
        [SerializeField] private int _width = 512;
        [SerializeField] private int _height = 512;
        public RenderTexture PaintTexture => _renderTexture;

        void Awake()
        {
            Renderer render = GetComponent<Renderer>();
            _renderTexture = new RenderTexture(_width, _height, 0);
            _renderTexture.Create();
            ClearRenderTexture(_renderTexture);
            render.material.SetTexture("_RenderTexture", _renderTexture);
        }

        private void ClearRenderTexture(RenderTexture rt)
        {
            RenderTexture previousActive = RenderTexture.active;

            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);

            RenderTexture.active = previousActive;
        }

        public void CopyPaintFrom(RenderTexture source)
        {
            if (source == null) return;
            Graphics.Blit(source, _renderTexture);
        }
    }
}