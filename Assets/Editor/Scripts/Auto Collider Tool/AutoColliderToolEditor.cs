using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    [CustomEditor(typeof(AutoColliderToolSO))]
    public class AutoColliderToolEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            // 1. Create a container to hold our UI elements
            VisualElement root = new VisualElement();

            // 2. Automatically draw default fields of the ScriptableObject
            // This ensures if you add variables to the tool later, they show up here.
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            // Add some space before our button
            root.style.marginTop = 15;

            // 3. Create the custom button
            Button generateButton = new Button();
            generateButton.text = "Generate Collider for Selected";
            
            // 4. Style the button directly via C# code (USS rules)
            generateButton.style.height = 40;
            generateButton.style.fontSize = 14;
            generateButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            generateButton.style.backgroundColor = new Color(0.2f, 0.4f, 0.7f); // Sleek Unity blue
            generateButton.style.color = Color.white;

            // 5. Connect the button click to our ScriptableObject's method
            AutoColliderToolSO tool = (AutoColliderToolSO)target;
            generateButton.clicked += () => 
            {
                tool.GenerateCollidersForChildrenOfSelectedObject();
            };

            // 6. Inject the button into the UI layout
            root.Add(generateButton);

            return root;
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
