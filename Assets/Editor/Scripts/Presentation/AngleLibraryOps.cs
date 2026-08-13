#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using DumyatRuntime;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// What the angle library actually DOES, kept out of its inspector.
    ///
    /// The inspector is a set of buttons; these are the operations behind them. Separating the two means
    /// the same operations are reachable from a menu item and from any other tool, and that none of this
    /// is trapped inside an OnInspectorGUI that only runs while the asset is selected.
    /// </summary>
    public static class AngleLibraryOps
    {
        public const string DemoFolder = "Assets/Runtime/Presentation/Scenes/Demo";

        // ---- scenes -----------------------------------------------------------------------------

        public static Scene OpenDemoScene(string sceneName)
        {
            Scene already = SceneManager.GetSceneByName(sceneName);
            if (already.isLoaded) return already;

            string path = DemoFolder + "/" + sceneName + ".unity";
            if (!File.Exists(path))
            {
                Debug.LogError("[AngleLibrary] no scene at " + path);
                return default;
            }
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        public static int OpenAllDemoScenes()
        {
            int opened = 0;
            foreach (string path in Directory.GetFiles(DemoFolder, "*_Demo.unity").OrderBy(f => f))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (SceneManager.GetSceneByName(name).isLoaded) continue;
                EditorSceneManager.OpenScene(path.Replace('\\', '/'), OpenSceneMode.Additive);
                opened++;
            }
            return opened;
        }

        // ---- one angle --------------------------------------------------------------------------

        /// <summary>Pushes an entry's numbers onto its Cinemachine camera, creating and registering it.</summary>
        public static bool Apply(CameraAngleLibrarySO.Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.AngleId)) return false;

            Scene scene = SceneManager.GetSceneByName(entry.SceneName);
            if (!scene.isLoaded) return false;

            CinemachineCamera live = AngleAuthoring.FindOrCreate(scene, entry.AngleId, true);
            if (live == null) return false;

            Undo.RecordObjects(new Object[] { live, live.transform }, "Apply presentation angle");
            live.transform.SetPositionAndRotation(entry.Position, entry.Rotation);
            LensSettings lens = live.Lens;
            lens.FieldOfView = entry.FieldOfView;
            live.Lens = lens;

            EditorUtility.SetDirty(live);
            EditorSceneManager.MarkSceneDirty(scene);
            Register(scene, entry.AngleId, live);
            return true;
        }

        /// <summary>
        /// Makes sure the scene's <see cref="CameraAngleSet"/> lists this angle id.
        ///
        /// Creating the camera is NOT enough. The runtime picks angles out of that component's array, so
        /// an unregistered camera is invisible to the deck and the slide logs "has no angle 'x'" — which
        /// looks like a broken slide rather than a missing list entry.
        /// </summary>
        public static void Register(Scene scene, string angleId, CinemachineCamera cam)
        {
            if (cam == null || !scene.isLoaded || string.IsNullOrEmpty(angleId)) return;

            CameraAngleSet set = scene.GetRootGameObjects()
                .Select(g => g.GetComponentInChildren<CameraAngleSet>(true))
                .FirstOrDefault(s => s != null);

            if (set == null)
            {
                Debug.LogWarning("[AngleLibrary] '" + scene.name + "' has no CameraAngleSet, so '" +
                                 angleId + "' could not be registered — the deck will not find it.");
                return;
            }

            var so = new SerializedObject(set);
            SerializedProperty arr = so.FindProperty("_angles");

            for (int i = 0; i < arr.arraySize; i++)
            {
                SerializedProperty row = arr.GetArrayElementAtIndex(i);
                if (row.FindPropertyRelative("Id").stringValue != angleId) continue;
                // Repair rather than duplicate: a lost camera reference is the common failure here.
                row.FindPropertyRelative("Camera").objectReferenceValue = cam;
                so.ApplyModifiedProperties();
                return;
            }

            int slot = arr.arraySize;
            arr.InsertArrayElementAtIndex(slot);
            SerializedProperty added = arr.GetArrayElementAtIndex(slot);
            added.FindPropertyRelative("Id").stringValue = angleId;
            added.FindPropertyRelative("Camera").objectReferenceValue = cam;

            // Inherit the first angle's hidden-layer mask so a new shot hides the same placeholders
            // instead of silently revealing the low-poly stand-ins this deck exists to keep off screen.
            if (slot > 0)
                added.FindPropertyRelative("HiddenLayers").FindPropertyRelative("m_Bits").uintValue =
                    arr.GetArrayElementAtIndex(0).FindPropertyRelative("HiddenLayers")
                       .FindPropertyRelative("m_Bits").uintValue;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[AngleLibrary] registered '" + angleId + "' in " + scene.name + "'s CameraAngleSet.", set);
        }

        /// <summary>True when the entry and its scene camera agree within authoring tolerance.</summary>
        public static bool InSync(CameraAngleLibrarySO.Entry entry, CinemachineCamera live)
        {
            if (entry == null || live == null) return false;
            return Vector3.Distance(live.transform.position, entry.Position) < 0.01f
                && Quaternion.Angle(live.transform.rotation, entry.Rotation) < 0.1f
                && Mathf.Abs(live.Lens.FieldOfView - entry.FieldOfView) < 0.1f;
        }

        // ---- bulk -------------------------------------------------------------------------------

        public static void ApplyAll(CameraAngleLibrarySO library)
        {
            if (library == null) return;

            int done = 0, skipped = 0;
            foreach (CameraAngleLibrarySO.Entry entry in library.Entries)
            {
                if (Apply(entry)) done++;
                else skipped++;
            }

            Debug.Log("[AngleLibrary] applied " + done + " angle(s); skipped " + skipped +
                      (skipped > 0 ? " — those scenes are not open. Press \"Open All Demo Scenes\" first." : "."),
                      library);
        }

        /// <summary>
        /// Reads every <c>Angle_*</c> camera out of every demo scene into the library. Safe to re-run:
        /// rows matching on scene + id are updated in place, so notes survive and nothing is duplicated.
        /// </summary>
        public static void Rebuild(CameraAngleLibrarySO library)
        {
            if (library == null) { Debug.LogError("[AngleLibrary] no library asset."); return; }

            OpenAllDemoScenes();
            Undo.RecordObject(library, "Rebuild angle library");

            var log = new StringBuilder("[AngleLibrary] rebuild from demo scenes:\n");
            int added = 0, updated = 0;

            foreach (string file in Directory.GetFiles(DemoFolder, "*_Demo.unity").OrderBy(f => f))
            {
                string sceneName = Path.GetFileNameWithoutExtension(file);
                Scene scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.isLoaded) { log.AppendLine("   " + sceneName + " — could not open, skipped"); continue; }

                // Stage ids follow the scene name without the _Demo suffix, the same rule SceneStage
                // uses when its own id field is left empty.
                string stageId = sceneName.EndsWith("_Demo")
                    ? sceneName.Substring(0, sceneName.Length - "_Demo".Length)
                    : sceneName;

                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (CinemachineCamera cam in root.GetComponentsInChildren<CinemachineCamera>(true))
                    {
                        if (!cam.name.StartsWith(AngleAuthoring.AnglePrefix)) continue;
                        string id = cam.name.Substring(AngleAuthoring.AnglePrefix.Length);

                        CameraAngleLibrarySO.Entry entry = library.Entries.FirstOrDefault(
                            x => x.AngleId == id && x.SceneName == sceneName);

                        if (entry == null)
                        {
                            entry = new CameraAngleLibrarySO.Entry();
                            library.Entries.Add(entry);
                            added++;
                        }
                        else updated++;

                        entry.SceneName = sceneName;
                        entry.StageId = stageId;
                        entry.AngleId = id;
                        entry.Position = cam.transform.position;
                        entry.Euler = cam.transform.rotation.eulerAngles;
                        entry.FieldOfView = cam.Lens.FieldOfView;

                        log.AppendLine("   " + sceneName.PadRight(24) + id.PadRight(14) +
                                       cam.transform.position.ToString("F1") + "  fov " +
                                       cam.Lens.FieldOfView.ToString("F0"));
                    }
            }

            // Grouped by scene so the inspector's per-scene headers come out as one block each.
            library.Entries.Sort((a, b) =>
            {
                int s = string.Compare(a.SceneName, b.SceneName, System.StringComparison.Ordinal);
                return s != 0 ? s : string.Compare(a.AngleId, b.AngleId, System.StringComparison.Ordinal);
            });

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            log.AppendLine("   " + added + " added, " + updated + " updated, " +
                           library.Entries.Count + " total.");
            Debug.Log(log.ToString(), library);
        }

        /// <summary>The one library asset in the project, or null.</summary>
        public static CameraAngleLibrarySO FindLibrary()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(CameraAngleLibrarySO));
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<CameraAngleLibrarySO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ---- menu -------------------------------------------------------------------------------

        [MenuItem("Dumyat/Presentation/Angles/Select Angle Library", priority = 50)]
        private static void SelectLibrary()
        {
            CameraAngleLibrarySO lib = FindLibrary();
            if (lib == null)
            {
                Debug.LogError("[AngleLibrary] no CameraAngleLibrary asset. Create one with " +
                               "Assets ▸ Create ▸ Dumyat ▸ Presentation ▸ Camera Angle Library.");
                return;
            }
            Selection.activeObject = lib;
            EditorGUIUtility.PingObject(lib);
        }

        [MenuItem("Dumyat/Presentation/Angles/Rebuild Angle Library From Demo Scenes", priority = 51)]
        private static void RebuildMenu() => Rebuild(FindLibrary());

        [MenuItem("Dumyat/Presentation/Angles/Apply Angle Library To Demo Scenes", priority = 52)]
        private static void ApplyAllMenu()
        {
            CameraAngleLibrarySO lib = FindLibrary();
            OpenAllDemoScenes();
            ApplyAll(lib);
        }
    }
}
#endif
