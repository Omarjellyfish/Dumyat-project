using DumyatRuntime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    [CustomEditor(typeof(VertexPainterPropertyInjector))]
    public class VertexPainterPropertyInjectorEditor : Editor
    {
        // Cache the editors so they don't recreate every frame
        private Editor _matAEditor;
        private Editor _matBEditor;
        private Editor _matCEditor;
        private Editor _matDEditor;
        private Editor _matEEditor;

        private void OnDisable()
        {
            CleanUpEditors();
        }

        private void OnDestroy()
        {
            CleanUpEditors();
        }

        private void CleanUpEditors()
        {
            if (_matAEditor != null) DestroyImmediate(_matAEditor);
            if (_matBEditor != null) DestroyImmediate(_matBEditor);
            if (_matCEditor != null) DestroyImmediate(_matCEditor);
            if (_matDEditor != null) DestroyImmediate(_matDEditor);
            if (_matEEditor != null) DestroyImmediate(_matEEditor);
        }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            // 1. Render the standard material slot fields and enum toggles
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            VertexPainterPropertyInjector script = (VertexPainterPropertyInjector)target;

            // Extract the fields from the default inspector layout by their property binding paths
            PropertyField enumField = root.Q<PropertyField>("PropertyField:_materialCount");
            PropertyField matAField = root.Q<PropertyField>("PropertyField:_materialA");
            PropertyField matBField = root.Q<PropertyField>("PropertyField:_materialB");
            PropertyField matCField = root.Q<PropertyField>("PropertyField:_materialC");
            PropertyField matDField = root.Q<PropertyField>("PropertyField:_materialD");
            PropertyField matEField = root.Q<PropertyField>("PropertyField:_materialE");

            // Get material assignments via serialized properties for the preview loop
            SerializedProperty matA = serializedObject.FindProperty("_materialA");
            SerializedProperty matB = serializedObject.FindProperty("_materialB");
            SerializedProperty matC = serializedObject.FindProperty("_materialC");
            SerializedProperty matD = serializedObject.FindProperty("_materialD");
            SerializedProperty matE = serializedObject.FindProperty("_materialE");
            SerializedProperty targetMat = serializedObject.FindProperty("_targetBlendMaterial");

            // 2. Source Material Previews Row Container
            VisualElement sourceContainer = new VisualElement();
            sourceContainer.style.flexDirection = FlexDirection.Row;
            sourceContainer.style.flexWrap = Wrap.Wrap;
            sourceContainer.style.justifyContent = Justify.FlexStart;
            sourceContainer.style.marginTop = 15;
            root.Add(sourceContainer);

            float previewSize = 100f;

            // Local layout refresh function
            System.Action updateVisibilityAndPreviews = () =>
            {
                // Get current enum value safely from serialized state
                int materialCountValue = serializedObject.FindProperty("_materialCount").enumValueIndex;

                // Toggle visibility of fields based on index (0 = Two, 1 = Three, 2 = Four, 3 = Five)
                if (matAField != null) matAField.style.display = DisplayStyle.Flex; // Always visible
                if (matBField != null) matBField.style.display = DisplayStyle.Flex; // Always visible
                if (matCField != null) matCField.style.display = (materialCountValue >= 1) ? DisplayStyle.Flex : DisplayStyle.None;
                if (matDField != null) matDField.style.display = (materialCountValue >= 2) ? DisplayStyle.Flex : DisplayStyle.None;
                if (matEField != null) matEField.style.display = (materialCountValue >= 3) ? DisplayStyle.Flex : DisplayStyle.None;

                // Clear dynamic previews and rebuild them according to active slots
                sourceContainer.Clear();

                if (matA.objectReferenceValue != null)
                    sourceContainer.Add(CreatePreviewColumn("Mat 1", (Material)matA.objectReferenceValue, previewSize, ref _matAEditor));
                
                if (matB.objectReferenceValue != null)
                    sourceContainer.Add(CreatePreviewColumn("Mat 2", (Material)matB.objectReferenceValue, previewSize, ref _matBEditor));
                
                if (matC.objectReferenceValue != null && materialCountValue >= 1)
                    sourceContainer.Add(CreatePreviewColumn("Mat 3", (Material)matC.objectReferenceValue, previewSize, ref _matCEditor));
                
                if (matD.objectReferenceValue != null && materialCountValue >= 2)
                    sourceContainer.Add(CreatePreviewColumn("Mat 4", (Material)matD.objectReferenceValue, previewSize, ref _matDEditor));
                
                if (matE.objectReferenceValue != null && materialCountValue >= 3)
                    sourceContainer.Add(CreatePreviewColumn("Mat 5", (Material)matE.objectReferenceValue, previewSize, ref _matEEditor));
            };

            // Register value change tracking to trigger UI toggle updates reactively
            if (enumField != null)
            {
                enumField.RegisterValueChangeCallback(evt =>
                {
                    serializedObject.ApplyModifiedProperties();
                    updateVisibilityAndPreviews();
                });
            }

            // Run initial visibility configuration layout pass
            updateVisibilityAndPreviews();

            root.Add(new SpacerElement(15));

            // 3. Action Sync Button
            Button syncButton = new Button(() =>
            {
                script.SyncAll();
                if (targetMat.objectReferenceValue != null)
                {
                    EditorUtility.SetDirty(targetMat.objectReferenceValue);
                }
            });
            
            syncButton.text = "Sync Material Properties Now";
            syncButton.style.height = 35;
            syncButton.style.backgroundColor = new Color(0.12f, 0.58f, 0.95f);
            syncButton.style.color = Color.white;
            syncButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(syncButton);

            return root;
        }

        private VisualElement CreatePreviewColumn(string headerText, Material mat, float height, ref Editor cachedEditor)
        {
            VisualElement container = new VisualElement();
            container.style.marginRight = 6;
            container.style.marginLeft = 6;
            container.style.marginBottom = 10;

            Label label = new Label(headerText);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            container.Add(label);

            Editor targetEditor = cachedEditor; 
            
            IMGUIContainer imguiContainer = new IMGUIContainer(() =>
            {
                if (mat == null) return;

                if (targetEditor == null || targetEditor.target != mat)
                {
                    if (targetEditor != null) DestroyImmediate(targetEditor);
                    targetEditor = CreateEditor(mat);
                }

                if (targetEditor != null)
                {
                    targetEditor.DrawPreview(GUILayoutUtility.GetRect(height, height));
                    targetEditor.Repaint(); 
                }
            });

            cachedEditor = targetEditor;

            imguiContainer.style.height = height;
            imguiContainer.style.width = height;
            container.Add(imguiContainer);

            return container;
        }
    }
}