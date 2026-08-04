using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Add this component to any GameObject you want the tutorial system to find 
    /// without relying on slow GameObject.FindWithTag() calls.
    /// </summary>
    public class TutorialAnchor : MonoBehaviour
    {
        [Tooltip("The ID used by TutorialStepSO to find this object (e.g. 'CraftingStation' or 'WoodRack')")]
        public string AnchorId;

        [Header("Events")]
        [Tooltip("Fired when the tutorial system highlights or unhighlights this anchor.")]
        public UnityEngine.Events.UnityEvent<bool> OnHighlightChanged;

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
