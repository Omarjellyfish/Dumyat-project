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

        /// <summary>Returns the transform of the first anchor matching the ID.</summary>
        public static Transform GetPrimaryAnchor(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return null;
            
            if (_anchors.TryGetValue(anchorId, out var list) && list.Count > 0)
            {
                return list[0].transform;
            }
            return null;
        }

        /// <summary>Returns all anchors matching the ID (useful for highlighting multiple objects).</summary>
        public static List<TutorialAnchor> GetAllAnchors(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return null;
            _anchors.TryGetValue(anchorId, out var list);
            return list;
        }
    }
}
