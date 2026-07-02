using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public enum FailReason
    {
        NoSlotMatch,
        Oversized,
        Undersized,
        OffCenter,
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

    public static class CraftingValidator
    {
        public static void ValidateBuild(Rigidbody rootBody, CraftingRecipe recipe)
        {
            if (recipe == null || rootBody == null)
            {
                string errorMsg = "Validation failed: ";
                if (recipe == null) errorMsg += "No recipe selected. ";
                if (rootBody == null) errorMsg += "Root object is missing.";

                var failResult = new ValidationResult
                {
                    IsSuccess = false,
                    QualityScore = 0f,
                    Message = errorMsg,
                    FailedParts = new List<PartFailure>()
                };
                EventBus.Publish(new CraftingValidatedEvent(failResult));
                return;
            }

            var failedParts = new List<PartFailure>();
            float totalScore = 0f;
            int matchedCount = 0;

            List<StructuralPiece> connectedPieces = AssemblyGraphUtility.GetAllConnectedPieces(rootBody);

            var unfulfilledSlots = new List<PartBlueprint>(recipe.RequiredParts);

            foreach (StructuralPiece piece in connectedPieces)
            {
                GameObject partObj = piece.gameObject;
                Rigidbody attachedBody = piece.GetComponent<Rigidbody>();

                if (attachedBody == null)
                {
                    Debug.LogWarning($"[CraftingValidator] '{partObj.name}' has StructuralPiece but no Rigidbody. Skipping.");
                    continue;
                }

                Bounds localBounds = CalculateStructuralLocalBounds(partObj, rootBody.transform);

                Vector3 localPos = localBounds.center;
                Quaternion localRot = Quaternion.Inverse(rootBody.transform.rotation) * attachedBody.rotation;

                bool matchedSlot = false;
                int bestSlotIndex = -1;
                float minPosError = float.MaxValue;
                float bestRotError = 0f;

                for (int i = 0; i < unfulfilledSlots.Count; i++)
                {
                    PartBlueprint blueprint = unfulfilledSlots[i];

                    float posError = Vector3.Distance(localPos, blueprint.ExpectedLocalPosition);

                    float rotError = blueprint.IgnoreRotation
                        ? 0f
                        : Quaternion.Angle(localRot, blueprint.ExpectedLocalRotation);

                    bool rotationPasses = blueprint.IgnoreRotation || rotError <= blueprint.RotationTolerance;

                    if (posError <= blueprint.PositionTolerance && rotationPasses)
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

                    Vector3 localSize = localBounds.size;

                    Vector3 minSize = blueprint.ExpectedSize - blueprint.SizeTolerance;
                    Vector3 maxSize = blueprint.ExpectedSize + blueprint.SizeTolerance;

                    bool isSizeValid =
                        localSize.x >= minSize.x && localSize.x <= maxSize.x &&
                        localSize.y >= minSize.y && localSize.y <= maxSize.y &&
                        localSize.z >= minSize.z && localSize.z <= maxSize.z;

                    if (!isSizeValid)
                    {
                        string sizeHint = BuildSizeHint(localSize, blueprint.ExpectedSize);
                        bool isTooLarge = localSize.x > maxSize.x || localSize.y > maxSize.y || localSize.z > maxSize.z;
                        FailReason sizeReason = isTooLarge ? FailReason.Oversized : FailReason.Undersized;

                        failedParts.Add(new PartFailure(
                            partObj,
                            sizeReason,
                            $"'{blueprint.SlotName}' — {sizeHint}"
                        ));
                        matchedSlot = true;
                    }
                    else
                    {
                        float posScore = 1f - (posError / blueprint.PositionTolerance);
                        float rotScore = blueprint.IgnoreRotation
                            ? 1f
                            : 1f - (rotError / blueprint.RotationTolerance);
                        totalScore += (posScore + rotScore) / 2f;
                        matchedCount++;

                        unfulfilledSlots.RemoveAt(bestSlotIndex);
                        matchedSlot = true;
                    }
                }

                if (!matchedSlot)
                {
                    // Find closest slot for a useful hint
                    string closestSlotName = "unknown";
                    float closestError = float.MaxValue;
                    foreach (var slot in unfulfilledSlots)
                    {
                        float pErr = Vector3.Distance(localPos, slot.ExpectedLocalPosition);
                        if (pErr < closestError)
                        {
                            closestError = pErr;
                            closestSlotName = slot.SlotName;
                        }
                    }

                    string positionHint = BuildPositionHint(localPos, closestSlotName, unfulfilledSlots);

                    failedParts.Add(new PartFailure(
                        partObj,
                        FailReason.OffCenter,
                        positionHint
                    ));
                }
            }

            foreach (PartBlueprint missingSlot in unfulfilledSlots)
            {
                failedParts.Add(new PartFailure(
                    null,
                    FailReason.MissingPart,
                    $"'{missingSlot.SlotName}' is missing"
                ));
            }

            var result = new ValidationResult
            {
                IsSuccess = failedParts.Count == 0,
                QualityScore = matchedCount > 0 ? totalScore / recipe.RequiredParts.Length : 0f,
                FailedParts = failedParts,
                Message = failedParts.Count == 0
                    ? $"Successfully built: {recipe.RecipeName}"
                    : $"Build failed with {failedParts.Count} issue(s)."
            };

            EventBus.Publish(new CraftingValidatedEvent(result));
        }

        private static Bounds CalculateStructuralLocalBounds(GameObject part, Transform rootTransform)
        {
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasInitialized = false;

            Matrix4x4 unscaledRootMatrix = Matrix4x4.TRS(rootTransform.position, rootTransform.rotation, Vector3.one);
            Matrix4x4 worldToUnscaledLocal = unscaledRootMatrix.inverse;

            Collider[] colliders = part.GetComponentsInChildren<Collider>();

            foreach (Collider col in colliders)
            {
                if (col.GetComponent<IDecoration>() != null) continue;

                Bounds colliderLocalBounds = GetColliderLocalBounds(col);
                Vector3[] corners = GetBoundsCorners(colliderLocalBounds);

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

            Vector3 localCenter = col.transform.InverseTransformPoint(col.bounds.center);
            Vector3 localSize = col.bounds.size;
            return new Bounds(localCenter, localSize);
        }

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

        #region Human-Readable Error Helpers

        private static string BuildSizeHint(Vector3 actualSize, Vector3 expectedSize)
        {
            float xDiff = ((actualSize.x - expectedSize.x) / expectedSize.x) * 100f;
            float yDiff = ((actualSize.y - expectedSize.y) / expectedSize.y) * 100f;
            float zDiff = ((actualSize.z - expectedSize.z) / expectedSize.z) * 100f;

            float maxAbsDiff = Mathf.Max(Mathf.Abs(xDiff), Mathf.Abs(yDiff), Mathf.Abs(zDiff));
            string direction = maxAbsDiff == Mathf.Abs(xDiff) ? "width"
                             : maxAbsDiff == Mathf.Abs(yDiff) ? "height"
                             : "depth";

            float signedDiff = direction == "width" ? xDiff : direction == "height" ? yDiff : zDiff;

            if (signedDiff > 0f)
                return $"Too big ({direction} +{signedDiff:F0}%)";
            else
                return $"Too small ({direction} {signedDiff:F0}%)";
        }

        private static string BuildPositionHint(Vector3 actualPos, string closestSlotName,
            List<PartBlueprint> slots)
        {
            PartBlueprint? matchedSlot = null;
            foreach (var slot in slots)
            {
                if (slot.SlotName == closestSlotName)
                {
                    matchedSlot = slot;
                    break;
                }
            }

            if (matchedSlot == null)
                return $"Extra part — doesn't match any slot";

            PartBlueprint bp = matchedSlot.Value;
            Vector3 offset = actualPos - bp.ExpectedLocalPosition;
            float distance = offset.magnitude;

            // Express error as percentage of position tolerance
            float errorPct = (distance / Mathf.Max(bp.PositionTolerance, 0.001f)) * 100f;

            // Cardinal direction hint
            string directionHint = GetDirectionHint(offset);

            return $"'{closestSlotName}' is off center — {directionHint} ({errorPct:F0}% error)";
        }

        private static string GetDirectionHint(Vector3 offset)
        {
            float absX = Mathf.Abs(offset.x);
            float absY = Mathf.Abs(offset.y);
            float absZ = Mathf.Abs(offset.z);

            if (absX >= absY && absX >= absZ)
                return offset.x > 0 ? "too far right" : "too far left";
            if (absY >= absX && absY >= absZ)
                return offset.y > 0 ? "too high" : "too low";
            return offset.z > 0 ? "too far forward" : "too far back";
        }

        #endregion
    }
}
