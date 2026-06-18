using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public enum FailReason
    {
        GluedTower,
        NoSlotMatch,
        Oversized,
        MissingPart
    }

    [System.Serializable]
    public struct PartFailure
    {
        public GameObject PartObject;
        public FailReason Reason;
        public string Details;

        public PartFailure(GameObject partObject, FailReason reason, string details)
        {
            PartObject = partObject;
            Reason = reason;
            Details = details;
        }
    }

    [System.Serializable]
    public struct ValidationResult
    {
        public bool IsSuccess;
        public float QualityScore;
        public string Message;
        public List<PartFailure> FailedParts;
    }

    /// <summary>
    /// Static utility class that runs the 3-tier validation gauntlet
    /// against a root Rigidbody and a CraftingRecipe.
    /// </summary>
    public static class CraftingValidator
    {
        public static event Action<ValidationResult> OnValidationComplete;

        public static void ValidateBuild(Rigidbody rootBody, CraftingRecipe recipe)
        {
            var failedParts = new List<PartFailure>();
            float totalScore = 0f;
            int matchedCount = 0;

            // Gather all direct attachments to the root via FixedJoints
            FixedJoint[] rootJoints = rootBody.GetComponents<FixedJoint>();

            // We store the attached body AND the exact local point on the root where it was glued (the anchor)
            var directAttachments = new List<(Rigidbody Body, Vector3 LocalGluePoint)>();

            foreach (FixedJoint joint in rootJoints)
            {
                if (joint.connectedBody != null)
                {
                    directAttachments.Add((joint.connectedBody, joint.anchor));
                }
            }

            // Copy blueprint slots so we can mark them fulfilled
            var unfulfilledSlots = new List<PartBlueprint>(recipe.RequiredParts);

            foreach (var attachment in directAttachments)
            {
                Rigidbody attachedBody = attachment.Body;
                GameObject partObj = attachedBody.gameObject;

                // Skip cosmetics attached directly to the root (e.g. a cushion on the seat)
                if (partObj.GetComponent<IDecoration>() != null) continue;

                // --- TIER 1: ANTI-CHEAT (Glued Tower Detection) ---
                GameObject contaminant = FindStructuralContaminant(attachedBody);
                if (contaminant != null)
                {
                    failedParts.Add(new PartFailure(
                        partObj,
                        FailReason.GluedTower,
                        $"'{partObj.name}' has structural piece '{contaminant.name}' glued to it."
                    ));
                    continue; // Don't bother grading a contaminated piece
                }

                // --- Calculate Physical Bounds (Tier 2 & 3) ---
                // We use the physical bounding box to determine position and size.
                // This makes the system 100% immune to weird model pivots or joint anchor variations!
                Bounds localBounds = CalculateStructuralLocalBounds(partObj, rootBody.transform);

                // --- TIER 2: SPATIAL MATH (Position & Rotation Match) ---
                Vector3 localPos = localBounds.center;
                Quaternion localRot = Quaternion.Inverse(rootBody.transform.rotation) * attachedBody.rotation;

                bool matchedSlot = false;
                int bestSlotIndex = -1;
                float minPosError = float.MaxValue;
                float bestRotError = 0f;

                // Find the best-fit slot out of all compatible slots
                for (int i = 0; i < unfulfilledSlots.Count; i++)
                {
                    PartBlueprint blueprint = unfulfilledSlots[i];

                    float posError = Vector3.Distance(localPos, blueprint.ExpectedLocalPosition);
                    float rotError = Quaternion.Angle(localRot, blueprint.ExpectedLocalRotation);

                    // Candidate must be within position and rotation tolerances
                    if (posError <= blueprint.PositionTolerance && rotError <= blueprint.RotationTolerance)
                    {
                        if (posError < minPosError)
                        {
                            minPosError = posError;
                            bestRotError = rotError;
                            bestSlotIndex = i;
                        }
                    }
                }

                if (bestSlotIndex != -1)
                {
                    PartBlueprint blueprint = unfulfilledSlots[bestSlotIndex];
                    float posError = minPosError;
                    float rotError = bestRotError;

                    // --- TIER 3: PROPORTIONAL BOUNDS ---
                    Vector3 localSize = localBounds.size;

                    Vector3 minSize = blueprint.ExpectedSize - blueprint.SizeTolerance;
                    Vector3 maxSize = blueprint.ExpectedSize + blueprint.SizeTolerance;

                    bool isSizeValid =
                        localSize.x >= minSize.x && localSize.x <= maxSize.x &&
                        localSize.y >= minSize.y && localSize.y <= maxSize.y &&
                        localSize.z >= minSize.z && localSize.z <= maxSize.z;

                    if (!isSizeValid)
                    {
                        failedParts.Add(new PartFailure(
                            partObj,
                            FailReason.Oversized, // Or Undersized based on details
                            $"'{partObj.name}' does not fit slot '{blueprint.SlotName}'. " +
                            $"Size: ({localSize.x:F3}, {localSize.y:F3}, {localSize.z:F3}) " +
                            $"Expected range: ({minSize.x:F3}, {minSize.y:F3}, {minSize.z:F3}) " +
                            $"to ({maxSize.x:F3}, {maxSize.y:F3}, {maxSize.z:F3})"
                        ));
                        matchedSlot = true; // Spatially matched, just failed bounds grading
                    }
                    else
                    {
                        // All 3 tiers passed — calculate quality score
                        float posScore = 1f - (posError / blueprint.PositionTolerance);
                        float rotScore = 1f - (rotError / blueprint.RotationTolerance);
                        totalScore += (posScore + rotScore) / 2f;
                        matchedCount++;

                        unfulfilledSlots.RemoveAt(bestSlotIndex);
                        matchedSlot = true;
                    }
                }

                if (!matchedSlot)
                {
                    // --- DEBUG DIAGNOSTICS ---
                    Debug.Log($"<color=yellow>[DIAG] '{partObj.name}' bounds center (localPos): {localPos}</color>");
                    Debug.Log($"<color=yellow>[DIAG] '{partObj.name}' localRot: {localRot.eulerAngles}</color>");
                    Debug.Log($"<color=yellow>[DIAG] '{partObj.name}' bounds size: {localBounds.size}</color>");

                    foreach (var slot in unfulfilledSlots)
                    {
                        float pErr = Vector3.Distance(localPos, slot.ExpectedLocalPosition);
                        float rErr = Quaternion.Angle(localRot, slot.ExpectedLocalRotation);
                        Debug.Log($"<color=cyan>  vs slot '{slot.SlotName}': " +
                                  $"expectedPos={slot.ExpectedLocalPosition} posError={pErr:F4} (tol={slot.PositionTolerance}) | " +
                                  $"rotError={rErr:F1}° (tol={slot.RotationTolerance})</color>");
                    }
                    // --- END DEBUG ---

                    failedParts.Add(new PartFailure(
                        partObj,
                        FailReason.NoSlotMatch,
                        $"'{partObj.name}' does not match any remaining blueprint slot."
                    ));
                }
            }

            // Check for unfulfilled slots (missing parts)
            foreach (PartBlueprint missingSlot in unfulfilledSlots)
            {
                failedParts.Add(new PartFailure(
                    null,
                    FailReason.MissingPart,
                    $"Required slot '{missingSlot.SlotName}' was not filled."
                ));
            }

            // Build final result
            var result = new ValidationResult
            {
                IsSuccess = failedParts.Count == 0,
                QualityScore = matchedCount > 0 ? totalScore / recipe.RequiredParts.Length : 0f,
                FailedParts = failedParts,
                Message = failedParts.Count == 0
                    ? $"Successfully built: {recipe.RecipeName}"
                    : $"Build failed with {failedParts.Count} issue(s)."
            };

            OnValidationComplete?.Invoke(result);
        }

        /// <summary>
        /// Checks if a structural piece has another structural piece FixedJoint'd to it.
        /// Returns the contaminating GameObject, or null if clean.
        /// </summary>
        private static GameObject FindStructuralContaminant(Rigidbody part)
        {
            FixedJoint[] childJoints = part.GetComponents<FixedJoint>();

            foreach (FixedJoint joint in childJoints)
            {
                if (joint.connectedBody == null) continue;

                if (joint.connectedBody.GetComponent<IStructuralPiece>() != null)
                {
                    return joint.connectedBody.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates the physical bounding box of a part in the root's unscaled local space.
        /// This ignores the root's scale so that measurements remain in actual world meters.
        /// </summary>
        private static Bounds CalculateStructuralLocalBounds(GameObject part, Transform rootTransform)
        {
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasInitialized = false;

            // Create a matrix that represents the root's position and rotation, but with a scale of 1.
            Matrix4x4 unscaledRootMatrix = Matrix4x4.TRS(rootTransform.position, rootTransform.rotation, Vector3.one);
            Matrix4x4 worldToUnscaledLocal = unscaledRootMatrix.inverse;

            Collider[] colliders = part.GetComponentsInChildren<Collider>();

            foreach (Collider col in colliders)
            {
                // Skip colliders on decoration objects
                if (col.GetComponent<IDecoration>() != null) continue;

                Vector3[] corners = GetBoundsCorners(col.bounds);

                foreach (Vector3 corner in corners)
                {
                    // Transform the world corner into the root's unscaled local space
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

        /// <summary>
        /// Extracts the 8 world-space corners from an axis-aligned bounding box.
        /// </summary>
        private static Vector3[] GetBoundsCorners(Bounds b)
        {
            return new Vector3[]
            {
                new Vector3(b.min.x, b.min.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.max.y, b.min.z),
                new Vector3(b.max.x, b.max.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z),
                new Vector3(b.min.x, b.max.y, b.max.z),
                new Vector3(b.max.x, b.max.y, b.max.z)
            };
        }
    }
}
