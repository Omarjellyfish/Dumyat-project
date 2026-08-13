#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// Puts a demo scene's content roots back to ACTIVE on disk.
    ///
    /// WHY THIS IS NEEDED, AND HOW THE DAMAGE HAPPENS:
    /// <c>SceneStage.Awake</c> deactivates every content root so six resident stages do not all render at
    /// once. That is correct at runtime. But if a demo scene is SAVED while play mode is running, that
    /// asleep state is written to disk, and the scene now opens empty in the editor.
    ///
    /// The deck keeps working, which is what makes it nasty: <c>SetLive(true)</c> re-activates the roots
    /// when a stage goes live, so a presenter sees nothing wrong. What breaks is every edit-mode task —
    /// framing an angle, capturing a reference, or inspecting anything. Worst of all,
    /// <c>PlaneGenerator</c> builds the water meshes in <c>OnEnable</c> and marks them
    /// <c>HideFlags.DontSave</c>, so a scene whose roots are asleep has NO water meshes at all: 127
    /// MeshFilters with a null mesh, correct bounds, and nothing to rasterise. That is exactly how "the
    /// water shader scene is not showing the water" happens with no error anywhere.
    ///
    /// NEVER call EditorSceneManager.SaveScene while EditorApplication.isPlaying. This tool exists
    /// because that rule was broken once.
    /// </summary>
    public static class DemoSceneRootRepair
    {
        private const string DemoFolder = "Assets/Runtime/Presentation/Scenes/Demo";

        /// <summary>The stage marker root is meant to stay as authored; everything else should be awake.</summary>
        private const string StageRootName = "--- STAGE ---";

        [MenuItem("Dumyat/Presentation/Repair Demo Scene Roots", priority = 30)]
        private static void Repair() => Run(false);

        [MenuItem("Dumyat/Presentation/Audit Demo Scene Roots", priority = 31)]
        private static void Audit() => Run(true);

        private static void Run(bool auditOnly)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[DemoSceneRootRepair] Stop play mode first. Opening and saving scenes " +
                               "during play mode is the very thing that causes this damage.");
                return;
            }

            if (!Directory.Exists(DemoFolder))
            {
                Debug.LogError("[DemoSceneRootRepair] no folder at " + DemoFolder);
                return;
            }

            // Everything the user had open is replaced, so ask before throwing away unsaved work.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var log = new StringBuilder(auditOnly
                ? "[DemoSceneRootRepair] audit:\n"
                : "[DemoSceneRootRepair] repair:\n");

            int scenesTouched = 0, rootsTouched = 0;

            foreach (string file in Directory.GetFiles(DemoFolder, "*_Demo.unity").OrderBy(f => f))
            {
                string path = file.Replace('\\', '/');
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (!scene.isLoaded) { log.AppendLine("   could not open " + path); continue; }

                GameObject[] roots = scene.GetRootGameObjects();
                var asleep = roots.Where(g => !g.activeSelf && g.name != StageRootName).ToList();

                log.AppendLine("   " + scene.name.PadRight(24) + roots.Length + " roots, " +
                               asleep.Count + " asleep" +
                               (asleep.Count == 0 ? "  (fine)" : "  -> " + string.Join(", ", asleep.Select(g => g.name))));

                if (asleep.Count > 0 && !auditOnly)
                {
                    foreach (GameObject g in asleep)
                    {
                        Undo.RecordObject(g, "Repair demo scene root");
                        g.SetActive(true);
                        rootsTouched++;
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    scenesTouched++;
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            log.AppendLine(auditOnly
                ? "   audit only — nothing was changed."
                : "   re-enabled " + rootsTouched + " root(s) across " + scenesTouched + " scene(s).");

            Debug.Log(log.ToString());
        }
    }
}
#endif
