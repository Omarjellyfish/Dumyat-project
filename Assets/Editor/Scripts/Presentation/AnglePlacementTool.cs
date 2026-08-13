#if UNITY_EDITOR
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// Authors presentation camera angles by flying the Scene view to a shot you like and recording it.
    ///
    /// WHY THIS RATHER THAN DRAGGING CAMERAS AROUND THE HIERARCHY:
    /// eighteen angles across five stages have to be composed by eye, and the Scene view is the only
    /// place you can actually see the composition while you move. Recording the view means the shot
    /// you approved is the shot that ships, to the pixel — no re-nudging a camera in the Inspector and
    /// wondering why it drifted.
    ///
    /// Angles land under one clearly-named root per scene so <see cref="DemoSceneStripper"/> leaves
    /// them alone and Phase 3's stage component can collect them without a search.
    ///
    /// The pose reading and Scene view flying live in <see cref="AngleAuthoring"/>, shared with the
    /// angle library inspector — see the note there for why they must not be duplicated. This class is
    /// now just the keyboard-driven front-end: record a brand new angle without opening an asset.
    /// </summary>
    public static class AnglePlacementTool
    {
        private const string AngleRootName = AngleAuthoring.AngleRootName;
        private const string AnglePrefix = AngleAuthoring.AnglePrefix;

        [MenuItem("Dumyat/Presentation/Angles/Create Angle From Scene View %#a", priority = 60)]
        private static void CreateAngle()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
            {
                Debug.LogError("[AnglePlacementTool] No active Scene view to record. Click in the Scene " +
                               "view first.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded)
            {
                Debug.LogError("[AnglePlacementTool] No active scene.");
                return;
            }

            Transform root = AngleAuthoring.EnsureAngleRoot(scene);
            int next = CountAngles(root) + 1;

            var go = new GameObject(AnglePrefix + next.ToString("00"));
            Undo.RegisterCreatedObjectUndo(go, "Create presentation angle");
            go.transform.SetParent(root, true);

            var cam = Undo.AddComponent<CinemachineCamera>(go);
            AngleAuthoring.WritePose(cam, view);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log($"[AnglePlacementTool] recorded {go.name} in '{scene.name}' at " +
                      $"{go.transform.position.ToString("F1")}, fov {cam.Lens.FieldOfView:F1}°. " +
                      "Priority left at 0 — the stage raises it when the slide is live.", go);
        }

        [MenuItem("Dumyat/Presentation/Angles/Update Selected Angle From Scene View", priority = 61)]
        private static void UpdateSelectedAngle()
        {
            CinemachineCamera cam = SelectedAngle();
            if (cam == null) return;

            if (AngleAuthoring.CaptureFromSceneView(cam))
                Debug.Log($"[AnglePlacementTool] {cam.name} re-recorded from the Scene view. If it is " +
                          "listed in a CameraAngleLibrary, press Pull there to match.", cam);
        }

        [MenuItem("Dumyat/Presentation/Angles/Update Selected Angle From Scene View", true)]
        private static bool UpdateSelectedAngleValidate() => SelectedAngle() != null;

        [MenuItem("Dumyat/Presentation/Angles/Align Scene View To Selected Angle", priority = 62)]
        private static void AlignSceneViewToAngle()
        {
            CinemachineCamera cam = SelectedAngle();
            if (cam == null) return;

            if (AngleAuthoring.AlignSceneViewTo(cam))
                Debug.Log($"[AnglePlacementTool] Scene view aligned to {cam.name}.", cam);
        }

        [MenuItem("Dumyat/Presentation/Angles/Align Scene View To Selected Angle", true)]
        private static bool AlignValidate() => SelectedAngle() != null;

        [MenuItem("Dumyat/Presentation/Angles/List Angles In Active Scene", priority = 63)]
        private static void ListAngles()
        {
            Scene scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder($"=== angles in '{scene.name}' ===\n");
            int n = 0;

            foreach (GameObject rootGo in scene.GetRootGameObjects())
            {
                foreach (CinemachineCamera cam in rootGo.GetComponentsInChildren<CinemachineCamera>(true))
                {
                    n++;
                    sb.AppendLine($"  {cam.name.PadRight(16)} pos {cam.transform.position.ToString("F0")}" +
                                  $"  fov {cam.Lens.FieldOfView:F0}°" +
                                  $"  enabled {cam.enabled}");
                }
            }

            sb.AppendLine($"  {n} angle(s).");
            Debug.Log(sb.ToString());
        }

        private static int CountAngles(Transform root)
        {
            int n = 0;
            foreach (CinemachineCamera _ in root.GetComponentsInChildren<CinemachineCamera>(true)) n++;
            return n;
        }

        private static CinemachineCamera SelectedAngle()
        {
            GameObject go = Selection.activeGameObject;
            return go != null ? go.GetComponent<CinemachineCamera>() : null;
        }
    }
}
#endif
