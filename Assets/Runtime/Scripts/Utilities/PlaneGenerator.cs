using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public class PlaneGenerator : MonoBehaviour {
        [Header("Mesh Settings")]
        [Tooltip("If enabled, changing plane settings below will instantly regenerate the mesh in the scene view.")]
        public bool realTimeMeshUpdates = true;

        [Tooltip("The physical width and length of the generated water plane in world units.")]
        public int planeLength = 10;

        [Tooltip("The density of the grid. Higher resolution means smoother wave curves, but more vertices for the GPU to process.")]
        public int quadRes = 10;

        public Mesh CurrentMesh => mesh;

        private Mesh mesh;
        private bool meshNeedsRebuild = false;

        void OnEnable() {
            Generate();
        }

        private void OnValidate() {
            planeLength = Mathf.Max(1, planeLength);
            quadRes = Mathf.Max(1, quadRes);

            if (realTimeMeshUpdates) {
                meshNeedsRebuild = true;

                #if UNITY_EDITOR
                EditorApplication.delayCall -= SafeRebuild;
                EditorApplication.delayCall += SafeRebuild;
                #endif
            }
        }

        private void SafeRebuild() {
            if (!meshNeedsRebuild || this == null) return;
            Generate();
            meshNeedsRebuild = false;
        }

        [ContextMenu("Force Regenerate Plane")]
        public void Generate() {
            mesh = new Mesh {
                name = "WaterPlane",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave
            };

            float halfLength = planeLength * 0.5f;
            int sideVertCount = planeLength * quadRes;

            var vertices = new Vector3[(sideVertCount + 1) * (sideVertCount + 1)];
            var uv = new Vector2[vertices.Length];

            for (int i = 0, x = 0; x <= sideVertCount; ++x) {
                for (int z = 0; z <= sideVertCount; ++z, ++i) {
                    vertices[i] = new Vector3(
                        ((float)x / sideVertCount * planeLength) - halfLength,
                        0,
                        ((float)z / sideVertCount * planeLength) - halfLength
                    );
                    uv[i] = new Vector2((float)x / sideVertCount, (float)z / sideVertCount);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;

            var triangles = new int[sideVertCount * sideVertCount * 6];
            for (int ti = 0, vi = 0, x = 0; x < sideVertCount; ++vi, ++x) {
                for (int z = 0; z < sideVertCount; ti += 6, ++vi, ++z) {
                    triangles[ti] = vi;
                    triangles[ti + 1] = vi + 1;
                    triangles[ti + 2] = vi + sideVertCount + 2;
                    triangles[ti + 3] = vi;
                    triangles[ti + 4] = vi + sideVertCount + 2;
                    triangles[ti + 5] = vi + sideVertCount + 1;
                }
            }

            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            GetComponent<MeshFilter>().mesh = mesh;

            // --- URP Material Assignment ---
            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial == null) {
                // 1. Try assigning the project's active Render Pipeline default material (URP Lit)
                if (GraphicsSettings.currentRenderPipeline != null) {
                    renderer.sharedMaterial = GraphicsSettings.currentRenderPipeline.defaultMaterial;
                } 
                
                // 2. Fallback: Search directly for Universal Render Pipeline Lit
                if (renderer.sharedMaterial == null) {
                    Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpShader != null) {
                        renderer.sharedMaterial = new Material(urpShader);
                    }
                }
            }
        }

        void OnDisable() {
            #if UNITY_EDITOR
            EditorApplication.delayCall -= SafeRebuild;
            #endif

            if (mesh != null) DestroyImmediate(mesh);
        }
    }
}