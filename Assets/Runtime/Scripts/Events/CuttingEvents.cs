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
}
