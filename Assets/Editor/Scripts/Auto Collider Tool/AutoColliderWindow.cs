using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    public class AutoColliderWindow : EditorWindow
    {
        [MenuItem("Dumyat/Auto Collider Tool")]
        public static void OpenWindow()
        {
            AutoColliderWindow window = GetWindow<AutoColliderWindow>();
            window.titleContent = new GUIContent("Auto Collider");
            window.minSize = new Vector2(250, 200);
            window.Show();
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingTop = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;

            // --- Header ---
            Label header = new Label("Auto Collider Tool");
            header.style.fontSize = 16;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 10;
            root.Add(header);

            // --- Buttons ---
            root.Add(MakeButton("Fit Selected Object", GenerateForSelected));
            root.Add(MakeButton("Fit Batch Selected Objects", GenerateForBatch));
            root.Add(MakeButton("Fit Batch Selected Objects (Children Included)",   GenerateForBatchChildrenIncluded));

            root.Add(MakeRedButton("Delete Colliders on Selected", DeleteCollidersOnSelected));
        }

        private Button MakeButton(string label, System.Action action)
        {
            Button btn = new Button(action);
            btn.text = label;
            btn.style.height = 40;
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = new Color(0.2f, 0.4f, 0.7f);
            btn.style.color = Color.white;
            btn.style.marginBottom = 6;
            return btn;
        }

        private Button MakeRedButton(string label, System.Action action)
        {
            Button btn = new Button(action);
            btn.text = label;
            btn.style.height = 40;
            btn.style.fontSize = 13;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            btn.style.color = Color.white;
            btn.style.marginBottom = 6;
            return btn;
        }

        // --- Logic (same as before, just moved here) ---

        private void GenerateForSelected()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObject(out GameObject selectedObj)) return;
            if (!ColliderFactory.TryGetMeshFilter(selectedObj, out MeshFilter meshFilter)) return;
            if (!ColliderFactory.TryGetVertices(meshFilter, out Vector3[] vertices)) return;

            var (size, center) = GeometryCalculator.CalculateLocalBounds(vertices);
            BoxCollider boxCollider = ColliderFactory.GetOrCreateBoxCollider(selectedObj);
            ColliderFactory.ApplyDimensions(boxCollider, size, center);

            Debug.Log($"Auto-fitted BoxCollider to {selectedObj.name}!", selectedObj);
        }

        private void GenerateForBatchChildrenIncluded()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObjects(out GameObject[] selectedObjects)) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Batch Auto-Fit Colliders (Children Included)");
            int group = Undo.GetCurrentGroup();

            foreach (var obj in selectedObjects)
            {
                if (!ColliderFactory.TryGetMultipleMeshFilters(obj, out MeshFilter[] meshFilters)) continue;

                foreach (var meshFilter in meshFilters)
                {
                    if (!ColliderFactory.TryGetVertices(meshFilter, out Vector3[] vertices)) continue;
                    var (size, center) = GeometryCalculator.CalculateLocalBounds(vertices);
                    BoxCollider boxCollider = ColliderFactory.GetOrCreateBoxCollider(meshFilter.gameObject);
                    ColliderFactory.ApplyDimensions(boxCollider, size, center);
                    Debug.Log($"Auto-fitted BoxCollider to {meshFilter.gameObject.name}!", meshFilter.gameObject);
                }
            }

            Undo.CollapseUndoOperations(group);
        }

        private void GenerateForChildren()
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

        private void GenerateForBatch()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObjects(out GameObject[] selectedObjects)) return;

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
                Debug.Log($"Auto-fitted BoxCollider to {obj.name}!", obj);
            }

            Undo.CollapseUndoOperations(group);
        }

        private void DeleteCollidersOnSelected()
        {
            if (!ObjectSelectionHelper.TryGetSelectedObjects(out GameObject[] selectedObjects)) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Delete Colliders on Selected");
            int group = Undo.GetCurrentGroup();

            int deletedCount = 0;

            foreach (var obj in selectedObjects)
            {
                BoxCollider[] colliders = obj.GetComponentsInChildren<BoxCollider>();
                foreach (var collider in colliders)
                {
                    Undo.DestroyObjectImmediate(collider);
                    deletedCount++;
                }
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Deleted {deletedCount} BoxCollider(s) from selected and their children.");
        }
    }
}