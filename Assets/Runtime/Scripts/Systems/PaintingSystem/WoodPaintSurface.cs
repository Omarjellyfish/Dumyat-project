using UnityEngine;

namespace DumyatRuntime
{
    public class WoodPaintSurface : MonoBehaviour
    {
        RenderTexture _renderTexture;
        [SerializeField] private int _width = 2048;
        [SerializeField] private int _height = 2048;
        public RenderTexture PaintTexture => _renderTexture;

        void Awake()
        {
            Renderer render = GetComponent<Renderer>();
            _renderTexture = new RenderTexture(_width, _height, 0);
            _renderTexture.Create();
            ClearRenderTexture(_renderTexture);
            render.material.SetTexture("_RenderTexture", _renderTexture);

            WarnIfNotPaintable();
        }

        /// <summary>
        /// The pen locates a mark by reading the UV of its raycast hit, and
        /// <see cref="RaycastHit.textureCoord2"/> only resolves against a non-convex
        /// MeshCollider. Any other collider reports (0,0) instead of failing, so every
        /// mark silently stacks in one corner of the texture - worth saying out loud.
        /// </summary>
        private void WarnIfNotPaintable()
        {
            bool hasPaintableCollider = false;

            foreach (Collider surfaceCollider in GetComponents<Collider>())
            {
                if (surfaceCollider is MeshCollider meshCollider && !meshCollider.convex)
                    hasPaintableCollider = true;
            }

            if (hasPaintableCollider)
                return;

            Debug.LogWarning(
                $"{nameof(WoodPaintSurface)} on {name} has no non-convex MeshCollider. " +
                "Paint UVs only resolve against one, so the pen cannot mark this surface.", this);
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