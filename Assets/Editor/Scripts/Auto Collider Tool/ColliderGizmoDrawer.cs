using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    public static class ColliderGizmoDrawer
    {
        // This attribute tells Unity to run this method for ALL BoxColliders in the scene
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Active)]
        private static void DrawBoxColliderGizmo(BoxCollider box, GizmoType gizmoType)
        {
            // Set the gizmo color (e.g., semi-transparent green)
            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            
            // Match the object's position, rotation, and scale
            Gizmos.matrix = box.transform.localToWorldMatrix;
            
            // Draw the wireframe outline
            Gizmos.DrawWireCube(box.center, box.size);
        }

        // This handles SphereColliders globally
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Active)]
        private static void DrawSphereColliderGizmo(SphereCollider sphere, GizmoType gizmoType)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.matrix = sphere.transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
    }
}
