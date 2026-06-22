using UnityEngine;
using System.Collections.Generic;
using EzySlice;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System;
namespace DumyatRuntime
{
    public class WoodSlicer : MonoBehaviour
    {
        [SerializeField] private Material _crossSectionMaterial;

        Vector3 _averagePosition;
        Vector3 _averageDirection;
        int originalWoodLayer;

        public void PerformCut(List<Vector3> _hitPoint, Vector3 sawDirection, GameObject wood)
        {
            Vector3 _sumOfPosition = new Vector3();

            for (int i = 0; i < _hitPoint.Count; i++)
            {
                _sumOfPosition += _hitPoint[i];
            }

            _averagePosition = _sumOfPosition / _hitPoint.Count;
            Vector3 bladeDirection = transform.up;
            _averageDirection = Vector3.Cross(sawDirection, bladeDirection).normalized;
            SlicedHull hull = wood.Slice(_averagePosition, _averageDirection);
            originalWoodLayer = wood.layer;
            if (hull == null)
            {
                Debug.LogWarning("Slice failed - no valid hull returned");
                return;
            }

            WoodPaintSurface originalSurface = wood.GetComponent<WoodPaintSurface>();
            RenderTexture originalPaint = originalSurface?.PaintTexture;

            GameObject upperPiece = hull.CreateUpperHull(wood, _crossSectionMaterial);
            GameObject lowerPiece = hull.CreateLowerHull(wood, _crossSectionMaterial);

            SetupPiece(upperPiece, originalPaint);
            SetupPiece(lowerPiece, originalPaint);

            Destroy(wood);
        }
        private void SetupPiece(GameObject piece, RenderTexture originalPaint)
        {
            if (piece == null) return;

            MeshCollider col = piece.AddComponent<MeshCollider>();
            col.convex = true;

            Rigidbody rb = piece.AddComponent<Rigidbody>();
            rb.useGravity = true;

            piece.AddComponent<XRGrabInteractable>();
            piece.layer = originalWoodLayer;

            WoodPaintSurface woodSurface = piece.AddComponent<WoodPaintSurface>();

            woodSurface.CopyPaintFrom(originalPaint);
        }
    }
}