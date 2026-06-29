using UnityEngine;
using System.Collections.Generic;
using EzySlice;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    public class WoodSlicer : MonoBehaviour
    {
        [SerializeField] private Material _crossSectionMaterial;
        [SerializeField] private Transform _bladeDirectionReference;

        [Header("Debug")]
        [SerializeField] private bool _drawDebug = true;
        [SerializeField] private float _debugRayLength = 0.5f;

        [Header("Generated Pieces")]
        [SerializeField] private bool _recenterPiecePivot = true;

        private Vector3 _averagePosition;
        private Vector3 _averageDirection;
        private int originalWoodLayer;

        private Vector3 _lastCutPosition;
        private Vector3 _lastCutNormal;
        private Vector3 _lastBladeForward;
        private Vector3 _lastBladeUp;
        private Vector3 _lastSawDirection;
        private bool _hasDebugCut;

        public void PerformCut(List<Vector3> hitPoints, Vector3 sawDirection, GameObject wood)
        {
            if (wood == null)
            {
                Debug.LogWarning("WoodSlicer: No wood object passed.");
                return;
            }

            if (_bladeDirectionReference == null)
            {
                Debug.LogError("WoodSlicer: Blade Direction Reference is not assigned.");
                return;
            }

            if (hitPoints == null || hitPoints.Count == 0)
            {
                Debug.LogWarning("WoodSlicer: No hit points collected.");
                return;
            }

            Vector3 sumOfPosition = Vector3.zero;

            for (int i = 0; i < hitPoints.Count; i++)
            {
                sumOfPosition += hitPoints[i];
            }

            _averagePosition = sumOfPosition / hitPoints.Count;

            Vector3 bladeLength = _bladeDirectionReference.right.normalized;
            Vector3 bladeHeight = _bladeDirectionReference.up.normalized;
            _averageDirection = Vector3.Cross(bladeLength, bladeHeight).normalized;

            if (_averageDirection == Vector3.zero)
            {
                Debug.LogWarning("WoodSlicer: Invalid cut direction.");
                return;
            }

            _lastCutPosition = _averagePosition;
            _lastCutNormal = _averageDirection;
            _lastBladeForward = bladeLength;
            _lastBladeUp = bladeHeight;
            _lastSawDirection = sawDirection;
            _hasDebugCut = true;

            Debug.Log(
                $"CUT DEBUG | hit count: {hitPoints.Count} | " +
                $"cut pos: {_averagePosition} | " +
                $"cut normal: {_averageDirection} | " +
                $"blade forward: {bladeLength} | " +
                $"blade up: {bladeHeight} | " +
                $"old saw direction: {sawDirection}"
            );

            SlicedHull hull = wood.Slice(_averagePosition, _averageDirection);
            originalWoodLayer = wood.layer;

            if (hull == null)
            {
                Debug.LogWarning("Slice failed - no valid hull returned.");
                return;
            }

            WoodPaintSurface originalSurface = wood.GetComponent<WoodPaintSurface>();
            RenderTexture originalPaint = originalSurface?.PaintTexture;

            GameObject upperPiece = hull.CreateUpperHull(wood, _crossSectionMaterial);
            GameObject lowerPiece = hull.CreateLowerHull(wood, _crossSectionMaterial);

            SetupPiece(upperPiece, wood, originalPaint);
            SetupPiece(lowerPiece, wood, originalPaint);

            Destroy(wood);
        }

        private void SetupPiece(GameObject piece, GameObject originalWood, RenderTexture originalPaint)
        {
            if (piece == null)
                return;

            if (_recenterPiecePivot)
                RecenterMeshPivot(piece);

            MeshCollider col = piece.AddComponent<MeshCollider>();
            col.convex = true;

            Rigidbody rb = piece.AddComponent<Rigidbody>();
            rb.useGravity = true;

            piece.AddComponent<XRGrabInteractable>().attachTransform = null;
            piece.layer = originalWoodLayer;

            AddCutPieceGameplayComponents(piece, originalWood);

            WoodPaintSurface woodSurface = piece.AddComponent<WoodPaintSurface>();
            woodSurface.CopyPaintFrom(originalPaint);
        }

        private void RecenterMeshPivot(GameObject piece)
        {
            MeshFilter meshFilter = piece.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.mesh == null)
                return;

            Mesh mesh = meshFilter.mesh;
            Vector3 localCenter = mesh.bounds.center;
            if (localCenter == Vector3.zero)
                return;

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] -= localCenter;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            piece.transform.position += piece.transform.TransformVector(localCenter);
        }

        private void AddCutPieceGameplayComponents(GameObject piece, GameObject originalWood)
        {
            if (piece.GetComponent<StructuralPiece>() == null)
                piece.AddComponent<StructuralPiece>();

            if (piece.GetComponent<CollisionAudio>() == null)
            {
                CollisionAudio collisionAudio = piece.AddComponent<CollisionAudio>();
                CollisionAudio originalCollisionAudio = originalWood != null
                    ? originalWood.GetComponent<CollisionAudio>()
                    : null;

                if (originalCollisionAudio != null)
                {
                    JsonUtility.FromJsonOverwrite(
                        JsonUtility.ToJson(originalCollisionAudio),
                        collisionAudio);
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!_drawDebug || !_hasDebugCut)
                return;

            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_lastCutPosition, 0.025f);
            Gizmos.DrawRay(_lastCutPosition, _lastCutNormal * _debugRayLength);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_lastCutPosition, _lastBladeForward * _debugRayLength);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(_lastCutPosition, _lastBladeUp * _debugRayLength);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(_lastCutPosition, _lastSawDirection.normalized * _debugRayLength);
        }
    }
}
