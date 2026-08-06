using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Base for anything that reacts to a nail being hit.
    ///
    /// It owns only the wiring every reaction needs and cannot own itself: finding the nail, holding
    /// the style preset, and subscribing for the lifetime of the component. The reaction itself goes
    /// in the overrides, one concern per subclass, so a designer builds a nail's feel by adding the
    /// components they want rather than configuring one god-component.
    ///
    /// This is a base class rather than the plain-C# solver CLAUDE.md prefers because what is shared
    /// here IS MonoBehaviour lifecycle — OnEnable/OnDisable symmetry that a plain class cannot hold.
    /// Anything that is pure maths belongs in a plain class instead.
    /// </summary>
    public abstract class NailStrikeBehaviour : MonoBehaviour
    {
        [Header("Nail")]
        [Tooltip("The nail this reacts to. Found on a parent when left empty, which is the normal " +
                 "case — labels live under the nail they belong to.")]
        [SerializeField] private NailAction _nail;

        [Tooltip("Look and timing override. Left empty, this falls back to the nail's own style, so " +
                 "every reaction on one nail stays in step from a single wiring point.")]
        [SerializeField] private NailLabelStyleSO _styleOverride;

        protected NailAction Nail => _nail;

        protected NailLabelStyleSO Style =>
            _styleOverride != null ? _styleOverride
            : _nail != null ? _nail.LabelStyle
            : null;

        protected virtual void Awake()
        {
            if (_nail == null) _nail = GetComponentInParent<NailAction>(true);

            if (_nail == null)
                Debug.LogWarning($"{GetType().Name} on '{name}' found no {nameof(NailAction)} on itself " +
                                 "or any parent — it will never react to a blow.", this);
        }

        protected virtual void OnEnable()
        {
            if (_nail == null) return;

            _nail.Struck += HandleStruck;
            _nail.Set += HandleSet;
            _nail.Restored += HandleRestored;
            _nail.Refused += HandleRefused;

            // Adopt the rest state immediately. A nail hidden mid-drive by NailGroupController and
            // shown again would otherwise come back still lit from the last time it was hit.
            HandleRestored();
        }

        protected virtual void OnDisable()
        {
            if (_nail == null) return;

            _nail.Struck -= HandleStruck;
            _nail.Set -= HandleSet;
            _nail.Restored -= HandleRestored;
            _nail.Refused -= HandleRefused;
        }

        /// <summary>A blow that landed and counted.</summary>
        protected virtual void HandleStruck(NailStrike strike) { }

        /// <summary>The blow that drove the nail home. Always preceded by a <see cref="HandleStruck"/>.</summary>
        protected virtual void HandleSet(NailStrike strike) { }

        /// <summary>Back to nothing-has-happened-yet. Also called when this component is enabled.</summary>
        protected virtual void HandleRestored() { }

        /// <summary>A blow that landed on a nail which refuses to be driven.</summary>
        protected virtual void HandleRefused() { }
    }
}
