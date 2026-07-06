using UnityEngine;

namespace DumyatRuntime
{
    public enum PenMode
    {
        FreeDraw,
        LineDrawing,
        Spray
    }

    public readonly struct PaintAppliedEvent : IGameEvent
    {
        public readonly WoodPaintSurface Surface;
        public readonly Vector3 WorldPosition;
        public readonly Vector2 UV;
        public readonly Color Color;
        public readonly float BrushSize;
        public readonly PenMode Mode;

        public PaintAppliedEvent(WoodPaintSurface surface, Vector3 worldPosition, Vector2 uv, Color color, float brushSize, PenMode mode)
        {
            Surface = surface;
            WorldPosition = worldPosition;
            UV = uv;
            Color = color;
            BrushSize = brushSize;
            Mode = mode;
        }
    }

    public readonly struct PaintLineStartedEvent : IGameEvent
    {
        public readonly Vector3 StartPosition;

        public PaintLineStartedEvent(Vector3 startPosition)
        {
            StartPosition = startPosition;
        }
    }

    public readonly struct PaintLineCompletedEvent : IGameEvent
    {
        public readonly WoodPaintSurface Surface;
        public readonly Vector3 StartPosition;
        public readonly Vector3 EndPosition;

        public PaintLineCompletedEvent(WoodPaintSurface surface, Vector3 startPosition, Vector3 endPosition)
        {
            Surface = surface;
            StartPosition = startPosition;
            EndPosition = endPosition;
        }
    }

    public class PaintingManager : MonoBehaviour
    {
        void Update()
        {

        }
    }
}
