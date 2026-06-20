using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    [CreateAssetMenu(fileName = "AutoColliderTool", menuName = "Dumyat/Tools/AutoColliderTool")]
    public class AutoColliderToolSO : ScriptableObject
    {
        public void GenerateColliderForSelected()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObject(out GameObject selectedObj)) return;
            if (!ColliderFactory.TryGetVertices(selectedObj, out Vector3[] vertices)) return;

            // 1. Calculate dimensions using our pure math class
            var (size, center) = GeometryCalculator.CalculateLocalBounds(vertices);

            // 2. Generate and apply using our component factory
            BoxCollider boxCollider = ColliderFactory.GetOrCreateBoxCollider(selectedObj);
            ColliderFactory.ApplyDimensions(boxCollider, size, center);

            Debug.Log($"Successfully auto-fitted BoxCollider to {selectedObj.name}!", selectedObj);
        }
    }

    public static class ObjectSelectionHelper
    {
        public static bool TryGetSelectedObject(out GameObject selectedObj)
        {
            selectedObj = Selection.activeGameObject;
            if (selectedObj != null) return true;

            Debug.LogWarning("Auto Collider: Please select a GameObject in the scene first!");
            return false;
        }
    }

    public static class GeometryCalculator
    {
        public static (Vector3 size, Vector3 center) CalculateLocalBounds(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0)
                return (Vector3.zero, Vector3.zero);

            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }

            Vector3 size = max - min;
            Vector3 center = (min + max) / 2f;

            return (size, center);
        }
    }

    public static class ColliderFactory
    {
        public static bool TryGetVertices(GameObject target, out Vector3[] vertices)
        {
            vertices = null;
            if (target == null) return false;

            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"Auto Collider: {target.name} lacks a valid MeshFilter.", target);
                return false;
            }

            vertices = meshFilter.sharedMesh.vertices;
            return vertices != null && vertices.Length > 0;
        }

        public static BoxCollider GetOrCreateBoxCollider(GameObject target)
        {
            if (target.TryGetComponent<BoxCollider>(out var existingCollider))
            {
                Undo.RecordObject(existingCollider, "Auto Fit Box Collider");
                return existingCollider;
            }

            return Undo.AddComponent<BoxCollider>(target);
        }

        public static void ApplyDimensions(BoxCollider collider, Vector3 size, Vector3 center)
        {
            collider.size = size;
            collider.center = center;
        }
    }
}