#if UNITY_EDITOR
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// The two operations every angle-authoring workflow needs: put an angle onto the Scene view
    /// ("go to"), and put the Scene view onto an angle ("capture").
    ///
    /// WHY THIS IS SHARED RATHER THAN LIVING IN THE TOOL THAT FIRST NEEDED IT:
    /// there are now two front-ends for the same job — the menu items in
    /// <see cref="AnglePlacementTool"/> and the per-row buttons on the angle library inspector. If each
    /// read the Scene view camera its own way they would drift, and the whole point of recording a shot
    /// is that the composition you approved is the one that ships. One implementation, to the pixel.
    /// </summary>
    internal static class AngleAuthoring
    {
        public const string AngleRootName = "--- PRESENTATION ANGLES ---";
        public const string AnglePrefix = "Angle_";

        /// <summary>
        /// How far ahead of the camera the Scene view's orbit pivot is dropped.
        ///
        /// The Scene view orbits a pivot rather than holding a position, so putting the pivot on the
        /// camera itself leaves a view that cannot be nudged afterwards — every drag spins in place.
        /// Ten metres ahead keeps the composition and stays draggable.
        /// </summary>
        private const float PivotDistance = 10f;

        /// <summary>The Scene view to author against, or null with a reason worth showing the user.</summary>
        public static SceneView ActiveView(out string error)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
            {
                error = "No active Scene view. Click in the Scene view once, then try again.";
                return null;
            }

            error = null;
            return view;
        }

        /// <summary>
        /// Scene view camera → Cinemachine camera. Returns false (with the reason logged) if there is no
        /// Scene view to read.
        /// </summary>
        public static bool CaptureFromSceneView(CinemachineCamera cam)
        {
            if (cam == null) return false;

            SceneView view = ActiveView(out string error);
            if (view == null) { Debug.LogWarning("[AngleAuthoring] " + error); return false; }

            Undo.RecordObjects(new Object[] { cam, cam.transform }, "Capture presentation angle");
            WritePose(cam, view);
            return true;
        }

        /// <summary>
        /// Writes a Scene view's pose and lens onto a Cinemachine camera. Read-modify-write on the lens
        /// so this works whether Lens is exposed as a field or a property.
        /// </summary>
        public static void WritePose(CinemachineCamera cam, SceneView view)
        {
            Camera src = view.camera;
            cam.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);

            LensSettings lens = cam.Lens;
            // An orthographic Scene view has no meaningful field of view to copy, so the authored one
            // is kept rather than overwritten with a nonsense number.
            if (!view.orthographic) lens.FieldOfView = src.fieldOfView;
            lens.NearClipPlane = Mathf.Max(0.05f, src.nearClipPlane);
            lens.FarClipPlane = Mathf.Max(lens.NearClipPlane + 1f, src.farClipPlane);
            cam.Lens = lens;

            EditorUtility.SetDirty(cam);
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        }

        /// <summary>
        /// Puts the Scene view exactly where the angle is, looking exactly where it looks, through the
        /// same vertical field of view — so the shot you nudge is the shot the deck renders. This is
        /// "go to".
        ///
        /// WHY THIS IS NOT SIMPLY LookAt(position, rotation, someDistance):
        /// the Scene view does not store a camera position. It stores a PIVOT, a rotation and a
        /// <c>size</c>, and derives the camera from them — <c>cameraDistance = size / tan(fov / 2)</c>.
        /// So <c>size</c> is NOT a distance. Passing one puts the camera somewhere near the angle,
        /// off by whatever the Scene view's field of view happened to be, which is why the first version
        /// of this framed approximately the right shot and never exactly the right one.
        ///
        /// The fix is to solve for <c>size</c> instead of guessing it: set the field of view, place the
        /// pivot a fixed distance ahead of the angle, ask the Scene view what camera distance a trial
        /// size actually produced, and scale by the ratio. The relationship is linear so one correction
        /// lands exactly — and reading <see cref="SceneView.cameraDistance"/> back rather than
        /// re-deriving Unity's formula means this keeps working if that formula ever changes.
        ///
        /// ONE HONEST LIMIT: Unity's field of view is VERTICAL. Position, rotation and vertical framing
        /// match exactly; how much you see left and right depends on the shape of your Scene view window.
        /// Make it roughly 16:9 to see the crop the audience will get.
        /// </summary>
        public static bool AlignSceneViewTo(Vector3 position, Quaternion rotation, float fov)
        {
            SceneView view = ActiveView(out string error);
            if (view == null) { Debug.LogWarning("[AngleAuthoring] " + error); return false; }

            view.orthographic = false;
            if (fov > 1f) view.cameraSettings.fieldOfView = fov;

            Vector3 pivot = position + rotation * Vector3.forward * PivotDistance;

            // Trial, measure, correct.
            view.LookAt(pivot, rotation, PivotDistance, false, true);
            float measured = view.cameraDistance;
            if (measured > 0.0001f)
                view.LookAt(pivot, rotation, PivotDistance * (PivotDistance / measured), false, true);

            view.Repaint();
            return true;
        }

        public static bool AlignSceneViewTo(CinemachineCamera cam)
        {
            if (cam == null) return false;
            Transform t = cam.transform;
            return AlignSceneViewTo(t.position, t.rotation, cam.Lens.FieldOfView);
        }

        /// <summary>The angle root for a scene, created if absent.</summary>
        public static Transform EnsureAngleRoot(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == AngleRootName) return go.transform;

            var root = new GameObject(AngleRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create angle root");
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.transform;
        }

        /// <summary>
        /// Finds the Cinemachine camera for an angle id in a loaded scene, optionally creating it.
        /// Matching is by object name (<c>Angle_&lt;id&gt;</c>) because an asset cannot hold a reference
        /// to a scene object — which is the whole reason the library stores poses by value.
        /// </summary>
        public static CinemachineCamera FindOrCreate(Scene scene, string angleId, bool create)
        {
            if (!scene.isLoaded || string.IsNullOrEmpty(angleId)) return null;

            string wanted = AnglePrefix + angleId;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CinemachineCamera cam in root.GetComponentsInChildren<CinemachineCamera>(true))
                    if (cam.name == wanted) return cam;

            if (!create) return null;

            var go = new GameObject(wanted);
            Undo.RegisterCreatedObjectUndo(go, "Create presentation angle");
            go.transform.SetParent(EnsureAngleRoot(scene), true);
            var created = Undo.AddComponent<CinemachineCamera>(go);
            // Priority stays at rest — the stage raises it when the slide goes live.
            created.Priority = 0;
            EditorSceneManager.MarkSceneDirty(scene);
            return created;
        }
    }
}
#endif
