using UnityEngine;
using System.Collections.Generic;
using EzySlice;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Reflex.Attributes;

namespace DumyatRuntime
{
    public class WoodSlicer : MonoBehaviour
    {
        [SerializeField] private Material _crossSectionMaterial;
        [SerializeField] private Transform _bladeDirectionReference;

        private IEventService _eventService;

        [Inject]
        private void Inject(IEventService eventService) => _eventService = eventService;

        [Header("Debug")]
        [SerializeField] private bool _drawDebug = true;
        [SerializeField] private float _debugRayLength = 0.5f;

        [Header("Generated Pieces")]
        [SerializeField] private bool _recenterPiecePivot = true;
        [SerializeField] private string _paintLayerName = "Wood Layer";

        private Vector3 _averagePosition;
        private Vector3 _averageDirection;
        private int originalWoodLayer;

        private Vector3 _lastCutPosition;
        private Vector3 _lastCutNormal;
        private Vector3 _lastBladeForward;
        private Vector3 _lastBladeUp;
        private Vector3 _lastSawDirection;
        private bool _hasDebugCut;
        private bool _isSlicing;

        public void PerformCut(List<Vector3> hitPoints, Vector3 sawDirection, GameObject wood)
        {
            if (_isSlicing)
            {
                // A slice is already in progress on this wood object this frame/pass -
                // Destroy() is deferred, so without this guard, rapid repeated contact
                // (e.g. every FixedUpdate while the blade stays touching) would slice
                // the same still-alive object multiple times and produce overlapping,
                // broken results instead of one clean cut.
                return;
            }

            if (wood == null)
            {
                Debug.LogWarning("WoodSlicer: No wood object passed.");
                return;
            }

            // FIXED (again): the previous fix used GetComponentInParent<Rigidbody>(),
            // but SetupPiece gives the "WoodPaint" CHILD its own kinematic Rigidbody
            // (specifically so it's excluded from the root's compound collider). That
            // meant GetComponentInParent<Rigidbody>() found WoodPaint's own Rigidbody
            // and stopped right there instead of climbing to the real root - so the
            // bug persisted. Only the true root ever has an XRGrabInteractable (no
            // child does), so use that instead to reliably find the real wood piece.
            XRGrabInteractable rootGrab = wood.GetComponentInParent<XRGrabInteractable>();
            if (rootGrab != null)
            {
                wood = rootGrab.gameObject;
            }
            else if (wood.transform.root != wood.transform)
            {
                wood = wood.transform.root.gameObject;
            }

            if (_bladeDirectionReference == null)
            {
                Debug.LogError("WoodSlicer: Blade Direction Reference is not assigned.");
                return;
            }

            if (hitPoints == null || hitPoints.Count == 0)
            {
                Debug.LogWarning("WoodSlicer: No hit points collected.");
                return;
            }

            _isSlicing = true;

            // FIXED: everything below used to be able to `return` early (invalid
            // direction, failed hull) WITHOUT resetting _isSlicing, and the success
            // path never reset it either. Once true, it stayed true forever on this
            // instance, so the `if (_isSlicing) return;` guard up top would silently
            // block every future call - which is why only the very first cut ever
            // worked, on any wood, including re-cutting a piece. Wrapping the body
            // in try/finally guarantees the flag always gets cleared no matter which
            // path we leave through (including thrown exceptions).
            try
            {
                // The root "wood" object no longer carries a mesh directly - it lives on
                // the WoodPaint child. Find it so EzySlice actually has geometry to cut.
                MeshFilter meshSourceFilter = wood.GetComponentInChildren<MeshFilter>();
                GameObject meshSource = meshSourceFilter != null ? meshSourceFilter.gameObject : wood;

                Vector3 sumOfPosition = Vector3.zero;

                for (int i = 0; i < hitPoints.Count; i++)
                {
                    sumOfPosition += hitPoints[i];
                }

                _averagePosition = sumOfPosition / hitPoints.Count;

                Vector3 bladeLength = _bladeDirectionReference.right.normalized;
                Vector3 bladeHeight = _bladeDirectionReference.up.normalized;
                _averageDirection = Vector3.Cross(bladeLength, bladeHeight).normalized;

                if (_averageDirection == Vector3.zero)
                {
                    Debug.LogWarning("WoodSlicer: Invalid cut direction.");
                    return;
                }

                _lastCutPosition = _averagePosition;
                _lastCutNormal = _averageDirection;
                _lastBladeForward = bladeLength;
                _lastBladeUp = bladeHeight;
                _lastSawDirection = sawDirection;
                _hasDebugCut = true;

                Debug.Log(
                    $"CUT DEBUG | hit count: {hitPoints.Count} | " +
                    $"cut pos: {_averagePosition} | " +
                    $"cut normal: {_averageDirection} | " +
                    $"blade forward: {bladeLength} | " +
                    $"blade up: {bladeHeight} | " +
                    $"old saw direction: {sawDirection}"
                );

                SlicedHull hull = meshSource.Slice(_averagePosition, _averageDirection);
                originalWoodLayer = wood.layer;

                if (hull == null)
                {
                    Debug.LogWarning("Slice failed - no valid hull returned.");
                    return;
                }

                WoodPaintSurface originalSurface = wood.GetComponentInChildren<WoodPaintSurface>();
                RenderTexture originalPaint = originalSurface?.PaintTexture;

                GameObject upperPiece = hull.CreateUpperHull(meshSource, _crossSectionMaterial);
                GameObject lowerPiece = hull.CreateLowerHull(meshSource, _crossSectionMaterial);

                // Destroy the old parent right away, now that the hull/pieces have been
                // created. Destroy() is deferred until end of frame, so "wood" and its
                // children stay fully readable for the rest of this function either way -
                // but doing it here means the parent is guaranteed to go away even if
                // SetupPiece throws or fails partway through below.
                Destroy(wood);

                SetupPiece(upperPiece, wood, originalPaint);
                SetupPiece(lowerPiece, wood, originalPaint);
            }
            finally
            {
                _isSlicing = false;
            }

            _lastCutPosition = _averagePosition;
            _lastCutNormal = _averageDirection;
            _lastBladeForward = bladeLength;
            _lastBladeUp = bladeHeight;
            _lastSawDirection = sawDirection;
            _hasDebugCut = true;

            Debug.Log(
                $"CUT DEBUG | hit count: {hitPoints.Count} | " +
                $"cut pos: {_averagePosition} | " +
                $"cut normal: {_averageDirection} | " +
                $"blade forward: {bladeLength} | " +
                $"blade up: {bladeHeight} | " +
                $"old saw direction: {sawDirection}"
            );

            SlicedHull hull = wood.Slice(_averagePosition, _averageDirection);
            originalWoodLayer = wood.layer;

            if (hull == null)
            {
                Debug.LogWarning("Slice failed - no valid hull returned.");
                return;
            }

            WoodPaintSurface originalSurface = wood.GetComponent<WoodPaintSurface>();
            RenderTexture originalPaint = originalSurface?.PaintTexture;

            GameObject upperPiece = hull.CreateUpperHull(wood, _crossSectionMaterial);
            GameObject lowerPiece = hull.CreateLowerHull(wood, _crossSectionMaterial);

            SetupPiece(upperPiece, wood, originalPaint);
            SetupPiece(lowerPiece, wood, originalPaint);

            _eventService.Publish(new WoodCutEvent(_averagePosition, wood, upperPiece, lowerPiece));

            Destroy(wood);
        }

        private void SetupPiece(GameObject piece, GameObject originalWood, RenderTexture originalPaint)
        {
            if (piece == null)
                return;

            if (_recenterPiecePivot)
                RecenterMeshPivot(piece);

            // EzySlice puts the sliced mesh directly on "piece" (MeshFilter + MeshRenderer).
            // We pull those off the root and move them onto a dedicated "WoodPaint" child,
            // matching the target hierarchy: root (physics/grab) -> BoxCollider child + WoodPaint child.
            MeshFilter sliceMeshFilter = piece.GetComponent<MeshFilter>();
            MeshRenderer sliceMeshRenderer = piece.GetComponent<MeshRenderer>();
            Mesh sliceMesh = sliceMeshFilter != null ? sliceMeshFilter.sharedMesh : null;
            Material[] sliceMaterials = sliceMeshRenderer != null ? sliceMeshRenderer.sharedMaterials : null;

            // Runtime-sliced meshes never went through the Editor's "Generate Lightmap UVs"
            // import step, so UV2 is empty here (every vertex reads back as (0,0)).
            // That's why paint bled onto every face at once - there was no per-face UV
            // separation at all. Build a non-overlapping UV2 by hand instead.
            if (sliceMesh != null)
                GenerateUniqueUV2(sliceMesh);

            // ---- Child: WoodPaint (visuals + non-convex painting collider) ----
            GameObject woodPaintObj = new GameObject("WoodPaint");
            woodPaintObj.transform.SetParent(piece.transform, false);

            int paintLayer = LayerMask.NameToLayer(_paintLayerName);
            woodPaintObj.layer = paintLayer >= 0 ? paintLayer : originalWoodLayer;

            MeshFilter paintMeshFilter = woodPaintObj.AddComponent<MeshFilter>();
            paintMeshFilter.sharedMesh = sliceMesh;

            MeshRenderer paintMeshRenderer = woodPaintObj.AddComponent<MeshRenderer>();
            paintMeshRenderer.sharedMaterials = sliceMaterials;

            MeshCollider paintCollider = woodPaintObj.AddComponent<MeshCollider>();
            paintCollider.sharedMesh = sliceMesh;
            paintCollider.convex = false;

            // Its own Rigidbody (kinematic) keeps it out of the root's compound collider,
            // which is what allows a non-convex collider here even though the root has a
            // dynamic Rigidbody.
            Rigidbody paintRb = woodPaintObj.AddComponent<Rigidbody>();
            paintRb.isKinematic = true;

            WoodPaintSurface woodSurface = woodPaintObj.AddComponent<WoodPaintSurface>();
            woodSurface.CopyPaintFrom(originalPaint);

            // Remove the mesh visuals from the root now that they live on WoodPaint.
            if (sliceMeshRenderer != null) Destroy(sliceMeshRenderer);
            if (sliceMeshFilter != null) Destroy(sliceMeshFilter);

            // ---- Child: grab/physics collider (convex MeshCollider, same mesh as WoodPaint) ----
            GameObject grabColliderObj = new GameObject("MeshCollider");
            grabColliderObj.transform.SetParent(piece.transform, false);
            grabColliderObj.layer = LayerMask.NameToLayer("AssemblyPart");

            MeshCollider grabCollider = grabColliderObj.AddComponent<MeshCollider>();
            grabCollider.sharedMesh = paintMeshFilter.sharedMesh; // same mesh WoodPaint uses
            grabCollider.convex = true;

            // ---- Root: physics body + grab interaction ----
            Rigidbody rb = piece.AddComponent<Rigidbody>();
            rb.useGravity = true;

            XRGrabInteractable grab = piece.AddComponent<XRGrabInteractable>();
            grab.attachTransform = null;

            // XRGrabInteractable auto-populates its colliders list from every collider
            // found in children (both the grab collider AND the paint collider) as soon
            // as it's added. Clear that out first so only the intended collider is used.
            grab.colliders.Clear();
            grab.colliders.Add(grabCollider);

            piece.layer = originalWoodLayer;

            AddCutPieceGameplayComponents(piece, originalWood);

            piece.transform.localScale = originalWood.transform.localScale;
        }

        // Packs the mesh into a 2x3 grid of non-overlapping UV cells, one cell per
        // dominant face direction (+Y/-Y, +X/-X, +Z/-Z). This guarantees top and
        // bottom faces (and every other opposite pair) never share a UV coordinate,
        // which is what was causing paint to appear on both sides at once.
        private void GenerateUniqueUV2(Mesh mesh)
        {
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Bounds bounds = mesh.bounds;

            Vector3 size = bounds.size;
            size.x = Mathf.Max(size.x, 0.0001f);
            size.y = Mathf.Max(size.y, 0.0001f);
            size.z = Mathf.Max(size.z, 0.0001f);

            Vector2[] uv2 = new Vector2[vertices.Length];
            const float cellWidth = 1f / 2f;
            const float cellHeight = 1f / 3f;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 n = normals[i];
                Vector3 p = vertices[i] - bounds.min;

                float absX = Mathf.Abs(n.x);
                float absY = Mathf.Abs(n.y);
                float absZ = Mathf.Abs(n.z);

                float u, v;
                int cell;

                if (absY >= absX && absY >= absZ)
                {
                    u = p.x / size.x;
                    v = p.z / size.z;
                    cell = n.y >= 0 ? 0 : 1; // top / bottom
                }
                else if (absX >= absY && absX >= absZ)
                {
                    u = p.z / size.z;
                    v = p.y / size.y;
                    cell = n.x >= 0 ? 2 : 3; // right / left
                }
                else
                {
                    u = p.x / size.x;
                    v = p.y / size.y;
                    cell = n.z >= 0 ? 4 : 5; // front / back
                }

                int col = cell % 2;
                int row = cell / 2;

                uv2[i] = new Vector2(
                    (col + Mathf.Clamp01(u)) * cellWidth,
                    (row + Mathf.Clamp01(v)) * cellHeight
                );
            }

            mesh.uv2 = uv2;
        }

        private void RecenterMeshPivot(GameObject piece)
        {
            MeshFilter meshFilter = piece.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.mesh == null)
                return;

            Mesh mesh = meshFilter.mesh;
            Vector3 localCenter = mesh.bounds.center;
            if (localCenter == Vector3.zero)
                return;

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] -= localCenter;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            piece.transform.position += piece.transform.TransformVector(localCenter);
        }

        private void AddCutPieceGameplayComponents(GameObject piece, GameObject originalWood)
        {
            if (piece.GetComponent<StructuralPiece>() == null)
                piece.AddComponent<StructuralPiece>();

            if (piece.GetComponent<CollisionAudio>() == null)
            {
                CollisionAudio collisionAudio = piece.AddComponent<CollisionAudio>();
                CollisionAudio originalCollisionAudio = originalWood != null
                    ? originalWood.GetComponent<CollisionAudio>()
                    : null;

                if (originalCollisionAudio != null)
                {
                    JsonUtility.FromJsonOverwrite(
                        JsonUtility.ToJson(originalCollisionAudio),
                        collisionAudio);
                }
            }

            // REMOVED: this used to add a fresh WoodSlicer component onto every cut
            // piece (copying _crossSectionMaterial, _bladeDirectionReference, etc.).
            // That was only needed if something called wood.GetComponent<WoodSlicer>()
            // per-piece to cut it. SawTip actually holds one fixed, manually-assigned
            // _woodSlicer reference and always calls that same instance for every cut,
            // regardless of which piece it's cutting - so the copy on each piece was
            // never used by anything and just added dead components.
        }

        private void OnDrawGizmos()
        {
            if (!_drawDebug || !_hasDebugCut)
                return;

            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_lastCutPosition, 0.025f);
            Gizmos.DrawRay(_lastCutPosition, _lastCutNormal * _debugRayLength);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_lastCutPosition, _lastBladeForward * _debugRayLength);

            Gizmos.color = Color.green;
            Gizmos.DrawRay(_lastCutPosition, _lastBladeUp * _debugRayLength);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(_lastCutPosition, _lastSawDirection.normalized * _debugRayLength);
        }
    }
}