using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>How <see cref="TutorialHighlighter"/> should draw attention to an anchor.</summary>
    public enum TutorialHighlightStyle
    {
        /// <summary>Frame for anything that is a UI panel; beacon for everything else.</summary>
        Auto,

        /// <summary>A vertical beam with a disc at its foot. For places and loose objects.</summary>
        Beacon,

        /// <summary>An outline drawn around the whole panel. For UI windows.</summary>
        Frame
    }

    /// <summary>
    /// Add this component to any GameObject you want the tutorial system to find
    /// without relying on slow GameObject.FindWithTag() calls.
    /// </summary>
    public class TutorialAnchor : MonoBehaviour
    {
        [Tooltip("The ID used by TutorialStepSO to find this object (e.g. 'CraftingStation' or 'WoodRack')")]
        public string AnchorId;

        [Header("Events")]
        [Tooltip("Fired when the tutorial system highlights or unhighlights this anchor. Use it to " +
                 "drive something specific to this object — an outline, a particle, an animation — " +
                 "alongside or instead of the built-in highlight below.")]
        public UnityEngine.Events.UnityEvent<bool> OnHighlightChanged;

        [Header("Built-in highlight")]
        [Tooltip("How the tutorial highlights this anchor when no custom highlight prefab is " +
                 "assigned on the TutorialRunner. Auto picks a frame for UI panels and a beacon for " +
                 "everything else, which is almost always what you want.")]
        public TutorialHighlightStyle HighlightStyle = TutorialHighlightStyle.Auto;

        [Tooltip("Frame size in METRES, for Frame style only. Leave at zero to measure the panel " +
                 "automatically — set it only when the measurement comes out wrong.")]
        public Vector2 FrameSizeOverride;

        /// <summary>Raises <see cref="OnHighlightChanged"/>. Called by the runner as a step starts and ends.</summary>
        public void SetHighlight(bool state)
        {
            OnHighlightChanged?.Invoke(state);
        }

        private void OnEnable()
        {
            TutorialAnchorRegistry.Register(this);
        }

        private void OnDisable()
        {
            TutorialAnchorRegistry.Unregister(this);
        }
    }

    /// <summary>
    /// Fast static registry of all active tutorial anchors in the scene.
    /// </summary>
    public static class TutorialAnchorRegistry
    {
        private static readonly Dictionary<string, List<TutorialAnchor>> _anchors = new Dictionary<string, List<TutorialAnchor>>();

        public static void Register(TutorialAnchor anchor)
        {
            if (string.IsNullOrEmpty(anchor.AnchorId)) return;
            
            if (!_anchors.TryGetValue(anchor.AnchorId, out var list))
            {
                list = new List<TutorialAnchor>();
                _anchors[anchor.AnchorId] = list;
            }
            
            if (!list.Contains(anchor)) list.Add(anchor);
        }

        public static void Unregister(TutorialAnchor anchor)
        {
            if (string.IsNullOrEmpty(anchor.AnchorId)) return;
            
            if (_anchors.TryGetValue(anchor.AnchorId, out var list))
            {
                list.Remove(anchor);
            }
        }

        /// <summary>
        /// The transform of the anchor matching the ID that is NEAREST the player.
        ///
        /// It used to be list[0] — whichever anchor happened to register first. That is the same
        /// answer whenever an id is unique, which every scene assumed it would be, and it is wrong the
        /// moment an id is shared: Ezbet Al Bourg's market has FOUR merchants, each carrying a
        /// 'Merchant' and a 'SellScale' anchor, so the market tutorial's card was placed at whichever
        /// stall woke up first and pointed there while the player stood at a different one.
        ///
        /// Nearest is the answer that degrades correctly — with a single anchor it IS list[0], so no
        /// existing scene changes behaviour.
        /// </summary>
        public static Transform GetPrimaryAnchor(string anchorId)
        {
            TutorialAnchor anchor = GetNearestAnchor(anchorId);
            return anchor != null ? anchor.transform : null;
        }

        /// <summary>
        /// The anchor with this ID closest to the player's head, or null if none is registered.
        /// Falls back to the first registered anchor while there is no camera to measure from — during
        /// a scene load there is a frame or two with no <c>Camera.main</c>, and "no card at all" is a
        /// worse answer than "the wrong one for one frame".
        /// </summary>
        public static TutorialAnchor GetNearestAnchor(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return null;
            if (!_anchors.TryGetValue(anchorId, out var list) || list.Count == 0) return null;

            if (list.Count == 1) return list[0];

            if (!_cameraSource.TryResolveCamera(out Camera camera)) return list[0];

            Vector3 eye = camera.transform.position;
            TutorialAnchor nearest = null;
            float best = float.PositiveInfinity;

            foreach (TutorialAnchor candidate in list)
            {
                if (candidate == null) continue;

                float distance = (candidate.transform.position - eye).sqrMagnitude;
                if (distance >= best) continue;

                best = distance;
                nearest = candidate;
            }

            return nearest;
        }

        // Borrowed rather than re-derived: CameraBillboard already owns the "re-resolve Camera.main
        // when the cached one has been destroyed" rule, and the reason it must be re-resolved is
        // written out there. A static here outlives scene loads, which is exactly the case that rule
        // exists for.
        private static readonly CameraBillboard _cameraSource = new CameraBillboard();

        /// <summary>Returns all anchors matching the ID (useful for highlighting multiple objects).</summary>
        public static List<TutorialAnchor> GetAllAnchors(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return null;
            _anchors.TryGetValue(anchorId, out var list);
            return list;
        }
    }
}
