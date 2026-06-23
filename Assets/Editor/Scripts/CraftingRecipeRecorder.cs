using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DumyatRuntime.Editor
{
    /// <summary>
    /// Editor tool that inspects a physical reference assembly in the scene 
    /// and records its layout into a CraftingRecipe ScriptableObject.
    /// After recording, runs a verification pass to confirm the recipe
    /// matches the assembly it was captured from.
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

            // --- GRAPH TRAVERSAL GATHER STEP ---
            // Uses the shared BFS utility to discover all connected structural
            // pieces at ANY depth (Root → A → B → C all discovered).
            // This is the same gather logic the CraftingValidator uses,
            // ensuring the Recorder and Validator always see identical pieces.
            List<StructuralPiece> connectedPieces = AssemblyGraphUtility.GetAllConnectedPieces(rootBody);

            List<PartBlueprint> blueprints = new List<PartBlueprint>();

            // Keep track of parts for the verification pass
            List<(Rigidbody Body, GameObject PartObj)> recordedParts = new List<(Rigidbody, GameObject)>();

            foreach (StructuralPiece piece in connectedPieces)
            {
                GameObject partObj = piece.gameObject;
                Rigidbody attachedBody = piece.GetComponent<Rigidbody>();

                if (attachedBody == null)
                {
                    Debug.LogWarning($"[RecipeRecorder] Skipping '{partObj.name}' — has StructuralPiece but no Rigidbody.");
                    continue;
                }

                // Calculate local bounds using oriented local bounds (rotation-stable)
                // This uses the unscaled root matrix so measurements are in actual world meters.
                Bounds localBounds = CalculateStructuralLocalBounds(partObj, rootBody.transform);

                PartBlueprint bp = new PartBlueprint();
                // Strip clone suffixes from names to make them look cleaner
                string cleanName = partObj.name.Replace("(Clone)", "").Trim();
                bp.SlotName = cleanName;
                bp.ExpectedLocalPosition = localBounds.center;
                bp.ExpectedLocalRotation = Quaternion.Inverse(rootBody.transform.rotation) * attachedBody.rotation;
                bp.ExpectedSize = localBounds.size;

                // Default: ignore rotation for most pieces (symmetric legs, planks, etc.)
                bp.IgnoreRotation = true;

                // Reasonable default tolerances (can be adjusted in Inspector)
                bp.PositionTolerance = 0.1f;
                bp.RotationTolerance = 15f;
                bp.SizeTolerance = new Vector3(0.02f, 0.05f, 0.02f);

                blueprints.Add(bp);
                recordedParts.Add((attachedBody, partObj));
            }

            if (blueprints.Count == 0)
            {
                EditorUtility.DisplayDialog("Warning", "No structural pieces (with StructuralPiece components) were found connected to the selected root Rigidbody via the FixedJoint graph.", "OK");
                return;
            }

            // --- VERIFICATION PASS ---
            // Run the same best-fit matching logic the validator uses to confirm
            // that every recorded part matches exactly one blueprint slot.
            int verifyErrors = VerifyRecipeAgainstAssembly(rootBody, blueprints, recordedParts);

            // --- RESOLVE ROOT PREFAB ---
            // Find the source prefab asset so the recipe can spawn it at runtime
            GameObject rootPrefab = PrefabUtility.GetCorrespondingObjectFromSource(selectedObj);
            if (rootPrefab == null)
            {
                Debug.LogWarning("[RecipeRecorder] Could not find source prefab for the root object. " +
                                 "The Root Prefab field will be empty — assign it manually in the Inspector.");
            }

            // Create and save ScriptableObject
            CraftingRecipe recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            recipe.Editor_SetData(selectedObj.name, rootPrefab, blueprints.ToArray());

            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string verifyMsg = verifyErrors == 0
                ? "Verification PASSED — all parts match their recorded slots."
                : $"Verification found {verifyErrors} issue(s). Check the Console for details. Recipe saved but may need tolerance adjustments.";

            EditorUtility.DisplayDialog("Success", $"Recorded {blueprints.Count} parts.\n\n{verifyMsg}\n\nSaved to:\n{path}", "OK");
            Selection.activeObject = recipe;
        }

        /// <summary>
        /// Runs the same best-fit matching logic from CraftingValidator against the recorded
        /// blueprints to confirm they produce a perfect match on the source assembly.
        /// Returns the number of mismatches found.
        /// </summary>
        private static int VerifyRecipeAgainstAssembly(
            Rigidbody rootBody,
            List<PartBlueprint> blueprints,
            List<(Rigidbody Body, GameObject PartObj)> parts)
        {
            int errors = 0;
            var unfulfilledSlots = new List<PartBlueprint>(blueprints);

            foreach (var (attachedBody, partObj) in parts)
            {
                Bounds localBounds = CalculateStructuralLocalBounds(partObj, rootBody.transform);
                Vector3 localPos = localBounds.center;
                Quaternion localRot = Quaternion.Inverse(rootBody.transform.rotation) * attachedBody.rotation;

                int bestSlotIndex = -1;
                float minPosError = float.MaxValue;

                for (int i = 0; i < unfulfilledSlots.Count; i++)
                {
                    PartBlueprint bp = unfulfilledSlots[i];
                    float posError = Vector3.Distance(localPos, bp.ExpectedLocalPosition);
                    float rotError = bp.IgnoreRotation ? 0f : Quaternion.Angle(localRot, bp.ExpectedLocalRotation);

                    bool rotationPasses = bp.IgnoreRotation || rotError <= bp.RotationTolerance;

                    if (posError <= bp.PositionTolerance && rotationPasses)
                    {
                        if (posError < minPosError)
                        {
                            minPosError = posError;
                            bestSlotIndex = i;
                        }
                    }
                }

                if (bestSlotIndex != -1)
                {
                    PartBlueprint matchedBp = unfulfilledSlots[bestSlotIndex];

                    // Also verify size tolerance
                    Vector3 localSize = localBounds.size;
                    Vector3 minSize = matchedBp.ExpectedSize - matchedBp.SizeTolerance;
                    Vector3 maxSize = matchedBp.ExpectedSize + matchedBp.SizeTolerance;

                    bool sizeOk =
                        localSize.x >= minSize.x && localSize.x <= maxSize.x &&
                        localSize.y >= minSize.y && localSize.y <= maxSize.y &&
                        localSize.z >= minSize.z && localSize.z <= maxSize.z;

                    if (sizeOk)
                    {
                        Debug.Log($"<color=green>[Verify] '{partObj.name}' → slot '{matchedBp.SlotName}' ✓ (posErr={minPosError:F4})</color>");
                        unfulfilledSlots.RemoveAt(bestSlotIndex);
                    }
                    else
                    {
                        Debug.LogWarning($"[Verify] '{partObj.name}' matched slot '{matchedBp.SlotName}' spatially but FAILED size check. " +
                                         $"Size: {localSize} Expected: {matchedBp.ExpectedSize} ±{matchedBp.SizeTolerance}");
                        errors++;
                    }
                }
                else
                {
                    Debug.LogError($"[Verify] '{partObj.name}' did NOT match any blueprint slot! " +
                                   $"localPos={localPos} localRot={localRot.eulerAngles} size={localBounds.size}");
                    errors++;
                }
            }

            foreach (var remaining in unfulfilledSlots)
            {
                Debug.LogError($"[Verify] Blueprint slot '{remaining.SlotName}' was never matched by any part!");
                errors++;
            }

            return errors;
        }

        /// <summary>
        /// Calculates the physical bounding box of a part in the root's unscaled local space.
        /// Uses each collider's own local bounds (rotation-stable) instead of the world-axis-aligned
        /// Collider.bounds, so measurements remain consistent regardless of assembly orientation.
        /// </summary>
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

                // Get the collider's own local bounds (these never change with world rotation)
                Bounds colliderLocalBounds = GetColliderLocalBounds(col);
                Vector3[] corners = GetBoundsCorners(colliderLocalBounds);

                // Transform: collider-local → world → root-local (unscaled)
                Matrix4x4 colliderToWorld = col.transform.localToWorldMatrix;

                foreach (Vector3 corner in corners)
                {
                    Vector3 worldCorner = colliderToWorld.MultiplyPoint3x4(corner);
                    Vector3 rootLocalCorner = worldToUnscaledLocal.MultiplyPoint3x4(worldCorner);

                    if (!hasInitialized)
                    {
                        localBounds = new Bounds(rootLocalCorner, Vector3.zero);
                        hasInitialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(rootLocalCorner);
                    }
                }
            }

            return localBounds;
        }

        /// <summary>
        /// Returns the collider's bounding box in its own local space (rotation-independent).
        /// </summary>
        private static Bounds GetColliderLocalBounds(Collider col)
        {
            if (col is BoxCollider box)
                return new Bounds(box.center, box.size);

            if (col is SphereCollider sphere)
                return new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);

            if (col is CapsuleCollider capsule)
            {
                float diameter = capsule.radius * 2f;
                Vector3 size = new Vector3(diameter, diameter, diameter);
                size[capsule.direction] = Mathf.Max(capsule.height, diameter);
                return new Bounds(capsule.center, size);
            }

            if (col is MeshCollider mesh && mesh.sharedMesh != null)
                return mesh.sharedMesh.bounds;

            // Fallback for unknown collider types
            Vector3 localCenter = col.transform.InverseTransformPoint(col.bounds.center);
            Vector3 localSize = col.bounds.size;
            return new Bounds(localCenter, localSize);
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
