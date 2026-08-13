#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// The angle authoring surface: one row per shot, with Go To and Capture on it.
    ///
    /// THE WORKFLOW THIS EXISTS FOR:
    /// press Go To, nudge the Scene view until the composition is right, press Capture. That is the whole
    /// loop, and it is the only honest way to compose a shot — you cannot judge a frame by typing numbers
    /// into a transform. The numeric fields are still there for when you already know the value.
    ///
    /// Every row reports whether the asset and the scene agree, because this asset is a catalogue over
    /// the real Cinemachine cameras rather than the thing the deck reads. A silent disagreement between
    /// the two is a shot that looks right in the library and wrong on the projector.
    ///
    /// All the actual work lives in <see cref="AngleLibraryOps"/>; this file is buttons and layout.
    /// </summary>
    [CustomEditor(typeof(CameraAngleLibrarySO))]
    public sealed class CameraAngleLibraryEditor : Editor
    {
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var library = (CameraAngleLibrarySO)target;

            DrawToolbar(library);
            EditorGUILayout.Space(6);

            SerializedProperty entries = serializedObject.FindProperty("_entries");
            if (entries.arraySize == 0)
                EditorGUILayout.HelpBox(
                    "No angles yet. Press \"Rebuild From Demo Scenes\" to read every Angle_* camera that " +
                    "already exists in the demo scenes into this list.", MessageType.Info);

            string currentScene = null;
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);
                string sceneName = e.FindPropertyRelative("SceneName").stringValue;

                if (sceneName != currentScene)
                {
                    currentScene = sceneName;
                    EditorGUILayout.Space(8);
                    bool loaded = SceneManager.GetSceneByName(sceneName).isLoaded;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(sceneName + (loaded ? "   (open)" : "   (not open)"),
                                                   EditorStyles.boldLabel);
                        if (!loaded && GUILayout.Button("Open", GUILayout.Width(60)))
                            AngleLibraryOps.OpenDemoScene(sceneName);
                    }
                }

                if (DrawEntry(e, i)) return;   // list changed under us; bail out of this repaint
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Add Empty Angle"))
                entries.InsertArrayElementAtIndex(entries.arraySize);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawToolbar(CameraAngleLibrarySO library)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Angle library", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Rebuild From Demo Scenes",
                            "Opens every *_Demo scene additively and reads all Angle_* cameras into this " +
                            "list. Safe to re-run — matching rows are updated in place.")))
                        AngleLibraryOps.Rebuild(library);

                    if (GUILayout.Button(new GUIContent("Apply All To Scenes",
                            "Pushes every row onto its Cinemachine camera, creating and registering the " +
                            "camera where it is missing.")))
                    {
                        AngleLibraryOps.OpenAllDemoScenes();
                        AngleLibraryOps.ApplyAll(library);
                    }

                    if (GUILayout.Button(new GUIContent("Open All Demo Scenes",
                            "Loads every demo scene additively so Go To works for any row.")))
                        AngleLibraryOps.OpenAllDemoScenes();
                }
                EditorGUILayout.LabelField(
                    library.Entries.Count + " angle(s).   Go To → nudge the Scene view → Capture.",
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>Returns true if the entry list was structurally changed and layout must stop.</summary>
        private bool DrawEntry(SerializedProperty e, int index)
        {
            SerializedProperty sceneName = e.FindPropertyRelative("SceneName");
            SerializedProperty angleId = e.FindPropertyRelative("AngleId");
            SerializedProperty pos = e.FindPropertyRelative("Position");
            SerializedProperty euler = e.FindPropertyRelative("Euler");
            SerializedProperty fov = e.FindPropertyRelative("FieldOfView");

            Scene scene = SceneManager.GetSceneByName(sceneName.stringValue);
            CinemachineCamera live = scene.isLoaded
                ? AngleAuthoring.FindOrCreate(scene, angleId.stringValue, false)
                : null;

            string key = sceneName.stringValue + "/" + angleId.stringValue + "/" + index;
            if (!_expanded.TryGetValue(key, out bool open)) open = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    open = EditorGUILayout.Foldout(open,
                        string.IsNullOrEmpty(angleId.stringValue) ? "(unnamed)" : angleId.stringValue,
                        true, EditorStyles.foldoutHeader);
                    _expanded[key] = open;

                    GUILayout.FlexibleSpace();
                    DrawStatus(live, pos, euler, fov, scene.isLoaded);

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(angleId.stringValue)))
                    {
                        if (GUILayout.Button(new GUIContent("Go To",
                                "Puts the Scene view exactly on this angle — same position, same " +
                                "rotation, same vertical field of view — opening its demo scene if " +
                                "needed.\n\nVertical framing matches exactly; how much you see left and " +
                                "right depends on your Scene view window shape. Make it roughly 16:9 to " +
                                "see the crop the audience gets."),
                                GUILayout.Width(56)))
                            GoTo(sceneName.stringValue, angleId.stringValue, pos, euler, fov);

                        if (GUILayout.Button(new GUIContent("Capture",
                                "Records the current Scene view into this row AND onto the scene camera, " +
                                "so the two cannot disagree."), GUILayout.Width(66)))
                            Capture(sceneName.stringValue, angleId.stringValue, pos, euler, fov);
                    }
                }

                if (!open) return false;

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(sceneName);
                EditorGUILayout.PropertyField(e.FindPropertyRelative("StageId"));
                EditorGUILayout.PropertyField(angleId);
                EditorGUILayout.PropertyField(pos);
                EditorGUILayout.PropertyField(euler);
                EditorGUILayout.PropertyField(fov);
                EditorGUILayout.PropertyField(e.FindPropertyRelative("Note"));

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!scene.isLoaded))
                    {
                        if (GUILayout.Button(new GUIContent("Apply → Scene",
                                "Writes these numbers onto the Cinemachine camera, creating and " +
                                "registering it if it does not exist yet.")))
                        {
                            serializedObject.ApplyModifiedProperties();
                            var lib = (CameraAngleLibrarySO)target;
                            if (index < lib.Entries.Count) AngleLibraryOps.Apply(lib.Entries[index]);
                        }

                        using (new EditorGUI.DisabledScope(live == null))
                            if (GUILayout.Button(new GUIContent("Pull ← Scene",
                                    "Overwrites these numbers from the camera in the scene.")))
                            {
                                pos.vector3Value = live.transform.position;
                                euler.vector3Value = live.transform.rotation.eulerAngles;
                                fov.floatValue = live.Lens.FieldOfView;
                            }
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(70)) &&
                        EditorUtility.DisplayDialog("Remove angle",
                            "Remove '" + angleId.stringValue + "' from the library?\n\n" +
                            "This edits the library only. The camera in the scene, its CameraAngleSet " +
                            "entry, and any slide pointing at this angle id are all left alone.",
                            "Remove", "Cancel"))
                    {
                        serializedObject.FindProperty("_entries").DeleteArrayElementAtIndex(index);
                        serializedObject.ApplyModifiedProperties();
                        EditorGUI.indentLevel--;
                        return true;
                    }
                }
                EditorGUI.indentLevel--;
            }
            return false;
        }

        private static void DrawStatus(CinemachineCamera live, SerializedProperty pos,
                                       SerializedProperty euler, SerializedProperty fov, bool sceneLoaded)
        {
            string text;
            Color colour;

            if (!sceneLoaded) { text = "scene closed"; colour = new Color(0.62f, 0.62f, 0.62f); }
            else if (live == null) { text = "no camera"; colour = new Color(1f, 0.61f, 0.54f); }
            else
            {
                bool same =
                    Vector3.Distance(live.transform.position, pos.vector3Value) < 0.01f &&
                    Quaternion.Angle(live.transform.rotation, Quaternion.Euler(euler.vector3Value)) < 0.1f &&
                    Mathf.Abs(live.Lens.FieldOfView - fov.floatValue) < 0.1f;
                text = same ? "in sync" : "differs";
                colour = same ? new Color(0.55f, 0.92f, 0.69f) : new Color(1f, 0.7f, 0.42f);
            }

            Color prev = GUI.contentColor;
            GUI.contentColor = colour;
            EditorGUILayout.LabelField(text, EditorStyles.miniLabel, GUILayout.Width(80));
            GUI.contentColor = prev;
        }

        private void GoTo(string sceneName, string angleId, SerializedProperty pos,
                          SerializedProperty euler, SerializedProperty fov)
        {
            Scene scene = AngleLibraryOps.OpenDemoScene(sceneName);
            if (!scene.isLoaded) return;

            // Prefer the camera actually in the scene: that is what the deck renders, so that is what you
            // should be judging. The stored numbers are only the fallback for a shot not built yet.
            CinemachineCamera live = AngleAuthoring.FindOrCreate(scene, angleId, false);
            if (live != null)
            {
                AngleAuthoring.AlignSceneViewTo(live);
                Selection.activeGameObject = live.gameObject;
                EditorGUIUtility.PingObject(live.gameObject);
                return;
            }

            AngleAuthoring.AlignSceneViewTo(pos.vector3Value, Quaternion.Euler(euler.vector3Value),
                                            fov.floatValue);
            Debug.LogWarning("[AngleLibrary] '" + angleId + "' has no camera in '" + sceneName +
                             "' yet — flew to the stored pose instead. Press \"Apply → Scene\" to create it.");
        }

        private void Capture(string sceneName, string angleId, SerializedProperty pos,
                             SerializedProperty euler, SerializedProperty fov)
        {
            SceneView view = AngleAuthoring.ActiveView(out string error);
            if (view == null) { Debug.LogWarning("[AngleLibrary] " + error); return; }

            Camera src = view.camera;
            pos.vector3Value = src.transform.position;
            euler.vector3Value = src.transform.rotation.eulerAngles;
            if (!view.orthographic) fov.floatValue = src.fieldOfView;
            serializedObject.ApplyModifiedProperties();

            // Write through to the scene in the same click. Capturing into the library alone would leave
            // the row reading "differs" while the projector still showed the old shot — that two-step is
            // exactly the drift this tool exists to prevent.
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.isLoaded)
            {
                Debug.LogWarning("[AngleLibrary] captured '" + angleId + "' into the library, but '" +
                                 sceneName + "' is not open so the scene camera was NOT updated. Open the " +
                                 "scene and press \"Apply → Scene\".");
                return;
            }

            CinemachineCamera live = AngleAuthoring.FindOrCreate(scene, angleId, true);
            if (live == null) return;
            AngleAuthoring.WritePose(live, view);
            AngleLibraryOps.Register(scene, angleId, live);
            Debug.Log("[AngleLibrary] captured '" + angleId + "' into the library and onto " + live.name +
                      " in '" + sceneName + "'. Save the scene to keep it.", live);
        }
    }
}
#endif
