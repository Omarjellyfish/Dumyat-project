using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    [CreateAssetMenu(fileName = "AutoColliderTool", menuName = "Dumyat/Tools/AutoColliderTool")]
    public class AutoColliderToolSO : ScriptableObject
    {
        public void GenerateColliderForOneSelectedObject()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObject(out GameObject selectedObj)) return;
            if (!ColliderFactory.TryGetMeshFilter(selectedObj, out MeshFilter meshFilter)) return;
            if (!ColliderFactory.TryGetVertices(meshFilter, out Vector3[] vertices)) return;

            // 1. Calculate dimensions using our pure math class
            var (size, center) = GeometryCalculator.CalculateLocalBounds(vertices);

            // 2. Generate and apply using our component factory
            BoxCollider boxCollider = ColliderFactory.GetOrCreateBoxCollider(selectedObj);
            ColliderFactory.ApplyDimensions(boxCollider, size, center);

            Debug.Log($"Successfully auto-fitted BoxCollider to {selectedObj.name}!", selectedObj);
        }

        public void GenerateCollidersForMultipleSelectedObjects()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObjects(out GameObject[] selectedObjects)) return;

            // Wrap the batch in a single Undo group so one Ctrl+Z undoes the whole batch
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Batch Auto-Fit Colliders");
            int group = Undo.GetCurrentGroup();

            foreach (var obj in selectedObjects)
            {
                if (!ColliderFactory.TryGetMeshFilter(obj, out MeshFilter meshFilter)) continue;
                if (!ColliderFactory.TryGetVertices(meshFilter, out Vector3[] vertices)) continue;

                var (size, center) = GeometryCalculator.CalculateLocalBounds(vertices);

                BoxCollider boxCollider = ColliderFactory.GetOrCreateBoxCollider(obj);
                ColliderFactory.ApplyDimensions(boxCollider, size, center);

                Debug.Log($"Successfully auto-fitted BoxCollider to {obj.name}!", obj);
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Successfully auto-fitted BoxColliders to {selectedObjects.Length} objects.");
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

        // Returns the full array of selected objects
        public static bool TryGetSelectedObjects(out GameObject[] selectedObjects)
        {
            selectedObjects = Selection.gameObjects;
            if (selectedObjects != null && selectedObjects.Length != 0)   return true;

            Debug.LogWarning("Auto Collider: Please select one or more GameObjects in the scene first!");
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
        public static bool TryGetMeshFilter(GameObject target, out MeshFilter meshFilter)
        {
            meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                return true;

            Debug.LogWarning($"Auto Collider: {target.name} lacks a valid MeshFilter with a mesh.", target);
            return false;
        }

        public static bool TryGetMultipleMeshFilters(GameObject target, out MeshFilter[] meshFilters)
        {
            meshFilters = target.GetComponentsInChildren<MeshFilter>();
            if (meshFilters != null && meshFilters.Length > 0)
                return true;

            Debug.LogWarning($"Auto Collider: {target.name} and its children lack valid MeshFilters with meshes.", target);
            return false;
        }

        public static bool TryGetVertices(MeshFilter meshFilter, out Vector3[] vertices)
        {
            vertices = null;
            if (meshFilter == null)
            {
                Debug.LogWarning($"Auto Collider: Provided MeshFilter is null.", meshFilter);
                return false;
            }

            if (meshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"Auto Collider: MeshFilter does not have a mesh assigned.", meshFilter.gameObject);
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