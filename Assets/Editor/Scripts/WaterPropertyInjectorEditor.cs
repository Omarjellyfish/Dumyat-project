using DumyatRuntime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    [CustomEditor(typeof(WaterPropertyInjector))]
    public class WaterPropertyInjectorEditor : Editor
    {
        // Cache the editors so they don't recreate every frame
        private Editor _riverEditor;
        private Editor _oceanEditor;
        private Editor _barzakhEditor;

        private void OnDisable()
        {
            // Proper memory cleanup when inspector loses focus
            CleanUpEditors();
        }

        private void OnDestroy()
        {
            CleanUpEditors();
        }

        private void CleanUpEditors()
        {
            if (_riverEditor != null) DestroyImmediate(_riverEditor);
            if (_oceanEditor != null) DestroyImmediate(_oceanEditor);
            if (_barzakhEditor != null) DestroyImmediate(_barzakhEditor);
        }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            // 1. Render the standard material slot fields
            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            WaterPropertyInjector script = (WaterPropertyInjector)target;

            // 3. Side-by-Side Source Previews (River & Ocean)
            VisualElement sourceRow = new VisualElement();
            sourceRow.style.flexDirection = FlexDirection.Row;
            sourceRow.style.justifyContent = Justify.SpaceBetween;
            sourceRow.style.marginTop = 15;

            // River Column
            if (script.riverMaterial != null)
            {
                sourceRow.Add(CreatePreviewColumn("River Material", script.riverMaterial, 140, ref _riverEditor));
            }

            // Ocean Column
            if (script.oceanMaterial != null)
            {
                sourceRow.Add(CreatePreviewColumn("Ocean Material", script.oceanMaterial, 140, ref _oceanEditor));
            }

            root.Add(sourceRow);

            root.Add(new SpacerElement(15));

            // 2. Action Sync Button
            Button syncButton = new Button(() =>
            {
                script.SyncAll();
                if (script.barzakhMaterial != null)
                {
                    EditorUtility.SetDirty(script.barzakhMaterial);
                }
            });
            syncButton.text = "Sync Materials Now";
            syncButton.style.height = 35;
            syncButton.style.backgroundColor = new Color(0.12f, 0.58f, 0.95f);
            syncButton.style.color = Color.white;
            syncButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(syncButton);

            

            // 4. Main Barzakh Blend Preview (Large)
            // if (script.barzakhMaterial != null)
            // {
            //     root.Add(new SpacerElement(20));
            //     VisualElement barzakhBox = CreatePreviewColumn("Combined Barzakh Material", script.barzakhMaterial, 220, ref _barzakhEditor);
            //     root.Add(barzakhBox);
            // }

            return root;
        }

        // Helper function passing the cached editor reference by 'ref'
        private VisualElement CreatePreviewColumn(string headerText, Material mat, float height, ref Editor cachedEditor)
        {
            VisualElement container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.marginRight = 4;
            container.style.marginLeft = 4;

            Label label = new Label(headerText);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            container.Add(label);

            // Local copy for the IMGUI lambda loop capture closure
            Editor targetEditor = cachedEditor; 
            
            IMGUIContainer imguiContainer = new IMGUIContainer(() =>
            {
                if (mat == null) return;

                // Only instantiate if it doesn't exist yet
                if (targetEditor == null || targetEditor.target != mat)
                {
                    if (targetEditor != null) DestroyImmediate(targetEditor);
                    targetEditor = CreateEditor(mat);
                }

                if (targetEditor != null)
                {
                    // Draw Interactive Frame
                    targetEditor.DrawPreview(GUILayoutUtility.GetRect(height, height));
                    
                    // CRITICAL FOR ANIMATION: Force the preview area to redraw every frame smoothly
                    targetEditor.Repaint(); 
                }
            });

            // Write back the initialized editor reference to our persistent cache variable
            cachedEditor = targetEditor;

            imguiContainer.style.height = height;
            container.Add(imguiContainer);

            return container;
        }
    }

    public class SpacerElement : VisualElement
    {
        public SpacerElement(float height)
        {
            style.height = height;
        }
    }
}