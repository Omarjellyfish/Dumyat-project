using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public class DecalPainter : MonoBehaviour
    {
        [SerializeField] private GameObject _decalPrefab;
        [SerializeField] private int _maxDecals = 300;
        [SerializeField] private float _decalOffsetDistance = 0.002f;
        private Queue<GameObject> _activeDecals = new Queue<GameObject>();

        public void SpawnDecal(Vector3 hitPosition, Vector3 surfaceNormal)
        {
            Vector3 offsetPosition = hitPosition + surfaceNormal * _decalOffsetDistance;
            GameObject decal = Instantiate(_decalPrefab, offsetPosition, Quaternion.LookRotation(-surfaceNormal));
            _activeDecals.Enqueue(decal);

            if (_activeDecals.Count > _maxDecals)
            {
                GameObject oldest = _activeDecals.Dequeue();
                Destroy(oldest);
            }
        }

        public void ClearAllDecals()
        {
            foreach (var decal in _activeDecals)
                Destroy(decal);

            _activeDecals.Clear();
        }
    }
}