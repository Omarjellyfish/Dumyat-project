#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// Every presentation camera angle in the deck, in one asset you can open, read, diff and edit —
    /// instead of hunting six additively-loaded demo scenes for a GameObject called Angle_something.
    ///
    /// WHY POSES ARE STORED BY VALUE AND NOT AS REFERENCES:
    /// a ScriptableObject lives in the asset database and the Cinemachine cameras live in scenes, and
    /// Unity cannot serialize a reference from an asset to a scene object. So this asset holds the pose
    /// numbers plus the scene name and angle id needed to find the camera again once that scene is open.
    ///
    /// WHICH ONE IS THE TRUTH:
    /// the SCENE is, at runtime — Cinemachine can only blend between real cameras, so the vcams are the
    /// rig and this is the authoring catalogue over them. That makes drift possible, so the inspector
    /// shows a live in-sync / differs status per row, "Capture" writes to both at once, and Pull / Apply
    /// exist to reconcile deliberately. Nothing here is read by the game.
    ///
    /// Editor-only on purpose: it is authoring data, it never ships, and it sits in the Editor assembly
    /// for the same reason <see cref="DemoStripRulesSO"/> does.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraAngleLibrary", menuName = "Dumyat/Presentation/Camera Angle Library")]
    public sealed class CameraAngleLibrarySO : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Demo scene holding this angle, e.g. 'Home_Demo'. Must match the scene name exactly.")]
            public string SceneName;

            [Tooltip("SceneStage id the angle belongs to, e.g. 'Home'. Informational — the slide asset " +
                     "is what actually pairs a stage with an angle.")]
            public string StageId;

            [Tooltip("Angle id a SceneShowcaseSlideSO refers to, e.g. 'godrays'. The GameObject in the " +
                     "scene is named 'Angle_' + this.")]
            public string AngleId;

            public Vector3 Position;

            [Tooltip("Euler angles in degrees. Typed values are fine — Apply pushes them to the scene.")]
            public Vector3 Euler;

            [Range(5f, 120f)] public float FieldOfView = 52f;

            [TextArea(1, 3), Tooltip("What this shot is for. Shows up nowhere but here; write it for " +
                                     "whoever re-frames this in six months.")]
            public string Note;

            public Quaternion Rotation => Quaternion.Euler(Euler);

            /// <summary>Name of the GameObject this entry maps to in its scene.</summary>
            public string ObjectName => AngleAuthoring.AnglePrefix + (AngleId ?? string.Empty);

            public string Label =>
                (string.IsNullOrEmpty(StageId) ? SceneName : StageId) + " · " + AngleId;
        }

        [SerializeField, Tooltip("Every angle across every demo scene, grouped by scene — the inspector " +
                                 "reads that grouping off this order and numbers each scene's angles " +
                                 "from 1. The numbers are for talking about shots while authoring; " +
                                 "nothing resolves an angle by index, at runtime or here.")]
        private List<Entry> _entries = new List<Entry>();

        public List<Entry> Entries => _entries;

        /// <summary>Entry for an angle id, or null. Ids are compared case-insensitively, as at runtime.</summary>
        public Entry Find(string angleId)
        {
            if (string.IsNullOrEmpty(angleId)) return null;
            foreach (Entry e in _entries)
                if (string.Equals(e.AngleId, angleId, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }
    }
}
#endif
