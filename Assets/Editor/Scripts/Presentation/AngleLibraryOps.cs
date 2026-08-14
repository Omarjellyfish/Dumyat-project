#if UNITY_EDITOR
using System.Collections.Generic;
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

            CameraAngleSet set = FindAngleSet(scene);
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

        // ---- add / delete / rename --------------------------------------------------------------

        /// <summary>
        /// Appends a genuinely blank row, optionally pre-filled with the scene it was added under.
        ///
        /// Blank matters: the previous "Add Empty Angle" called InsertArrayElementAtIndex, and Unity
        /// defines that as DUPLICATING the element before it. Every "empty" angle arrived carrying the
        /// last angle's id and pose, so the first Apply silently overwrote a real shot.
        ///
        /// No camera is created here. The row has no id yet, and a camera called "Angle_" is worse than
        /// no camera — <see cref="Apply"/> makes it the moment there is an id to name it after.
        /// </summary>
        public static CameraAngleLibrarySO.Entry Create(CameraAngleLibrarySO library,
                                                        string sceneName, string stageId)
        {
            if (library == null) return null;

            Undo.RecordObject(library, "Add presentation angle");

            var entry = new CameraAngleLibrarySO.Entry
            {
                SceneName = sceneName ?? string.Empty,
                StageId = stageId ?? string.Empty,
                AngleId = string.Empty,
                FieldOfView = 52f,
            };

            // Next to its own scene's rows rather than at the very bottom, so the per-scene grouping the
            // inspector draws does not break the moment you add anything.
            int at = library.Entries.Count;
            if (!string.IsNullOrEmpty(sceneName))
            {
                for (int i = library.Entries.Count - 1; i >= 0; i--)
                {
                    if (library.Entries[i].SceneName == sceneName) { at = i + 1; break; }
                }
            }

            library.Entries.Insert(at, entry);
            EditorUtility.SetDirty(library);
            return entry;
        }

        /// <summary>
        /// Removes a row, and optionally the Cinemachine camera and CameraAngleSet entry behind it.
        ///
        /// The scene half is opt-in because the two failure modes are not symmetric: a library row with
        /// no camera is a visible "no camera" status you can fix with Apply, whereas a deleted camera is
        /// a composition somebody spent time framing and cannot get back. Callers ask first.
        /// </summary>
        public static void Delete(CameraAngleLibrarySO library, int index, bool alsoScene)
        {
            if (library == null || index < 0 || index >= library.Entries.Count) return;

            CameraAngleLibrarySO.Entry entry = library.Entries[index];

            if (alsoScene && !string.IsNullOrEmpty(entry.AngleId))
            {
                Scene scene = SceneManager.GetSceneByName(entry.SceneName);
                if (scene.isLoaded)
                {
                    Unregister(scene, entry.AngleId);

                    CinemachineCamera live = AngleAuthoring.FindOrCreate(scene, entry.AngleId, false);
                    if (live != null) Undo.DestroyObjectImmediate(live.gameObject);

                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log("[AngleLibrary] deleted '" + entry.AngleId + "' from '" + entry.SceneName +
                              "'. Save the scene to keep it.");
                }
                else
                {
                    Debug.LogWarning("[AngleLibrary] removed '" + entry.AngleId + "' from the library, " +
                                     "but '" + entry.SceneName + "' is not open so the camera and its " +
                                     "CameraAngleSet entry are still there.");
                }
            }

            Undo.RecordObject(library, "Delete presentation angle");
            library.Entries.RemoveAt(index);
            EditorUtility.SetDirty(library);
        }

        /// <summary>
        /// Renames an angle everywhere its id is written down: this row, the <c>Angle_*</c> GameObject,
        /// the scene's <see cref="CameraAngleSet"/>, and every slide that asks for it.
        ///
        /// WHY ALL FOUR OR NONE: the id is a string join, not a reference. Renaming the row alone leaves
        /// a row pointing at nothing; renaming the camera alone makes every slide using it log "has no
        /// angle" mid-talk. There is no useful halfway state, so this does the whole set and reports
        /// what it could not reach rather than leaving you to find out from the projector.
        ///
        /// Slides are matched on STAGE PLUS id, not id alone. Angle ids only have to be unique within
        /// their own stage — two places may both reasonably own a "wide" — so matching on the id by
        /// itself would rename another stage's slide.
        /// </summary>
        public static bool Rename(CameraAngleLibrarySO library, CameraAngleLibrarySO.Entry entry,
                                  string newId)
        {
            if (library == null || entry == null) return false;

            newId = (newId ?? string.Empty).Trim();
            string oldId = entry.AngleId ?? string.Empty;

            if (newId.Length == 0)
            {
                Debug.LogWarning("[AngleLibrary] an angle id cannot be empty.");
                return false;
            }
            if (newId == oldId) return true;

            foreach (CameraAngleLibrarySO.Entry other in library.Entries)
            {
                if (!ReferenceEquals(other, entry) &&
                    other.SceneName == entry.SceneName &&
                    string.Equals(other.AngleId, newId, System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[AngleLibrary] '" + entry.SceneName + "' already has an angle '" +
                                     newId + "'. Ids must be unique within a scene — the runtime picks " +
                                     "the first match.");
                    return false;
                }
            }

            var log = new StringBuilder("[AngleLibrary] renamed '" + oldId + "' → '" + newId + "':\n");

            Undo.RecordObject(library, "Rename presentation angle");
            entry.AngleId = newId;
            EditorUtility.SetDirty(library);
            log.AppendLine("   library row updated.");

            Scene scene = SceneManager.GetSceneByName(entry.SceneName);
            if (scene.isLoaded)
            {
                CinemachineCamera live = AngleAuthoring.FindOrCreate(scene, oldId, false);
                if (live != null)
                {
                    Undo.RecordObject(live.gameObject, "Rename presentation angle");
                    live.gameObject.name = AngleAuthoring.AnglePrefix + newId;
                    log.AppendLine("   camera renamed to " + live.gameObject.name + ".");
                }
                else log.AppendLine("   no camera called " + AngleAuthoring.AnglePrefix + oldId +
                                    " in the scene — nothing to rename.");

                if (RenameInAngleSet(scene, oldId, newId)) log.AppendLine("   CameraAngleSet updated.");
                EditorSceneManager.MarkSceneDirty(scene);
            }
            else
            {
                log.AppendLine("   ! '" + entry.SceneName + "' is not open, so the camera and its " +
                               "CameraAngleSet entry still use '" + oldId + "'. Open the scene and " +
                               "rename again, or the deck will not find this angle.");
            }

            int slides = RetargetSlides(entry.StageId, oldId, newId);
            log.AppendLine("   " + slides + " slide(s) repointed at the new id.");

            Debug.Log(log.ToString(), library);
            return true;
        }

        /// <summary>Drops an angle id out of a scene's <see cref="CameraAngleSet"/>.</summary>
        private static void Unregister(Scene scene, string angleId)
        {
            CameraAngleSet set = FindAngleSet(scene);
            if (set == null) return;

            var so = new SerializedObject(set);
            SerializedProperty arr = so.FindProperty("_angles");

            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                if (!string.Equals(arr.GetArrayElementAtIndex(i).FindPropertyRelative("Id").stringValue,
                                   angleId, System.StringComparison.OrdinalIgnoreCase)) continue;

                arr.DeleteArrayElementAtIndex(i);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
        }

        private static bool RenameInAngleSet(Scene scene, string oldId, string newId)
        {
            CameraAngleSet set = FindAngleSet(scene);
            if (set == null) return false;

            var so = new SerializedObject(set);
            SerializedProperty arr = so.FindProperty("_angles");
            bool touched = false;

            for (int i = 0; i < arr.arraySize; i++)
            {
                SerializedProperty id = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Id");
                if (!string.Equals(id.stringValue, oldId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                id.stringValue = newId;
                touched = true;
            }

            if (!touched) return false;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
            return true;
        }

        private static CameraAngleSet FindAngleSet(Scene scene)
        {
            if (!scene.isLoaded) return null;
            return scene.GetRootGameObjects()
                        .Select(g => g.GetComponentInChildren<CameraAngleSet>(true))
                        .FirstOrDefault(s => s != null);
        }

        /// <summary>
        /// Every scene slide asking a stage for an angle id. Used to warn before a delete and to
        /// repoint after a rename — the reference is a string, so nothing else would notice it break.
        /// </summary>
        public static List<SceneShowcaseSlideSO> SlidesUsing(string stageId, string angleId)
        {
            var hits = new List<SceneShowcaseSlideSO>();
            if (string.IsNullOrEmpty(angleId)) return hits;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SceneShowcaseSlideSO)))
            {
                var slide = AssetDatabase.LoadAssetAtPath<SceneShowcaseSlideSO>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (slide == null) continue;
                if (!string.Equals(slide.AngleId, angleId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // An empty stage id on the entry cannot disambiguate, so it matches on the angle alone.
                if (!string.IsNullOrEmpty(stageId) &&
                    !string.Equals(slide.StageId, stageId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                hits.Add(slide);
            }

            return hits;
        }

        private static int RetargetSlides(string stageId, string oldId, string newId)
        {
            List<SceneShowcaseSlideSO> slides = SlidesUsing(stageId, oldId);

            foreach (SceneShowcaseSlideSO slide in slides)
            {
                var so = new SerializedObject(slide);
                so.FindProperty("_angleId").stringValue = newId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(slide);
            }

            if (slides.Count > 0) AssetDatabase.SaveAssets();
            return slides.Count;
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
