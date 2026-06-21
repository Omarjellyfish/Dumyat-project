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

        public void GenerateCollidersForChildrenOfSelectedObject()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObject(out GameObject selectedObj)) return;
            if (!ColliderFactory.TryGetMultipleMeshFilters(selectedObj, out MeshFilter[] meshFilters)) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Auto-Fit Colliders for Children");
            int group = Undo.GetCurrentGroup();

            foreach (var meshFilter in meshFilters)
            {
                if (!ColliderFactory.TryGetVertices(meshFilter, out Vector3[] vertices)) continue;

                var (size, center) = GeometryCalculator.CalculateLocalBounds(vertices);

                BoxCollider boxCollider = ColliderFactory.GetOrCreateBoxCollider(meshFilter.gameObject);
                ColliderFactory.ApplyDimensions(boxCollider, size, center);

                Debug.Log($"Auto-fitted BoxCollider to {meshFilter.gameObject.name}!", meshFilter.gameObject);
            }

            Undo.CollapseUndoOperations(group);
        }   
    }
}