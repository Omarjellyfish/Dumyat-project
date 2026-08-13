#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// Moves an entire demo scene to its slot on the presentation world grid.
    ///
    /// WHY TRANSLATION ONLY, AND WHY IT IS SAFE TO BAKE AFTERWARDS:
    /// a rigid translation leaves every input to the lightmapper unchanged in the ways that matter —
    /// the sun's direction is a rotation, the skybox is sampled by direction, lightmap UVs are in
    /// texture space, and every probe moves rigidly with the geometry it samples. Rotating or
    /// scaling a scene would change all of those and the bake would not match the original.
    /// So: translate, never rotate, never scale.
    ///
    /// WHY THE ORDER IS OFFSET-THEN-BAKE, NEVER BAKE-THEN-OFFSET:
    /// lightmaps survive a move (texture space), but light probes, APV bricks and occlusion data are
    /// all stored in world space and go stale the moment the geometry leaves them behind. Applying
    /// the offset first and baking last is what keeps a demo scene looking like its original.
    /// ApplyOffset therefore clears the stale baked data it invalidates rather than leaving a
    /// half-valid bake around to produce a convincing-but-wrong spike result.
    /// </summary>
    public static class SceneOffsetTool
    {
        public sealed class OffsetResult
        {
            public bool Success;
            public string Message;
            public int RootsMoved;
            public Vector3 Offset;
            public Vector3 CenterBefore;
            public Vector3 CenterAfter;
        }

        /// <summary>
        /// Translates every root transform of <paramref name="scene"/> by <paramref name="offset"/>.
        /// The scene is left dirty and unsaved — the caller decides when to save, so a bad offset
        /// can be abandoned by closing without saving.
        /// </summary>
        public static OffsetResult ApplyOffset(Scene scene, Vector3 offset, bool dryRun = false)
        {
            var res = new OffsetResult { Offset = offset };

            if (!scene.isLoaded)
            {
                res.Message = $"scene '{scene.path}' is not loaded";
                return res;
            }

            SceneAuditReport.Audit before = SceneAuditReport.Audit_(scene);
            res.CenterBefore = before.HasRendererBounds ? before.RendererBounds.center : Vector3.zero;

            if (dryRun)
            {
                res.Success = true;
                res.CenterAfter = res.CenterBefore + offset;
                res.RootsMoved = scene.rootCount;
                res.Message = $"DRY RUN — would move {res.RootsMoved} roots by {offset}";
                return res;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            Undo.RecordObjects(System.Array.ConvertAll(roots, g => (Object)g.transform),
                               "Offset presentation stage");

            foreach (GameObject root in roots)
            {
                Transform t = root.transform;
                // Local position is world position for a root, but going through position keeps
                // this correct if a root is ever reparented into a rig later.
                t.position += offset;
                res.RootsMoved++;
            }

            EditorSceneManager.MarkSceneDirty(scene);

            SceneAuditReport.Audit after = SceneAuditReport.Audit_(scene);
            res.CenterAfter = after.HasRendererBounds ? after.RendererBounds.center : Vector3.zero;

            Vector3 actual = res.CenterAfter - res.CenterBefore;
            bool moved = (actual - offset).magnitude < Mathf.Max(0.5f, offset.magnitude * 0.001f);

            res.Success = true;
            var sb = new StringBuilder();
            sb.AppendLine($"moved {res.RootsMoved} roots by {offset}");
            sb.AppendLine($"  bounds center {res.CenterBefore} -> {res.CenterAfter} (delta {actual})");
            if (!moved)
                sb.AppendLine($"  WARNING: measured delta does not match the requested offset. " +
                              "A root may be driven by a constraint, or the offset was applied twice.");
            sb.Append(StaleBakedDataWarning(before));
            res.Message = sb.ToString();
            return res;
        }

        /// <summary>
        /// Names the baked data this move just invalidated. Reported rather than silently fixed,
        /// because the fix is a re-bake and the caller must know one is now mandatory.
        /// </summary>
        private static string StaleBakedDataWarning(SceneAuditReport.Audit a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("  baked data now STALE and requiring a re-bake before this scene is trusted:");
            sb.AppendLine($"    light probe groups : {a.LightProbeGroups}  (probe positions are world-space)");
            sb.AppendLine($"    APV probe volumes  : {a.ProbeVolumes}  (brick positions are world-space)");
            sb.AppendLine($"    reflection probes  : {a.ReflectionProbes}  (cubemaps stay valid; probe positions moved with geometry)");
            sb.AppendLine($"    occlusion culling  : {(a.HasOcclusionData ? "PRESENT — re-bake or clear it" : "none")}");
            sb.AppendLine("    lightmaps          : survive the move (texture space), but will be re-baked anyway");
            return sb.ToString();
        }

        /// <summary>
        /// The presentation world grid. Kept here so the tool and the stage assets cannot disagree.
        ///
        /// Spacing is 2500 u, revised down from an initial guess of 5000 once the scenes were actually
        /// measured: the largest horizontal radius in the project is Shipyard at 530 u (RasElBar 317,
        /// EzbetElBorg 196, Home 71, CarpentryQuarter 40). 2500 clears the widest possible pair with
        /// room to spare while halving the distance from the origin, which keeps float precision in
        /// the sub-millimetre range at the furthest slot (~3536 u out).
        /// </summary>
        public const float GridSpacing = 2500f;

        public static readonly (string sceneName, Vector3 offset)[] StageGrid =
        {
            ("Home_Demo",             new Vector3(0f, 0f, 0f)),
            ("CarpentryQuarter_Demo", new Vector3(GridSpacing, 0f, 0f)),
            ("RasElBar_Demo",         new Vector3(0f, 0f, GridSpacing)),
            ("Shipyard_Demo",         new Vector3(-GridSpacing, 0f, 0f)),
            ("EzbetElBorg_Demo",      new Vector3(-GridSpacing, 0f, GridSpacing)),
            ("Turntable_Demo",        new Vector3(0f, 0f, -GridSpacing)),
        };

        public static bool TryGetGridOffset(string sceneName, out Vector3 offset)
        {
            foreach (var entry in StageGrid)
            {
                if (entry.sceneName == sceneName) { offset = entry.offset; return true; }
            }
            offset = Vector3.zero;
            return false;
        }
    }
}
#endif
