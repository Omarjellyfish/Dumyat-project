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
    /// </summary>
    public static class AnglePlacementTool
    {
        private const string AngleRootName = "--- PRESENTATION ANGLES ---";
        private const string AnglePrefix = "Angle_";

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

            Transform root = EnsureAngleRoot(scene);
            int next = CountAngles(root) + 1;

            var go = new GameObject(AnglePrefix + next.ToString("00"));
            Undo.RegisterCreatedObjectUndo(go, "Create presentation angle");
            go.transform.SetParent(root, true);

            var cam = Undo.AddComponent<CinemachineCamera>(go);
            ApplyPose(cam, view);

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
            SceneView view = SceneView.lastActiveSceneView;
            if (cam == null || view == null) return;

            Undo.RecordObjects(new Object[] { cam, cam.transform }, "Update presentation angle");
            ApplyPose(cam, view);
            Debug.Log($"[AnglePlacementTool] {cam.name} re-recorded from the Scene view.", cam);
        }

        [MenuItem("Dumyat/Presentation/Angles/Update Selected Angle From Scene View", true)]
        private static bool UpdateSelectedAngleValidate() => SelectedAngle() != null;

        [MenuItem("Dumyat/Presentation/Angles/Align Scene View To Selected Angle", priority = 62)]
        private static void AlignSceneViewToAngle()
        {
            CinemachineCamera cam = SelectedAngle();
            SceneView view = SceneView.lastActiveSceneView;
            if (cam == null || view == null) return;

            // LookAt with a pivot a little ahead of the camera, because the Scene view orbits a pivot
            // rather than holding a position — dropping the pivot on the camera itself makes the view
            // unusable to nudge afterwards.
            Transform t = cam.transform;
            view.LookAt(t.position + t.forward * 10f, t.rotation, 10f);
            view.camera.fieldOfView = cam.Lens.FieldOfView;
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

        private static void ApplyPose(CinemachineCamera cam, SceneView view)
        {
            Camera src = view.camera;
            cam.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);

            // Read-modify-write so this works whether Lens is exposed as a field or a property.
            LensSettings lens = cam.Lens;
            if (!view.orthographic) lens.FieldOfView = src.fieldOfView;
            lens.NearClipPlane = Mathf.Max(0.05f, src.nearClipPlane);
            lens.FarClipPlane = Mathf.Max(lens.NearClipPlane + 1f, src.farClipPlane);
            cam.Lens = lens;

            EditorUtility.SetDirty(cam);
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        }

        private static Transform EnsureAngleRoot(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == AngleRootName) return go.transform;

            var root = new GameObject(AngleRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create angle root");
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.transform;
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
