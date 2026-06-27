using UnityEngine;

namespace DumyatRuntime
{
    [ExecuteAlways] 
    public class PlaneSDFDriver : MonoBehaviour
    {
        [Header("Material Assignment")]
        public Material targetMaterial;

        [Header("Gizmo Settings")]
        public Vector2 gizmoSize = new Vector2(5f, 5f);
        public float gizmoArrowLength = 1f;
        public Color gizmoColor = new Color(0f, 1f, 1f, 0.4f);
        public bool drawGizmos = true;

        // Cached property IDs for optimization
        private static readonly int PlaneNormalID = Shader.PropertyToID("_PlaneNormal");
        private static readonly int PlanePositionID = Shader.PropertyToID("_PlanePositionFromOriginAlongNormal");
        private static readonly int PlaneRightID = Shader.PropertyToID("_PlaneRight");

        void Update()
        {
            if (targetMaterial == null) return;

            Vector3 planeNormal = transform.forward;
            Vector3 planePosition = transform.position;
            float h = Vector3.Dot(planeNormal, planePosition);
            Vector3 planeRight = transform.right;
            

            targetMaterial.SetVector(PlaneNormalID, planeNormal);
            targetMaterial.SetFloat(PlanePositionID, h);
            targetMaterial.SetVector(PlaneRightID, planeRight); 
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            // 1. Setup local space for the flat quad slice
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = gizmoColor;
            
            // Draws a flat quad representing the boundary slice
            Gizmos.DrawCube(Vector3.zero, new Vector3(gizmoSize.x, gizmoSize.y, 0.01f));
            
            // 2. Draw a solid 3D directional arrow handle
            #if UNITY_EDITOR
            UnityEditor.Handles.color = Color.blue;
            UnityEditor.Handles.ArrowHandleCap(
                0,                                   // Unique Control ID
                transform.position,                  // World-space start position
                transform.rotation,                  // World-space direction alignment
                gizmoArrowLength,                    // Total arrow scaling
                EventType.Repaint                    // Render mode configuration
            );
            #endif
        }
    }
}
