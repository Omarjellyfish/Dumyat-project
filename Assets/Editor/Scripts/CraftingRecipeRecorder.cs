using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DumyatRuntime.Editor
{
    /// <summary>
    /// Editor tool that inspects a physical reference assembly in the scene 
    /// and records its layout into a CraftingRecipe ScriptableObject.
    /// </summary>
    public class CraftingRecipeRecorder
    {
        [MenuItem("Dumyat/Crafting/Record Recipe from Selection")]
        public static void RecordRecipe()
        {
            GameObject selectedObj = Selection.activeGameObject;
            if (selectedObj == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select the root Rigidbody GameObject (e.g., the Seat) of your reference assembly.", "OK");
                return;
            }

            Rigidbody rootBody = selectedObj.GetComponent<Rigidbody>();
            if (rootBody == null)
            {
                EditorUtility.DisplayDialog("Error", "Selected object must have a Rigidbody component to act as the root body.", "OK");
                return;
            }

            // Ask where to save the recipe ScriptableObject
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Crafting Recipe",
                $"Recipe_{selectedObj.name}",
                "asset",
                "Choose a name and location for the recorded crafting recipe."
            );

            if (string.IsNullOrEmpty(path)) return;

            // Find all connected parts via FixedJoints
            FixedJoint[] rootJoints = rootBody.GetComponents<FixedJoint>();
            List<PartBlueprint> blueprints = new List<PartBlueprint>();

            foreach (FixedJoint joint in rootJoints)
            {
                if (joint.connectedBody == null) continue;

                Rigidbody attachedBody = joint.connectedBody;
                GameObject partObj = attachedBody.gameObject;

                // Skip cosmetics
                if (partObj.GetComponent<IDecoration>() != null) continue;

                // Verify it has structural piece script
                if (partObj.GetComponent<IStructuralPiece>() == null)
                {
                    Debug.LogWarning($"[RecipeRecorder] Skipping '{partObj.name}' because it does not have a component implementing IStructuralPiece.");
                    continue;
                }

                // Calculate local bounds in unscaled space
                Bounds localBounds = CalculateStructuralLocalBounds(partObj, rootBody.transform);

                PartBlueprint bp = new PartBlueprint();
                // Strip clone suffixes from names to make them look cleaner
                string cleanName = partObj.name.Replace("(Clone)", "").Trim();
                bp.SlotName = cleanName;
                bp.ExpectedLocalPosition = localBounds.center;
                bp.ExpectedLocalRotation = Quaternion.Inverse(rootBody.transform.rotation) * attachedBody.rotation;
                bp.ExpectedSize = localBounds.size;

                // Reasonable default tolerances (can be adjusted in Inspector)
                bp.PositionTolerance = 0.1f;
                bp.RotationTolerance = 15f;
                bp.SizeTolerance = new Vector3(0.02f, 0.05f, 0.02f);

                blueprints.Add(bp);
            }

            if (blueprints.Count == 0)
            {
                EditorUtility.DisplayDialog("Warning", "No structural pieces (with IStructuralPiece components) were found attached to the selected root Rigidbody via FixedJoints.", "OK");
                return;
            }

            // Create and save ScriptableObject
            CraftingRecipe recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            recipe.Editor_SetData(selectedObj.name, blueprints.ToArray());

            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", $"Recorded {blueprints.Count} parts. Crafting Recipe saved successfully to:\n{path}", "OK");
            Selection.activeObject = recipe;
        }

        private static Bounds CalculateStructuralLocalBounds(GameObject part, Transform rootTransform)
        {
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasInitialized = false;

            // Use unscaled space to remain scale-independent
            Matrix4x4 unscaledRootMatrix = Matrix4x4.TRS(rootTransform.position, rootTransform.rotation, Vector3.one);
            Matrix4x4 worldToUnscaledLocal = unscaledRootMatrix.inverse;

            Collider[] colliders = part.GetComponentsInChildren<Collider>();

            foreach (Collider col in colliders)
            {
                if (col.GetComponent<IDecoration>() != null) continue;

                Vector3[] corners = GetBoundsCorners(col.bounds);
                foreach (Vector3 corner in corners)
                {
                    Vector3 localCorner = worldToUnscaledLocal.MultiplyPoint3x4(corner);
                    if (!hasInitialized)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasInitialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            return localBounds;
        }

        private static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new Vector3[]
            {
                min,
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                max
            };
        }
    }
}
