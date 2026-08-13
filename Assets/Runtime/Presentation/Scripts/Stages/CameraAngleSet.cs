using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// The authored camera angles for one demo stage, addressed by id.
    ///
    /// WHY LAYER MASKS RATHER THAN SetActive FOR HIDING:
    /// the brief asks for low-poly placeholders to be hidden and only the best angles shown, and those
    /// two things interact — a prop that ruins the close-up is often fine in the wide shot. Putting the
    /// offenders on a layer and giving each angle a mask means one prop can be visible from one angle
    /// and gone from the next with no objects being toggled, no state to restore, and no risk of a
    /// half-hidden scene if a slide is skipped. CarpentryQuarter and RasElBar both carry low-poly
    /// character instances, so this is load-bearing, not theoretical.
    /// </summary>
    public sealed class CameraAngleSet : MonoBehaviour
    {
        [System.Serializable]
        public struct Angle
        {
            [Tooltip("Stable id referenced by a SceneShowcaseSlideSO. Keep it short: 'wide', 'bench'.")]
            public string Id;

            [Tooltip("The Cinemachine camera holding the composition, authored with " +
                     "Dumyat ▸ Presentation ▸ Angles ▸ Create Angle From Scene View.")]
            public CinemachineCamera Camera;

            [Tooltip("Layers this angle must NOT see. Usually just PresentationHidden; set per angle " +
                     "so a prop can survive the wide shot and vanish in the detail.")]
            public LayerMask HiddenLayers;
        }

        [SerializeField] private Angle[] _angles = new Angle[0];

        [SerializeField, Tooltip("Priority given to the live angle. Any value above the resting " +
                                 "priority wins; the number itself does not matter.")]
        private int _livePriority = 100;

        [SerializeField, Tooltip("Priority the other angles in this set drop to. Must be below the " +
                                 "live priority — they stay enabled so the brain can blend away from " +
                                 "them, and priority is the only thing choosing the shot.")]
        private int _restingPriority;

        public IReadOnlyList<Angle> Angles => _angles;
        public int Count => _angles.Length;

        /// <summary>Angle ids in authoring order — used by the renderer to draw the angle pips.</summary>
        public int IndexOf(string angleId)
        {
            for (int i = 0; i < _angles.Length; i++)
            {
                if (string.Equals(_angles[i].Id, angleId, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Makes one angle live and returns the layers the rendering camera should exclude, or 0 if the
        /// id is unknown.
        ///
        /// <paramref name="allowBlend"/> is the whole difference between a camera move and a scene
        /// change. A CINEMACHINE BLEND NEEDS BOTH ENDS: the brain interpolates from the outgoing virtual
        /// camera to the incoming one, and a DISABLED virtual camera is not a blend source. This method
        /// used to switch the outgoing angle off, which is exactly why every move inside a place was a
        /// hard cut no matter what the brain's DefaultBlend said — there was nothing left to blend from.
        ///
        /// So when blending is wanted, every angle in this set stays ENABLED and priority alone picks the
        /// shot. That costs a handful of idle virtual-camera updates for the one live stage, which is
        /// nothing; the reason the others stay off is that 19 of them across six stages is not nothing,
        /// and <see cref="DeactivateAll"/> still shuts a whole stage down when it stops being live.
        /// </summary>
        public int Activate(string angleId, bool allowBlend)
        {
            int index = IndexOf(angleId);
            if (index < 0)
            {
                Debug.LogWarning($"[CameraAngleSet] '{name}' has no angle '{angleId}'. " +
                                 $"Known ids: {KnownIds()}", this);
                return 0;
            }

            for (int i = 0; i < _angles.Length; i++)
            {
                CinemachineCamera cam = _angles[i].Camera;
                if (cam == null) continue;

                bool live = i == index;
                cam.gameObject.SetActive(allowBlend || live);

                // Both ends are set explicitly, every time. Leaving the outgoing angle at the live
                // priority used to be harmless because it was disabled a moment later; now that it stays
                // enabled, two cameras claiming the top priority would let the brain pick either one.
                cam.Priority = live ? _livePriority : _restingPriority;
            }

            return _angles[index].HiddenLayers.value;
        }

        /// <summary>Puts every angle in this set to sleep. Called when the stage deactivates.</summary>
        public void DeactivateAll()
        {
            foreach (Angle a in _angles)
            {
                if (a.Camera != null) a.Camera.gameObject.SetActive(false);
            }
        }

        public string KnownIds()
        {
            if (_angles.Length == 0) return "<none>";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _angles.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(string.IsNullOrEmpty(_angles[i].Id) ? "<empty>" : _angles[i].Id);
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Fills the list from the angle root the editor tool writes into, so authoring a shot and
        /// registering it are not two separate chores that can fall out of step.
        /// </summary>
        [ContextMenu("Collect Angles From Scene")]
        private void CollectAngles()
        {
            var found = new List<Angle>();
            foreach (CinemachineCamera cam in
                     GetComponentsInChildren<CinemachineCamera>(true))
            {
                found.Add(new Angle
                {
                    Id = cam.name.StartsWith("Angle_") ? cam.name.Substring(6) : cam.name,
                    Camera = cam,
                    HiddenLayers = 0,
                });
            }

            if (found.Count == 0)
            {
                // The tool parents angles under a root in the scene, not under this component.
                foreach (CinemachineCamera cam in
                         gameObject.scene.GetRootGameObjects().Length > 0
                             ? FindAnglesInScene()
                             : new CinemachineCamera[0])
                {
                    found.Add(new Angle
                    {
                        Id = cam.name.StartsWith("Angle_") ? cam.name.Substring(6) : cam.name,
                        Camera = cam,
                        HiddenLayers = 0,
                    });
                }
            }

            _angles = found.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[CameraAngleSet] collected {found.Count} angle(s): {KnownIds()}", this);
        }

        private CinemachineCamera[] FindAnglesInScene()
        {
            var list = new List<CinemachineCamera>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<CinemachineCamera>(true));
            return list.ToArray();
        }
#endif
    }
}
