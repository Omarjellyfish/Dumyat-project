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

    public static class CraftingValidator
    {
        public static event Action<ValidationResult> OnValidationComplete;

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
                OnValidationComplete?.Invoke(failResult);
                return;
            }

            var failedParts = new List<PartFailure>();
            float totalScore = 0f;
            int matchedCount = 0;

            FixedJoint[] rootJoints = rootBody.GetComponents<FixedJoint>();

            var directAttachments = new List<(Rigidbody Body, Vector3 LocalGluePoint)>();

            foreach (FixedJoint joint in rootJoints)
            {
                if (joint.connectedBody != null)
                {
                    directAttachments.Add((joint.connectedBody, joint.anchor));
                }
            }

            var unfulfilledSlots = new List<PartBlueprint>(recipe.RequiredParts);

            foreach (var attachment in directAttachments)
            {
                Rigidbody attachedBody = attachment.Body;
                GameObject partObj = attachedBody.gameObject;

                if (partObj.GetComponent<IDecoration>() != null) continue;

                GameObject contaminant = FindStructuralContaminant(attachedBody);
                if (contaminant != null)
                {
                    failedParts.Add(new PartFailure(
                        partObj,
                        FailReason.GluedTower,
                        $"'{partObj.name}' has structural piece '{contaminant.name}' glued to it."
                    ));
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
                        failedParts.Add(new PartFailure(
                            partObj,
                            FailReason.Oversized,
                            $"'{partObj.name}' does not fit slot '{blueprint.SlotName}'. " +
                            $"Size: ({localSize.x:F3}, {localSize.y:F3}, {localSize.z:F3}) " +
                            $"Expected range: ({minSize.x:F3}, {minSize.y:F3}, {minSize.z:F3}) " +
                            $"to ({maxSize.x:F3}, {maxSize.y:F3}, {maxSize.z:F3})"
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
                    Debug.Log($"<color=yellow>[DIAG] '{partObj.name}' bounds center (localPos): {localPos}</color>");
                    Debug.Log($"<color=yellow>[DIAG] '{partObj.name}' localRot: {localRot.eulerAngles}</color>");
                    Debug.Log($"<color=yellow>[DIAG] '{partObj.name}' bounds size: {localBounds.size}</color>");

                    foreach (var slot in unfulfilledSlots)
                    {
                        float pErr = Vector3.Distance(localPos, slot.ExpectedLocalPosition);
                        float rErr = slot.IgnoreRotation ? 0f : Quaternion.Angle(localRot, slot.ExpectedLocalRotation);
                        Debug.Log($"<color=cyan>  vs slot '{slot.SlotName}': " +
                                  $"expectedPos={slot.ExpectedLocalPosition} posError={pErr:F4} (tol={slot.PositionTolerance}) | " +
                                  $"rotError={rErr:F1}° (tol={slot.RotationTolerance}) ignoreRot={slot.IgnoreRotation}</color>");
                    }

                    failedParts.Add(new PartFailure(
                        partObj,
                        FailReason.NoSlotMatch,
                        $"'{partObj.name}' does not match any remaining blueprint slot."
                    ));
                }
            }

            foreach (PartBlueprint missingSlot in unfulfilledSlots)
            {
                failedParts.Add(new PartFailure(
                    null,
                    FailReason.MissingPart,
                    $"Required slot '{missingSlot.SlotName}' was not filled."
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

            OnValidationComplete?.Invoke(result);
        }

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
    }
}
