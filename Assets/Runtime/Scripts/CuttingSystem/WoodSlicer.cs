using UnityEngine;
using System.Collections.Generic;
using EzySlice;

namespace DumyatRuntime
{
    public class WoodSlicer : MonoBehaviour
    {
        [SerializeField] private Material _crossSectionMaterial;

        Vector3 _averagePosition;
        Vector3 _averageDirection;

        public void PerformCut(List<Vector3> _hitPoint,Vector3 sawDirection, GameObject wood)
        {
            Vector3 _sumOfPosition = new Vector3();
            //Vector3 _sumOfDirection = new Vector3();

            for (int i = 0; i < _hitPoint.Count; i++)
            {
                _sumOfPosition += _hitPoint[i];
            }

            // _averageDirection = (_sumOfDirection / _hitNormal.Count).normalized;
            _averagePosition = _sumOfPosition / _hitPoint.Count;
            Vector3 bladeDirection = transform.up; // blade tip to base direction
            _averageDirection = Vector3.Cross(sawDirection, bladeDirection).normalized;
            SlicedHull hull = wood.Slice(_averagePosition, _averageDirection);

            if (hull == null)
            {
                Debug.LogWarning("Slice failed - no valid hull returned");
                return;
            }

            GameObject upperPiece = hull.CreateUpperHull(wood, _crossSectionMaterial);
            GameObject lowerPiece = hull.CreateLowerHull(wood, _crossSectionMaterial);

            SetupPiece(upperPiece);
            SetupPiece(lowerPiece);

            Destroy(wood);
        }

        private void SetupPiece(GameObject piece)
        {
            if (piece == null) return;

            MeshCollider collider = piece.AddComponent<MeshCollider>();
            collider.convex = true;

            Rigidbody rb = piece.AddComponent<Rigidbody>();
            rb.useGravity = true;
        }
    }
}