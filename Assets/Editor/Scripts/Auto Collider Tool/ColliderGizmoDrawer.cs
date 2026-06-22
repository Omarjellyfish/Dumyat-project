using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    public static class ColliderGizmoDrawer
    {
        private static bool _isDisplayEnabled = false;

        public static void DisplayColliders(bool display)
        {
            _isDisplayEnabled = display;
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Active)]
        private static void DrawBoxColliderGizmo(BoxCollider box, GizmoType gizmoType)
        {
            if (!_isDisplayEnabled) return;  // <-- early exit when toggle is off

            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Active)]
        private static void DrawSphereColliderGizmo(SphereCollider sphere, GizmoType gizmoType)
        {
            if (!_isDisplayEnabled) return;  // <-- same here

            Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
            Gizmos.matrix = sphere.transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
    }
}
