using System;
using UnityEngine;

namespace DumyatRuntime
{
    public class PaintBrush : MonoBehaviour
    {
        [SerializeField] private Texture2D _brushTexture;
        [SerializeField] private Material _brushMaterial;
        public void PaintAt(WoodPaintSurface wood, Vector2 uvCor, Color color, float size)
        {
            RenderTexture.active = wood.PaintTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, wood.PaintTexture.width, wood.PaintTexture.height, 0);
            _brushMaterial.color = color;
            int pixelX = Mathf.FloorToInt(uvCor.x * wood.width);
            int pixelY = Mathf.FloorToInt((1 - uvCor.y) * wood.height);
            int brushPixelSize = Mathf.FloorToInt(size * wood.width);
            Rect rect = new Rect(
                pixelX - brushPixelSize / 2,
                pixelY - brushPixelSize / 2,
                brushPixelSize,
                brushPixelSize);
            Graphics.DrawTexture(rect, _brushTexture, _brushMaterial);
            GL.PopMatrix();
            RenderTexture.active = null;
        }
        
    }
}