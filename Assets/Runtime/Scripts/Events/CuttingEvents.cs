using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Published by <see cref="WoodSlicer"/> immediately after a plank is sliced into two pieces
    /// (before the original wood is destroyed).
    /// </summary>
    public readonly struct WoodCutEvent : IGameEvent
    {
        public readonly Vector3 CutPosition;
        public readonly GameObject OriginalWood;
        public readonly GameObject UpperPiece;
        public readonly GameObject LowerPiece;

        public WoodCutEvent(Vector3 cutPosition, GameObject originalWood, GameObject upperPiece, GameObject lowerPiece)
        {
            CutPosition = cutPosition;
            OriginalWood = originalWood;
            UpperPiece = upperPiece;
            LowerPiece = lowerPiece;
        }
    }

    /// <summary>Published by <see cref="SawTip"/> on each completed saw stroke while cutting.</summary>
    public readonly struct SawStrokeEvent : IGameEvent
    {
        public readonly int StrokeCount;
        public readonly int StrokesNeeded;
        public readonly Vector3 Position;

        public SawStrokeEvent(int strokeCount, int strokesNeeded, Vector3 position)
        {
            StrokeCount = strokeCount;
            StrokesNeeded = strokesNeeded;
            Position = position;
        }
    }

    /// <summary>Published when a constrained tool starts or stops being locked to a surface.</summary>
    public readonly struct SurfaceConstraintChangedEvent : IGameEvent
    {
        public readonly SurfaceConstraint Constraint;
        public readonly bool IsConstrained;
        public readonly Vector3 Position;
        public readonly Vector3 Normal;

        public SurfaceConstraintChangedEvent(SurfaceConstraint constraint, bool isConstrained, Vector3 position, Vector3 normal)
        {
            Constraint = constraint;
            IsConstrained = isConstrained;
            Position = position;
            Normal = normal;
        }
    }

    /// <summary>Published whenever a constrained tool reports progress along its current action.</summary>
    public readonly struct SurfaceConstraintProgressEvent : IGameEvent
    {
        public readonly SurfaceConstraint Constraint;
        public readonly int Current;
        public readonly int Required;

        public SurfaceConstraintProgressEvent(SurfaceConstraint constraint, int current, int required)
        {
            Constraint = constraint;
            Current = current;
            Required = required;
        }
    }

    public readonly struct PaintAppliedEvent : IGameEvent
    {
        public readonly WoodPaintSurface Surface;
        public readonly Vector2 Uv;
        public readonly Color Color;
        public readonly float BrushSize;

        public PaintAppliedEvent(WoodPaintSurface surface, Vector2 uv, Color color, float brushSize)
        {
            Surface = surface;
            Uv = uv;
            Color = color;
            BrushSize = brushSize;
        }
    }

    public readonly struct PaintLineCompletedEvent : IGameEvent
    {
        public readonly WoodPaintSurface Surface;
        public readonly Vector2 StartUv;
        public readonly Vector2 EndUv;
        public readonly float BrushSize;

        public PaintLineCompletedEvent(WoodPaintSurface surface, Vector2 startUv, Vector2 endUv, float brushSize)
        {
            Surface = surface;
            StartUv = startUv;
            EndUv = endUv;
            BrushSize = brushSize;
        }
    }
}
