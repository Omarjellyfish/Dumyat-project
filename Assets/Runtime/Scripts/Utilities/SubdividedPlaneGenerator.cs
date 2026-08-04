using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public class SubdividedPlaneGenerator : MonoBehaviour 
    {
        [Header("Plane Dimensions")]
        [Tooltip("The physical width and length of the plane in world units.")]
        public float planeSize = 10f;

        [Range(0, 8)]
        [Tooltip("Base subdivision depth for LOD0 (0 = 1 quad, 1 = 2x2 grid, 2 = 4x4 grid, 6 = 64x64 grid, etc.).")]
        public int subdivisionLevel = 6;

        [Header("LOD Configuration")]
        [Tooltip("Enable to generate multiple LOD child meshes automatically using Unity's LODGroup.")]
        public bool useLODGroup = false;

        [Range(1, 4)]
        [Tooltip("How many LOD levels to generate when useLODGroup is enabled.")]
        public int lodCount = 3;

        [Range(1, 3)]
        [Tooltip("How many subdivision levels to drop per LOD step. (e.g. Step = 2 with Base = 6 creates LOD0 = 6, LOD1 = 4, LOD2 = 2).")]
        public int subdivisionStep = 1;

        [Header("Editor & Bounds")]
        [Tooltip("When enabled, modifying properties in the Inspector updates the mesh instantly.")]
        public bool realTimeUpdates = true;

        [Tooltip("Vertical padding added to mesh bounds to prevent frustum culling when displaced by shaders.")]
        public float verticalBoundsPadding = 5f;

        [Tooltip("Optional custom material to assign directly to generated renderers.")]
        public Material planeMaterial;

        public Mesh CurrentMesh => _mesh;

        private Mesh _mesh;
        private bool _meshNeedsRebuild = false;

        // Meshes this component instance has generated for its LOD children.
        // Only ever destroy meshes we personally created - never a mesh we merely
        // find attached (e.g. right after duplicating, a child's sharedMesh may
        // still point at the ORIGINAL object's mesh instance until we regenerate).
        private readonly System.Collections.Generic.List<Mesh> _ownedLodMeshes = new System.Collections.Generic.List<Mesh>();

        private void OnEnable() 
        {
            Generate();
        }

        private void OnValidate() 
        {
            planeSize = Mathf.Max(0.01f, planeSize);
            subdivisionLevel = Mathf.Clamp(subdivisionLevel, 0, 8);
            lodCount = Mathf.Clamp(lodCount, 1, 4);
            subdivisionStep = Mathf.Clamp(subdivisionStep, 1, 3);

            if (realTimeUpdates) 
            {
                _meshNeedsRebuild = true;

                #if UNITY_EDITOR
                EditorApplication.delayCall -= SafeRebuild;
                EditorApplication.delayCall += SafeRebuild;
                #endif
            }
        }

        private void SafeRebuild() 
        {
            if (!_meshNeedsRebuild || this == null) return;
            Generate();
            _meshNeedsRebuild = false;
        }

        [ContextMenu("Regenerate Plane")]
        public void Generate() 
        {
            #if UNITY_EDITOR
            // Prefab assets viewed in the Project window (not an instance placed
            // in a scene, and not open in Prefab Mode) don't allow structural
            // changes like reparenting - creating/destroying LOD children here
            // would throw "SetParent is disabled to prevent data corruption".
            // Prefab Mode instances and scene instances are NOT persistent, so
            // this only skips the passive "selected in Project window" case.
            if (UnityEditor.EditorUtility.IsPersistent(gameObject)) 
            {
                return;
            }
            #endif

            if (useLODGroup)
            {
                GenerateLODSystem();
            }
            else
            {
                GenerateSingleMesh();
            }
        }

        private void GenerateSingleMesh()
        {
            ClearLODChildren();
            LODGroup existingLodGroup = GetComponent<LODGroup>();
            if (existingLodGroup != null) DestroyImmediate(existingLodGroup);

            MeshRenderer parentRenderer = GetComponent<MeshRenderer>();
            parentRenderer.enabled = true;

            // Release the previous mesh before generating a new one, or every
            // regenerate (e.g. dragging a slider with realTimeUpdates on) leaks it
            if (_mesh != null)
            {
                DestroyImmediate(_mesh);
                _mesh = null;
            }

            _mesh = CreatePlaneMesh(subdivisionLevel);
            _mesh.name = $"{gameObject.name}_SubdividedPlane";

            GetComponent<MeshFilter>().mesh = _mesh;
            AssignParentMaterial(parentRenderer);
        }

        private void GenerateLODSystem()
        {
            ClearLODChildren();

            MeshRenderer parentRenderer = GetComponent<MeshRenderer>();
            AssignParentMaterial(parentRenderer);

            // The parent itself now serves as LOD0, keeping its own renderer
            // live (not disabled) so it stays clickable/selectable in the Scene
            // view. Only the lower-detail fallback LODs (1..N-1) live on hidden
            // child objects, since those only render when you're too far away
            // to realistically need to click-select the object anyway.
            parentRenderer.enabled = true;

            LODGroup lodGroup = GetComponent<LODGroup>();
            if (lodGroup == null)
            {
                lodGroup = gameObject.AddComponent<LODGroup>();
            }

            lodGroup.fadeMode = LODFadeMode.CrossFade;
            lodGroup.animateCrossFading = true;

            LOD[] lods = new LOD[lodCount];
            float[] screenHeights = new float[] { 0.6f, 0.3f, 0.1f, 0.02f };

            // LOD0: assign directly to the parent's own MeshFilter/MeshRenderer
            if (_mesh != null)
            {
                DestroyImmediate(_mesh);
                _mesh = null;
            }

            _mesh = CreatePlaneMesh(subdivisionLevel);
            int lod0Segments = 1 << subdivisionLevel;
            _mesh.name = $"{gameObject.name}_LOD0_{lod0Segments}x{lod0Segments}";
            GetComponent<MeshFilter>().mesh = _mesh;

            lods[0] = new LOD(screenHeights[0], new Renderer[] { parentRenderer })
            {
                fadeTransitionWidth = 0.25f
            };

            // LOD1..N-1: hidden fallback children, same as before
            for (int i = 1; i < lodCount; i++)
            {
                // Calculate exact subdivision drop based on subdivisionStep
                int currentSubdivision = Mathf.Max(0, subdivisionLevel - (i * subdivisionStep));

                GameObject lodChild = new GameObject($"LOD_{i}");
                lodChild.transform.SetParent(transform, false);

                MeshFilter mf = lodChild.AddComponent<MeshFilter>();
                MeshRenderer mr = lodChild.AddComponent<MeshRenderer>();

                Mesh lodMesh = CreatePlaneMesh(currentSubdivision);
                int segments = 1 << currentSubdivision;
                lodMesh.name = $"{gameObject.name}_LOD{i}_{segments}x{segments}";
                mf.sharedMesh = lodMesh;
                _ownedLodMeshes.Add(lodMesh);

                mr.sharedMaterial = parentRenderer.sharedMaterial;

                float height = (i < screenHeights.Length) ? screenHeights[i] : (0.1f / (i + 1));
                lods[i] = new LOD(height, new Renderer[] { mr })
                {
                    fadeTransitionWidth = 0.25f
                };
            }

            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }

        private Mesh CreatePlaneMesh(int subLevel)
        {
            Mesh mesh = new Mesh
            {
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave
            };

            int segments = 1 << subLevel; 
            float halfSize = planeSize * 0.5f;

            var vertices = new Vector3[(segments + 1) * (segments + 1)];
            var uv = new Vector2[vertices.Length];

            for (int i = 0, x = 0; x <= segments; ++x) 
            {
                for (int z = 0; z <= segments; ++z, ++i) 
                {
                    float xPercent = (float)x / segments;
                    float zPercent = (float)z / segments;

                    vertices[i] = new Vector3(
                        (xPercent * planeSize) - halfSize,
                        0f,
                        (zPercent * planeSize) - halfSize
                    );
                    uv[i] = new Vector2(xPercent, zPercent);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;

            var triangles = new int[segments * segments * 6];
            for (int ti = 0, vi = 0, x = 0; x < segments; ++vi, ++x) 
            {
                for (int z = 0; z < segments; ti += 6, ++vi, ++z) 
                {
                    triangles[ti]     = vi;
                    triangles[ti + 1] = vi + 1;
                    triangles[ti + 2] = vi + segments + 2;
                    triangles[ti + 3] = vi;
                    triangles[ti + 4] = vi + segments + 2;
                    triangles[ti + 5] = vi + segments + 1;
                }
            }

            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            if (verticalBoundsPadding > 0f)
            {
                Bounds bounds = mesh.bounds;
                bounds.Expand(new Vector3(0f, verticalBoundsPadding * 2f, 0f));
                mesh.bounds = bounds;
            }

            return mesh;
        }

        private void AssignParentMaterial(MeshRenderer mr)
        {
            if (planeMaterial != null)
            {
                mr.sharedMaterial = planeMaterial;
            }
            else if (mr.sharedMaterial == null)
            {
                if (GraphicsSettings.currentRenderPipeline != null) 
                {
                    mr.sharedMaterial = GraphicsSettings.currentRenderPipeline.defaultMaterial;
                } 
                else 
                {
                    Shader defaultShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (defaultShader != null) 
                    {
                        mr.sharedMaterial = new Material(defaultShader);
                    }
                }
            }
        }

        private void ClearLODChildren()
        {
            // Destroy only the meshes WE created and tracked - not whatever a
            // child's MeshFilter currently references. Right after a Ctrl+D
            // duplicate, a duplicated child's sharedMesh still points at the
            // ORIGINAL object's mesh instance (Unity shallow-copies the
            // reference, it doesn't clone the Mesh). Destroying based on that
            // live reference would delete the original's mesh out from under it.
            foreach (Mesh ownedMesh in _ownedLodMeshes)
            {
                if (ownedMesh != null) DestroyImmediate(ownedMesh);
            }
            _ownedLodMeshes.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }
        }

        private void OnDisable() 
        {
            #if UNITY_EDITOR
            EditorApplication.delayCall -= SafeRebuild;
            #endif

            if (_mesh != null) DestroyImmediate(_mesh);

            foreach (Mesh ownedMesh in _ownedLodMeshes)
            {
                if (ownedMesh != null) DestroyImmediate(ownedMesh);
            }
            _ownedLodMeshes.Clear();
        }
    }
}