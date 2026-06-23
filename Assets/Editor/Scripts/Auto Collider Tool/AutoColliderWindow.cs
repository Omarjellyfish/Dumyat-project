using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    public class AutoColliderWindow : EditorWindow
    {
        private bool _displayColliders = false;

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
            root.Add(MakeButton("Fit Batch Selected Objects (Children Included)", GenerateForBatchChildrenIncluded));

            root.Add(MakeRedButton("Delete Colliders on Selected", DeleteCollidersOnSelected));

            // --- Divider ---
            MakeDivider(root);

            // --- Display Colliders Toggle ---
            root.Add(MakeDisplayToggle("Display Colliders", _displayColliders, OnDisplayCollidersToggled));
        }

        private Toggle MakeDisplayToggle(string label, bool value = false, System.Action<bool> OnToggleChanged = null)
        {
            Toggle displayToggle = new Toggle(label);
            displayToggle.value = value;
            displayToggle.style.marginBottom = 6;
            displayToggle.RegisterValueChangedCallback(evt =>
            {
                _displayColliders = evt.newValue;
                OnToggleChanged?.Invoke(_displayColliders);
            });
            return displayToggle;
        }

        private static void MakeDivider(VisualElement root)
        {
            VisualElement divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
            divider.style.marginTop = 8;
            divider.style.marginBottom = 8;
            root.Add(divider);
        }

        private void OnDisplayCollidersToggled(bool isOn)
        {
            ColliderGizmoDrawer.DisplayColliders(isOn);
            SceneView.RepaintAll();
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

        // --- Logic ---

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