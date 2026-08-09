using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Project.Presentation.UI;

namespace Project.Presentation.Editor
{
    public class PresentationUIShowcaseGenerator
    {
        private const string SourceFolders = "Assets/Runtime/Prefabs";
        private const string TargetUIFolder = "Assets/Presentation Resources/UI Showcase";

        [MenuItem("Tools/Presentation/Generate UI Showcase Variants")]
        public static void GenerateVariants()
        {
            if (!Directory.Exists(TargetUIFolder))
            {
                Directory.CreateDirectory(TargetUIFolder);
                AssetDatabase.Refresh();
            }

            // Find all prefabs in the source folders
            string[] sourcePrefabs = Directory.GetFiles(SourceFolders, "*.prefab", SearchOption.AllDirectories);

            int generatedCount = 0;

            foreach (string sourcePath in sourcePrefabs)
            {
                string formattedSourcePath = sourcePath.Replace('\\', '/');
                
                // We only want to duplicate prefabs that actually contain a UIDocument.
                // We can check by loading it.
                using (var prefabScope = new PrefabContentsScope(formattedSourcePath))
                {
                    GameObject prefabRoot = prefabScope.PrefabRoot;
                    if (prefabRoot == null) continue;

                    UIDocument uiDoc = prefabRoot.GetComponentInChildren<UIDocument>(true);
                    
                    // If it doesn't have a UIDocument, or it's a manager, we skip it
                    if (uiDoc == null || prefabRoot.name.Contains("Manager"))
                    {
                        continue;
                    }
                }

                string fileName = Path.GetFileName(formattedSourcePath);
                string targetPath = $"{TargetUIFolder}/{fileName}";

                // Copy asset
                AssetDatabase.CopyAsset(formattedSourcePath, targetPath);

                // Load the copied prefab to strip components
                GameObject showcasePrefab = PrefabUtility.LoadPrefabContents(targetPath);
                if (showcasePrefab == null)
                {
                    Debug.LogWarning($"Failed to load prefab contents for: {targetPath}");
                    continue;
                }

                // Remove all components except Transform, RectTransform, GameObject, and UIDocument
                Component[] components = showcasePrefab.GetComponentsInChildren<Component>(true);
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    Component comp = components[i];
                    if (comp == null) continue;

                    // Keep Transform, RectTransform, and UIDocument
                    if (comp is Transform || comp is RectTransform || comp is UIDocument)
                    {
                        continue;
                    }

                    // Destroy the component (e.g. logic scripts, data saving components)
                    Object.DestroyImmediate(comp, true);
                }

                // Add PresentationUIPopulator to give dummy items for Showcase
                var populator = showcasePrefab.AddComponent<PresentationUIPopulator>();
                
                // Configure standard populate rules based on prefab name
                if (fileName.Contains("Inventory"))
                {
                    populator.Rules.Add(new PresentationUIPopulator.PopulationRule 
                    { 
                        ContainerName = "SlotContainer", 
                        ItemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Runtime/UI Toolkit/Inventory/InventorySlot.uxml"),
                        Count = 12
                    });
                }
                else if (fileName.Contains("Achievement"))
                {
                    populator.Rules.Add(new PresentationUIPopulator.PopulationRule 
                    { 
                        ContainerName = "AchievementsGrid", 
                        ItemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Runtime/UI Toolkit/Achievements/AchievementItem.uxml"),
                        Count = 8
                    });
                }
                else if (fileName.Contains("WristQuest"))
                {
                    populator.Rules.Add(new PresentationUIPopulator.PopulationRule 
                    { 
                        ContainerName = "QuestListContainer", 
                        ItemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Runtime/UI Toolkit/Quests/WristQuestItem.uxml"),
                        Count = 3
                    });
                }
                else if (fileName.Contains("CraftingUI"))
                {
                    populator.Rules.Add(new PresentationUIPopulator.PopulationRule 
                    { 
                        ContainerName = "BlueprintIndex", 
                        ItemTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Runtime/UI Toolkit/Crafting/DiagnosticItem.uxml"),
                        Count = 6
                    });
                }
                else if (fileName.Contains("Tutorial"))
                {
                    populator.ClassesToRemove.Add("tutorial-panel--hidden");
                }

                // Save and unload
                PrefabUtility.SaveAsPrefabAsset(showcasePrefab, targetPath);
                PrefabUtility.UnloadPrefabContents(showcasePrefab);

                Debug.Log($"Generated showcase variant for: {fileName}");
                generatedCount++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"UI Showcase generation complete! Generated {generatedCount} UI variants.");
        }

        // Helper to load and auto-unload prefab contents safely
        private struct PrefabContentsScope : System.IDisposable
        {
            public GameObject PrefabRoot { get; private set; }
            private string _path;

            public PrefabContentsScope(string path)
            {
                _path = path;
                PrefabRoot = PrefabUtility.LoadPrefabContents(path);
            }

            public void Dispose()
            {
                if (PrefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(PrefabRoot);
                }
            }
        }
    }
}
