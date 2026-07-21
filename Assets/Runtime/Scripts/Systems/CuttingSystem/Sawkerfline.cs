using UnityEngine;
using System.Collections.Generic;

namespace DumyatRuntime
{
    /// <summary>
    /// Draws a growing "kerf" line along the saw's contact points on the wood surface,
    /// so there's a visible cut mark building up before the mesh actually splits.
    /// Feed it points from SawTip every time it registers a stroke/hit, and call
    /// ClearLine() once WoodSlicer.PerformCut actually completes the cut.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class SawKerfLine : MonoBehaviour
    {
        [SerializeField] private float _surfaceOffset = 0.001f;
        [SerializeField] private float _lineWidth = 0.003f;
        [SerializeField] private Color _lineColor = new Color(0.15f, 0.1f, 0.05f, 1f);

        private LineRenderer _line;
        private readonly List<Vector3> _points = new List<Vector3>();

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = 0;
            _line.widthMultiplier = _lineWidth;
            _line.startColor = _lineColor;
            _line.endColor = _lineColor;
            _line.useWorldSpace = true;
        }

        /// <summary>
        /// Redraws the line using the current stroke's contact points (typically
        /// tip/mid/base from SawTip). Called fresh each stroke, not accumulated,
        /// so the line reflects where the blade is cutting right now.
        /// </summary>
        public void SetPoints(IList<Vector3> points, IList<Vector3> normals)
        {
            if (points == null || points.Count == 0)
            {
                ClearLine();
                return;
            }

            _points.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 normal = (normals != null && i < normals.Count) ? normals[i] : Vector3.up;
                _points.Add(points[i] + normal * _surfaceOffset);
            }

            _line.positionCount = _points.Count;
            _line.SetPositions(_points.ToArray());
        }

        /// <summary>
        /// Draws a single straight line spanning the FULL width of the wood piece,
        /// not just the blade's own contact width. Clips an infinite line (passing
        /// through centerPoint, running along acrossDirection) against the wood
        /// mesh's local bounding box, so the two endpoints land exactly at the
        /// plank's edges.
        /// </summary>
        public void SetFullWidthLine(GameObject wood, Vector3 centerPoint, Vector3 acrossDirection, Vector3 normal)
        {
            if (wood == null)
            {
                ClearLine();
                return;
            }

            MeshFilter meshFilter = wood.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null || acrossDirection.sqrMagnitude < 1e-8f)
            {
                ClearLine();
                return;
            }

            Transform meshTransform = meshFilter.transform;
            Bounds localBounds = meshFilter.sharedMesh.bounds;

            Vector3 localPoint = meshTransform.InverseTransformPoint(centerPoint);
            Vector3 localDir = meshTransform.InverseTransformDirection(acrossDirection).normalized;

            if (!IntersectLineWithBounds(localBounds, localPoint, localDir, out float tMin, out float tMax))
            {
                ClearLine();
                return;
            }

            Vector3 worldStart = meshTransform.TransformPoint(localPoint + localDir * tMin);
            Vector3 worldEnd = meshTransform.TransformPoint(localPoint + localDir * tMax);

            _points.Clear();
            _points.Add(worldStart + normal * _surfaceOffset);
            _points.Add(worldEnd + normal * _surfaceOffset);

            _line.positionCount = _points.Count;
            _line.SetPositions(_points.ToArray());
        }

        // Slab method: clips the line (point + t*dir) against an axis-aligned box,
        // returning the entry/exit t values where it's still inside all three axes.
        private bool IntersectLineWithBounds(Bounds bounds, Vector3 point, Vector3 dir, out float tMin, out float tMax)
        {
            tMin = float.NegativeInfinity;
            tMax = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float d = dir[axis];
                float p = point[axis];
                float minB = bounds.min[axis];
                float maxB = bounds.max[axis];

                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (p < minB || p > maxB) return false; // parallel to this axis and outside the slab
                    continue;
                }

                float t1 = (minB - p) / d;
                float t2 = (maxB - p) / d;
                if (t1 > t2)
                {
                    float temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);

                if (tMin > tMax) return false;
            }

            return true;
        }

        /// <summary>Call this once the real cut happens - the line's job is done, the mesh split replaces it.</summary>
        public void ClearLine()
        {
            _points.Clear();
            _line.positionCount = 0;
        }
    }
}