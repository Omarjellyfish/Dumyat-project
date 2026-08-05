#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// Builds the invisible wall that keeps the player inside a scene.
    ///
    /// WHY A TOOL AND NOT HAND-PLACED BOXES
    /// ------------------------------------
    /// Every one of these scenes sits on an enormous water or ground plane — 142 m in First_room,
    /// 657 m in El_manara, 848 m in Shipyard — and every one of those planes HAS a collider. So the
    /// player never falls through the world; they simply walk out onto open water and keep going until
    /// the scene is a dot behind them. A hand-placed wall would have to be re-measured every time the
    /// set dressing moves, and there are five scenes.
    ///
    /// This derives the play area from the built environment instead: the union of every solid collider
    /// SMALL enough to be a real prop or building, which excludes the ground/water planes precisely
    /// because they are the things the player should not be walking out across. Walls then go up around
    /// that union, plus a floor lid below it so nothing can drop out the bottom.
    ///
    /// The result is ordinary GameObjects with BoxColliders — no runtime component, no cost, and a
    /// designer can drag any wall afterwards. Re-running replaces them, so it is safe to iterate.
    /// </summary>
    public static class PlayerBoundaryBuilder
    {
        private const string RootName = "PlayerBounds (generated)";

        /// <summary>
        /// Colliders wider than this on either horizontal axis are treated as the ground/water plane
        /// and left out of the play area. Comfortably larger than any building in these scenes and far
        /// smaller than the 142 m+ planes.
        /// </summary>
        private const float GroundPlaneSize = 90f;

        private const float WallHeight = 12f;
        private const float WallThickness = 1f;

        /// <summary>Breathing room outside the built geometry, so walls do not clip the buildings.</summary>
        private const float Margin = 2f;

        [MenuItem("Dumyat/Player/Build Boundary For Open Scene")]
        private static void BuildForOpenScene()
        {
            string report = Build(SceneManager.GetActiveScene());
            Debug.Log(report);
        }

        /// <summary>
        /// Puts a boundary around <paramref name="scene"/> and returns what it did. Public so it can be
        /// driven per-scene from a script rather than clicked five times.
        /// </summary>
        public static string Build(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return $"boundary: scene '{scene.name}' not loaded";

            RemoveExisting(scene);

            if (!TryMeasurePlayArea(scene, out Bounds area, out int counted, out int ignored))
                return $"boundary: '{scene.name}' has no solid geometry to measure — skipped";

            area.Expand(new Vector3(Margin * 2f, 0f, Margin * 2f));

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.isStatic = true;

            float top = area.max.y + WallHeight;
            float bottom = area.min.y;
            float midY = (top + bottom) * 0.5f;
            float height = top - bottom;

            // Four walls, each a slab standing just outside the play area.
            AddWall(root, "Wall +X", new Vector3(area.max.x + WallThickness * 0.5f, midY, area.center.z),
                    new Vector3(WallThickness, height, area.size.z + WallThickness * 2f));
            AddWall(root, "Wall -X", new Vector3(area.min.x - WallThickness * 0.5f, midY, area.center.z),
                    new Vector3(WallThickness, height, area.size.z + WallThickness * 2f));
            AddWall(root, "Wall +Z", new Vector3(area.center.x, midY, area.max.z + WallThickness * 0.5f),
                    new Vector3(area.size.x + WallThickness * 2f, height, WallThickness));
            AddWall(root, "Wall -Z", new Vector3(area.center.x, midY, area.min.z - WallThickness * 0.5f),
                    new Vector3(area.size.x + WallThickness * 2f, height, WallThickness));

            // Lid underneath: the last resort against a hole in the floor, well below anything walkable
            // so it never interferes with stairs, pits or the quayside drop.
            AddWall(root, "Floor Lid", new Vector3(area.center.x, bottom - WallThickness * 0.5f, area.center.z),
                    new Vector3(area.size.x + WallThickness * 2f, WallThickness, area.size.z + WallThickness * 2f));

            EditorSceneManager.MarkSceneDirty(scene);

            return $"boundary '{scene.name}': area centre={area.center:F1} size={area.size:F1} " +
                   $"(from {counted} colliders, {ignored} oversized planes ignored)";
        }

        // The union of solid geometry that is plausibly part of the built set, not the world plane.
        private static bool TryMeasurePlayArea(Scene scene, out Bounds area, out int counted, out int ignored)
        {
            area = default;
            counted = 0;
            ignored = 0;
            bool started = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == RootName) continue;

                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;

                    Bounds b = c.bounds;
                    if (b.size.x > GroundPlaneSize || b.size.z > GroundPlaneSize) { ignored++; continue; }

                    counted++;
                    if (!started) { area = b; started = true; }
                    else area.Encapsulate(b);
                }
            }

            return started;
        }

        private static void AddWall(GameObject parent, string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = centre;
            go.isStatic = true;

            var box = go.AddComponent<BoxCollider>();
            box.size = size;
        }

        private static void RemoveExisting(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects().ToList())
                if (root.name == RootName) Object.DestroyImmediate(root);
        }
    }
}
#endif
