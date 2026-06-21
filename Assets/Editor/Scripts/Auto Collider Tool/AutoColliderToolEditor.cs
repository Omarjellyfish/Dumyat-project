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
                tool.GenerateCollidersForMultipleSelectedObjects();
            };

            // 6. Inject the button into the UI layout
            root.Add(generateButton);

            return root;
        }
    }
}
