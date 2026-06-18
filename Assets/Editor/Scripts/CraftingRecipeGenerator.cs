using UnityEngine;
using UnityEditor;
using DumyatRuntime;

namespace DumyatRuntime.Editor
{
    public class CraftingRecipeGenerator
    {
        [MenuItem("Dumyat/Crafting/Generate Recipe: Wooden Chair")]
        public static void GenerateWoodenChairRecipe()
        {
            // Create the SO instance
            CraftingRecipe recipe = ScriptableObject.CreateInstance<CraftingRecipe>();

            // Common leg values
            Vector3 legExpectedSize = new Vector3(0.05f, 0.45f, 0.05f);
            Vector3 legSizeTolerance = new Vector3(0.02f, 0.1f, 0.02f);
            Quaternion legExpectedRot = Quaternion.identity;
            float legPosTolerance = 0.1f;
            float legRotTolerance = 15f;

            PartBlueprint[] parts = new PartBlueprint[]
            {
                new PartBlueprint
                {
                    SlotName = "Front Left Leg",
                    ExpectedLocalPosition = new Vector3(-0.2f, -0.25f, 0.2f),
                    ExpectedLocalRotation = legExpectedRot,
                    PositionTolerance = legPosTolerance,
                    RotationTolerance = legRotTolerance,
                    ExpectedSize = legExpectedSize,
                    SizeTolerance = legSizeTolerance
                },
                new PartBlueprint
                {
                    SlotName = "Front Right Leg",
                    ExpectedLocalPosition = new Vector3(0.2f, -0.25f, 0.2f),
                    ExpectedLocalRotation = legExpectedRot,
                    PositionTolerance = legPosTolerance,
                    RotationTolerance = legRotTolerance,
                    ExpectedSize = legExpectedSize,
                    SizeTolerance = legSizeTolerance
                },
                new PartBlueprint
                {
                    SlotName = "Back Left Leg",
                    ExpectedLocalPosition = new Vector3(-0.2f, -0.25f, -0.2f),
                    ExpectedLocalRotation = legExpectedRot,
                    PositionTolerance = legPosTolerance,
                    RotationTolerance = legRotTolerance,
                    ExpectedSize = legExpectedSize,
                    SizeTolerance = legSizeTolerance
                },
                new PartBlueprint
                {
                    SlotName = "Back Right Leg",
                    ExpectedLocalPosition = new Vector3(0.2f, -0.25f, -0.2f),
                    ExpectedLocalRotation = legExpectedRot,
                    PositionTolerance = legPosTolerance,
                    RotationTolerance = legRotTolerance,
                    ExpectedSize = legExpectedSize,
                    SizeTolerance = legSizeTolerance
                },
                new PartBlueprint
                {
                    SlotName = "Backrest",
                    ExpectedLocalPosition = new Vector3(0f, 0.25f, -0.2f),
                    ExpectedLocalRotation = Quaternion.identity,
                    PositionTolerance = 0.15f,
                    RotationTolerance = 20f,
                    ExpectedSize = new Vector3(0.4f, 0.5f, 0.05f),
                    SizeTolerance = new Vector3(0.1f, 0.1f, 0.02f)
                }
            };

            // Set the data
            recipe.Editor_SetData("Wooden Chair", parts);

            // Ensure directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Runtime"))
                AssetDatabase.CreateFolder("Assets", "Runtime");
            if (!AssetDatabase.IsValidFolder("Assets/Runtime/Data Layer"))
                AssetDatabase.CreateFolder("Assets/Runtime", "Data Layer");

            // Save asset
            string path = "Assets/Runtime/Data Layer/Recipe_WoodenChair.asset";
            
            // Check if it already exists
            if (AssetDatabase.LoadAssetAtPath<CraftingRecipe>(path) != null)
            {
                Debug.LogWarning($"[CraftingRecipeGenerator] Recipe already exists at {path}. Deleting old one.");
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CraftingRecipeGenerator] Successfully generated Recipe_WoodenChair at {path}", recipe);
            Selection.activeObject = recipe;
        }
    }
}
